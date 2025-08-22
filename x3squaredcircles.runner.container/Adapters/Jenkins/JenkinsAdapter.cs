using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.runner.container.Engine;
using X3SquaredCircles.Runner.Container.Engine;

namespace x3squaredcircles.runner.container.Adapters.Jenkins;

/// <summary>
/// An implementation of <see cref="IPlatformAdapter"/> for Jenkins declarative pipelines.
/// </summary>
public class JenkinsAdapter : IPlatformAdapter
{
    private readonly ILogger<JenkinsAdapter> _logger;
    private const string FileName = "Jenkinsfile";

    public string PlatformId => "jenkins";

    public JenkinsAdapter(ILogger<JenkinsAdapter> logger)
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
            throw new FileNotFoundException($"No Jenkinsfile found in the project root.");
        }

        _logger.LogDebug("Parsing Jenkinsfile using the declarative linter for model extraction.");

        var jsonOutput = await GetJenkinsModelAsJsonAsync(pipelinePath);

        if (string.IsNullOrWhiteSpace(jsonOutput))
        {
            throw new InvalidOperationException("Failed to generate a model from the Jenkinsfile. It may have syntax errors or be a scripted (non-declarative) pipeline.");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var jenkinsModel = JsonSerializer.Deserialize<JenkinsDeclarativeModel>(jsonOutput, options);

        if (jenkinsModel is null)
        {
            throw new JsonException("Failed to deserialize the Jenkins pipeline model from the linter's JSON output.");
        }

        return TransformToBlueprint(jenkinsModel);
    }

    private async Task<string> GetJenkinsModelAsJsonAsync(string jenkinsfilePath)
    {
        // This command uses the Jenkins Declarative Linter tool, which must be installed in the container.
        // It's a self-contained JAR that can parse a Jenkinsfile and output its structure as JSON.
        var command = $"java -jar /usr/share/jenkins/jenkins-declarative-linter.jar -j \"{jenkinsfilePath}\"";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var outputBuilder = new System.Text.StringBuilder();
        process.OutputDataReceived += (sender, args) => outputBuilder.AppendLine(args.Data);

        var errorBuilder = new System.Text.StringBuilder();
        process.ErrorDataReceived += (sender, args) => errorBuilder.AppendLine(args.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("The Jenkins declarative linter failed with exit code {ExitCode}. Errors: {Errors}", process.ExitCode, errorBuilder.ToString());
            return string.Empty;
        }

        return outputBuilder.ToString();
    }

    public bool EvaluateCondition(string? condition, IExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        _logger.LogWarning("Jenkins 'when' directive evaluation is not yet fully implemented. Condition '{Condition}' will be treated as TRUE. Step will run.", condition);
        return true;
    }

    private UniversalBlueprint TransformToBlueprint(JenkinsDeclarativeModel jenkinsModel)
    {
        var jobs = new List<Job>();

        // A Jenkinsfile represents a single pipeline, which we map to a single job in our model.
        var steps = new List<Step>();
        var stageIndex = 0;
        foreach (var stage in jenkinsModel.Pipeline.Stages)
        {
            var stageCommands = new List<string>();

            // Jenkins stages contain steps, but for simplicity, we'll collect all commands into one step per stage.
            foreach (var jenkinsStep in stage.Steps)
            {
                if (!string.IsNullOrWhiteSpace(jenkinsStep.Sh))
                {
                    stageCommands.AddRange(jenkinsStep.Sh.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)));
                }
            }

            var step = new Step(
                Id: $"stage-{stage.Name.Replace(" ", "-").ToLowerInvariant()}",
                DisplayName: stage.Name,
                RunCondition: stage.When?.ToString(), // A simplified representation
                WorkingDirectory: null,
                Task: new TaskDefinition("shell", stageCommands)
            );
            steps.Add(step);
            stageIndex++;
        }

        var job = new Job(
            Id: "jenkins-pipeline",
            DisplayName: "Jenkins Pipeline",
            RunCondition: null,
            Environment: new Dictionary<string, string>(), // Environment is typically defined at the top level in Jenkins
            Steps: steps
        );
        jobs.Add(job);

        return new UniversalBlueprint("1.0.0", PlatformId, jobs);
    }

    #region Private Deserialization Models
    private record JenkinsDeclarativeModel { public JenkinsPipeline Pipeline { get; init; } = new(); }
    private record JenkinsPipeline { public List<JenkinsStage> Stages { get; init; } = new(); }
    private record JenkinsStage { public string Name { get; init; } = string.Empty; public List<JenkinsStep> Steps { get; init; } = new(); public object? When { get; init; } }
    private record JenkinsStep { public string? Sh { get; init; } }
    #endregion
}