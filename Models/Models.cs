using System;
using System.Collections.Generic;

namespace x3squaredcircles.API.Assembler.Models
{
    /// <summary>
    /// Defines the standardized exit codes for the 3SC API Assembler application.
    /// </summary>
    public enum AssemblerExitCode
    {
        Success = 0,
        InvalidConfiguration = 1,
        LicenseUnavailable = 2,
        AssemblyScanFailure = 5,
        GenerationFailure = 7,
        DeploymentFailure = 8,
        ArtifactVerificationFailure = 13,
        ManifestGenerationFailure = 14,
        UnhandledException = 99
    }

    /// <summary>
    /// Custom exception class for the API Assembler.
    /// </summary>
    public class AssemblerException : Exception
    {
        public AssemblerExitCode ExitCode { get; }

        public AssemblerException(AssemblerExitCode exitCode, string message)
            : base(message)
        {
            ExitCode = exitCode;
        }

        public AssemblerException(AssemblerExitCode exitCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ExitCode = exitCode;
        }
    }

    /// <summary>
    /// Represents a single, complete, and buildable generated project for a deployment group.
    /// </summary>
    public class GeneratedProject
    {
        public string GroupName { get; set; }
        public string OutputPath { get; set; }
        public string Language { get; set; }
        public List<string> SourceFiles { get; set; } = new List<string>();
        public string ProjectFile { get; set; } // e.g., path to .csproj
    }

    /// <summary>
    /// Represents the result of the tag template resolution process.
    /// </summary>
    public class TagTemplateResult
    {
        public string Template { get; set; } = string.Empty;
        public string GeneratedTag { get; set; } = string.Empty;
        public Dictionary<string, string> TokenValues { get; set; } = new Dictionary<string, string>();
        public DateTime GenerationTime { get; set; }
    }
}