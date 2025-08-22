using Microsoft.Extensions.Logging;
using System.Text;
using x3squaredcircles.RiskCalculator.Container.Models;

using Microsoft of Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    public class OutputFormatter : IOutputFormatter
    {
        private readonly ILogger<OutputFormatter> _logger;
        private readonly RiskCalculatorConfiguration _config;

        public OutputFormatter(ILogger<OutputFormatter> logger, RiskCalculatorConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task LogFinalOutputAsync(RiskAnalysisResult result)
        {
            LogInfoLevelOutput(result);

            if (_config.Logging.Verbose)
            {
                LogVerboseLevelOutput(result);
            }
            await Task.CompletedTask;
        }

        private void LogInfoLevelOutput(RiskAnalysisResult result)
        {
            var fromCommit = result.PreviousState.LastCommitHash != null ? result.PreviousState.LastCommitHash.Substring(0, 7) : "repository root";
            var toCommit = result.CurrentState.LastCommitHash.Substring(0, 7);

            _logger.LogInformation("Analysis complete from {FromCommit} to {ToCommit}", fromCommit, toCommit);

            switch (result.Decision)
            {
                case AnalysisDecision.Pass:
                    _logger.LogInformation("✅ PASS: No significant risk pattern changes detected.");
                    break;
                case AnalysisDecision.Alert:
                    _logger.LogWarning("🚨 ALERT: Pipeline paused due to risk pattern changes.");
                    break;
                case AnalysisDecision.Fail:
                    _logger.LogError("❌ FAIL: Pipeline stopped due to critical risk pattern changes.");
                    break;
            }

            foreach (var reason in result.Reasons)
            {
                _logger.LogInformation(reason);
            }
        }

        private void LogVerboseLevelOutput(RiskAnalysisResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine();

            // All-Time Risk Rankings
            sb.AppendLine("=== ALL TIME RISK RANKINGS (Top 10) ===");
            var rankedAreas = result.CurrentState.TrackedAreas
                .Where(a => a.CurrentRanking.HasValue)
                .OrderBy(a => a.CurrentRanking.Value);

            foreach (var area in rankedAreas.Take(10))
            {
                var churn = area.Metrics.TotalLinesAdded + area.Metrics.TotalLinesDeleted;
                sb.AppendLine($"🔥 #{area.CurrentRanking} ({area.Percentile:F1}%) {area.Path} │ {area.Metrics.TotalFilesChanged} files │ {churn} lines │ Score: {area.RiskScore:F2}");
            }
            sb.AppendLine("========================================");
            sb.AppendLine();

            // Blast Radius Analysis
            sb.AppendLine("=== BLAST RADIUS ANALYSIS (Top 3) ===");
            var topRiskAreas = rankedAreas.Take(3);
            foreach (var area in topRiskAreas)
            {
                var blastRadius = result.CurrentState.BlastRadius.FirstOrDefault(br => br.SourcePath == area.Path);
                if (blastRadius != null && blastRadius.CorrelatedPaths.Any())
                {
                    sb.AppendLine($"🔥 {area.Path} │ When changed, triggers:");
                    var topCorrelations = blastRadius.CorrelatedPaths.OrderByDescending(cp => cp.CorrelationScore).Take(3);
                    foreach (var correlation in topCorrelations)
                    {
                        var percentage = correlation.CorrelationScore * 100;
                        sb.AppendLine($"    ├─ {percentage:F1}% {correlation.Path} ({correlation.CooccurrenceCount} of {area.Metrics.TotalCommits} times)");
                    }
                }
            }
            sb.AppendLine("====================================");
            sb.AppendLine();

            _logger.LogInformation(sb.ToString());
        }
    }
}