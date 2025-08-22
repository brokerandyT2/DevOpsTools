using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Configuration;
using x3squaredcircles.scribe.container.Models.Artifacts;
using x3squaredcircles.scribe.container.Models.Firehose;
using x3squaredcircles.scribe.container.Models.WorkItems;
using x3squaredcircles.scribe.container.Services;

namespace x3squaredcircles.scribe.container.Hosting
{
    /// <summary>
    /// The core application logic for The Scribe, implemented as a background service.
    /// This service orchestrates the entire process of generating the release artifact.
    /// </summary>
    public class ScribeHostedService : IHostedService
    {
        private readonly ILogger<ScribeHostedService> _logger;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IOutputManagerService _outputManager;
        private readonly IForensicLogService _forensicLog;
        private readonly IArtifactDiscoveryService _artifactDiscovery;
        private readonly IMarkdownGenerationService _markdownGenerator;
        private readonly IWorkItemProviderManager _workItemManager;
        private readonly IWorkItemParserService _workItemParser;
        private readonly IEnvironmentService _environment;
        private readonly IGitService _git;
        private readonly IFirehoseService _firehose;
        private readonly ScribeSettings _settings;

        // State captured during execution to be used by the OnEnd phase.
        private IEnumerable<WorkItem> _enrichedWorkItems = Enumerable.Empty<WorkItem>();
        private DiscoveryResult? _discoveryResult;
        private string _pipelinePageName = string.Empty;

        public ScribeHostedService(
            ILogger<ScribeHostedService> logger, IHostApplicationLifetime hostApplicationLifetime, IOptions<ScribeSettings> settings,
            IOutputManagerService outputManager, IForensicLogService forensicLog, IArtifactDiscoveryService artifactDiscovery,
            IMarkdownGenerationService markdownGenerator, IWorkItemParserService workItemParser, IWorkItemProviderManager workItemManager,
            IEnvironmentService environment, IGitService git, IFirehoseService firehose)
        {
            _logger = logger; _hostApplicationLifetime = hostApplicationLifetime; _settings = settings.Value;
            _outputManager = outputManager; _forensicLog = forensicLog; _artifactDiscovery = artifactDiscovery;
            _markdownGenerator = markdownGenerator; _workItemParser = workItemParser; _workItemManager = workItemManager;
            _environment = environment; _git = git; _firehose = firehose;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _hostApplicationLifetime.ApplicationStarted.Register(() => {
                Task.Run(async () => {
                    try
                    {
                        await OnStartupAsync(cancellationToken);
                        await ExecuteAsync(cancellationToken);
                        await OnEndAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) { _logger.LogWarning("Scribe execution was cancelled."); }
                    catch (Exception ex) { _logger.LogCritical(ex, "A fatal error occurred during the Scribe's execution phase."); }
                    finally
                    {
                        _logger.LogInformation("Scribe execution finished. Signaling host to shut down.");
                        _hostApplicationLifetime.StopApplication();
                    }
                }, cancellationToken);
            });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task OnStartupAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("--- OnStartup Phase ---");
            await _outputManager.InitializeOutputStructureAsync(_environment.GetReleaseVersion());
            await _forensicLog.LoadLogEntriesAsync(_environment.GetWorkspacePath());
            _logger.LogInformation("Startup phase complete.");
        }

        private async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("--- Execute Phase ---");
            var workspacePath = _environment.GetWorkspacePath();
            var gitRange = _environment.GetGitRange();

            _discoveryResult = await _artifactDiscovery.DiscoverArtifactsAsync(workspacePath);
            var genericArtifacts = _discoveryResult.Artifacts.ToList();

            var rawGitLog = await _git.GetCommitLogAsync(workspacePath, gitRange);
            var rawWorkItemIds = _workItemParser.ParseWorkItemIdsFromLog(rawGitLog);
            _enrichedWorkItems = await _workItemManager.EnrichWorkItemsAsync(rawWorkItemIds);

            // Determine the pipeline page name upfront to include it in the index.
            if (!string.IsNullOrEmpty(_discoveryResult.PipelineFilePath))
            {
                var pipelinePageIndex = (genericArtifacts.Any() ? genericArtifacts.Count : 0) + 2;
                _pipelinePageName = $"{pipelinePageIndex}_-_Pipeline_Definition.md";
            }

            var frontPageContent = _markdownGenerator.GenerateWorkItemsPage(
                _environment.GetReleaseVersion(), _environment.GetPipelineRunId(), gitRange,
                _enrichedWorkItems, genericArtifacts, _pipelinePageName);

            var frontPagePath = Path.Combine(_outputManager.FinalReleasePath, "1_-_Work_Items.md");
            await File.WriteAllTextAsync(frontPagePath, frontPageContent, cancellationToken);
            _logger.LogInformation("Successfully generated front page.");

            // Process all generic tool artifacts
            foreach (var artifact in genericArtifacts)
            {
                var logEntry = _forensicLog.Entries.FirstOrDefault(e => e.ToolName.Equals(Path.GetFileNameWithoutExtension(artifact.SourceFilePath), StringComparison.OrdinalIgnoreCase));
                var subPageContent = _markdownGenerator.GenerateToolSubPage(artifact, logEntry);
                var subPagePath = Path.Combine(_outputManager.FinalReleasePath, artifact.PageFileName);
                await File.WriteAllTextAsync(subPagePath, subPageContent, cancellationToken);
                File.Copy(artifact.SourceFilePath, Path.Combine(_outputManager.AttachmentsPath, Path.GetFileName(artifact.SourceFilePath)), true);
            }
            _logger.LogInformation("Successfully processed {Count} generic artifacts.", genericArtifacts.Count);

            // Process the pipeline definition file, if found
            if (!string.IsNullOrEmpty(_discoveryResult.PipelineFilePath))
            {
                var pipelinePageContent = _markdownGenerator.GeneratePipelinePage(_discoveryResult.PipelineFilePath);
                var pipelinePagePath = Path.Combine(_outputManager.FinalReleasePath, _pipelinePageName);
                await File.WriteAllTextAsync(pipelinePagePath, pipelinePageContent, cancellationToken);
                File.Copy(_discoveryResult.PipelineFilePath, Path.Combine(_outputManager.AttachmentsPath, Path.GetFileName(_discoveryResult.PipelineFilePath)), true);
                _logger.LogInformation("Successfully processed the pipeline definition file.");
            }
        }

        private Task OnEndAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("--- OnEnd Phase ---");
            if (_discoveryResult == null) return Task.CompletedTask; // Should not happen

            var evidencePackages = _discoveryResult.Artifacts.Select(artifact => new EvidencePackage(
                artifact.ToolName, artifact.PageFileName, artifact.SourceFilePath,
                _forensicLog.Entries.FirstOrDefault(e => e.ToolName.Equals(Path.GetFileNameWithoutExtension(artifact.SourceFilePath), StringComparison.OrdinalIgnoreCase))
            )).ToList();

            if (!string.IsNullOrEmpty(_discoveryResult.PipelineFilePath))
            {
                evidencePackages.Add(new EvidencePackage(
                    "Pipeline Definition", _pipelinePageName, _discoveryResult.PipelineFilePath, null
                ));
            }

            var finalArtifactData = new ReleaseArtifactData(
                _settings.AppName, _environment.GetReleaseVersion(), _environment.GetPipelineRunId(),
                _environment.GetGitRange(), _enrichedWorkItems, evidencePackages);

            _firehose.LogArtifact(finalArtifactData);

            _logger.LogInformation("Scribe artifact generation complete. All files written to {Path}", _outputManager.FinalReleasePath);
            return Task.CompletedTask;
        }
    }
}