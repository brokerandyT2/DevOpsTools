using System.Collections.Generic;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Models.Forensic;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a service that reads and parses the
    /// forensic log of record, 'pipeline-log.json'.
    /// </summary>
    public interface IForensicLogService
    {
        /// <summary>
        /// Gets the collection of log entries loaded from the pipeline-log.json file.
        /// This collection will be empty if the log file was not found or has not been loaded yet.
        /// </summary>
        IEnumerable<LogEntry> Entries { get; }

        /// <summary>
        /// Attempts to locate, read, and deserialize the pipeline-log.json file from a given workspace path.
        /// If the file is not found, it logs a warning but does not throw an exception, allowing for
        // graceful degradation as per the architectural specification. If the file is found but is
        /// malformed, a critical exception will be thrown.
        /// </summary>
        /// <param name="workspacePath">The root path of the CI/CD workspace where the log file is expected.</param>
        /// <returns>A task representing the asynchronous loading operation.</returns>
        Task LoadLogEntriesAsync(string workspacePath);
    }
}