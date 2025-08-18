using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Weavers;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines the contract for the main orchestrator that runs the end-to-end DataLink workflow.
    /// </summary>
    public interface IDataLinkOrchestrator
    {
        /// <summary>
        /// Executes the complete source-to-source transformation and commit workflow.
        /// </summary>
        /// <returns>The final exit code for the application.</returns>
        Task<int> ExecuteAsync();
    }

    /// <summary>
    /// The central orchestrator for the DataLink tool. It coordinates all services to perform
    /// the analysis of business logic, weaving of the shim, and the final commit to the destination repository.
    /// </summary>
    public class DataLinkOrchestrator : IDataLinkOrchestrator
    {
        private readonly IAppLogger _logger;
        private readonly IConfigurationService _configService;
        private readonly IGitService _gitService;
        private readonly ILanguageAnalyzerFactory _analyzerFactory;
        private readonly ICodeWeaverService _codeWeaver;

        public DataLinkOrchestrator(
            IAppLogger logger,
            IConfigurationService configService,
            IGitService gitService,
            ILanguageAnalyzerFactory analyzerFactory,
            ICodeWeaverService codeWeaver)
        {
            _logger = logger;
            _configService = configService;
            _gitService = gitService;
            _analyzerFactory = analyzerFactory;
            _codeWeaver = codeWeaver;
        }

        public async Task<int> ExecuteAsync()
        {
            // Step 1: Load and Log Configuration
            var config = _configService.GetConfiguration();
            _logger.LogConfiguration(config);

            // Step 2: Discover the authoritative version tag from the business logic repo
            _logger.LogStartPhase("Version Discovery");
            config.DiscoveredVersionTag = await _gitService.GetLatestTagAsync(config.BusinessLogicRepo, config.DestinationRepoPat, config.VersionTagPattern);
            _logger.LogEndPhase("Version Discovery", true);

            // Step 3: Clone all required source repositories to temporary local paths
            _logger.LogStartPhase("Source Code Acquisition");
            var logicPath = await _gitService.CloneRepoAsync(config.BusinessLogicRepo, config.DestinationRepoPat, config.DiscoveredVersionTag);

            string? testPath = null;
            if (config.GenerateTestHarness && !string.IsNullOrEmpty(config.TestHarnessRepo))
            {
                testPath = await _gitService.CloneRepoAsync(config.TestHarnessRepo, config.DestinationRepoPat, config.DiscoveredVersionTag);
            }

            var destinationPath = await _gitService.CloneRepoAsync(config.DestinationRepo, config.DestinationRepoPat, "main");
            _logger.LogEndPhase("Source Code Acquisition", true);

            // Step 4: Analyze the business logic source code to create blueprints
            var analyzer = _analyzerFactory.GetAnalyzerForDirectory(logicPath);
            var serviceBlueprints = await analyzer.AnalyzeSourceAsync(logicPath);

            if (!serviceBlueprints.Any())
            {
                _logger.LogWarning("No DataConsumers found in the source repository. Nothing to generate.");
                return (int)ExitCode.Success;
            }

            // Per our architecture, we process only the first discovered service blueprint.
            var blueprint = serviceBlueprints.First();
            if (serviceBlueprints.Count > 1)
            {
                _logger.LogWarning($"Multiple DataConsumers found. Processing '{blueprint.ServiceName}' and ignoring others as per single-service-per-repo rule.");
            }

            blueprint.Metadata = new GenerationMetadata
            {
                SourceRepo = config.BusinessLogicRepo,
                SourceVersionTag = config.DiscoveredVersionTag,
                GenerationTimestampUtc = DateTime.UtcNow,
                ToolVersion = "1.0.0" // This would be populated from assembly version
            };

            // Step 5: Weave the complete source project, including the shim and the test harness skeleton files.
            await _codeWeaver.WeaveServiceAsync(blueprint, logicPath, testPath, destinationPath);

            // Step 6: Commit the final generated source code and platform files to the destination repo
            _logger.LogStartPhase("Committing to Destination Repository");
            var commitMessage = $"feat: Generate shim and test harness for {blueprint.ServiceName} from source {config.DiscoveredVersionTag}";
            await _gitService.CommitAndPushAsync(destinationPath, config.DestinationRepo, config.DestinationRepoPat, config.DiscoveredVersionTag, commitMessage);
            _logger.LogEndPhase("Committing to Destination Repository", true);

            _logger.LogInfo("Successfully generated and committed shim source code. The destination repository's CI/CD pipeline can now build and deploy the artifact.");

            return (int)ExitCode.Success;
        }
    }
}