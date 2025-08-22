using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.runner.container.Engine;
using X3SquaredCircles.Runner.Container.Engine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace x3squaredcircles.runner.container.Adapters.GitHub;

/// <summary>
/// An implementation of <see cref="IPlatformAdapter"/> for GitHub Actions workflows.
/// </summary>
public class GitHubAdapter : IPlatformAdapter
{
    private readonly ILogger<GitHubAdapter> _logger;

    public string PlatformId => "github";

    public GitHubAdapter(ILogger<GitHubAdapter> logger)
    {
        _logger = logger;
    }

    public Task<bool> CanHandleAsync(string projectRootPath)
    {
        var workflowPath = Path.Combine(projectRootPath, ".github", "workflows");
        var result = Directory.Exists(workflowPath);
        return Task.FromResult(result);
    }

    public async Task<UniversalBlueprint> ParseAsync(string projectRootPath)
    {
        var workflowDirectory = Path.Combine(projectRootPath, ".github", "workflows");
        var workflowFile = Directory.EnumerateFiles(workflowDirectory)
            .FirstOrDefault(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

        if (workflowFile is null)
        {
            throw new FileNotFoundException("No GitHub Actions workflow file (.yml or .yaml) found in the .github/workflows directory.");
        }

        _logger.LogDebug("Parsing GitHub workflow file: {WorkflowFile}", workflowFile);

        var yamlContent = await File.ReadAllTextAsync(workflowFile);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var ghFile = deserializer.Deserialize<GitHubActionFile>(yamlContent);
        return TransformToBlueprint(ghFile);
    }

    public bool EvaluateCondition(string? condition, IExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            // A step with no 'if' condition always runs.
            return true;
        }

        var trimmedCondition = condition.Trim();
        if (bool.TryParse(trimmedCondition, out var result))
        {
            // Handles explicit 'if: true' or 'if: false'
            return result;
        }

        _logger.LogWarning("Complex condition evaluation is not yet fully implemented for GitHub Actions. Condition '{Condition}' will be treated as TRUE. Step will run.", condition);
        // For any complex expression like '${{...}}', we default to running the step.
        // This ensures a "fail-safe" behavior where steps are not unexpectedly skipped.
        return true;
    }

    private UniversalBlueprint TransformToBlueprint(GitHubActionFile ghFile)
    {
        var jobs = new List<Job>();
        foreach (var jobEntry in ghFile.Jobs)
        {
            var ghJob = jobEntry.Value;
            var steps = new List<Step>();
            var stepIndex = 0;

            foreach (var ghStep in ghJob.Steps)
            {
                var stepId = ghStep.Id ?? $"{jobEntry.Key}-step-{stepIndex}";
                var displayName = ghStep.Name ?? $"Unnamed Step {stepIndex}";
                var commands = ghStep.Run?.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();

                var step = new Step(
                    Id: stepId,
                    DisplayName: displayName,
                    RunCondition: ghStep.If,
                    WorkingDirectory: null, // GitHub steps inherit working directory, can be enhanced later.
                    Task: new TaskDefinition("shell", commands)
                );
                steps.Add(step);
                stepIndex++;
            }

            var job = new Job(
                Id: jobEntry.Key,
                DisplayName: ghJob.Name ?? jobEntry.Key,
                RunCondition: ghJob.If,
                Environment: ghJob.Env ?? new Dictionary<string, string>(),
                Steps: steps
            );
            jobs.Add(job);
        }

        return new UniversalBlueprint("1.0.0", PlatformId, jobs);
    }

    #region Private Deserialization Models
    // These models are used internally by YamlDotNet to deserialize the native GitHub Actions workflow file.

    private record GitHubActionFile
    {
        [YamlMember(Alias = "name")]
        public string? Name { get; init; }

        [YamlMember(Alias = "jobs")]
        public Dictionary<string, GitHubJob> Jobs { get; init; } = new();
    }

    private record GitHubJob
    {
        [YamlMember(Alias = "name")]
        public string? Name { get; init; }

        [YamlMember(Alias = "runs-on")]
        public string? RunsOn { get; init; }

        [YamlMember(Alias = "if")]
        public string? If { get; init; }

        [YamlMember(Alias = "env")]
        public Dictionary<string, string>? Env { get; init; }

        [YamlMember(Alias = "steps")]
        public List<GitHubStep> Steps { get; init; } = new();
    }

    private record GitHubStep
    {
        [YamlMember(Alias = "id")]
        public string? Id { get; init; }

        [YamlMember(Alias = "name")]
        public string? Name { get; init; }

        [YamlMember(Alias = "if")]
        public string? If { get; init; }

        [YamlMember(Alias = "run")]
        public string? Run { get; init; }
    }
    #endregion
}