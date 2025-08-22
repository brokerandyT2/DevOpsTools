using System.Threading.Tasks;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    /// <summary>
    /// Defines the contract for a service that retrieves secrets from a configured key vault.
    /// </summary>
    public interface IKeyVaultService
    {
        /// <summary>
        /// Retrieves the value of a secret from the configured key vault.
        /// </summary>
        /// <param name="secretName">The name/identifier of the secret to retrieve.</param>
        /// <returns>The string value of the secret, or null if not found.</returns>
        Task<string> GetSecretAsync(string secretName);
    }
}