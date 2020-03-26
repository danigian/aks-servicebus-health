using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceBusSubscriptionProcessor.Processor.Interfaces;

namespace ServiceBusSubscriptionProcessor.HealthChecks
{
    /// <summary>
    /// Service bus subscription health check.
    /// </summary>
    public class SubscriptionHealthCheck : IHealthCheck
    {
        private readonly IMonitor _subscriptionMonitor;

        /// <summary>
        /// Initializes a new instance of the SubscriptionHealthCheck class.
        /// </summary>
        public SubscriptionHealthCheck(IMonitor subscriptionMonitor)
        {
            _subscriptionMonitor = subscriptionMonitor ?? throw new System.ArgumentNullException(nameof(subscriptionMonitor));
        }

        /// <summary>
        /// Runs the health check, returning the status of the component being checked.
        /// </summary>
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
           return _subscriptionMonitor.IsConnectionAlive() ?
                Task.FromResult(HealthCheckResult.Healthy($"Device lifecycle notification connection is healthy.")) :
                Task.FromResult(HealthCheckResult.Unhealthy($"No connection established to device lifecycle notification topic."));
        }
    }
}
