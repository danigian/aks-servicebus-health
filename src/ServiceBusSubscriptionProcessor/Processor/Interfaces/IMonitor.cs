using System;

namespace ServiceBusSubscriptionProcessor.Processor.Interfaces
{
    /// <summary>
    /// Interface for a generic monitor.
    /// </summary>
    public interface IMonitor
    {
        /// <summary>
        /// Returns true if connection is alive. False otherwise.
        /// </summary>
        bool IsConnectionAlive();

        /// <summary>
        /// Adds an exception to the list of the reported exceptions.
        /// </summary>
        void ReportException(Exception exception);
    }
}
