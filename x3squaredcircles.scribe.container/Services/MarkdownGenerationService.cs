using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using x3squaredcircles.scribe.container.Configuration;
using x3squaredcircles.scribe.container.Models.Artifacts;
using x3squaredcircles.scribe.container.Models.Forensic;
using x3squaredcircles.scribe.container.Models.WorkItems;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the logic for generating Markdown content for the release artifact.
    /// </summary>
    public class MarkdownGenerationService : IMarkdownGenerationService
    {
        private readonly ScribeSettings _settings;
        private readonly ILogger<MarkdownGenerationService> _logger;

        public MarkdownGenerationService(IOptions<ScribeSettings> settings, ILogger<MarkdownGenerationService> logger)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string GenerateWorkItemsPage(
            string releaseVersion,
            string pipelineRunId,
            string gitRange,
            IEnumerable<WorkItem> workItems,
            IEnumerable<ScribeArtifact> discoveredArtifacts,
            string? pipelinePageName)
        {
            _logger.LogInformation("Generating '1_-_Work_Items.md' front page...");
            var sb = new StringBuilder();

            sb.AppendLine($"# Release: {_settings.AppName} {releaseVersion}");
            sb.AppendLine();
            sb.AppendLine("| Attribute | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| **Version** | `{releaseVersion}` |");
            sb.AppendLine($"| **Generated On** | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC |");
            sb.AppendLine($"| **Pipeline Run** | `{pipelineRunId}` |");
            sb.AppendLine($"| **Git Range** | `{gitRange}` |");
            sb.AppendLine();

            sb.AppendLine("## Work Items");
            sb.AppendLine($"> **Style:** `{_settings.WorkItemStyle}`");
            sb.AppendLine();

            if (workItems == null || !workItems.Any())
            {
                sb.AppendLine("No work items were found in the specified Git commit range.");
            }
            else
            {
                if (_settings.WorkItemStyle.Equals("categorized", StringComparison.OrdinalIgnoreCase)) BuildCategorizedWorkItems(sb, workItems);
                else BuildListWorkItems(sb, workItems);
            }
            sb.AppendLine();

            sb.AppendLine("## Evidence Index");
            sb.AppendLine();
            var artifactsList = discoveredArtifacts?.ToList() ?? new List<ScribeArtifact>();
            if (artifactsList.Any() || !string.IsNullOrEmpty(pipelinePageName))
            {
                foreach (var artifact in artifactsList)
                {
                    sb.AppendLine($"- [{artifact.ToolName}](./{artifact.PageFileName})");
                }
                if (!string.IsNullOrEmpty(pipelinePageName))
                {
                    sb.AppendLine($"- [Pipeline Definition](./{pipelinePageName})");
                }
            }
            else
            {
                sb.AppendLine("No evidence artifacts were discovered.");
            }
            sb.AppendLine();

            return sb.ToString();
        }

        /// <inheritdoc />
        public string GenerateToolSubPage(ScribeArtifact artifact, LogEntry? logEntry)
        {
            _logger.LogInformation("Generating sub-page for artifact: {SourceFile}", artifact.SourceFilePath);
            var sb = new StringBuilder();

            sb.AppendLine($"# {artifact.ToolName}");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.Append(GenerateFileContentBlock(artifact.SourceFilePath));
            sb.AppendLine();

            sb.AppendLine("## Forensic Details");
            sb.AppendLine();
            if (logEntry != null)
            {
                sb.AppendLine("| Attribute | Value |");
                sb.AppendLine("|---|---|");
                sb.AppendLine($"| **Tool Version** | `{logEntry.ToolVersion}` |");
                sb.AppendLine($"| **Executed On** | {logEntry.ExecutionTimestamp:yyyy-MM-dd HH:mm:ss} UTC |");
                sb.AppendLine();
                sb.AppendLine("### Configuration");
                sb.AppendLine();
                if (logEntry.Configuration != null && logEntry.Configuration.Any())
                {
                    sb.AppendLine("| Variable | Value |");
                    sb.AppendLine("|---|---|");
                    foreach (var config in logEntry.Configuration.OrderBy(c => c.Key))
                    {
                        var value = config.Value.Replace("|", "\\|");
                        sb.AppendLine($"| `{config.Key}` | `{value}` |");
                    }
                }
                else { sb.AppendLine("No specific configuration variables were logged for this tool's execution."); }
            }
            else { sb.AppendLine("> [!WARNING]\n> No corresponding entry was found in `pipeline-log.json` for this tool. Configuration data is unavailable."); }
            sb.AppendLine();

            sb.AppendLine("---\n");
            sb.AppendLine($"[View Raw Evidence](./attachments/{Path.GetFileName(artifact.SourceFilePath)})");
            sb.AppendLine();

            return sb.ToString();
        }

        /// <inheritdoc />
        public string GeneratePipelinePage(string pipelineFilePath)
        {
            _logger.LogInformation("Generating sub-page for pipeline definition: {SourceFile}", pipelineFilePath);
            var sb = new StringBuilder();

            sb.AppendLine("# Pipeline Definition");
            sb.AppendLine();
            sb.AppendLine("## Source");
            sb.AppendLine();
            sb.Append(GenerateFileContentBlock(pipelineFilePath));
            sb.AppendLine();

            sb.AppendLine("---\n");
            sb.AppendLine($"[View Raw Evidence](./attachments/{Path.GetFileName(pipelineFilePath)})");
            sb.AppendLine();

            return sb.ToString();
        }

        private string GenerateFileContentBlock(string filePath)
        {
            var sb = new StringBuilder();
            try
            {
                var fileContent = File.ReadAllText(filePath);
                var fileExtension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
                sb.AppendLine($"```{fileExtension}");
                sb.AppendLine(string.IsNullOrWhiteSpace(fileContent) ? "[File is empty]" : fileContent);
                sb.AppendLine("```");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read content from file: {Path}", filePath);
                sb.AppendLine("> [!ERROR]");
                sb.AppendLine($"> **Could not read file:** `{Path.GetFileName(filePath)}`");
                sb.AppendLine($"> **Error:** `{ex.Message}`");
            }
            return sb.ToString();
        }

        private static void BuildListWorkItems(StringBuilder sb, IEnumerable<WorkItem> workItems)
        {
            sb.AppendLine("| ID | Title | Type |");
            sb.AppendLine("|---|---|---|");
            foreach (var item in workItems.OrderBy(wi => wi.Id))
            {
                if (item.IsEnriched) sb.AppendLine($"| [{item.Id}]({item.Url}) | {item.Title} | {item.Type} |");
                else sb.AppendLine($"| `{item.Id}` | *(Data Unavailable)* | *(Data Unavailable)* |");
            }
        }

        private static void BuildCategorizedWorkItems(StringBuilder sb, IEnumerable<WorkItem> workItems)
        {
            var groupedItems = workItems.GroupBy(wi => wi.IsEnriched ? wi.Type : "Uncategorized").OrderBy(g => g.Key);
            foreach (var group in groupedItems)
            {
                sb.AppendLine($"### {group.Key}\n");
                foreach (var item in group.OrderBy(wi => wi.Id))
                {
                    if (item.IsEnriched) sb.AppendLine($"- [{item.Id}]({item.Url}) - {item.Title}");
                    else sb.AppendLine($"- `{item.Id}` - *(Data Unavailable)*");
                }
                sb.AppendLine();
            }
        }
    }
}