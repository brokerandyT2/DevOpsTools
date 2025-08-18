namespace x3squaredcircles.MobileAdapter.Generator.Configuration
{
    /// <summary>
    /// Root model for all application configuration, populated from environment variables.
    /// Acts as a pure data container for settings.
    /// </summary>
    public class GeneratorConfiguration
    {
        // Language & Platform Selection
        public bool LanguageCSharp { get; set; }
        public bool LanguageJava { get; set; }
        public bool LanguageKotlin { get; set; }
        public bool LanguageJavaScript { get; set; }
        public bool LanguageTypeScript { get; set; }
        public bool LanguagePython { get; set; }
        public bool PlatformAndroid { get; set; }
        public bool PlatformIOS { get; set; }

        // Core & Authentication
        public string RepoUrl { get; set; }
        public string Branch { get; set; }
        public string PatToken { get; set; }
        public string PatSecretName { get; set; }

        // Licensing
        public string LicenseServer { get; set; }
        public string ToolName { get; set; }
        public int LicenseTimeout { get; set; }
        public int LicenseRetryInterval { get; set; }

        // Discovery
        public string TrackAttribute { get; set; }
        public string TrackPattern { get; set; }
        public string TrackNamespace { get; set; }
        public string TrackFilePattern { get; set; }

        // Operation
        public OperationMode Mode { get; set; }
        public bool DryRun { get; set; }
        public bool ValidateOnly { get; set; }
        public bool OverwriteExisting { get; set; }
        public bool PreserveCustomCode { get; set; }
        public bool GenerateTests { get; set; }
        public bool IncludeDocumentation { get; set; }

        // Tagging & Logging
        public string TagTemplate { get; set; }
        public bool Verbose { get; set; }
        public Microsoft.Extensions.Logging.LogLevel LogLevel { get; set; } // Using the standard LogLevel

        // Environment Context
        public string Environment { get; set; }
        public string Vertical { get; set; }

        // Nested Configuration Objects
        public VaultConfiguration Vault { get; set; } = new VaultConfiguration();
        public AssemblyConfiguration Assembly { get; set; } = new AssemblyConfiguration();
        public SourceConfiguration Source { get; set; } = new SourceConfiguration();
        public OutputConfiguration Output { get; set; } = new OutputConfiguration();
        public CodeGenerationConfiguration CodeGeneration { get; set; } = new CodeGenerationConfiguration();
        public TypeMappingConfiguration TypeMapping { get; set; } = new TypeMappingConfiguration();

        // Helper methods to interpret the boolean flags
        public SourceLanguage GetSelectedLanguage()
        {
            if (LanguageCSharp) return SourceLanguage.CSharp;
            if (LanguageJava) return SourceLanguage.Java;
            if (LanguageKotlin) return SourceLanguage.Kotlin;
            if (LanguageJavaScript) return SourceLanguage.JavaScript;
            if (LanguageTypeScript) return SourceLanguage.TypeScript;
            if (LanguagePython) return SourceLanguage.Python;
            return SourceLanguage.None;
        }

        public TargetPlatform GetSelectedPlatform()
        {
            if (PlatformAndroid) return TargetPlatform.Android;
            if (PlatformIOS) return TargetPlatform.iOS;
            return TargetPlatform.None;
        }
    }

    #region Nested Configuration Models

    public class VaultConfiguration
    {
        public VaultType Type { get; set; }
        public string Url { get; set; }
        public string AzureClientId { get; set; }
        public string AzureClientSecret { get; set; }
        public string AzureTenantId { get; set; }
        public string AwsRegion { get; set; }
        public string AwsAccessKeyId { get; set; }
        public string AwsSecretAccessKey { get; set; }
        public string HashiCorpToken { get; set; }
    }

    public class AssemblyConfiguration
    {
        public string CoreAssemblyPath { get; set; }
        public string TargetAssemblyPath { get; set; }
        public string SearchFolders { get; set; }
        public string AssemblyPattern { get; set; }
    }

    public class SourceConfiguration
    {
        public string SourcePaths { get; set; }
        public string ClassPath { get; set; }
        public string PackagePattern { get; set; }
        public string NodeModulesPath { get; set; }
        public string TypeScriptConfig { get; set; }
        public string PythonPaths { get; set; }
        public string VirtualEnvPath { get; set; }
        public string RequirementsFile { get; set; }
    }

    public class OutputConfiguration
    {
        public string OutputDir { get; set; }
        public string AndroidOutputDir { get; set; }
        public string IosOutputDir { get; set; }
        public string AndroidPackageName { get; set; }
        public string IosModuleName { get; set; }
        public bool GenerateManifest { get; set; }
    }

    public class CodeGenerationConfiguration
    {
        public AndroidGenerationOptions Android { get; set; } = new AndroidGenerationOptions();
        public IosGenerationOptions Ios { get; set; } = new IosGenerationOptions();
    }

    public class AndroidGenerationOptions
    {
        public bool UseCoroutines { get; set; }
        public bool UseStateFlow { get; set; }
        public int TargetApi { get; set; }
        public string KotlinVersion { get; set; }
    }

    public class IosGenerationOptions
    {
        public bool UseCombine { get; set; }
        public bool UseAsyncAwait { get; set; }
        public string TargetVersion { get; set; }
        public string SwiftVersion { get; set; }
    }

    public class TypeMappingConfiguration
    {
        public string CustomTypeMappings { get; set; }
        public bool PreserveNullableTypes { get; set; }
        public bool UsePlatformCollections { get; set; }
    }

    #endregion

    #region Enums

    public enum SourceLanguage { None, CSharp, Java, Kotlin, JavaScript, TypeScript, Python, Go }
    public enum TargetPlatform { None, Android, iOS }
    public enum OperationMode { Analyze, Generate, Validate }
    public enum VaultType { None, Azure, Aws, HashiCorp }

    // The custom LogLevel enum has been removed to prevent ambiguity with Microsoft.Extensions.Logging.LogLevel

    #endregion
}