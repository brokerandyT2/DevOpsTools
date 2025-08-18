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
        /// Initializes a new workspace for a `generate` run.
        /// </summary>
        /// <param name="baseOutputDirectory">The root output directory specified by the user.</param>
        /// <returns>The path to the newly created, unique workspace directory.</returns>
        string InitializeForGenerate(string baseOutputDirectory);

        /// <summary>
        /// Resolves the workspace for a `deploy` run.
        /// </summary>
        /// <returns>The path to the active workspace directory for the current pipeline run.</returns>
        string ResolveForDeploy();
    }

    /// <summary>
    /// Manages the creation and resolution of the unique, GUID-named workspace directory
    /// that contains all generated artifacts and metadata for a single pipeline run.
    /// </summary>
    public class WorkspaceService : IWorkspaceService
    {
        private const string ManifestFileName = "3sc-assembler.manifest";
        private const string WorkspaceDirName = ".3sc-workspaces";

        private readonly ILogger<WorkspaceService> _logger;
        private readonly string _pipelineWorkspaceRoot;

        public WorkspaceService(ILogger<WorkspaceService> logger)
        {
            _logger = logger;
            // Assumes the tool is run from the root of the pipeline's checkout directory.
            _pipelineWorkspaceRoot = Directory.GetCurrentDirectory();
        }

        public string InitializeForGenerate(string baseOutputDirectory)
        {
            try
            {
                var runGuid = Guid.NewGuid();
                _logger.LogInformation("Initializing new workspace with Generation ID: {Guid}", runGuid);

                var managedWorkspacePath = Path.Combine(_pipelineWorkspaceRoot, WorkspaceDirName, runGuid.ToString());
                Directory.CreateDirectory(managedWorkspacePath);
                _logger.LogDebug("Created managed workspace directory: {Path}", managedWorkspacePath);

                // Create the pointer manifest in the root of the pipeline workspace.
                var pointerManifestPath = Path.Combine(_pipelineWorkspaceRoot, ManifestFileName);
                File.WriteAllText(pointerManifestPath, runGuid.ToString());
                _logger.LogInformation("Created pointer manifest file at: {Path}", pointerManifestPath);

                // Create the user-visible output directory
                Directory.CreateDirectory(baseOutputDirectory);

                // Return the path to the managed workspace where internal artifacts will be stored.
                return managedWorkspacePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize workspace for generate command.");
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, "Could not create workspace directory.", ex);
            }
        }

        public string ResolveForDeploy()
        {
            try
            {
                var pointerManifestPath = Path.Combine(_pipelineWorkspaceRoot, ManifestFileName);
                if (!File.Exists(pointerManifestPath))
                {
                    throw new AssemblerException(AssemblerExitCode.InvalidConfiguration,
                        $"Workspace manifest '{ManifestFileName}' not found in the current directory. Ensure the 'generate' command was run first in a previous step.");
                }

                var runGuid = File.ReadAllText(pointerManifestPath).Trim();
                if (!Guid.TryParse(runGuid, out _))
                {
                    throw new AssemblerException(AssemblerExitCode.InvalidConfiguration,
                       $"Invalid GUID found in workspace manifest file: {runGuid}");
                }

                _logger.LogInformation("Resolved active workspace with Generation ID: {Guid}", runGuid);

                var managedWorkspacePath = Path.Combine(_pipelineWorkspaceRoot, WorkspaceDirName, runGuid);
                if (!Directory.Exists(managedWorkspacePath))
                {
                    throw new AssemblerException(AssemblerExitCode.InvalidConfiguration,
                       $"The workspace directory for run '{runGuid}' could not be found. Ensure the workspace artifact was correctly passed from the generate step.");
                }

                _logger.LogDebug("Resolved managed workspace directory: {Path}", managedWorkspacePath);
                return managedWorkspacePath;
            }
            catch (AssemblerException)
            {
                throw; // Re-throw our specific exceptions.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve workspace for deploy command.");
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, "Could not resolve active workspace.", ex);
            }
        }
    }
}