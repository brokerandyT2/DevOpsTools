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
    public class PythonAnalyzerService : ILanguageAnalyzerService
    {
        private readonly IAppLogger _logger;
        private const string PythonAnalyzerName = "python-analyzer.py";

        public PythonAnalyzerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory)
        {
            _logger.LogStartPhase("Python Source Code Analysis");

            var analyzerScriptPath = await ExtractEmbeddedScriptAsync();
            var pythonExecutable = FindPythonExecutable();

            if (string.IsNullOrEmpty(pythonExecutable))
            {
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "PYTHON_NOT_FOUND", "Could not find 'python' or 'python3' executable on the system PATH.");
            }

            var pyFiles = Directory.GetFiles(sourceDirectory, "*.py", SearchOption.AllDirectories);
            if (!pyFiles.Any())
            {
                _logger.LogWarning("No Python source files (*.py) found. Analysis cannot proceed.");
                _logger.LogEndPhase("Python Source Code Analysis", true);
                return new List<ServiceBlueprint>();
            }

            // Shell out to the Python analyzer script, passing the source directory.
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExecutable,
                    Arguments = $"\"{analyzerScriptPath}\" \"{sourceDirectory}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _logger.LogDebug($"Executing Python analyzer: {pythonExecutable} {process.StartInfo.Arguments}");
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError($"Python analyzer failed with exit code {process.ExitCode}.");
                _logger.LogError($"---> Stderr: {error}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "PYTHON_ANALYSIS_FAILED", "The Python source code analysis subprocess failed.");
            }

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var blueprints = JsonSerializer.Deserialize<List<ServiceBlueprint>>(output, options) ?? new List<ServiceBlueprint>();

                if (!blueprints.Any())
                {
                    _logger.LogWarning("Python analysis complete, but no classes decorated with @function_handler were found.");
                }
                else
                {
                    _logger.LogInfo($"✓ Python analysis complete. Found {blueprints.Count} service(s) to generate.");
                }

                _logger.LogEndPhase("Python Source Code Analysis", true);
                return blueprints;
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Failed to deserialize JSON output from Python analyzer: {ex.Message}");
                _logger.LogDebug($"---> Raw output: {output}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "PYTHON_JSON_DESERIALIZATION_FAILED", "Could not parse the analysis results from the Python subprocess.");
            }
        }

        private string FindPythonExecutable()
        {
            // Prefer 'python3' if available, otherwise fall back to 'python'.
            // This is a common strategy for handling different OS environments.
            var path = Environment.GetEnvironmentVariable("PATH");
            var pathDirs = path?.Split(Path.PathSeparator) ?? Array.Empty<string>();

            var executables = new[] { "python3", "python" };
            foreach (var exe in executables)
            {
                foreach (var dir in pathDirs)
                {
                    var fullPath = Path.Combine(dir, exe);
                    if (File.Exists(fullPath) || File.Exists(fullPath + ".exe"))
                    {
                        return exe; // Return the command, not the full path
                    }
                }
            }
            return string.Empty;
        }

        private async Task<string> ExtractEmbeddedScriptAsync()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), PythonAnalyzerName);

            // For simplicity in a container, we can extract every time.
            _logger.LogDebug($"Extracting embedded Python analyzer to '{tempPath}'");
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"x3squaredcircles.datalink.container.Assets.{PythonAnalyzerName}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "EMBEDDED_PY_NOT_FOUND", $"The required analysis tool '{PythonAnalyzerName}' was not found as an embedded resource.");
            }

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            await File.WriteAllTextAsync(tempPath, content);

            return tempPath;
        }
    }
}