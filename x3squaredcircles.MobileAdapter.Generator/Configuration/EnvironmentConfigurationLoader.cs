using Microsoft.Extensions.Logging;
using System;

namespace x3squaredcircles.MobileAdapter.Generator.Configuration
{
    /// <summary>
    /// A static class responsible for loading all application configuration from environment variables.
    /// </summary>
    public static class EnvironmentConfigurationLoader
    {
        /// <summary>
        /// Loads and parses all known environment variables into a strongly-typed GeneratorConfiguration object.
        /// </summary>
        /// <returns>A populated GeneratorConfiguration instance.</returns>
        public static GeneratorConfiguration LoadConfiguration()
        {
            var config = new GeneratorConfiguration();

            // Language and Platform Selection
            config.LanguageCSharp = GetBool("LANGUAGE_CSHARP");
            config.LanguageJava = GetBool("LANGUAGE_JAVA");
            config.LanguageKotlin = GetBool("LANGUAGE_KOTLIN");
            config.LanguageJavaScript = GetBool("LANGUAGE_JAVASCRIPT");
            config.LanguageTypeScript = GetBool("LANGUAGE_TYPESCRIPT");
            config.LanguagePython = GetBool("LANGUAGE_PYTHON");
            config.PlatformAndroid = GetBool("PLATFORM_ANDROID");
            config.PlatformIOS = GetBool("PLATFORM_IOS");

            // Core and Authentication
            config.RepoUrl = GetString("REPO_URL");
            config.Branch = GetString("BRANCH");
            config.PatToken = GetString("PAT_TOKEN");
            config.PatSecretName = GetString("PAT_SECRET_NAME");

            // Licensing
            config.LicenseServer = GetString("LICENSE_SERVER");
            config.ToolName = GetString("TOOL_NAME", "mobile-adapter-generator");
            config.LicenseTimeout = GetInt("LICENSE_TIMEOUT", 300);
            config.LicenseRetryInterval = GetInt("LICENSE_RETRY_INTERVAL", 30);

            // Vault, Discovery, and Source Paths
            LoadVaultConfiguration(config);
            LoadDiscoveryConfiguration(config);
            LoadSourceConfiguration(config);
            LoadAssemblyConfiguration(config);

            // Output and Code Generation settings
            LoadOutputConfiguration(config);
            LoadCodeGenerationConfiguration(config);
            LoadTypeMappingConfiguration(config);

            // Operational Mode
            config.Mode = GetEnum<OperationMode>("MODE", OperationMode.Generate);
            config.DryRun = GetBool("DRY_RUN");
            config.ValidateOnly = GetBool("VALIDATE_ONLY");
            config.OverwriteExisting = GetBool("OVERWRITE_EXISTING", true);
            config.PreserveCustomCode = GetBool("PRESERVE_CUSTOM_CODE", true);
            config.GenerateTests = GetBool("GENERATE_TESTS");
            config.IncludeDocumentation = GetBool("INCLUDE_DOCUMENTATION", true);

            // Tagging and Logging
            config.TagTemplate = GetString("TAG_TEMPLATE", "{branch}/{repo}/adapters/{version}");
            config.Verbose = GetBool("VERBOSE");
            config.LogLevel = GetEnum<LogLevel>("LOG_LEVEL", LogLevel.Information);

            return config;
        }

        #region Private Loader Methods

        private static void LoadVaultConfiguration(GeneratorConfiguration config)
        {
            config.Vault.Type = GetEnum<VaultType>("VAULT_TYPE", VaultType.None);
            config.Vault.Url = GetString("VAULT_URL");
            config.Vault.AzureClientId = GetString("AZURE_CLIENT_ID");
            config.Vault.AzureClientSecret = GetString("AZURE_CLIENT_SECRET");
            config.Vault.AzureTenantId = GetString("AZURE_TENANT_ID");
            config.Vault.AwsRegion = GetString("AWS_REGION");
            config.Vault.AwsAccessKeyId = GetString("AWS_ACCESS_KEY_ID");
            config.Vault.AwsSecretAccessKey = GetString("AWS_SECRET_ACCESS_KEY");
            config.Vault.HashiCorpToken = GetString("VAULT_TOKEN");
        }

        private static void LoadDiscoveryConfiguration(GeneratorConfiguration config)
        {
            config.TrackAttribute = GetString("TRACK_ATTRIBUTE");
            config.TrackPattern = GetString("TRACK_PATTERN");
            config.TrackNamespace = GetString("TRACK_NAMESPACE");
            config.TrackFilePattern = GetString("TRACK_FILE_PATTERN");
        }

        private static void LoadAssemblyConfiguration(GeneratorConfiguration config)
        {
            config.Assembly.CoreAssemblyPath = GetString("CORE_ASSEMBLY_PATH");
            config.Assembly.TargetAssemblyPath = GetString("TARGET_ASSEMBLY_PATH");
            config.Assembly.SearchFolders = GetString("SEARCH_FOLDERS");
            config.Assembly.AssemblyPattern = GetString("ASSEMBLY_PATTERN");
        }

        private static void LoadSourceConfiguration(GeneratorConfiguration config)
        {
            config.Source.SourcePaths = GetString("SOURCE_PATHS");
            config.Source.ClassPath = GetString("CLASSPATH");
            config.Source.PackagePattern = GetString("PACKAGE_PATTERN");
            config.Source.NodeModulesPath = GetString("NODE_MODULES_PATH");
            config.Source.TypeScriptConfig = GetString("TYPESCRIPT_CONFIG");
            config.Source.PythonPaths = GetString("PYTHON_PATHS");
            config.Source.VirtualEnvPath = GetString("VIRTUAL_ENV_PATH");
            config.Source.RequirementsFile = GetString("REQUIREMENTS_FILE");
        }

        private static void LoadOutputConfiguration(GeneratorConfiguration config)
        {
            config.Output.OutputDir = GetString("OUTPUT_DIR", "Generated-Adapters");
            config.Output.AndroidOutputDir = GetString("ANDROID_OUTPUT_DIR", "android/kotlin/");
            config.Output.IosOutputDir = GetString("IOS_OUTPUT_DIR", "ios/swift/");
            config.Output.AndroidPackageName = GetString("ANDROID_PACKAGE_NAME");
            config.Output.IosModuleName = GetString("IOS_MODULE_NAME");
            config.Output.GenerateManifest = GetBool("GENERATE_MANIFEST", true);
        }

        private static void LoadCodeGenerationConfiguration(GeneratorConfiguration config)
        {
            config.CodeGeneration.Android.UseCoroutines = GetBool("ANDROID_USE_COROUTINES", true);
            config.CodeGeneration.Android.UseStateFlow = GetBool("ANDROID_USE_STATEFLOW", true);
            config.CodeGeneration.Android.TargetApi = GetInt("ANDROID_TARGET_API", 34);
            config.CodeGeneration.Android.KotlinVersion = GetString("ANDROID_KOTLIN_VERSION", "1.9");

            config.CodeGeneration.Ios.UseCombine = GetBool("IOS_USE_COMBINE", true);
            config.CodeGeneration.Ios.UseAsyncAwait = GetBool("IOS_USE_ASYNC_AWAIT", true);
            config.CodeGeneration.Ios.TargetVersion = GetString("IOS_TARGET_VERSION", "15.0");
            config.CodeGeneration.Ios.SwiftVersion = GetString("IOS_SWIFT_VERSION", "5.9");
        }

        private static void LoadTypeMappingConfiguration(GeneratorConfiguration config)
        {
            config.TypeMapping.CustomTypeMappings = GetString("CUSTOM_TYPE_MAPPINGS");
            config.TypeMapping.PreserveNullableTypes = GetBool("PRESERVE_NULLABLE_TYPES", true);
            config.TypeMapping.UsePlatformCollections = GetBool("USE_PLATFORM_COLLECTIONS", true);
        }

        #endregion

        #region Private Getters

        private static string GetString(string name, string defaultValue = null)
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        private static bool GetBool(string name, bool defaultValue = false)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) return defaultValue;
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(string name, int defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private static T GetEnum<T>(string name, T defaultValue) where T : struct, Enum
        {
            var value = Environment.GetEnvironmentVariable(name);
            return Enum.TryParse<T>(value, true, out var result) ? result : defaultValue;
        }

        #endregion
    }
}