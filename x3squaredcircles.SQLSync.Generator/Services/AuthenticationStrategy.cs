using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Data.Common;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    /// <summary>
    /// Defines the contract for an authentication strategy responsible for creating a database connection.
    /// </summary>
    public interface IAuthenticationStrategy
    {
        /// <summary>
        /// Creates and returns a ready-to-use database connection object based on the strategy.
        /// </summary>
        /// <param name="config">The application configuration.</param>
        /// <param name="password">The resolved password, if required by the strategy.</param>
        /// <returns>A DbConnection object.</returns>
        Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config, string password);
    }

    /// <summary>
    /// Defines the contract for a factory that selects the appropriate authentication strategy.
    /// </summary>
    public interface IAuthenticationStrategyFactory
    {
        IAuthenticationStrategy GetStrategy(AuthMode authMode);
    }

    /// <summary>
    /// Factory for creating the correct IAuthenticationStrategy based on the application configuration.
    /// </summary>
    public class AuthenticationStrategyFactory : IAuthenticationStrategyFactory
    {
        private readonly IServiceProvider _serviceProvider;
        public AuthenticationStrategyFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IAuthenticationStrategy GetStrategy(AuthMode authMode)
        {
            return authMode switch
            {
                AuthMode.Password => (IAuthenticationStrategy)_serviceProvider.GetService(typeof(UsernamePasswordStrategy)),
                // Other strategies like AzureMsi will be added here.
                _ => throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration, $"Authentication mode '{authMode}' is not supported."),
            };
        }
    }

    /// <summary>
    /// Implements the basic username and password authentication strategy.
    /// </summary>
    public class UsernamePasswordStrategy : IAuthenticationStrategy
    {
        private readonly ILogger<UsernamePasswordStrategy> _logger;

        public UsernamePasswordStrategy(ILogger<UsernamePasswordStrategy> logger)
        {
            _logger = logger;
        }

        public async Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config, string password)
        {
            _logger.LogDebug("Creating connection using Password authentication strategy for provider {Provider}.", config.Database.Provider);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = config.Database.Port > 0 ? $"{config.Database.Server},{config.Database.Port}" : config.Database.Server,
                InitialCatalog = config.Database.DatabaseName,
                UserID = config.Authentication.Username,
                Password = password,
                ConnectTimeout = config.Database.ConnectionTimeoutSeconds,
            };

            // Provider-specific settings would be added here in a full implementation.
            if (config.Database.Provider == DbProvider.SqlServer)
            {
                builder.Encrypt = true;
                builder.TrustServerCertificate = false;
            }

            var connectionString = builder.ToString();

            // This would be abstracted in a full multi-provider implementation.
            // For now, we assume SQL Server as it's the most complex.
            var connection = new SqlConnection(connectionString);

            await Task.CompletedTask;
            return connection;
        }
    }
}