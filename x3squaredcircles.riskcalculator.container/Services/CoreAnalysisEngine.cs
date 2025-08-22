using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    public class CoreAnalysisEngine : ICoreAnalysisEngine
    {
        private readonly ILogger<CoreAnalysisEngine> _logger;
        private readonly RiskCalculatorConfiguration _config;
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public CoreAnalysisEngine(ILogger<CoreAnalysisEngine> logger, RiskCalculatorConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task<AnalysisState> RunAnalysisAsync(GitDelta delta, AnalysisState previousState)
        {
            _logger.LogInformation("Starting core risk analysis engine...");

            var newState = InitializeNewState(previousState, delta.ToCommit);
            var areasToProcess = GetLeafDirectoriesFromDelta(delta);

            foreach (var areaPath in areasToProcess)
            {
                var previousArea = previousState.TrackedAreas.FirstOrDefault(a => a.Path == areaPath);
                var newArea = UpdateAreaMetrics(previousArea, areaPath, delta);
                newState.TrackedAreas.Add(newArea);
            }

            UpdateBlastRadius(newState, delta);
            CalculateRiskScoresAndRankings(newState);

            _logger.LogInformation("Core risk analysis complete. Processed {AreaCount} areas.", newState.TrackedAreas.Count);

            await Task.CompletedTask;
            return newState;
        }

        private AnalysisState InitializeNewState(AnalysisState previousState, string newCommitHash)
        {
            // Start with a deep copy of the previous state to preserve historical data
            // for areas that haven't changed in this delta.
            return new AnalysisState
            {
                ToolVersion = ToolVersion,
                AnalysisDateUtc = DateTime.UtcNow,
                LastCommitHash = newCommitHash,
                TrackedAreas = previousState.TrackedAreas.Select(area => new TrackedArea
                {
                    Path = area.Path,
                    Metrics = new AreaMetrics
                    {
                        TotalCommits = area.Metrics.TotalCommits,
                        TotalLinesAdded = area.Metrics.TotalLinesAdded,
                        TotalLinesDeleted = area.Metrics.TotalLinesDeleted,
                        TotalFilesChanged = area.Metrics.TotalFilesChanged,
                        FirstCommitDateUtc = area.Metrics.FirstCommitDateUtc,
                        LastCommitDateUtc = area.Metrics.LastCommitDateUtc
                    }
                }).ToList(),
                BlastRadius = previousState.BlastRadius.Select(br => new BlastRadiusAnalysis
                {
                    SourcePath = br.SourcePath,
                    CorrelatedPaths = br.CorrelatedPaths.Select(cp => new CorrelatedPath
                    {
                        Path = cp.Path,
                        CorrelationScore = cp.CorrelationScore,
                        CooccurrenceCount = cp.CooccurrenceCount
                    }).ToList()
                }).ToList()
            };
        }

        private HashSet<string> GetLeafDirectoriesFromDelta(GitDelta delta)
        {
            var allDirs = new HashSet<string>();
            var nonLeafDirs = new HashSet<string>();

            foreach (var change in delta.Changes)
            {
                var dir = Path.GetDirectoryName(change.Path);
                if (string.IsNullOrEmpty(dir)) continue;

                allDirs.Add(dir);

                // Add all parent directories to the non-leaf set
                var parent = Path.GetDirectoryName(dir);
                while (!string.IsNullOrEmpty(parent))
                {
                    nonLeafDirs.Add(parent);
                    parent = Path.GetDirectoryName(parent);
                }
            }

            // The leaf directories are those in allDirs that are not in nonLeafDirs
            allDirs.ExceptWith(nonLeafDirs);

            // Apply exclusions
            var excludedPrefixes = _config.Analysis.ExcludedAreas.Select(e => e.Replace('\\', '/').TrimEnd('/') + "/");
            allDirs.RemoveWhere(dir => excludedPrefixes.Any(prefix => (dir.Replace('\\', '/') + "/").StartsWith(prefix)));

            return allDirs;
        }

        private TrackedArea UpdateAreaMetrics(TrackedArea previousArea, string areaPath, GitDelta delta)
        {
            var areaChanges = delta.Changes.Where(c => Path.GetDirectoryName(c.Path) == areaPath).ToList();
            var newArea = previousArea ?? new TrackedArea { Path = areaPath };

            // Incremental update of metrics
            newArea.Metrics.TotalCommits += 1; // Simplified: assume 1 commit per delta run for velocity
            newArea.Metrics.TotalLinesAdded += areaChanges.Sum(c => c.LinesAdded);
            newArea.Metrics.TotalLinesDeleted += areaChanges.Sum(c => c.LinesDeleted);
            newArea.Metrics.TotalFilesChanged += areaChanges.Count;

            if (newArea.Metrics.FirstCommitDateUtc == default)
            {
                newArea.Metrics.FirstCommitDateUtc = DateTime.UtcNow;
            }
            newArea.Metrics.LastCommitDateUtc = DateTime.UtcNow;

            return newArea;
        }

        private void UpdateBlastRadius(AnalysisState state, GitDelta delta)
        {
            // Simplified co-change analysis
            var uniquePathsInDelta = delta.CochangedPaths.Distinct().ToList();
            if (uniquePathsInDelta.Count < 2) return;

            for (int i = 0; i < uniquePathsInDelta.Count; i++)
            {
                for (int j = i + 1; j < uniquePathsInDelta.Count; j++)
                {
                    var pathA = uniquePathsInDelta[i];
                    var pathB = uniquePathsInDelta[j];

                    UpdateCorrelation(state, pathA, pathB);
                    UpdateCorrelation(state, pathB, pathA);
                }
            }
        }

        private void UpdateCorrelation(AnalysisState state, string sourcePath, string targetPath)
        {
            var blastRadius = state.BlastRadius.FirstOrDefault(br => br.SourcePath == sourcePath);
            if (blastRadius == null)
            {
                blastRadius = new BlastRadiusAnalysis { SourcePath = sourcePath };
                state.BlastRadius.Add(blastRadius);
            }

            var correlatedPath = blastRadius.CorrelatedPaths.FirstOrDefault(cp => cp.Path == targetPath);
            if (correlatedPath == null)
            {
                correlatedPath = new CorrelatedPath { Path = targetPath };
                blastRadius.CorrelatedPaths.Add(correlatedPath);
            }

            correlatedPath.CooccurrenceCount++;

            var sourceArea = state.TrackedAreas.FirstOrDefault(a => a.Path == sourcePath);
            if (sourceArea != null)
            {
                correlatedPath.CorrelationScore = (double)correlatedPath.CooccurrenceCount / sourceArea.Metrics.TotalCommits;
            }
        }

        private void CalculateRiskScoresAndRankings(AnalysisState state)
        {
            if (!state.TrackedAreas.Any()) return;

            foreach (var area in state.TrackedAreas)
            {
                // Simple risk scoring algorithm: prioritizes recent, large changes.
                var ageInDays = (DateTime.UtcNow - area.Metrics.LastCommitDateUtc).TotalDays;
                var churn = area.Metrics.TotalLinesAdded + area.Metrics.TotalLinesDeleted;
                var frequency = (double)area.Metrics.TotalCommits / (DateTime.UtcNow - area.Metrics.FirstCommitDateUtc).TotalDays;

                // Recency is heavily weighted. A change today is riskier than a change 30 days ago.
                var recencyWeight = Math.Exp(-0.1 * ageInDays);

                area.RiskScore = (churn * frequency) * recencyWeight;
            }

            // Calculate Percentiles
            var sortedScores = state.TrackedAreas.Select(a => a.RiskScore).OrderBy(s => s).ToList();
            for (int i = 0; i < state.TrackedAreas.Count; i++)
            {
                var area = state.TrackedAreas[i];
                var index = sortedScores.BinarySearch(area.RiskScore);
                if (index < 0) index = ~index;

                area.Percentile = (double)(index + 1) / sortedScores.Count * 100;
            }

            // Assign Rankings to areas above the minimum percentile
            var rankedAreas = state.TrackedAreas
                .Where(a => a.Percentile >= _config.Analysis.MinimumPercentile)
                .OrderByDescending(a => a.RiskScore)
                .ToList();

            for (int i = 0; i < rankedAreas.Count; i++)
            {
                rankedAreas[i].CurrentRanking = i + 1;
            }
        }
    }
}