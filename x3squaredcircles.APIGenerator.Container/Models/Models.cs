using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace x3squaredcircles.datalink.container.Models
{
    #region Core Application Models

    /// <summary>
    /// Defines the standard, deterministic exit codes for the application,
    /// enabling reliable CI/CD pipeline integration and error handling.
    /// </summary>
    public enum ExitCode
    {
        Success = 0,
        TestHarnessFailed = 1,

        // NEW: Dedicated exit code for the "list variables and exit" business rule.
        // This allows a pipeline to specifically identify this outcome.
        VariablesDiscovered = 2,

        InvalidConfiguration = 10,
        GitOperationFailed = 11,
        SourceAnalysisFailed = 12,
        CodeGenerationFailed = 13,
        BuildFailed = 20,
        DeployFailed = 21,
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
        public bool GenerateTestHarness { get; set; } = true;

        // NEW: Property to hold the state of the "list variables and exit" flag.
        public bool ListVariablesAndExit { get; set; }

        // Target platform configuration
        public string TargetLanguage { get; set; } = string.Empty;
        public string CloudProvider { get; set; } = string.Empty;
        public string DeploymentPattern { get; set; } = string.Empty;
        public string OutputPath { get; set; } = "./output";
        // NEW: Properties for Control Point configuration
        public string? ControlPointOnStartupUrl { get; set; }
        public string? ControlPointOnSuccessUrl { get; set; }
        public string? ControlPointOnFailureUrl { get; set; }
        public int ControlPointTimeoutSeconds { get; set; } = 60;
        public string ControlPointTimeoutAction { get; set; } = "fail"; // "fail" or "continue"
        public string? ControlPointDeploymentOverrideUrl { get; set; }
        public string? LogEndpointUrl { get; set; }
        public string? LogEndpointToken { get; set; }
    }

    #endregion

    #region Intermediate Representation (IR) Models for Signature-Driven Generation

    /// <summary>
    /// Represents the complete, language-agnostic blueprint of a service to be generated.
    /// </summary>
    public class ServiceBlueprint
    {
        public string ServiceName { get; set; } = string.Empty;
        public string HandlerClassFullName { get; set; } = string.Empty;
        public List<TriggerMethod> TriggerMethods { get; set; } = new();
        public GenerationMetadata Metadata { get; set; } = new();
    }

    /// <summary>
    /// Represents a single method to be exposed as an event-driven function,
    /// capturing the complete signature details needed for code generation.
    /// </summary>
    public class TriggerMethod
    {
        public string MethodName { get; set; } = string.Empty;
        public string ReturnType { get; set; } = "void";

        // Captures attributes applied directly to the method, e.g., [Function("MyFunction")]
        public List<AttributeDefinition> Attributes { get; set; } = new();

        // Captures the full list of parameters in the method's signature.
        public List<ParameterDefinition> Parameters { get; set; } = new();

        // Captures the developer's original DSL attributes for weaving cross-cutting concerns.
        public List<DslAttributeDefinition> DslAttributes { get; set; } = new();
    }

    /// <summary>
    /// Represents a single, complete attribute discovered in the source code, preserving its exact syntax.
    /// e.g., [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "widgets")]
    /// </summary>
    public class AttributeDefinition
    {
        public string Name { get; set; } = string.Empty; // "HttpTrigger"
        public string FullSyntax { get; set; } = string.Empty; // The verbatim string of the attribute
    }

    /// <summary>
    /// Defines a single parameter for a trigger method, including its own attributes.
    /// e.g., "[FromBody] Widget newWidget"
    /// </summary>
    public class ParameterDefinition
    {
        public string Name { get; set; } = string.Empty; // "newWidget"
        public string TypeFullName { get; set; } = string.Empty; // "MyCompany.Models.Widget"
        public List<AttributeDefinition> Attributes { get; set; } = new(); // e.g., [FromBody]

        /// <summary>
        /// Flag indicating if this parameter is a service to be injected via DI,
        /// rather than an input from the function trigger itself.
        /// </summary>
        public bool IsBusinessLogicDependency { get; set; }
    }

    /// <summary>
    /// Represents a parsed instance of one of the 3SC Assembler's DSL attributes
    /// like [Requires] or [EventSource], used for weaving and analysis.
    /// </summary>
    public class DslAttributeDefinition
    {
        public string Name { get; set; } = string.Empty; // e.g., "EventSource", "Requires"
        public Dictionary<string, string> Arguments { get; set; } = new();
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