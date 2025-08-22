using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.runner.container.Engine;
using X3SquaredCircles.Runner.Container.Engine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace x3squaredcircles.runner.container.Adapters.Azure;

/// <summary>
/// An implementation of <see cref="IPlatformAdapter"/> for Azure DevOps Pipelines.
/// </summary>
public class AzureAdapter : IPlatformAdapter
{
    private readonly ILogger<AzureAdapter> _logger;
    private const string DefaultFileName = "azure-pipelines.yml";
    private const string AlternateFileName = "azure-pipelines.yaml";

    public string PlatformId => "azure";

    public AzureAdapter(ILogger<AzureAdapter> logger)
    {
        _logger = logger;
    }

    public Task<bool> CanHandleAsync(string projectRootPath)
    {
        var defaultPath = Path.Combine(projectRootPath, DefaultFileName);
        var alternatePath = Path.Combine(projectRootPath, AlternateFileName);
        var result = File.Exists(defaultPath) || File.Exists(alternatePath);
        return Task.FromResult(result);
    }

    public async Task<UniversalBlueprint> ParseAsync(string projectRootPath)
    {
        var pipelinePath = Path.Combine(projectRootPath, DefaultFileName);
        if (!File.Exists(pipelinePath))
        {
            pipelinePath = Path.Combine(projectRootPath, AlternateFileName);
        }

        if (!File.Exists(pipelinePath))
        {
            throw new FileNotFoundException($"No Azure Pipelines file ({DefaultFileName} or {AlternateFileName}) found in the project root.");
        }

        _logger.LogDebug("Parsing Azure Pipelines file: {PipelineFile}", pipelinePath);

        var yamlContent = await File.ReadAllTextAsync(pipelinePath);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var azureFile = deserializer.Deserialize<AzurePipelineFile>(yamlContent);
        return TransformToBlueprint(azureFile);
    }

    public bool EvaluateCondition(string? condition, IExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        var trimmedCondition = condition.Trim();
        if (bool.TryParse(trimmedCondition, out var result))
        {
            return result;
        }

        _logger.LogWarning("Complex condition evaluation is not yet fully implemented for Azure Pipelines. Condition '{Condition}' will be treated as TRUE. Step will run.", condition);
        // Default to running the step for complex expressions like '$[...]'.
        return true;
    }

    private UniversalBlueprint TransformToBlueprint(AzurePipelineFile azureFile)
    {
        var jobs = new List<Job>();

        // Azure Pipelines can have steps at the root, implying a single, anonymous job.
        if (azureFile.Steps.Any())
        {
            jobs.Add(CreateJobFromRootSteps("default-job", "Default Job", azureFile.Steps));
        }

        // It can also have explicit jobs.
        if (azureFile.Jobs.Any())
        {
            foreach (var azureJob in azureFile.Jobs)
            {
                var steps = azureJob.Steps.Select((s, i) => CreateStep(s, azureJob.Job, i)).ToList();
                var job = new Job(
                    Id: azureJob.Job,
                    DisplayName: azureJob.DisplayName ?? azureJob.Job,
                    RunCondition: azureJob.Condition,
                    Environment: azureJob.Variables ?? new Dictionary<string, string>(),
                    Steps: steps
                );
                jobs.Add(job);
            }
        }

        return new UniversalBlueprint("1.0.0", PlatformId, jobs);
    }

    private Job CreateJobFromRootSteps(string id, string displayName, List<AzureStep> rootSteps)
    {
        var steps = rootSteps.Select((s, i) => CreateStep(s, id, i)).ToList();
        return new Job(
            Id: id,
            DisplayName: displayName,
            RunCondition: null,
            Environment: new Dictionary<string, string>(),
            Steps: steps
        );
    }

    private Step CreateStep(AzureStep azureStep, string parentJobId, int index)
    {
        var stepId = azureStep.Name ?? $"{parentJobId}-step-{index}";
        var displayName = azureStep.DisplayName ?? azureStep.Task ?? azureStep.Script ?? $"Unnamed Step {index}";
        var commands = azureStep.Script?.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();

        return new Step(
            Id: stepId,
            DisplayName: displayName,
            RunCondition: azureStep.Condition,
            WorkingDirectory: azureStep.WorkingDirectory,
            Task: new TaskDefinition("shell", commands)
        );
    }

    #region Private Deserialization Models
    private record AzurePipelineFile
    {
        public List<AzureJobDefinition> Jobs { get; init; } = new();
        public List<AzureStep> Steps { get; init; } = new();
    }

    private record AzureJobDefinition
    {
        public string Job { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        public string? Condition { get; init; }
        public Dictionary<string, string>? Variables { get; init; }
        public List<AzureStep> Steps { get; init; } = new();
    }

    private record AzureStep
    {
        public string? Task { get; init; }
        public string? Script { get; init; }
        public string? DisplayName { get; init; }
        public string? Name { get; init; } // Used for identification
        public string? Condition { get; init; }
        public string? WorkingDirectory { get; init; }
    }
    #endregion
}