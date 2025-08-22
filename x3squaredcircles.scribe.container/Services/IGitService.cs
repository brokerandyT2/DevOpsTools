using System.Threading.Tasks;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a service that interacts with a Git repository.
    /// </summary>
    public interface IGitService
    {
        /// <summary>
        /// Retrieves the raw commit log from the Git repository for a specified range.
        /// </summary>
        /// <param name="workspacePath">The path to the directory containing the .git repository.</param>
        /// <param name="gitRange">
        /// The commit range to inspect. This can be a single commit SHA (implying all history up to that point)
        /// or a range format like "tag1..tag2".
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result is a single string
        /// containing the multi-line output of the `git log` command, including commit messages.
        /// Returns an empty string if the log cannot be retrieved.
        /// </returns>
        Task<string> GetCommitLogAsync(string workspacePath, string gitRange);
    }
}