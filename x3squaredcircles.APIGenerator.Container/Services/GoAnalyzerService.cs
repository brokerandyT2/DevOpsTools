using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    public class GoAnalyzerService : ILanguageAnalyzerService
    {
        private readonly IAppLogger _logger;
        private const string GoAnalyzerName = "go-analyzer";

        public GoAnalyzerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory)
        {
            _logger.LogStartPhase("Go Source Code Analysis");

            var analyzerExePath = await ExtractEmbeddedExecutableAsync();

            // Per our BYOR architecture, we don't need Go on the path,
            // as we ship a self-contained, pre-compiled analyzer executable.

            var goFiles = Directory.GetFiles(sourceDirectory, "*.go", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("_test.go"));

            if (!goFiles.Any())
            {
                _logger.LogWarning("No Go source files (*.go) found. Analysis cannot proceed.");
                _logger.LogEndPhase("Go Source Code Analysis", true);
                return new List<ServiceBlueprint>();
            }

            // Shell out to the pre-compiled Go analyzer executable.
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = analyzerExePath,
                    Arguments = $"\"{sourceDirectory}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _logger.LogDebug($"Executing Go analyzer: {analyzerExePath} {process.StartInfo.Arguments}");
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError($"Go analyzer failed with exit code {process.ExitCode}.");
                _logger.LogError($"---> Stderr: {error}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "GO_ANALYSIS_FAILED", "The Go source code analysis subprocess failed.");
            }

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var blueprints = JsonSerializer.Deserialize<List<ServiceBlueprint>>(output, options) ?? new List<ServiceBlueprint>();

                if (!blueprints.Any())
                {
                    _logger.LogWarning("Go analysis complete, but no structs with @FunctionHandler comment directives were found.");
                }
                else
                {
                    _logger.LogInfo($"✓ Go analysis complete. Found {blueprints.Count} service(s) to generate.");
                }

                _logger.LogEndPhase("Go Source Code Analysis", true);
                return blueprints;
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Failed to deserialize JSON output from Go analyzer: {ex.Message}");
                _logger.LogDebug($"---> Raw output: {output}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "GO_JSON_DESERIALIZATION_FAILED", "Could not parse analysis results from the Go subprocess.");
            }
        }

        private string GetAnalyzerExeNameForPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return $"{GoAnalyzerName}.exe";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GoAnalyzerName; // No extension on Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return $"{GoAnalyzerName}_macos"; // Convention for macOS executable

            throw new NotSupportedException("The Go analyzer does not support the current operating system.");
        }

        private async Task<string> ExtractEmbeddedExecutableAsync()
        {
            var exeName = GetAnalyzerExeNameForPlatform();
            var tempPath = Path.Combine(Path.GetTempPath(), exeName);

            _logger.LogDebug($"Extracting embedded Go analyzer to '{tempPath}'");
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"x3squaredcircles.datalink.container.Assets.{exeName}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "EMBEDDED_GO_EXE_NOT_FOUND", $"The required analysis tool '{exeName}' was not found as an embedded resource for the current OS.");
            }

            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(fileStream);
            }

            // On Linux/macOS, we must make the extracted file executable.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var chmodProcess = Process.Start("chmod", $"+x \"{tempPath}\"");
                await chmodProcess!.WaitForExitAsync();
                if (chmodProcess.ExitCode != 0)
                {
                    _logger.LogWarning($"Failed to set executable permission on '{tempPath}'. The Go analyzer may fail to run.");
                }
            }

            return tempPath;
        }
    }
}