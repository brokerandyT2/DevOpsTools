using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;
using x3squaredcircles.datalink.container.Weavers;

namespace x3squaredcircles.DataLink.Container.Weavers
{
    public abstract class GoWeaverBase : ILanguageWeaver
    {
        protected readonly IAppLogger _logger;
        protected readonly ServiceBlueprint _blueprint;

        protected GoWeaverBase(IAppLogger logger, ServiceBlueprint blueprint)
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

        #endregion

        #region Concrete Shared Logic

        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            _logger.LogInfo($"Assembling Go test harness project for {_blueprint.ServiceName}...");
            var testsPath = Path.Combine(testProjectPath, "tests");
            Directory.CreateDirectory(testsPath);

            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var testFilePattern = $"*{ToSnakeCase(handlerClassNameShort)}_test.go";

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

        protected string ToSnakeCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            return string.Concat(input.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToLower();
        }
        protected string ToCamelCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            return char.ToLowerInvariant(input[0]) + input.Substring(1);
        }
        protected void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            Directory.CreateDirectory(destinationDir);
            foreach (FileInfo file in dir.GetFiles())
            {
                file.CopyTo(Path.Combine(destinationDir, file.Name), true);
            }
            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                if (subDir.Name != "tests" && subDir.Name != ".git")
                {
                    CopyDirectory(subDir.FullName, Path.Combine(destinationDir, subDir.Name));
                }
            }
        }

        #endregion
    }
}