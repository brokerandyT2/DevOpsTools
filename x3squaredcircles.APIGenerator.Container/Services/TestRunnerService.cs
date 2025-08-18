using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Weavers;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines the contract for a service that assembles and runs a test harness
    /// for a newly generated service shim.
    /// </summary>
    public interface ITestRunnerService
    {
        /// <summary>
        /// Assembles and executes the test harness for a given service blueprint.
        /// </summary>
        /// <param name="blueprint">The blueprint of the service being tested.</param>
        /// <param name="wovenServicePath">The local path to the root of the newly woven service and test project.</param>
        /// <returns>True if all tests passed, otherwise false.</returns>
        Task<bool> RunTestsAsync(ServiceBlueprint blueprint, string wovenServicePath);
    }

    /// <summary>
    /// Implements the logic for running the generated test harness by invoking the 'dotnet test' command.
    /// </summary>
    public class TestRunnerService : ITestRunnerService
    {
        private readonly IAppLogger _logger;

        public TestRunnerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<bool> RunTestsAsync(ServiceBlueprint blueprint, string wovenServicePath)
        {
            var testProjectPath = Path.Combine(wovenServicePath, "tests", $"{blueprint.ServiceName}.Tests");

            if (!Directory.Exists(testProjectPath) || !Directory.GetFiles(testProjectPath, "*.csproj").Any())
            {
                _logger.LogWarning($"No test project found at '{testProjectPath}'. Skipping test execution.");
                return true; // No tests to run is considered a success.
            }

            _logger.LogStartPhase($"Running Test Harness for: {blueprint.ServiceName}");

            var arguments = $"test \"{testProjectPath}\" --logger \"console;verbosity=normal\"";

            var (success, output, error) = await ExecuteDotnetCommandAsync(arguments, wovenServicePath);

            if (success)
            {
                _logger.LogInfo("✓ All tests in the assembled harness passed.");
                _logger.LogEndPhase($"Running Test Harness for: {blueprint.ServiceName}", true);
                return true;
            }
            else
            {
                _logger.LogError($"Test harness execution failed for {blueprint.ServiceName}.");
                _logger.LogError($"---> Test Output:\n{output}\n{error}");
                _logger.LogEndPhase($"Running Test Harness for: {blueprint.ServiceName}", false);
                return false;
            }
        }

        private async Task<(bool Success, string Output, string Error)> ExecuteDotnetCommandAsync(string arguments, string workingDirectory)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using var outputWaitHandle = new System.Threading.AutoResetEvent(false);
            using var errorWaitHandle = new System.Threading.AutoResetEvent(false);

            process.OutputDataReceived += (_, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); else outputWaitHandle.Set(); };
            process.ErrorDataReceived += (_, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); else errorWaitHandle.Set(); };

            _logger.LogDebug($"Executing dotnet command: dotnet {arguments}");

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            outputWaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            errorWaitHandle.WaitOne(TimeSpan.FromSeconds(30));

            var output = outputBuilder.ToString().Trim();
            var error = errorBuilder.ToString().Trim();

            // "dotnet test" can have a non-zero exit code on test failure, which is expected behavior.
            // We determine success by parsing the output for the "Failed!" summary line.
            bool testsPassed = process.ExitCode == 0 && !output.Contains("Failed!");

            if (!testsPassed)
            {
                _logger.LogWarning($"Dotnet command finished with a failure status. Exit Code: {process.ExitCode}");
            }

            return (testsPassed, output, error);
        }
    }
}