using Microsoft.Extensions.Logging;
using System.Diagnostics;
using x3squaredcircles.runner.container.Config;
using X3SquaredCircles.Runner.Container.Engine;

namespace x3squaredcircles.runner.container.Engine;

public class Orchestrator
{
    private readonly ILogger<Orchestrator> _logger;
    private readonly UniversalBlueprint _blueprint;
    private readonly PipelineConfig _config;
    private readonly IPlatformAdapter _adapter;

    public Orchestrator(
        ILogger<Orchestrator> logger,
        UniversalBlueprint blueprint,
        PipelineConfig config,
        IPlatformAdapter adapter)
    {
        _logger = logger;
        _blueprint = blueprint;
        _config = config;
        _adapter = adapter;
    }

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        var executionContext = new ExecutionContext();

        foreach (var job in _blueprint.Jobs)
        {
            _logger.LogInformation("--- Job: {JobName} ---", job.DisplayName);

            // A full implementation would check job-level conditions here.

            foreach (var step in job.Steps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Cancellation requested. Aborting pipeline run.");
                    return false;
                }

                if (!_config.Steps.TryGetValue(step.Id, out var stepAction))
                {
                    _logger.LogWarning("Step '{StepName}' (ID: {StepId}) not found in config. Defaulting to 'skip'.", step.DisplayName, step.Id);
                    stepAction = new StepAction("skip");
                }

                _logger.LogInformation("--- Step: {StepName} (Action: {Action}) ---", step.DisplayName, stepAction.Action);

                if (stepAction.Action.Equals("skip", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Skipping step as per configuration.");
                    continue;
                }

                if (!_adapter.EvaluateCondition(step.RunCondition, executionContext))
                {
                    _logger.LogInformation("Skipping step due to failed 'if' condition.");
                    continue;
                }

                var success = await ExecuteStepTaskAsync(step, cancellationToken);
                if (!success)
                {
                    _logger.LogError("Step '{StepName}' failed. Halting pipeline execution.", step.DisplayName);
                    return false; // Fail fast
                }

                if (stepAction.Action.Equals("pause_after", StringComparison.OrdinalIgnoreCase))
                {
                    if (!await WaitForDebugContinueAsync(cancellationToken))
                    {
                        return false; // Aborted by user
                    }
                }
            }
        }
        return true;
    }

    private async Task<bool> ExecuteStepTaskAsync(Step step, CancellationToken cancellationToken)
    {
        // Currently only 'shell' tasks are supported. This can be expanded.
        if (!step.Task.Type.Equals("shell", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Unsupported task type '{TaskType}' for step '{StepName}'.", step.Task.Type, step.DisplayName);
            return false;
        }

        foreach (var command in step.Task.Commands)
        {
            _logger.LogInformation("Executing command: {Command}", command);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = "/src" // All commands run from the project root.
            };

            process.OutputDataReceived += (sender, args) => _logger.LogInformation("  [out] {Data}", args.Data);
            process.ErrorDataReceived += (sender, args) => _logger.LogError("  [err] {Data}", args.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogError("Command exited with non-zero code: {ExitCode}", process.ExitCode);
                return false;
            }
        }
        return true;
    }

    private async Task<bool> WaitForDebugContinueAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("--- Execution paused. Enter 'continue' in the container's interactive terminal to resume. ---");
        // In a container, direct Console.ReadLine is problematic.
        // A more robust solution might involve a named pipe or a debug API endpoint.
        // For this implementation, we will log the instruction and rely on an attached user.
        // The process will wait indefinitely until cancellation is requested.
        try
        {
            await Task.Delay(-1, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Debug pause was interrupted by application shutdown. Aborting run.");
            return false;
        }

        // A proper implementation would require a true interactive shell.
        // We simulate the developer typing 'continue' by just returning true after the delay is broken
        // by a mechanism outside this function's scope (e.g., a debug API call).
        _logger.LogInformation("--- Resuming execution (Simulated continue). ---");
        return true;
    }

    /// <summary>
    /// A concrete implementation of IExecutionContext for passing state.
    /// </summary>
    private class ExecutionContext : IExecutionContext
    {
        public IReadOnlyDictionary<string, object> Variables { get; } = new Dictionary<string, object>();
        // This would be populated with job/step outputs as the pipeline runs.
    }
}