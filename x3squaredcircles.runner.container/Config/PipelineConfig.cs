using System.Text.Json.Serialization;

namespace x3squaredcircles.runner.container.Config;

/// <summary>
/// Represents the developer's control configuration for a local pipeline run,
/// deserialized from the pipeline-config.json file.
/// </summary>
/// <param name="Version">The schema version of the configuration file.</param>
/// <param name="Steps">A dictionary mapping step identifiers to their desired execution action.</param>
public record PipelineConfig(
    [property: JsonPropertyName("version")]
    string Version,

    [property: JsonPropertyName("steps")]
    IReadOnlyDictionary<string, StepAction> Steps
);

/// <summary>
/// Defines the specific action to be taken for a given pipeline step.
/// </summary>
/// <param name="Action">The desired action, which can be "run", "skip", or "pause_after".</param>
public record StepAction(
    [property: JsonPropertyName("action")]
    string Action
);