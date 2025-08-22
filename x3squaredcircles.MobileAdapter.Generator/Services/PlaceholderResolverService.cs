using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System;

namespace x3squaredcircles.MobileAdapter.Generator.Services
{
    /// <summary>
    /// Defines the contract for a service that resolves logical placeholders in strings.
    /// </summary>
    public interface IPlaceholderResolverService
    {
        /// <summary>
        /// Resolves all placeholders in the format {placeholderName} within the input string.
        /// </summary>
        /// <param name="input">The string containing potential placeholders.</param>
        /// <returns>The string with all resolvable placeholders replaced by their environment variable values.</returns>
        string ResolvePlaceholders(string input);
    }

    /// <summary>
    /// Implements placeholder resolution by looking up corresponding `ADAPTERGEN_CUSTOM_*` environment variables.
    /// </summary>
    public class PlaceholderResolverService : IPlaceholderResolverService
    {
        private const string CustomVariablePrefix = "ADAPTERGEN_CUSTOM_";
        private readonly ILogger<PlaceholderResolverService> _logger;
        private static readonly Regex PlaceholderRegex = new Regex(@"\{(?<name>[a-zA-Z0-9_.-]+)\}", RegexOptions.Compiled);

        public PlaceholderResolverService(ILogger<PlaceholderResolverService> logger)
        {
            _logger = logger;
        }

        public string ResolvePlaceholders(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || !input.Contains('{'))
            {
                return input;
            }

            return PlaceholderRegex.Replace(input, match =>
            {
                var placeholderName = match.Groups["name"].Value;
                var variableName = $"{CustomVariablePrefix}{placeholderName.ToUpperInvariant()}";
                var variableValue = Environment.GetEnvironmentVariable(variableName);

                if (variableValue != null)
                {
                    _logger.LogDebug("Resolved placeholder '{{{Placeholder}}}' using environment variable '{VariableName}'.", placeholderName, variableName);
                    return variableValue;
                }

                _logger.LogWarning("Could not resolve placeholder '{{{Placeholder}}}'. The environment variable '{VariableName}' was not found. The placeholder will not be replaced.", placeholderName, variableName);
                // Return the original match (e.g., "{myPlaceholder}") if the variable is not found.
                return match.Value;
            });
        }
    }
}