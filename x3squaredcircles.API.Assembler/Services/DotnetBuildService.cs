using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Implements the IBuildService for .NET (C#) projects.
    /// </summary>
    public class DotnetBuildService : IBuildService
    {
        private readonly ILogger<DotnetBuildService> _logger;
        public string Language => "csharp";

        public DotnetBuildService(ILogger<DotnetBuildService> logger)
        {
            _logger = logger;
        }

        public async Task<BuildResult> BuildAsync(string projectPath)
        {
            var projectFile = Directory.GetFiles(projectPath, "*.csproj").FirstOrDefault();
            if (projectFile == null)
            {
                var error = $"No .csproj file found in the specified project path: {projectPath}";
                _logger.LogError(error);
                return new BuildResult(false, string.Empty, error);
            }

            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var publishDir = Path.Combine(projectPath, "dist", "publish");
            var artifactPath = Path.Combine(Path.GetDirectoryName(projectPath)!, $"{projectName}.zip");

            // Ensure the output directory is clean before publishing
            if (Directory.Exists(publishDir))
            {
                Directory.Delete(publishDir, true);
            }
            Directory.CreateDirectory(publishDir);

            // Using -r linux-x64 to ensure a self-contained runtime for containerized environments
            var args = $"publish \"{projectFile}\" -c Release -r linux-x64 --self-contained true -o \"{publishDir}\" /p:UseAppHost=false --nologo";

            var (success, output, error) = await ExecuteCommandLineProcessAsync("dotnet", args, projectPath);

            if (!success)
            {
                var fullErrorLog = $"Output:\n{output}\nError:\n{error}";
                _logger.LogError(".NET publish command failed. Error: {ErrorLog}", fullErrorLog);
                return new BuildResult(false, string.Empty, fullErrorLog);
            }

            try
            {
                _logger.LogInformation(".NET publish successful. Creating deployment artifact at: {ArtifactPath}", artifactPath);

                if (File.Exists(artifactPath))
                {
                    File.Delete(artifactPath);
                }
                ZipFile.CreateFromDirectory(publishDir, artifactPath);

                return new BuildResult(true, artifactPath, output);
            }
            catch (Exception ex)
            {
                var zipError = $"Failed to create zip artifact from '{publishDir}': {ex.Message}";
                _logger.LogError(ex, zipError);
                return new BuildResult(false, string.Empty, zipError);
            }
        }

        private async Task<(bool Success, string Output, string Error)> ExecuteCommandLineProcessAsync(string command, string args, string workingDirectory)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            var processExitCompletionSource = new TaskCompletionSource<int>();
            process.Exited += (_, _) => processExitCompletionSource.SetResult(process.ExitCode);
            process.EnableRaisingEvents = true;

            process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute command '{Command} {Args}'", command, args);
                return (false, string.Empty, ex.Message);
            }

            var exitCode = await processExitCompletionSource.Task;
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            return (exitCode == 0, output, error);
        }
    }
}