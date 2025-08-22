using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Weavers;

namespace x3squaredcircles.datalink.container.Services
{
    public class DataLinkOrchestrator : IDataLinkOrchestrator
    {
        private readonly IAppLogger _logger;
        private readonly DataLinkConfiguration _config;
        private readonly IGitService _gitService;
        private readonly ILanguageAnalyzerFactory _analyzerFactory;
        private readonly ICodeWeaverService _codeWeaver;
        private readonly IControlPointService _controlPointService;

        private static readonly Regex _placeholderRegex = new Regex(@"\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

        public DataLinkOrchestrator(
            IAppLogger logger,
            DataLinkConfiguration config,
            IGitService gitService,
            ILanguageAnalyzerFactory analyzerFactory,
            ICodeWeaverService codeWeaver,
            IControlPointService controlPointService)
        {
            _logger = logger;
            _config = config;
            _gitService = gitService;
            _analyzerFactory = analyzerFactory;
            _codeWeaver = codeWeaver;
            _controlPointService = controlPointService;
        }

        public async Task<HashSet<string>> DiscoverRequiredVariablesAsync()
        {
            _logger.LogStartPhase("Variable Discovery");

            var logicPath = await _gitService.CloneRepoAsync(_config.BusinessLogicRepo, _config.DestinationRepoPat, "main");
            _logger.LogInfo($"Cloned '{_config.BusinessLogicRepo}' for analysis.");

            var analyzer = _analyzerFactory.GetAnalyzerForDirectory(logicPath);
            var serviceBlueprints = await analyzer.AnalyzeSourceAsync(logicPath);

            if (!serviceBlueprints.Any())
            {
                _logger.LogWarning("No function handlers found. No variables to discover.");
                return new HashSet<string>();
            }

            var discoveredVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var blueprint in serviceBlueprints)
            {
                foreach (var method in blueprint.TriggerMethods)
                {
                    var eventSource = method.DslAttributes.FirstOrDefault(a => a.Name == "EventSource");
                    if (eventSource != null && eventSource.Arguments.TryGetValue("EventUrn", out var urn))
                    {
                        var matches = _placeholderRegex.Matches(urn);
                        foreach (Match match in matches)
                        {
                            discoveredVariables.Add(match.Groups[1].Value);
                        }
                    }
                }
            }

            _logger.LogEndPhase("Variable Discovery", true);
            return discoveredVariables;
        }

        public async Task<int> GenerateAsync()
        {
            await _controlPointService.InvokeNotificationAsync(ControlPointType.OnStartup, "generate");
            try
            {
                _logger.LogStartPhase("GENERATE Command Execution");
                _logger.LogConfiguration(_config);

                _logger.LogStartPhase("Version Discovery");
                _config.DiscoveredVersionTag = await _gitService.GetLatestTagAsync(_config.BusinessLogicRepo, _config.DestinationRepoPat, _config.VersionTagPattern);
                _logger.LogEndPhase("Version Discovery", true);

                _logger.LogStartPhase("Source Code Acquisition");
                var logicPath = await _gitService.CloneRepoAsync(_config.BusinessLogicRepo, _config.DestinationRepoPat, _config.DiscoveredVersionTag);
                string? testPath = null;
                if (_config.GenerateTestHarness && !string.IsNullOrEmpty(_config.TestHarnessRepo))
                {
                    try
                    {
                        testPath = await _gitService.CloneRepoAsync(_config.TestHarnessRepo, _config.DestinationRepoPat, _config.DiscoveredVersionTag);
                    }
                    catch (DataLinkException ex) when (ex.ErrorCode == "GIT_CLONE_FAILED")
                    {
                        _logger.LogWarning($"Could not clone tag '{_config.DiscoveredVersionTag}' from test harness repo. Continuing without developer tests.");
                    }
                }
                _logger.LogEndPhase("Source Code Acquisition", true);

                var analyzer = _analyzerFactory.GetAnalyzerForDirectory(logicPath);
                var serviceBlueprints = await analyzer.AnalyzeSourceAsync(logicPath);

                if (!serviceBlueprints.Any())
                {
                    _logger.LogWarning("No function handlers found in the source repository. Nothing to generate.");
                    await _controlPointService.InvokeNotificationAsync(ControlPointType.OnSuccess, "generate", "No handlers found, successful no-op.");
                    return (int)ExitCode.Success;
                }

                foreach (var blueprint in serviceBlueprints)
                {
                    blueprint.Metadata = new GenerationMetadata
                    {
                        SourceRepo = _config.BusinessLogicRepo,
                        SourceVersionTag = _config.DiscoveredVersionTag,
                        GenerationTimestampUtc = DateTime.UtcNow,
                        ToolVersion = "6.0.0"
                    };

                    await _codeWeaver.WeaveServiceAsync(blueprint, logicPath, testPath, _config.OutputPath);
                }

                _logger.LogInfo($"Successfully generated all service source code to '{_config.OutputPath}'.");
                _logger.LogEndPhase("GENERATE Command Execution", true);

                await _controlPointService.InvokeNotificationAsync(ControlPointType.OnSuccess, "generate");
                return (int)ExitCode.Success;
            }
            catch (Exception ex)
            {
                var message = ex is DataLinkException de ? de.Message : ex.ToString();
                await _controlPointService.InvokeNotificationAsync(ControlPointType.OnFailure, "generate", message);
                throw;
            }
        }

        public async Task<int> BuildAsync(string groupName)
        {
            await _controlPointService.InvokeNotificationAsync(ControlPointType.OnStartup, "build");
            try
            {
                _logger.LogStartPhase($"BUILD Command Execution for group: {groupName}");
                _logger.LogWarning("The 'build' command is a placeholder and has not been implemented.");
                _logger.LogEndPhase($"BUILD Command Execution", true);

                await _controlPointService.InvokeNotificationAsync(ControlPointType.OnSuccess, "build");
                return (int)ExitCode.Success;
            }
            catch (Exception ex)
            {
                var message = ex is DataLinkException de ? de.Message : ex.ToString();
                await _controlPointService.InvokeNotificationAsync(ControlPointType.OnFailure, "build", message);
                throw;
            }
        }

        public async Task<int> DeployAsync(string groupName, string artifactPath)
        {
            await _controlPointService.InvokeNotificationAsync(ControlPointType.OnStartup, "deploy");
            try
            {
                _logger.LogStartPhase($"DEPLOY Command Execution for group: {groupName}");

                if (!string.IsNullOrEmpty(_config.ControlPointDeploymentOverrideUrl))
                {
                    _logger.LogInfo("Deployment Override Control Point is configured. Handing off deployment...");
                    var payload = new DeploymentOverridePayload
                    {
                        ServiceName = groupName,
                        ArtifactPath = artifactPath,
                        Version = _config.DiscoveredVersionTag,
                        TargetCloud = _config.CloudProvider,
                        TargetEnvironment = Environment.GetEnvironmentVariable("DATALINK_CONTEXT_ENV") ?? "unknown",
                        DeploymentPattern = _config.DeploymentPattern
                    };

                    bool success = await _controlPointService.InvokeDeploymentOverrideAsync(payload);
                    if (!success)
                    {
                        throw new DataLinkException(ExitCode.DeployFailed, "DEPLOY_OVERRIDE_FAILED", "The external deployment override service reported a failure.");
                    }
                }
                else
                {
                    _logger.LogWarning("The built-in 'deploy' command is a placeholder and has not been implemented.");
                }

                _logger.LogEndPhase($"DEPLOY Command Execution", true);
                await _controlPointService.InvokeNotificationAsync(ControlPointType.OnSuccess, "deploy");
                return (int)ExitCode.Success;
            }
            catch (Exception ex)
            {
                var message = ex is DataLinkException de ? de.Message : ex.ToString();
                await _controlPointService.InvokeNotificationAsync(ControlPointType.OnFailure, "deploy", message);
                throw;
            }
        }

        public async Task InvokeControlPointFailureAsync(string command, string errorMessage)
        {
            await _controlPointService.InvokeNotificationAsync(ControlPointType.OnFailure, command, errorMessage);
        }
    }
}