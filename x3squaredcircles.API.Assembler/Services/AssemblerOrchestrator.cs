using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class AssemblerOrchestrator
    {
        private readonly ILogger<AssemblerOrchestrator> _logger;
        private readonly AssemblerConfiguration _config;
        private readonly ConfigurationValidator _validator;
        private readonly IWorkspaceService _workspaceService;
        private readonly IFileOutputService _fileOutputService;
        private readonly IManifestGeneratorService _manifestGenerator;
        private readonly IDiscoveryService _discoveryService;
        private readonly ICodeGenerationService _codeGenerationService;
        private readonly IDeploymentService _deploymentService;
        private readonly ILicenseClientService _licenseClient;
        private readonly IEnumerable<IBuildService> _buildServices;

        public AssemblerOrchestrator(
            ILogger<AssemblerOrchestrator> logger,
            AssemblerConfiguration config,
            ConfigurationValidator validator,
            IWorkspaceService workspaceService,
            IFileOutputService fileOutputService,
            IManifestGeneratorService manifestGenerator,
            IDiscoveryService discoveryService,
            ICodeGenerationService codeGenerationService,
            IDeploymentService deploymentService,
            ILicenseClientService licenseClient,
            IEnumerable<IBuildService> buildServices)
        {
            _logger = logger;
            _config = config;
            _validator = validator;
            _workspaceService = workspaceService;
            _fileOutputService = fileOutputService;
            _manifestGenerator = manifestGenerator;
            _discoveryService = discoveryService;
            _codeGenerationService = codeGenerationService;
            _deploymentService = deploymentService;
            _licenseClient = licenseClient;
            _buildServices = buildServices;
        }

        public async Task<int> RunAsync(string[] args)
        {
            var rootCommand = BuildCommandLine();
            return await rootCommand.InvokeAsync(args);
        }

        private RootCommand BuildCommandLine()
        {
            var rootCommand = new RootCommand("3SC API Assembler: Generates, builds, and deploys API shims from existing business logic.");

            // --- GENERATE Command ---
            var outputOption = new System.CommandLine.Option<DirectoryInfo>("--output", "The root directory for all generated source code projects.");
            var generateCommand = new Command("generate", "Generates the complete, buildable API source projects.") { outputOption };

            // --- BUILD Command ---
            var projectPathOption = new System.CommandLine.Option<DirectoryInfo>("--project-path", "The path to the generated source project to build.") { IsRequired = true };
            var buildCommand = new Command("build", "Compiles and packages a generated source project into a deployable artifact.") { projectPathOption };

            // --- DEPLOY Command ---
            var groupOption = new System.CommandLine.Option<string>("--group", "The name of the deployment group to deploy.") { IsRequired = true };
            var artifactPathOption = new System.CommandLine.Option<FileInfo>("--artifact-path", "The path to the compiled and packaged artifact to be deployed.") { IsRequired = true };
            var deployCommand = new Command("deploy", "Verifies and deploys a pre-built artifact for a specific group.") { groupOption, artifactPathOption };

            rootCommand.AddCommand(generateCommand);
            rootCommand.AddCommand(buildCommand);
            rootCommand.AddCommand(deployCommand);

            generateCommand.SetHandler(async (InvocationContext context) => {
                var outputDir = context.ParseResult.GetValueForOption(outputOption);
                if (outputDir != null && !string.IsNullOrWhiteSpace(outputDir.FullName))
                {
                    _config.OutputPath = outputDir.FullName;
                }
                context.ExitCode = await ExecuteWorkflow(ExecuteGenerateAsync, "Generate");
            });

            buildCommand.SetHandler(async (InvocationContext context) => {
                var projectPath = context.ParseResult.GetValueForOption(projectPathOption);
                context.ExitCode = await ExecuteWorkflow(() => ExecuteBuildAsync(projectPath!.FullName), "Build");
            });

            deployCommand.SetHandler(async (InvocationContext context) => {
                var groupName = context.ParseResult.GetValueForOption(groupOption)!;
                var artifactPath = context.ParseResult.GetValueForOption(artifactPathOption)!;
                context.ExitCode = await ExecuteWorkflow(() => ExecuteDeployAsync(groupName, artifactPath.FullName), "Deploy");
            });

            return rootCommand;
        }

        private async Task<int> ExecuteWorkflow(Func<Task> workflowAction, string phaseName)
        {
            object? licenseSession = null;
            _logger.LogInformation("--- Starting {PhaseName} phase ---", phaseName.ToUpper());
            try
            {
                _validator.Validate(_config);
                _logger.LogDebug("Configuration loaded and validated.");

                licenseSession = await _licenseClient.AcquireLicenseAsync();

                await workflowAction();

                _logger.LogInformation("--- {PhaseName} phase completed successfully ---", phaseName.ToUpper());
                return (int)AssemblerExitCode.Success;
            }
            catch (AssemblerException ex)
            {
                _logger.LogError(ex, "[{PhaseName}] A handled exception occurred: {Message}", phaseName.ToUpper(), ex.Message);
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "[{PhaseName}] An unhandled exception occurred during the workflow execution.", phaseName.ToUpper());
                return (int)AssemblerExitCode.UnhandledException;
            }
            finally
            {
                if (licenseSession != null)
                {
                    await _licenseClient.ReleaseLicenseAsync(licenseSession);
                }
            }
        }

        private async Task ExecuteGenerateAsync()
        {
            var managedWorkspacePath = _workspaceService.InitializeForGenerate();

            _logger.LogInformation("Step 1/3: Generating deployment manifest from source code...");
            var manifest = await _manifestGenerator.GenerateAsync(_config.Sources);

            _logger.LogInformation("Step 2/3: Discovering API endpoints from business logic assemblies...");
            var discoveredApis = await _discoveryService.DiscoverAsync(_config.Libs, manifest);

            if (_config.NoOp)
            {
                await _fileOutputService.WriteGenerationReceiptAsync(managedWorkspacePath, manifest, discoveredApis, null);
                _logger.LogInformation("NO-OP mode enabled. Skipping source code generation.");
                return;
            }

            _logger.LogInformation("Step 3/3: Assembling API shim source code projects...");
            var generatedProjects = await _codeGenerationService.GenerateProjectsAsync(discoveredApis, manifest);

            await _fileOutputService.WriteGenerationReceiptAsync(managedWorkspacePath, manifest, discoveredApis, generatedProjects);
        }

        private async Task ExecuteBuildAsync(string projectPath)
        {
            _logger.LogInformation("Starting build for project at: {ProjectPath}", projectPath);

            if (!Directory.Exists(projectPath))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Project path not found: {projectPath}");
            }

            var projectType = DetectProjectType(projectPath);
            if (string.IsNullOrEmpty(projectType))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Could not determine project type for directory: {projectPath}. No recognizable project file found.");
            }

            var buildService = _buildServices.FirstOrDefault(s => s.Language.Equals(projectType, StringComparison.OrdinalIgnoreCase));
            if (buildService == null)
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"No build service is registered for project type '{projectType}'.");
            }

            _logger.LogInformation("Detected '{ProjectType}' project. Using '{BuildService}' to build.", projectType, buildService.GetType().Name);

            if (_config.NoOp)
            {
                _logger.LogInformation("NO-OP mode enabled. Skipping actual build.");
                return;
            }

            var buildResult = await buildService.BuildAsync(projectPath);

            if (!buildResult.Success)
            {
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Build failed for project '{projectPath}'. See logs for details.");
            }

            var managedWorkspacePath = _workspaceService.ResolveForDeploy();
            await _fileOutputService.WriteBuildReceiptAsync(managedWorkspacePath, projectPath, buildResult);

            _logger.LogInformation("✓ Build artifact created successfully at: {ArtifactPath}", buildResult.ArtifactPath);
        }

        private async Task ExecuteDeployAsync(string groupName, string artifactPath)
        {
            var managedWorkspacePath = _workspaceService.ResolveForDeploy();

            _logger.LogInformation("Step 1/3: Reading and validating generation receipt...");
            var receipt = await _fileOutputService.ReadGenerationReceiptAsync(managedWorkspacePath);
            var deployable = receipt.GetDeploymentGroup(groupName);

            _logger.LogInformation("Step 2/3: Verifying artifact at '{ArtifactPath}'...", artifactPath);
            await _deploymentService.VerifyArtifactAsync(deployable, artifactPath);

            if (_config.NoOp)
            {
                _logger.LogInformation("NO-OP mode enabled. Skipping actual deployment.");
                return;
            }

            _logger.LogInformation("Step 3/3: Executing deployment to cloud...");
            await _deploymentService.DeployAsync(deployable, artifactPath);

            await _fileOutputService.WriteDeploymentReceiptAsync(managedWorkspacePath, groupName, artifactPath);
        }

        private string? DetectProjectType(string projectPath)
        {
            if (Directory.GetFiles(projectPath, "*.csproj").Any()) return "csharp";
            if (Directory.GetFiles(projectPath, "pom.xml").Any()) return "java";
            if (Directory.GetFiles(projectPath, "requirements.txt").Any()) return "python";
            if (Directory.GetFiles(projectPath, "package.json").Any())
            {
                return Directory.GetFiles(projectPath, "tsconfig.json").Any() ? "typescript" : "javascript";
            }
            if (Directory.GetFiles(projectPath, "go.mod").Any()) return "go";

            return null;
        }
    }
}