using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.runner.container.Engine;
using X3SquaredCircles.Runner.Container.Engine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace x3squaredcircles.runner.container.Adapters.GitLab;

/// <summary>
/// An implementation of <see cref="IPlatformAdapter"/> for GitLab CI/CD pipelines.
/// </summary>
public class GitLabAdapter : IPlatformAdapter
{
    private readonly ILogger<GitLabAdapter> _logger;
    private const string FileName = ".gitlab-ci.yml";

    public string PlatformId => "gitlab";

    public GitLabAdapter(ILogger<GitLabAdapter> logger)
    {
        _logger = logger;
    }

    public Task<bool> CanHandleAsync(string projectRootPath)
    {
        var pipelinePath = Path.Combine(projectRootPath, FileName);
        var result = File.Exists(pipelinePath);
        return Task.FromResult(result);
    }

    public async Task<UniversalBlueprint> ParseAsync(string projectRootPath)
    {
        var pipelinePath = Path.Combine(projectRootPath, FileName);
        if (!File.Exists(pipelinePath))
        {
            throw new FileNotFoundException($"No GitLab CI/CD file ({FileName}) found in the project root.");
        }

        _logger.LogDebug("Parsing GitLab CI/CD file: {PipelineFile}", pipelinePath);

        var yamlContent = await File.ReadAllTextAsync(pipelinePath);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties() // GitLab has many top-level keys we don't need (e.g., 'stages', 'image')
            .Build();

        // GitLab YAML is a Dictionary of jobs, not a structured object.
        var gitlabJobs = deserializer.Deserialize<Dictionary<string, GitLabJob>>(yamlContent);
        return TransformToBlueprint(gitlabJobs);
    }

    public bool EvaluateCondition(string? condition, IExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        _logger.LogWarning("Complex 'rules' and 'only/except' condition evaluation is not yet fully implemented for GitLab. Condition '{Condition}' will be treated as TRUE. Step will run.", condition);
        // GitLab 'rules' can be very complex. Defaulting to true ensures a safe, predictable run.
        return true;
    }

    private UniversalBlueprint TransformToBlueprint(Dictionary<string, GitLabJob> gitlabJobs)
    {
        var jobs = new List<Job>();

        foreach (var jobEntry in gitlabJobs)
        {
            // Ignore special, non-job keys in GitLab YAML.
            if (jobEntry.Key.StartsWith('.') || IsReservedKeyword(jobEntry.Key)) continue;

            var gitlabJob = jobEntry.Value;

            // GitLab jobs are essentially a single step with a multi-line script.
            var commands = new List<string>();
            if (gitlabJob.Script is not null)
            {
                commands.AddRange(gitlabJob.Script);
            }

            var step = new Step(
                Id: $"{jobEntry.Key}-script",
                DisplayName: "Script",
                RunCondition: null, // GitLab conditions are job-level.
                WorkingDirectory: null,
                Task: new TaskDefinition("shell", commands)
            );

            // In our model, a GitLab job maps to a Universal Blueprint job with a single step.
            var job = new Job(
                Id: jobEntry.Key,
                DisplayName: jobEntry.Key,
                RunCondition: gitlabJob.Rules?.FirstOrDefault()?.If, // Simplification: take the first 'if' rule.
                Environment: gitlabJob.Variables ?? new Dictionary<string, string>(),
                Steps: new List<Step> { step }
            );
            jobs.Add(job);
        }

        return new UniversalBlueprint("1.0.0", PlatformId, jobs);
    }

    private bool IsReservedKeyword(string key)
    {
        var reserved = new HashSet<string> { "stages", "variables", "include", "default", "workflow" };
        return reserved.Contains(key);
    }

    #region Private Deserialization Models
    private record GitLabJob
    {
        public string? Stage { get; init; }
        public List<string>? Script { get; init; }
        public Dictionary<string, string>? Variables { get; init; }
        public List<GitLabRule>? Rules { get; init; }
    }

    private record GitLabRule
    {
        public string? If { get; init; }
        public string? When { get; init; }
    }
    #endregion
}