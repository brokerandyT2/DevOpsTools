namespace X3SquaredCircles.Runner.Container.Engine;

/// <summary>
/// Defines the immutable contract that every platform-specific adapter (e.g., GitHub, Azure) MUST implement.
/// The Core Engine interacts with this interface, remaining completely decoupled from the specific implementation details of any given CI/CD platform.
/// </summary>
public interface IPlatformAdapter
{
    /// <summary>
    /// Gets the unique, machine-readable identifier for the platform (e.g., "github", "azure").
    /// </summary>
    string PlatformId { get; }

    /// <summary>
    /// Asynchronously determines if this adapter can handle the project located at the specified root path.
    /// This is typically done by looking for a specific file (e.g., "Jenkinsfile", ".github/workflows/").
    /// </summary>
    /// <param name="projectRootPath">The absolute path to the project's root directory.</param>
    /// <returns>A task that resolves to true if the adapter recognizes the project; otherwise, false.</returns>
    Task<bool> CanHandleAsync(string projectRootPath);

    /// <summary>
    /// Asynchronously parses the native pipeline file(s) and transforms them into the Universal Blueprint.
    /// </summary>
    /// <param name="projectRootPath">The absolute path to the project's root directory.</param>
    /// <returns>A task that resolves to the platform-agnostic <see cref="UniversalBlueprint"/> representation of the pipeline.</returns>
    Task<UniversalBlueprint> ParseAsync(string projectRootPath);

    /// <summary>
    /// Evaluates a platform-specific condition string against the current execution context.
    /// </summary>
    /// <param name="condition">The native condition string (e.g., "${{ success() && github.ref == 'refs/heads/main' }}").</param>
    /// <param name="context">The current state of the pipeline run, including job status and variables.</param>
    /// <returns>True if the condition is met and the step should be executed; otherwise, false.</returns>
    bool EvaluateCondition(string? condition, IExecutionContext context);
}

/// <summary>
/// Represents the execution context at a specific point in a pipeline run, providing state for condition evaluation.
/// </summary>
public interface IExecutionContext
{
    /// <summary>
    /// Gets a dictionary of variables available at the current scope of the pipeline execution.
    /// </summary>
    IReadOnlyDictionary<string, object> Variables { get; }
}

/// <summary>
/// Represents the canonical, internal data model of any pipeline, translated from its native format.
/// This is the "lingua franca" of The Conductor.
/// </model>
/// <param name="Version">The version of the Blueprint schema.</param>
/// <param name="Platform">The originating platform identifier (e.g., "github").</param>
/// <param name="Jobs">An ordered list of jobs to be executed.</param>
public record UniversalBlueprint(
    string Version,
    string Platform,
    IReadOnlyList<Job> Jobs);

/// <summary>
/// Represents a collection of steps that run sequentially on the same runner.
/// </summary>
/// <param name="Id">The unique identifier for the job.</param>
/// <param name="DisplayName">The human-readable name of the job.</param>
/// <param name="RunCondition">The native condition string for the job's execution.</param>
/// <param name="Environment">A collection of environment variables to be set for all steps in this job.</param>
/// <param name="Steps">An ordered list of steps to be executed within this job.</param>
public record Job(
    string Id,
    string DisplayName,
    string? RunCondition,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<Step> Steps);

/// <summary>
/// Represents a single task or command within a job.
/// </summary>
/// <param name="Id">The unique identifier for the step.</param>
/// <param name="DisplayName">The human-readable name of the step.</param>
/// <param name="RunCondition">The native condition string for the step's execution.</param>
/// <param name="WorkingDirectory">The directory in which to execute the task, relative to the project root.</param>
/// <param name="Task">The specific action to be performed by this step.</param>
public record Step(
    string Id,
    string DisplayName,
    string? RunCondition,
    string? WorkingDirectory,
    TaskDefinition Task);

/// <summary>
/// Defines the specific command or action to be executed by a step.
/// </summary>
/// <param name="Type">The type of task to be executed (e.g., "shell").</param>
/// <param name="Commands">An ordered list of shell commands to execute.</param>
public record TaskDefinition(
    string Type,
    IReadOnlyList<string> Commands);