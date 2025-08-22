using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    public class DecisionEngine : IDecisionEngine
    {
        private readonly ILogger<DecisionEngine> _logger;
        private readonly RiskCalculatorConfiguration _config;

        public DecisionEngine(ILogger<DecisionEngine> logger, RiskCalculatorConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task<RiskAnalysisResult> MakeDecisionAsync(AnalysisState previousState, AnalysisState currentState)
        {
            _logger.LogInformation("Making final PASS/ALERT/FAIL decision based on risk pattern changes...");

            var result = new RiskAnalysisResult
            {
                PreviousState = previousState,
                CurrentState = currentState,
                Decision = AnalysisDecision.Pass // Default to Pass
            };

            var previousRankings = previousState.TrackedAreas
                .Where(a => a.CurrentRanking.HasValue)
                .ToDictionary(a => a.Path, a => a.CurrentRanking.Value);

            var currentRankings = currentState.TrackedAreas
                .Where(a => a.CurrentRanking.HasValue)
                .ToDictionary(a => a.Path, a => a.CurrentRanking.Value);

            foreach (var currentArea in currentState.TrackedAreas.Where(a => a.CurrentRanking.HasValue))
            {
                if (previousRankings.TryGetValue(currentArea.Path, out var previousRank))
                {
                    // Area existed in previous rankings
                    var movement = previousRank - currentArea.CurrentRanking.Value;
                    if (movement > 0) // Moved up in rank (e.g., from #5 to #2 is a movement of 3)
                    {
                        result.RankingChanges.Add(new RiskRankingChange
                        {
                            Path = currentArea.Path,
                            PreviousRanking = previousRank,
                            CurrentRanking = currentArea.CurrentRanking,
                            Type = ChangeType.MovedUp
                        });

                        if (movement >= _config.Analysis.FailThreshold)
                        {
                            result.Decision = AnalysisDecision.Fail;
                            result.Reasons.Add($"CRITICAL SHIFT: Area '{currentArea.Path}' jumped from #{previousRank} to #{currentArea.CurrentRanking} (moved up {movement} positions, exceeds fail threshold of {_config.Analysis.FailThreshold}).");
                        }
                        else if (movement >= _config.Analysis.AlertThreshold)
                        {
                            // If we aren't already failing, we can alert
                            if (result.Decision != AnalysisDecision.Fail)
                            {
                                result.Decision = AnalysisDecision.Alert;
                            }
                            result.Reasons.Add($"RISK SHIFT: Area '{currentArea.Path}' moved from #{previousRank} to #{currentArea.CurrentRanking} (moved up {movement} positions, exceeds alert threshold of {_config.Analysis.AlertThreshold}).");
                        }
                    }
                }
                else
                {
                    // Area is new to the rankings
                    result.RankingChanges.Add(new RiskRankingChange
                    {
                        Path = currentArea.Path,
                        PreviousRanking = null,
                        CurrentRanking = currentArea.CurrentRanking,
                        Type = ChangeType.NewEntry
                    });

                    if (_config.Analysis.AlertOnNewEntries)
                    {
                        if (result.Decision != AnalysisDecision.Fail)
                        {
                            result.Decision = AnalysisDecision.Alert;
                        }
                        result.Reasons.Add($"NEW HOTSPOT: Area '{currentArea.Path}' entered the risk rankings at #{currentArea.CurrentRanking}.");
                    }
                }
            }

            if (result.Decision == AnalysisDecision.Pass)
            {
                result.Reasons.Add("No significant risk pattern changes detected.");
            }

            _logger.LogInformation("Decision complete: {Decision}", result.Decision);

            await Task.CompletedTask;
            return result;
        }
    }
}