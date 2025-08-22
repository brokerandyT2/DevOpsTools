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
    public class JavaScriptAnalyzerService : ILanguageAnalyzerService
    {
        private readonly IAppLogger _logger;
        private const string NodeAnalyzerName = "node-analyzer.js";

        public JavaScriptAnalyzerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory)
        {
            _logger.LogStartPhase("JavaScript/TypeScript Source Code Analysis");

            var analyzerScriptPath = await ExtractEmbeddedScriptAsync();

            // Per our BYOR architecture, we expect Node.js to be on the PATH.
            if (!IsCommandOnPath("node") || !IsCommandOnPath("npm"))
            {
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "NODE_NOT_FOUND", "Could not find 'node' and 'npm' executables on the system PATH. Please ensure Node.js is installed on the build agent.");
            }

            // Install the analyzer's own dependencies (e.g., @babel/parser) in a temp directory.
            await InstallNpmDependencies(Path.GetDirectoryName(analyzerScriptPath)!);

            var jsTsFiles = Directory.GetFiles(sourceDirectory, "*.js", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(sourceDirectory, "*.ts", SearchOption.AllDirectories))
                .Where(f => !f.Contains("node_modules") && !f.EndsWith(".d.ts"));

            if (!jsTsFiles.Any())
            {
                _logger.LogWarning("No JavaScript or TypeScript files (*.js, *.ts) found. Analysis cannot proceed.");
                _logger.LogEndPhase("JavaScript/TypeScript Source Code Analysis", true);
                return new List<ServiceBlueprint>();
            }

            // Shell out to the Node.js analyzer script.
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{analyzerScriptPath}\" \"{sourceDirectory}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _logger.LogDebug($"Executing Node analyzer: node {process.StartInfo.Arguments}");
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError($"Node analyzer failed with exit code {process.ExitCode}.");
                _logger.LogError($"---> Stderr: {error}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "NODE_ANALYSIS_FAILED", "The Node.js source code analysis subprocess failed.");
            }

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var blueprints = JsonSerializer.Deserialize<List<ServiceBlueprint>>(output, options) ?? new List<ServiceBlueprint>();

                if (!blueprints.Any())
                {
                    _logger.LogWarning("JS/TS analysis complete, but no classes decorated with @FunctionHandler were found.");
                }
                else
                {
                    _logger.LogInfo($"✓ JS/TS analysis complete. Found {blueprints.Count} service(s) to generate.");
                }

                _logger.LogEndPhase("JavaScript/TypeScript Source Code Analysis", true);
                return blueprints;
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Failed to deserialize JSON output from Node analyzer: {ex.Message}");
                _logger.LogDebug($"---> Raw output: {output}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "NODE_JSON_DESERIALIZATION_FAILED", "Could not parse analysis results from the Node.js subprocess.");
            }
        }

        private async Task InstallNpmDependencies(string workingDirectory)
        {
            _logger.LogDebug($"Installing Node analyzer dependencies in '{workingDirectory}'...");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "npm",
                    Arguments = "install @babel/parser",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogError("Failed to install npm dependencies for the internal analyzer script.");
                _logger.LogError($"---> Stderr: {error}");
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "NPM_INSTALL_FAILED", "Could not install dependencies for the internal Node.js analyzer.");
            }
            _logger.LogDebug("Node analyzer dependencies installed successfully.");
        }

        private async Task<string> ExtractEmbeddedScriptAsync()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"3sc-node-analyzer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, NodeAnalyzerName);

            _logger.LogDebug($"Extracting embedded Node analyzer to '{scriptPath}'");
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"x3squaredcircles.datalink.container.Assets.{NodeAnalyzerName}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "EMBEDDED_JS_NOT_FOUND", $"The required analysis tool '{NodeAnalyzerName}' was not found as an embedded resource.");
            }

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            await File.WriteAllTextAsync(scriptPath, content);

            return scriptPath;
        }

        private bool IsCommandOnPath(string command)
        {
            var testCommand = Environment.OSVersion.Platform == PlatformID.Win32NT ? "where" : "which";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = testCommand,
                    Arguments = command,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
    }
}