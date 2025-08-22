using Microsoft.Extensions.Logging;
using System;
using System.IO;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for managing the Assembler's working directory and state.
    /// </summary>
    public interface IWorkspaceService
    {
        /// <summary>
        /// Initializes a new workspace for a `generate` run, creating a unique directory
        /// for internal artifacts and a pointer file for subsequent steps.
        /// </summary>
        /// <returns>The path to the newly created, unique managed workspace directory.</returns>
        string InitializeForGenerate();

        /// <summary>
        /// Resolves the active workspace for a `deploy` run by reading the pointer file.
        /// </summary>
        /// <returns>The path to the active managed workspace directory for the current pipeline run.</returns>
        string ResolveForDeploy();
    }

    /// <summary>
    /// Manages the creation and resolution of the unique, GUID-named workspace directory
    /// that contains all generated artifacts and metadata for a single pipeline run.
    /// It uses a pointer file in the root to pass state between CI/CD steps.
    /// </summary>
    public class WorkspaceService : IWorkspaceService
    {
        private const string PointerFileName = ".3sc-assembler-workspace"; // Hidden file to avoid clutter
        private const string ManagedWorkspacesDirName = ".3sc-workspaces";

        private readonly ILogger<WorkspaceService> _logger;
        private readonly AssemblerConfiguration _config;
        private readonly string _pipelineRoot;

        public WorkspaceService(ILogger<WorkspaceService> logger, AssemblerConfiguration config)
        {
            _logger = logger;
            _config = config;
            // Assumes the tool is run from the root of the pipeline's checkout directory.
            _pipelineRoot = Directory.GetCurrentDirectory();
        }

        public string InitializeForGenerate()
        {
            try
            {
                var runGuid = Guid.NewGuid();
                _logger.LogInformation("Initializing new workspace with Generation ID: {Guid}", runGuid);

                // This is the hidden directory for our internal state and artifacts.
                var managedWorkspacePath = Path.Combine(_pipelineRoot, ManagedWorkspacesDirName, runGuid.ToString());
                Directory.CreateDirectory(managedWorkspacePath);
                _logger.LogDebug("Created managed workspace directory: {Path}", managedWorkspacePath);

                // This file is the "bridge" between the 'generate' and 'deploy' steps.
                var pointerFilePath = Path.Combine(_pipelineRoot, PointerFileName);
                File.WriteAllText(pointerFilePath, runGuid.ToString());
                _logger.LogInformation("Created workspace pointer file at: {Path}", pointerFilePath);

                // Ensure the user-visible output directory from the configuration exists.
                if (!Directory.Exists(_config.OutputPath))
                {
                    _logger.LogDebug("Creating user-specified output directory: {Path}", _config.OutputPath);
                    Directory.CreateDirectory(_config.OutputPath);
                }

                return managedWorkspacePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize workspace for generate command.");
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, "Could not create workspace directory. Check file system permissions.", ex);
            }
        }

        public string ResolveForDeploy()
        {
            try
            {
                var pointerFilePath = Path.Combine(_pipelineRoot, PointerFileName);
                if (!File.Exists(pointerFilePath))
                {
                    _logger.LogError("Workspace pointer file not found at '{Path}'.", pointerFilePath);
                    throw new AssemblerException(AssemblerExitCode.InvalidConfiguration,
                        $"Workspace pointer file '{PointerFileName}' not found. Ensure the 'generate' command was run in a previous step and that the workspace is persisted.");
                }

                var runGuid = File.ReadAllText(pointerFilePath).Trim();
                if (!Guid.TryParse(runGuid, out _))
                {
                    throw new AssemblerException(AssemblerExitCode.InvalidConfiguration,
                       $"Invalid GUID found in workspace pointer file: {runGuid}");
                }

                _logger.LogInformation("Resolved active workspace with Generation ID: {Guid}", runGuid);

                var managedWorkspacePath = Path.Combine(_pipelineRoot, ManagedWorkspacesDirName, runGuid);
                if (!Directory.Exists(managedWorkspacePath))
                {
                    _logger.LogError("The resolved workspace directory '{Path}' does not exist.", managedWorkspacePath);
                    throw new AssemblerException(AssemblerExitCode.InvalidConfiguration,
                       $"The workspace directory for run '{runGuid}' could not be found. Ensure the workspace artifact was correctly passed from the generate step.");
                }

                _logger.LogDebug("Resolved managed workspace directory: {Path}", managedWorkspacePath);
                return managedWorkspacePath;
            }
            catch (AssemblerException)
            {
                throw; // Re-throw our specific, handled exceptions.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve workspace for deploy command.");
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, "Could not resolve active workspace due to an unexpected error.", ex);
            }
        }
    }
}