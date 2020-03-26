using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceBusSubscriptionProcessor.Configurations;
using ServiceBusSubscriptionProcessor.Processor.Interfaces;

namespace ServiceBusSubscriptionProcessor.Processor
{
    /// <summary>
    /// Handler of internal service bus for device lifecycle notifications.
    /// </summary>
    public class SubscriptionProcessor : IAsyncDisposable
    {
        private const string DeviceLifecycleRule = "DeviceLifecycleRule";
        private readonly ILogger<SubscriptionProcessor> _logger;
        private readonly IMonitor _subscriptionMonitor;
        private readonly ServiceBusConfiguration _serviceBusConfiguration;
        private ISubscriptionClient _subscriptionClient;
        private int _registryInitialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionProcessor"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="serviceBusConfiguration">The configuration for the service bus.</param>
        /// <param name="currentStamp">The current stamp configuration.</param>
        /// <param name="metricsTracker">The metrics tracker.</param>
        /// <param name="subscriptionMonitor">The service monitor.</param>
        public SubscriptionProcessor(ILogger<SubscriptionProcessor> logger, IOptions<ServiceBusConfiguration> serviceBusConfiguration,  IMonitor subscriptionMonitor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceBusConfiguration = serviceBusConfiguration?.Value ?? throw new ArgumentNullException(nameof(serviceBusConfiguration));
            _serviceBusConfiguration.CheckValidity();
            _subscriptionMonitor = subscriptionMonitor ?? throw new ArgumentNullException(nameof(subscriptionMonitor));
        }

        /// <summary>
        /// Gets the subscription name for this instance.
        /// </summary>
        public string SubscriptionName { get; private set; }

        /// <summary>
        /// Initializes the notification handler.
        /// </summary>
        /// <param name="token">A cancellation token.</param>
        /// <returns>A Task.</returns>
        public async Task InitializeAsync(CancellationToken token = default)
        {
            var wasInitialized = Interlocked.Exchange(ref _registryInitialized, 1);
            if (wasInitialized == 1)
            {
                throw new InvalidOperationException("Device Lifecycle Notification Handler is already initialized.");
            }

            SubscriptionName = await CreateAutoDeleteOnIdleSubscriptionAsync(_serviceBusConfiguration, token);
            InitializeSubscriptionClient(SubscriptionName);
            RegisterOnMessageHandlerAndReceiveMessages();
        }

        /// <summary>
        /// Closes the connection to the service bus and notifies the observers.
        /// </summary>
        /// <returns>Awaitable result of CloseAsync.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_subscriptionClient != null)
            {
                await _subscriptionClient.CloseAsync();
            }
        }

        private void InitializeSubscriptionClient(string subscriptionName)
        {
            var exponentialRetry = new RetryExponential(
                minimumBackoff: TimeSpan.FromSeconds(_serviceBusConfiguration.SbMinimumAllowedBackoffTime),
                maximumBackoff: TimeSpan.FromSeconds(_serviceBusConfiguration.SbMaximumAllowedBackoffTime),
                _serviceBusConfiguration.SbMaximumAllowedRetries);

            if (_serviceBusConfiguration.UseManagedIdentity)
            {
                var tokenProvider = Microsoft.Azure.ServiceBus.Primitives.TokenProvider.CreateManagedIdentityTokenProvider();
                _subscriptionClient = new SubscriptionClient(_serviceBusConfiguration.EndpointURI, _serviceBusConfiguration.EntityPath, subscriptionName, tokenProvider, retryPolicy: exponentialRetry);
            }
            else
            {
                _subscriptionClient = new SubscriptionClient(_serviceBusConfiguration.ConnectionString, _serviceBusConfiguration.EntityPath, subscriptionName, retryPolicy: exponentialRetry);
            }
        }

        /// <summary>
        /// Creates a subscription with auto delete on idle option and filter based on tenant, stamp and serviceEndpointName in ('iothub', 'lora').
        /// </summary>
        /// <param name="deviceLifecycleEndpoint">The endpoint.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The subscription name.</returns>
        private async Task<string> CreateAutoDeleteOnIdleSubscriptionAsync(ServiceBusConfiguration deviceLifecycleEndpoint, CancellationToken token)
        {
            var subscriptionName = Guid.NewGuid().ToString();

            var topicName = deviceLifecycleEndpoint.EntityPath;
            try
            {
                ManagementClient manager;
                if (!string.IsNullOrEmpty(deviceLifecycleEndpoint.Namespace) && !string.IsNullOrEmpty(deviceLifecycleEndpoint.EntityPath))
                {
                    var tokenProvider = Microsoft.Azure.ServiceBus.Primitives.TokenProvider.CreateManagedIdentityTokenProvider();
                    manager = new ManagementClient(deviceLifecycleEndpoint.EndpointURI, tokenProvider);
                }
                else
                {
                    manager = new ManagementClient(deviceLifecycleEndpoint.ConnectionString);
                }

                if (!await manager.SubscriptionExistsAsync(topicName, subscriptionName, token))
                {
                    await manager.CreateSubscriptionAsync(
                        new SubscriptionDescription(topicName, subscriptionName)
                        {
                            AutoDeleteOnIdle = TimeSpan.FromMinutes(5),
                            MaxDeliveryCount = 1,
                            LockDuration = TimeSpan.FromSeconds(5),
                            DefaultMessageTimeToLive = TimeSpan.FromDays(1),
                            EnableDeadLetteringOnFilterEvaluationExceptions = false,
                            EnableDeadLetteringOnMessageExpiration = false,
                        }, token);
                }

                _logger.LogInformation($"Subscription named {subscriptionName} created for topic {topicName} on service bus {deviceLifecycleEndpoint.EndpointURI}");
            }
            catch (Exception)
            {
                throw;
            }

            return subscriptionName;
        }

        private void RegisterOnMessageHandlerAndReceiveMessages()
        {
            // Configure the message handler options in terms of exception handling, number of concurrent messages to deliver, etc.
            var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
            {
                // Maximum number of concurrent calls to the callback ProcessMessagesAsync(), set to 1 for simplicity.
                // Set it according to how many messages the application wants to process in parallel.
                MaxConcurrentCalls = 1,

                // Indicates whether the message pump should automatically complete the messages after returning from user callback.
                // False below indicates the complete operation is handled by the user callback as in ProcessMessagesAsync().
                AutoComplete = false,
            };

            // Register the function that processes messages.
            _subscriptionClient.RegisterMessageHandler(ProcessMessagesAsync, messageHandlerOptions);
        }

        private async Task ProcessMessagesAsync(Message message, CancellationToken token)
        {
            _logger.LogInformation($"Received the following message: {Encoding.UTF8.GetString(message.Body)}");

            // Complete the message so that it is not received again.
            await _subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);
        }

        private Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            _logger.LogError($"Reporting {exceptionReceivedEventArgs.Exception.GetType()} exception to monitor.");
            _subscriptionMonitor.ReportException(exceptionReceivedEventArgs.Exception);

            return Task.CompletedTask;
        }
    }
}
