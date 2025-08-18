using System;
using System.Collections.Generic;

namespace x3squaredcircles.datalink.container.Models
{
    #region Core Application Models

    /// <summary>
    /// Defines the standard, deterministic exit codes for the DataLink application,
    /// enabling reliable CI/CD pipeline integration and error handling.
    /// </summary>
    public enum ExitCode
    {
        Success = 0,
        TestHarnessFailed = 1,
        InvalidConfiguration = 10,
        GitOperationFailed = 11,
        SourceAnalysisFailed = 12,
        CodeGenerationFailed = 13,
        UnhandledException = 99
    }

    /// <summary>
    /// Custom exception for handling controlled, expected errors within the application.
    /// It carries a specific ExitCode and a machine-readable ErrorCode for downstream processing.
    /// </summary>
    public class DataLinkException : Exception
    {
        public ExitCode ExitCode { get; }
        public string ErrorCode { get; }

        public DataLinkException(ExitCode exitCode, string errorCode, string message) : base(message)
        {
            ExitCode = exitCode;
            ErrorCode = errorCode;
        }

        public DataLinkException(ExitCode exitCode, string errorCode, string message, Exception innerException) : base(message, innerException)
        {
            ExitCode = exitCode;
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// A strongly-typed representation of all configuration parameters provided via environment variables.
    /// This object is the single source of truth for the application's runtime configuration.
    /// </summary>
    public class DataLinkConfiguration
    {
        // Repository Configuration
        public string BusinessLogicRepo { get; set; } = string.Empty;
        public string? TestHarnessRepo { get; set; }
        public string DestinationRepo { get; set; } = string.Empty;
        public string DestinationRepoPat { get; set; } = string.Empty;

        // Versioning and Tagging
        public string VersionTagPattern { get; set; } = "v*";
        public string DiscoveredVersionTag { get; set; } = string.Empty;

        // Logging
        public bool Verbose { get; set; }
        public string LogLevel { get; set; } = "INFO";

        // Operational Overrides & Flags
        public bool ContinueOnTestFailure { get; set; }
        public bool GenerateTestHarness { get; set; } = true; // Default to generating tests
    }

    #endregion

    #region Intermediate Representation (IR) Models

    /// <summary>
    /// Represents the complete, language-agnostic blueprint of a service to be generated.
    /// This is the output of the analysis stage and the input to the code weaving stage.
    /// </summary>
    public class ServiceBlueprint
    {
        public string ServiceName { get; set; } = string.Empty;
        public string HandlerClassFullName { get; set; } = string.Empty; // e.g., "MyCompany.Handlers.OrderProcessor"
        public List<TriggerMethod> TriggerMethods { get; set; } = new();
        public GenerationMetadata Metadata { get; set; } = new();
    }

    /// <summary>
    /// Represents a single method to be exposed as an event-driven function.
    /// </summary>
    public class TriggerMethod
    {
        public string MethodName { get; set; } = string.Empty;
        public string HandlerClassFullName { get; set; } = string.Empty;
        public List<TriggerDefinition> Triggers { get; set; } = new();
        public List<HookDefinition> RequiredHooks { get; set; } = new();
        public List<ParameterDefinition> Parameters { get; set; } = new();
        public string ReturnType { get; set; } = "void";
    }

    /// <summary>
    /// Defines the event source that will invoke a method.
    /// </summary>
    public class TriggerDefinition
    {
        public string Type { get; set; } = string.Empty; // e.g., "Http", "AwsSqsQueue"
        public string Name { get; set; } = string.Empty; // e.g., "/orders", "new-orders-queue"
        public Dictionary<string, string> Properties { get; set; } = new(); // e.g., "Method: GET", "Filter: my-subscription"
    }

    /// <summary>
    /// Defines a cross-cutting concern (like auth or logging) to be woven into the execution pipeline.
    /// </summary>
    public class HookDefinition
    {
        public string HookType { get; set; } = string.Empty; // "Requires", "RequiresLogger", or "RequiresResultsLogger"
        public string HandlerClassFullName { get; set; } = string.Empty;
        public string HandlerMethodName { get; set; } = string.Empty;
        public string? LogAction { get; set; } // Only for RequiresLogger, e.g., "OnInbound"
        public string? TraceVariableName { get; set; } // Only for RequiresResultsLogger
    }

    /// <summary>
    /// Defines a parameter for a trigger method, used for dependency injection and payload binding.
    /// </summary>
    public class ParameterDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string TypeFullName { get; set; } = string.Empty;
        public bool IsPayload { get; set; }
    }

    /// <summary>
    /// Contains forensic metadata about the generation process, providing an auditable link
    /// between the generated shim and its source.
    /// </summary>
    public class GenerationMetadata
    {
        public string SourceRepo { get; set; } = string.Empty;
        public string SourceVersionTag { get; set; } = string.Empty;
        public DateTime GenerationTimestampUtc { get; set; }
        public string ToolVersion { get; set; } = string.Empty;
    }

    #endregion
}