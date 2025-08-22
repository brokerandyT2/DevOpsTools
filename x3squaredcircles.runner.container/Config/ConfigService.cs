using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using x3squaredcircles.runner.container.Engine;
using X3SquaredCircles.Runner.Container.Engine;

namespace x3squaredcircles.runner.container.Config;

/// <summary>
/// Defines the contract for a service that manages the lifecycle of the pipeline-config.json file.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Ensures a configuration file exists. If it does not, it performs a first-run safety analysis
    /// on the provided blueprint and generates a new, safe-by-default configuration file.
    /// </summary>
    /// <param name="configPath">The full path to the configuration file.</param>
    /// <param name="blueprint">The parsed Universal Blueprint of the pipeline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task BootstrapAsync(string configPath, UniversalBlueprint blueprint);

    /// <summary>
    /// Asynchronously loads and deserializes the PipelineConfig from the specified path.
    /// </summary>
    /// <param name="configPath">The full path to the configuration file.</param>
    /// <returns>A task that resolves to the loaded PipelineConfig instance.</returns>
    Task<PipelineConfig> LoadAsync(string configPath);
}

public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Heuristics for the first-run safety analysis.
    private static readonly IReadOnlyList<string> UnsafeKeywords = new List<string>
    {
        "deploy", "publish", "push", "apply", "terraform", "ansible", "helm", "promote", "release", "delete"
    };

    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;
    }

    public async Task BootstrapAsync(string configPath, UniversalBlueprint blueprint)
    {
        if (File.Exists(configPath))
        {
            _logger.LogDebug("Configuration file '{ConfigPath}' already exists. Skipping bootstrap.", configPath);
            return;
        }

        _logger.LogInformation("Configuration file not found. Performing first-run safety analysis to generate '{ConfigPath}'.", configPath);

        var steps = new Dictionary<string, StepAction>();

        foreach (var job in blueprint.Jobs)
        {
            foreach (var step in job.Steps)
            {
                var stepNameLower = step.DisplayName.ToLowerInvariant();
                var action = "run"; // Default to a safe action
                string? reason = null;

                foreach (var keyword in UnsafeKeywords)
                {
                    if (stepNameLower.Contains(keyword))
                    {
                        action = "skip";
                        reason = $"unsafe keyword '{keyword}'";
                        break;
                    }
                }

                if (reason != null)
                {
                    _logger.LogInformation("  - Step '{StepName}': Marked as '{Action}' (Reason: {Reason})", step.DisplayName, action, reason);
                }
                else
                {
                    _logger.LogInformation("  - Step '{StepName}': Marked as '{Action}' (safe)", step.DisplayName, action);
                }

                steps[step.Id] = new StepAction(action);
            }
        }

        var newConfig = new PipelineConfig("1.0", steps);
        var jsonContent = JsonSerializer.Serialize(newConfig, SerializerOptions);

        await File.WriteAllTextAsync(configPath, jsonContent);
        _logger.LogInformation("Configuration generated. Edit the file to enable or modify skipped steps.");
    }

    public async Task<PipelineConfig> LoadAsync(string configPath)
    {
        _logger.LogDebug("Loading configuration from '{ConfigPath}'.", configPath);
        var jsonContent = await File.ReadAllTextAsync(configPath);
        var config = JsonSerializer.Deserialize<PipelineConfig>(jsonContent);

        if (config is null)
        {
            throw new InvalidOperationException($"Failed to deserialize configuration file at '{configPath}'. The file may be empty or malformed.");
        }

        return config;
    }
}