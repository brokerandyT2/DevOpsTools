using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// Implements the logic for reading from and writing to the 'change-analysis.json' state file.
    /// </summary>
    public class AnalysisStateService : IAnalysisStateService
    {
        private readonly ILogger<AnalysisStateService> _logger;
        private readonly string _stateFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public AnalysisStateService(ILogger<AnalysisStateService> logger)
        {
            _logger = logger;
            _stateFilePath = Path.Combine("/src", "change-analysis.json");
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };
        }

        public async Task<AnalysisState> LoadStateAsync()
        {
            if (!File.Exists(_stateFilePath))
            {
                _logger.LogInformation("Analysis state file not found at '{FilePath}'. Assuming initial run.", _stateFilePath);
                return new AnalysisState(); // Return a new, empty state for the first run
            }

            try
            {
                _logger.LogInformation("Loading existing analysis state from '{FilePath}'.", _stateFilePath);
                var jsonContent = await File.ReadAllTextAsync(_stateFilePath);
                var state = JsonSerializer.Deserialize<AnalysisState>(jsonContent, _jsonOptions);
                if (state == null)
                {
                    _logger.LogWarning("Analysis state file is malformed or empty. Treating as initial run.");
                    return new AnalysisState();
                }
                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load or parse analysis state file. Treating as initial run.");
                // In case of a corrupted file, it's safer to start fresh than to fail the build.
                return new AnalysisState();
            }
        }

        public async Task<string> SaveStateAsync(AnalysisState state)
        {
            try
            {
                _logger.LogInformation("Saving updated analysis state to '{FilePath}'.", _stateFilePath);
                var jsonContent = JsonSerializer.Serialize(state, _jsonOptions);
                await File.WriteAllTextAsync(_stateFilePath, jsonContent);
                _logger.LogDebug("Successfully saved analysis state file.");
                return _stateFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save analysis state file.");
                throw new RiskCalculatorException(RiskCalculatorExitCode.FileIOFailure, "Failed to write analysis state file.", ex);
            }
        }
    }
}