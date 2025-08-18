using Microsoft.Extensions.Logging;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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
            ILicenseClientService licenseClient)
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
        }

        public async Task<int> RunAsync(string[] args)
        {
            var rootCommand = BuildCommandLine();
            return await rootCommand.InvokeAsync(args);
        }

        private RootCommand BuildCommandLine()
        {
            var outputOption = new System.CommandLine.Option<DirectoryInfo>("--output", () => new DirectoryInfo("./output"), "The root directory for all generated source code projects.");
            var generateCommand = new Command("generate", "Generates the complete, buildable API source projects.")
            {
                outputOption
            };

            var groupOption = new System.CommandLine.Option<string>("--group", "The name of the deployment group to deploy.") { IsRequired = true };
            var artifactPathOption = new System.CommandLine.Option<FileInfo>("--artifact-path", "The path to the compiled and packaged artifact to be deployed.") { IsRequired = true };
            var deployCommand = new Command("deploy", "Verifies and deploys a pre-built artifact for a specific group.")
            {
                groupOption,
                artifactPathOption
            };

            var rootCommand = new RootCommand("3SC API Assembler: Generates and deploys API shims from existing business logic.");
            rootCommand.AddCommand(generateCommand);
            rootCommand.AddCommand(deployCommand);

            generateCommand.SetHandler(async (InvocationContext context) => {
                var outputDir = context.ParseResult.GetValueForOption(outputOption);
                _config.OutputPath = outputDir.FullName;
                await ExecuteWorkflow(ExecuteGenerateAsync);
            });

            deployCommand.SetHandler(async (InvocationContext context) => {
                var groupName = context.ParseResult.GetValueForOption(groupOption);
                var artifactPath = context.ParseResult.GetValueForOption(artifactPathOption);
                await ExecuteWorkflow(() => ExecuteDeployAsync(groupName, artifactPath.FullName));
            });

            return rootCommand;
        }

        private async Task ExecuteWorkflow(Func<Task> workflowAction)
        {
            try
            {
                _validator.Validate(_config);
                _logger.LogInformation("Configuration loaded and validated successfully.");
                _logger.LogInformation("Executing in '{Environment}' environment.", _config.AssemblerEnv);

                await workflowAction();
            }
            catch (AssemblerException ex)
            {
                _logger.LogError(ex, "A handled exception occurred: {Message}", ex.Message);
                Environment.ExitCode = (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unhandled exception occurred during the workflow execution.");
                Environment.ExitCode = (int)AssemblerExitCode.UnhandledException;
            }
        }

        private async Task ExecuteGenerateAsync()
        {
            _logger.LogInformation("--- Starting GENERATE phase ---");

            var managedWorkspacePath = _workspaceService.InitializeForGenerate(_config.OutputPath);
            var licenseSession = await _licenseClient.AcquireLicenseAsync();

            _logger.LogInformation("Step 1/4: Generating deployment manifest from source code...");
            var manifest = await _manifestGenerator.GenerateAsync(_config.Sources);

            _logger.LogInformation("Step 2/4: Discovering API endpoints from business logic assemblies...");
            var discoveredApis = await _discoveryService.DiscoverAsync(_config.Libs, manifest);

            if (_config.NoOp && licenseSession == null)
            {
                await _fileOutputService.WriteGenerationReceiptAsync(managedWorkspacePath, manifest, discoveredApis, null);
                _logger.LogInformation("--- GENERATE phase completed in NO-OP (Analysis Only) mode ---");
                return;
            }

            _logger.LogInformation("Step 3/4: Assembling API shim source code projects...");
            var generatedProjects = await _codeGenerationService.GenerateProjectsAsync(discoveredApis, manifest);

            _logger.LogInformation("Step 4/4: Writing final generation receipt...");
            await _fileOutputService.WriteGenerationReceiptAsync(managedWorkspacePath, manifest, discoveredApis, generatedProjects);

            await _licenseClient.ReleaseLicenseAsync(licenseSession);
            _logger.LogInformation("--- GENERATE phase completed successfully ---");
        }

        private async Task ExecuteDeployAsync(string groupName, string artifactPath)
        {
            _logger.LogInformation("--- Starting DEPLOY phase for group: {Group} ---", groupName);

            var managedWorkspacePath = _workspaceService.ResolveForDeploy();
            var licenseSession = await _licenseClient.AcquireLicenseAsync();

            _logger.LogInformation("Step 1/3: Reading and validating generation receipt...");
            var receipt = await _fileOutputService.ReadGenerationReceiptAsync(managedWorkspacePath);
            var deployable = receipt.GetDeployable(groupName);

            _logger.LogInformation("Step 2/3: Verifying artifact at '{ArtifactPath}'...", artifactPath);
            await _deploymentService.VerifyArtifactAsync(deployable, artifactPath);

            _logger.LogInformation("Step 3/3: Executing deployment to cloud...");
            await _deploymentService.DeployAsync(deployable, artifactPath);

            await _fileOutputService.WriteDeploymentReceiptAsync(managedWorkspacePath, groupName, artifactPath);

            await _licenseClient.ReleaseLicenseAsync(licenseSession);
            _logger.LogInformation("--- DEPLOY phase for group {Group} completed successfully ---", groupName);
        }
    }
}