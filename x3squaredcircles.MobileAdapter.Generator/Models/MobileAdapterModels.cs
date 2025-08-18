using System;
using System.Collections.Generic;
using x3squaredcircles.MobileAdapter.Generator.Discovery;
using x3squaredcircles.MobileAdapter.Generator.TypeMapping;

namespace x3squaredcircles.MobileAdapter.Generator.Models
{
    /// <summary>
    /// Defines the exit codes for the Mobile Adapter Generator application.
    /// </summary>
    public enum MobileAdapterExitCode
    {
        Success = 0,
        InvalidConfiguration = 1,
        UnhandledException = 2,
        LicenseValidationFailure = 3,
        LicenseExpired = 4,
        LicenseUnavailable = 5,
        DiscoveryFailure = 6,
        TypeMappingFailure = 7,
        GenerationFailure = 8,
        FileWriteFailure = 9
    }

    /// <summary>
    /// Custom exception class for the Mobile Adapter Generator.
    /// </summary>
    public class MobileAdapterException : Exception
    {
        public MobileAdapterExitCode ExitCode { get; }

        public MobileAdapterException(MobileAdapterExitCode exitCode, string message)
            : base(message)
        {
            ExitCode = exitCode;
        }

        public MobileAdapterException(MobileAdapterExitCode exitCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ExitCode = exitCode;
        }
    }

    /// <summary>
    /// Represents the complete result of an adapter generation process.
    /// </summary>
    public class GenerationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public MobileAdapterExitCode ExitCode { get; set; }
        public List<DiscoveredClass> DiscoveredClasses { get; set; } = new List<DiscoveredClass>();
        public Dictionary<string, TypeMappingInfo> TypeMappings { get; set; } = new Dictionary<string, TypeMappingInfo>();
        public List<string> GeneratedFiles { get; set; } = new List<string>();
    }

    /// <summary>
    /// Contains detailed information about a single type mapping from a source language to a target platform.
    /// </summary>
    public class TypeMappingInfo
    {
        public string SourceType { get; set; }
        public string TargetType { get; set; }
        public string TargetPlatform { get; set; }
        public bool IsNullable { get; set; }
        public bool IsCollection { get; set; }
        public TypeMappingSource MappingSource { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Indicates the origin of a specific type mapping.
    /// </summary>
    public enum TypeMappingSource
    {
        Unknown,
        Custom,
        BuiltIn,
        Generic,
        Fallback
    }
}