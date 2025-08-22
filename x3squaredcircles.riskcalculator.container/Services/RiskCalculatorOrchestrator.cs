using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    public class RiskCalculatorOrchestrator : IRiskCalculatorOrchestrator
    {
        private readonly ILogger<RiskCalculatorOrchestrator> _logger;
        private readonly RiskCalculatorConfiguration _config;
        private readonly IGitAnalysisService _gitAnalysisService;
        private readonly IAnalysisStateService _analysisStateService;
        private readonly ICoreAnalysisEngine _coreAnalysisEngine;
        private readonly IDecisionEngine _decisionEngine;
        private readonly IOutputFormatter _outputFormatter;

        public RiskCalculatorOrchestrator(
            ILogger<RiskCalculatorOrchestrator> logger,
            RiskCalculatorConfiguration config,
            IGitAnalysisService gitAnalysisService,
            IAnalysisStateService analysisStateService,
            ICoreAnalysisEngine coreAnalysisEngine,
            IDecisionEngine decisionEngine,
            IOutputFormatter outputFormatter)
        {
            _logger = logger;
            _config = config;
            _gitAnalysisService = gitAnalysisService;
            _analysisStateService = analysisStateService;
            _coreAnalysisEngine = coreAnalysisEngine;
            _decisionEngine = decisionEngine;
            _outputFormatter = outputFormatter;
        }

        public async Task<int> RunAsync()
        {
            try
            {
                // Step 1: Load the previous state
                var previousState = await _analysisStateService.LoadStateAsync();

                // Step 2: Get the delta of changes since the last run
                var delta = await _gitAnalysisService.GetDeltaSinceLastRunAsync();

                // If there are no changes, we can exit early.
                if (delta.FromCommit == delta.ToCommit)
                {
                    _logger.LogInformation("No new commits found since last analysis. Exiting.");
                    return (int)RiskCalculatorExitCode.Pass;
                }

                // Step 3: Run the core analysis to get the new state
                var currentState = await _coreAnalysisEngine.RunAnalysisAsync(delta, previousState);

                // Step 4: Make the final decision based on the changes
                var finalResult = await _decisionEngine.MakeDecisionAsync(previousState, currentState);

                // Step 5: Log the output to the console
                await _outputFormatter.LogFinalOutputAsync(finalResult);

                // Step 6: Save the new state and update the Git tag
                var savedFilePath = await _analysisStateService.SaveStateAsync(currentState);
                await _gitAnalysisService.CommitAnalysisStateAndMoveTagAsync(savedFilePath);

                // Step 7: Return the appropriate exit code
                return (int)finalResult.Decision switch
                {
                    (int)AnalysisDecision.Pass => (int)RiskCalculatorExitCode.Pass,
                    (int)AnalysisDecision.Alert => (int)RiskCalculatorExitCode.Alert,
                    (int)AnalysisDecision.Fail => (int)RiskCalculatorExitCode.Fail,
                    _ => (int)RiskCalculatorExitCode.Pass,
                };
            }
            catch (RiskCalculatorException)
            {
                // Re-throw controlled exceptions to be caught by Program.cs
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during orchestration.");
                throw new RiskCalculatorException(RiskCalculatorExitCode.UnhandledException, "Orchestration failed with an unexpected error.", ex);
            }
        }
    }
}