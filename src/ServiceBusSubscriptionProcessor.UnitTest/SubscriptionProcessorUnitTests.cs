using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NFluent;
using ServiceBusSubscriptionProcessor.Configurations;
using ServiceBusSubscriptionProcessor.Processor;

namespace ServiceBusSubscriptionProcessor.UnitTest
{
    [TestClass]
    public class SubscriptionProcessorUnitTests
    {
        private const int TimeoutBeforePodRestart = 70_000;
        private IOptions<ServiceBusConfiguration> servicebusConfiguration;

        [TestInitialize]
        public void Initialize()
        {
            servicebusConfiguration = Options.Create(new ServiceBusConfiguration() { EntityPath = "test", Namespace = "test" });
        }

        [TestMethod]
        public void Valid_Configuration_Should_Not_Throw_Exception()
        {
            Check.ThatCode(() => new SubscriptionMonitor(servicebusConfiguration)).DoesNotThrow();
        }

        [TestMethod]
        public void Null_Argument_For_ReportException_Should_Throw_Exception()
        {
            Check.ThatCode(() => new SubscriptionMonitor(servicebusConfiguration).ReportException(null)).Throws<ArgumentNullException>();
        }

        [TestMethod]
        [DataRow(0, true)]
        [DataRow(1, false)]
        [DataRow(5, false)]
        [DataRow(10, false)]
        public void IsConnectionAlive_Should_Return_Correct_Value_For_Default_Configuration(int exceptionsNumber, bool healthStatus)
        {
            // GIVEN
            var subscriptionMonitor = new SubscriptionMonitor(servicebusConfiguration);
            var exceptionToReport = new Exception();

            // WHEN
            for (int i = 0; i < exceptionsNumber; i++)
            {
                subscriptionMonitor.ReportException(exceptionToReport);
            }

            // THEN
            Check.That(subscriptionMonitor.IsConnectionAlive()).IsEqualTo(healthStatus);
        }

        [TestMethod]
        public void Invalid_Configuration_Should_Throw_Exception()
        {
            Check.ThatCode(() => new SubscriptionMonitor(Options.Create(new ServiceBusConfiguration() { SbMaximumAllowedRetries = 100, SbMinimumAllowedBackoffTime = 0, SbMaximumAllowedBackoffTime = 15 }))).Throws<InvalidOperationException>();
        }

        [TestMethod]
        [DataRow(0, 30, 2, 8)]
        [DataRow(30, 30, 5, 1)]
        [DataRow(20, 30, 5, 0)]
        public void Should_Return_Correct_Retry_Number_For_Grace_Period(int minimumBackOffTime, int maximumBackOffTime, int maximumRetries, int expectedRetries)
        {
            // GIVEN
            var servicebusConfiguration = Options.Create(new ServiceBusConfiguration() { SbMaximumAllowedRetries = maximumRetries, SbMinimumAllowedBackoffTime = minimumBackOffTime, SbMaximumAllowedBackoffTime = maximumBackOffTime, EntityPath = "test", Namespace = "test" });
            var servicebusMonitor = new SubscriptionMonitor(servicebusConfiguration);

            // WHEN
            var calculatedRetries = servicebusMonitor.CalculateMaxNumberOfExceptionsByGracePeriod();

            // THEN
            Check.That(expectedRetries).IsEqualTo(calculatedRetries);
        }

        [TestMethod]
        [Timeout(TimeoutBeforePodRestart)]
        public async Task Should_Restart_Pod_When_Transient_Errors_Exceed_Grace_Period()
        {
            const int podPollingInterval = 3_000;

            var servicebusConfiguration = Options.Create(new ServiceBusConfiguration()
            {
                SbMaximumAllowedBackoffTime = 5,
                SbMaximumAllowedRetries = 2,
                SbMinimumAllowedBackoffTime = 0,
                SbMonitorGracePeriod = 45,
                EntityPath = "test",
                Namespace = "test"
            });

            var subscriptionMonitor = new SubscriptionMonitor(servicebusConfiguration);
            var timeoutForPodRestart = TimeSpan.FromMilliseconds(TimeoutBeforePodRestart);

            using var cts = new CancellationTokenSource((int)timeoutForPodRestart.TotalMilliseconds);

            var taskErrorReporter = Task.Run(
                async () =>
                {
                    var config = servicebusConfiguration.Value;
                    var retry = new RetryExponential(TimeSpan.FromSeconds(config.SbMinimumAllowedBackoffTime), TimeSpan.FromSeconds(config.SbMaximumAllowedBackoffTime), config.SbMaximumAllowedRetries);
                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            await retry.RunOperation(() => throw new ServiceBusCommunicationException("timeout"), TimeSpan.FromSeconds(10));
                        }
                        catch (ServiceBusCommunicationException ex)
                        {
                            subscriptionMonitor.ReportException(ex);
                        }
                    }
                },
                cts.Token);

            var podRestarted = false;

            var taskHealthProbe = Task.Run(
                                    async () =>
                                    {
                                        var downCount = 0;

                                        while (!cts.IsCancellationRequested && taskErrorReporter.Status <= TaskStatus.Running)
                                        {
                                            if (!cts.IsCancellationRequested)
                                            {
                                                await Task.Delay(podPollingInterval, cts.Token);
                                                var healthState = subscriptionMonitor.IsConnectionAlive();
                                                if (!healthState)
                                                {
                                                    downCount++;
                                                }
                                                else
                                                {
                                                    downCount = 0;
                                                }
                                            }

                                            if (downCount == 3)
                                            {
                                                podRestarted = true;
                                                cts.Cancel();
                                                break;
                                            }
                                        }
                                    },
                                    cts.Token);

            try
            {
                await Task.WhenAll(taskErrorReporter, taskHealthProbe);
            }
            catch (OperationCanceledException)
            {
            }

            Check.That(podRestarted).IsEqualTo(true);
        }
    }
}