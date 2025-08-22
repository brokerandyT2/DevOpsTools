using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    public class JavaAnalyzerService : ILanguageAnalyzerService
    {
        private readonly IAppLogger _logger;
        private const string JarAnalyzerName = "java-analyzer.jar";

        public JavaAnalyzerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory)
        {
            _logger.LogStartPhase("Java Source Code Analysis");

            var analyzerJarPath = await ExtractEmbeddedJarAsync();
            var classPath = BuildClasspath(sourceDirectory);

            if (string.IsNullOrEmpty(classPath))
            {
                _logger.LogWarning("Could not find any .jar files in the source directory. Analysis may fail if dependencies are not on the classpath.");
            }

            var javaFiles = Directory.GetFiles(sourceDirectory, "*.java", SearchOption.AllDirectories);
            if (!javaFiles.Any())
            {
                _logger.LogWarning("No Java source files (*.java) found. Analysis cannot proceed.");
                _logger.LogEndPhase("Java Source Code Analysis", true);
                return new List<ServiceBlueprint>();
            }

            // Shell out to the Java analyzer JAR, passing the classpath and source files.
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = $"-jar \"{analyzerJarPath}\" --classpath \"{classPath}\" --source-path \"{sourceDirectory}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _logger.LogDebug($"Executing Java analyzer: java {process.StartInfo.Arguments}");
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError($"Java analyzer failed with exit code {process.ExitCode}.");
                _logger.LogError($"---> Stderr: {error}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "JAVA_ANALYSIS_FAILED", "The Java source code analysis subprocess failed.");
            }

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var blueprints = JsonSerializer.Deserialize<List<ServiceBlueprint>>(output, options) ?? new List<ServiceBlueprint>();

                if (!blueprints.Any())
                {
                    _logger.LogWarning("Java analysis complete, but no classes annotated with @FunctionHandler were found.");
                }
                else
                {
                    _logger.LogInfo($"✓ Java analysis complete. Found {blueprints.Count} service(s) to generate.");
                }

                _logger.LogEndPhase("Java Source Code Analysis", true);
                return blueprints;
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Failed to deserialize JSON output from Java analyzer: {ex.Message}");
                _logger.LogDebug($"---> Raw output: {output}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "JAVA_JSON_DESERIALIZATION_FAILED", "Could not parse the analysis results from the Java subprocess.");
            }
        }

        private string BuildClasspath(string sourceDirectory)
        {
            var jarFiles = Directory.GetFiles(sourceDirectory, "*.jar", SearchOption.AllDirectories);
            var separator = Path.PathSeparator;
            // Include the current directory for compiled .class files and all found JARs.
            return $".{separator}{string.Join(separator, jarFiles)}";
        }

        private async Task<string> ExtractEmbeddedJarAsync()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), JarAnalyzerName);
            if (File.Exists(tempPath))
            {
                _logger.LogDebug($"Using existing extracted Java analyzer at '{tempPath}'");
                return tempPath;
            }

            _logger.LogDebug($"Extracting embedded Java analyzer to '{tempPath}'");
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"x3squaredcircles.datalink.container.Assets.{JarAnalyzerName}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "EMBEDDED_JAR_NOT_FOUND", $"The required analysis tool '{JarAnalyzerName}' was not found as an embedded resource.");
            }

            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
            await stream.CopyToAsync(fileStream);

            return tempPath;
        }
    }
}