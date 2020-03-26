using System;

namespace ServiceBusSubscriptionProcessor.Configurations
{
    /// <summary>
    /// Configuration for ServiceBus endpoint.
    /// </summary>
    public class ServiceBusConfiguration
    {
        private const int MinimumAllowedBackoffTime = 0;
        private const int MaximumAllowedBackoffTime = 30;
        private const int MaximumAllowedRetries = 5;
        private const int MinimumAllowedGracePeriod = 45;
        private const int DefaultGracePeriodInSeconds = 120;

        /// <summary>
        /// Gets the full endpoint uri.
        /// </summary>
        public string EndpointURI
        {
            get
            {
                return !string.IsNullOrEmpty(Namespace) ? $"sb://{Namespace}.servicebus.windows.net/" : null;
            }
        }
        
        /// <summary>
        /// Gets or sets the connection string.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the entity path.
        /// </summary>
        public string EntityPath { get; set; }

        /// <summary>
        /// Gets or sets the name space.
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Gets a value indicating if it is possible to try to use managed identity.
        /// </summary>
        public bool UseManagedIdentity => !string.IsNullOrEmpty(EntityPath) && !string.IsNullOrEmpty(Namespace);

        /// <summary>
        /// Gets or sets the minimum backoff time for exponential retry policy (in seconds).
        /// </summary>
        public int SbMinimumAllowedBackoffTime { get; set; } = MinimumAllowedBackoffTime;

        /// <summary>
        /// Gets or sets the maximum backoff time for exponential retry policy (in seconds).
        /// </summary>
        public int SbMaximumAllowedBackoffTime { get; set; } = MaximumAllowedBackoffTime;

        /// <summary>
        /// Gets or sets the maximum retry number for exponential retry policy.
        /// </summary>
        public int SbMaximumAllowedRetries { get; set; } = MaximumAllowedRetries;

        /// <summary>
        /// Gets or sets the monitor grace period.
        /// </summary>
        public int SbMonitorGracePeriod { get; set; } = DefaultGracePeriodInSeconds;

        /// <summary>
        /// Ensures the validity of the retry policy configuration.
        /// </summary>
        public void CheckValidity()
        {
            if (SbMinimumAllowedBackoffTime < MinimumAllowedBackoffTime)
            {
                throw new InvalidOperationException($"The value of {nameof(SbMinimumAllowedBackoffTime)} must be higher than {MinimumAllowedBackoffTime}");
            }

            if (SbMaximumAllowedBackoffTime > MaximumAllowedBackoffTime)
            {
                throw new InvalidOperationException($"The value of {nameof(SbMaximumAllowedBackoffTime)} can't be higher than {MaximumAllowedBackoffTime}");
            }

            if (SbMaximumAllowedRetries > MaximumAllowedRetries)
            {
                throw new InvalidOperationException($"The value of {nameof(SbMaximumAllowedRetries)} can't be higher than {MaximumAllowedRetries}");
            }

            if (SbMonitorGracePeriod < MinimumAllowedGracePeriod)
            {
                throw new InvalidOperationException($"The value of {nameof(SbMonitorGracePeriod)} can't be lower than {MinimumAllowedGracePeriod}");
            }

            if (string.IsNullOrEmpty(EntityPath))
            {
                throw new InvalidOperationException($"{nameof(EntityPath)} property cannot be null");
            }

            if (string.IsNullOrEmpty(ConnectionString) && string.IsNullOrEmpty(Namespace))
            {
                throw new InvalidOperationException($"{nameof(ConnectionString)} or {nameof(Namespace)} property cannot be null");
            }
        }
    }
}
