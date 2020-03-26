using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using ServiceBusSubscriptionProcessor.Configurations;
using ServiceBusSubscriptionProcessor.Processor.Interfaces;

namespace ServiceBusSubscriptionProcessor.Processor
{
    /// <summary>
    /// Monitor for internal service bus device lifecycle notifications.
    /// </summary>
    public class SubscriptionMonitor : IMonitor
    {
        private readonly TimeSpan _gracePeriod;
        private readonly int _possibleRetriesInEvaluationPeriod;
        private object _lockObject = new object();
        private IList<int> _exceptionsTimestamps;
        private ServiceBusConfiguration _serviceBusConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionMonitor"/> class.
        /// </summary>
        /// <param name="serviceBusConfiguration">The endpoint configuration.</param>
        public SubscriptionMonitor(IOptions<ServiceBusConfiguration> serviceBusConfiguration)
        {
            _serviceBusConfiguration = serviceBusConfiguration?.Value ?? throw new ArgumentNullException(nameof(serviceBusConfiguration));
            _serviceBusConfiguration.CheckValidity();

            _gracePeriod = TimeSpan.FromSeconds(_serviceBusConfiguration.SbMonitorGracePeriod);

            _possibleRetriesInEvaluationPeriod = CalculateMaxNumberOfExceptionsByGracePeriod();
            _exceptionsTimestamps = new List<int>();
        }

        /// <inheritdoc/>
        public bool IsConnectionAlive()
        {
            lock (_lockObject)
            {
                if (_exceptionsTimestamps.Count == 0)
                {
                    // exit immediately as we have no exceptions recorded in the last grace period
                    return true;
                }

                var rollingGracePeriod = Environment.TickCount - _gracePeriod.TotalMilliseconds;

                bool liveness = _exceptionsTimestamps
                                .Where(x => x >= rollingGracePeriod)
                                .Count() < _possibleRetriesInEvaluationPeriod ? true : false;

                _exceptionsTimestamps = _exceptionsTimestamps.Where(x => x > rollingGracePeriod).ToList();
                return liveness;
            }
        }

        /// <inheritdoc/>
        public void ReportException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            lock (_lockObject)
            {
                _exceptionsTimestamps.Add(Environment.TickCount);
            }
        }

        /// <summary>
        /// Calculate how many exceptions will be thrown, given the number of possible retries in the defined grace period.
        /// The outcome is based on the maximum number of retries attempted by Exponential Retry policy (link below)
        /// https://github.com/Azure/azure-sdk-for-net/blob/master/sdk/servicebus/Microsoft.Azure.ServiceBus/src/RetryExponential.cs
        /// </summary>
        public int CalculateMaxNumberOfExceptionsByGracePeriod()
        {
            int currentRetryCount = 0;
            TimeSpan elapsedTime = TimeSpan.FromSeconds(0);

            const int mSecMultiplier = 1_000;
            const int maxInterval = 3_600; // ServiceBus exponential retry max interval

            while (currentRetryCount <= _serviceBusConfiguration.SbMaximumAllowedRetries)
            {
                double increment = (Math.Pow(2, currentRetryCount) - 1) * maxInterval;
                double timeToSleepMsec = Math.Min((_serviceBusConfiguration.SbMinimumAllowedBackoffTime * mSecMultiplier) + increment, _serviceBusConfiguration.SbMaximumAllowedBackoffTime * mSecMultiplier);
                var retryInterval = TimeSpan.FromMilliseconds(timeToSleepMsec);
                elapsedTime = elapsedTime.Add(retryInterval);
                if (elapsedTime < _gracePeriod)
                {
                    currentRetryCount++;
                    continue;
                }

                break;
            }

            var maxMonitorExceptionsByGracePeriod = (int)Math.Floor(_gracePeriod.TotalMilliseconds / elapsedTime.TotalMilliseconds);
            return maxMonitorExceptionsByGracePeriod;
        }
    }
}
