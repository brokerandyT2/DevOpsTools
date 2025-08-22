using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;
using x3squaredcircles.datalink.container.Weavers;

namespace x3squaredcircles.DataLink.Container.Weavers
{
    public abstract class ScriptingWeaverBase : ILanguageWeaver
    {
        protected readonly IAppLogger _logger;
        protected readonly ServiceBlueprint _blueprint;

        protected ScriptingWeaverBase(IAppLogger logger, ServiceBlueprint blueprint)
        {
            _logger = logger;
            _blueprint = blueprint;
        }

        #region Abstract Methods (To be implemented by concrete weavers)

        public abstract Task GenerateProjectFileAsync(string projectPath, string logicSourcePath);
        public abstract Task GenerateStartupFileAsync(string projectPath);
        public abstract Task GeneratePlatformFilesAsync(string projectPath);
        public abstract Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath);
        protected abstract Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testPackagePath);
        protected abstract string GetDeveloperTestFilePattern(string handlerClassNameShort);
        protected abstract string GetHandlerFileName();

        #endregion

        #region Concrete Shared Logic

        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            _logger.LogInfo($"Assembling scripting test harness project for {_blueprint.ServiceName}...");
            var testsPath = Path.Combine(testProjectPath, "tests");
            Directory.CreateDirectory(testsPath);

            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var testFilePattern = GetDeveloperTestFilePattern(handlerClassNameShort);

            try
            {
                var relevantTestFiles = Directory.GetFiles(testSourcePath, testFilePattern, SearchOption.AllDirectories);
                foreach (var testFile in relevantTestFiles)
                {
                    var destinationFile = Path.Combine(testsPath, Path.GetFileName(testFile));
                    File.Copy(testFile, destinationFile, true);
                    _logger.LogDebug($"Copied developer business logic test file: {Path.GetFileName(testFile)}");
                }
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogWarning($"Test source path '{testSourcePath}' not found. Skipping copy of developer tests.");
            }

            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                await GenerateSingleTestHarnessFileAsync(triggerMethod, testsPath);
            }
        }

        #endregion

        #region Protected Helper Methods

        protected string ToCamelCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            return char.ToLowerInvariant(input[0]) + input.Substring(1);
        }

        protected string ToSnakeCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            return string.Concat(input.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToLower();
        }

        protected string GetHandlerPath(string functionName)
        {
            return $"{Path.GetFileNameWithoutExtension(GetHandlerFileName())}.{functionName}";
        }

        protected (string eventType, string eventProperties) ParseUrnForSam(string urn)
        {
            var parts = urn.Split(':');
            if (parts.Length < 4)
            {
                _logger.LogWarning($"Invalid URN format: '{urn}'. Defaulting to sample HTTP trigger.");
                return ("Api", "            Path: /invalid-urn\n            Method: get");
            }

            var service = parts[1].ToLowerInvariant();
            var resource = parts[2];
            var action = string.Join(":", parts.Skip(3));

            switch (service)
            {
                case "s3":
                    return ("S3", $"            Bucket: {resource}\n            Events: s3:{action}");
                case "sqs":
                    return ("SQS", $"            Queue: {resource}");
                case "apigateway":
                    return ("Api", $"            Path: {resource}\n            Method: {action.ToLowerInvariant()}");
                default:
                    _logger.LogWarning($"Unsupported AWS service in URN: '{service}'. Defaulting to sample HTTP trigger.");
                    return ("Api", $"            Path: /unsupported-service\n            Method: get");
            }
        }

        #endregion
    }
}