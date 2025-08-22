using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;
using DesignTokenModel = x3squaredcircles.DesignToken.Generator.Models.DesignToken;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public class SketchConnectorService : ISketchConnectorService
    {
        private readonly HttpClient _httpClient;
        private readonly IAppLogger _logger;
        private const string SketchApiBaseUrl = "https://api.sketch.com/v1";

        public SketchConnectorService(HttpClient httpClient, IAppLogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config)
        {
            _logger.LogStartPhase("Token Extraction (Sketch)");
            var apiToken = Environment.GetEnvironmentVariable("SKETCH_API_TOKEN");
            if (string.IsNullOrEmpty(apiToken)) throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, "Sketch API token not found in environment.");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.sketch.v1+json");
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);

            try
            {
                var document = await GetDocumentAsync(config.Sketch.DocumentId);
                var colorTokens = await GetShareColorsAsync(document.Share.Id);

                var tokens = colorTokens.Select(c => new DesignTokenModel
                {
                    Name = c.Name,
                    Type = "color",
                    Category = "color",
                    Value = c.Color.Hex,
                    Description = $"Sketch shared color from document '{document.Name}'"
                }).ToList();

                _logger.LogInfo($"✓ Extracted {tokens.Count} shared colors from Sketch document '{document.Name}'.");
                _logger.LogEndPhase("Token Extraction (Sketch)", true);

                return new TokenCollection
                {
                    Name = $"Sketch Tokens - {document.Name}",
                    Source = "sketch",
                    Tokens = tokens,
                    Metadata = new Dictionary<string, object> { ["documentId"] = config.Sketch.DocumentId, ["shareId"] = document.Share.Id }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("An unexpected error occurred while extracting tokens from Sketch.", ex);
                throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, $"Sketch token extraction failed: {ex.Message}", ex);
            }
        }

        private async Task<SketchDocument> GetDocumentAsync(string documentId)
        {
            var url = $"{SketchApiBaseUrl}/documents/{documentId}";
            var response = await _httpClient.GetFromJsonAsync<SketchDocumentResponse>(url);
            if (response?.Data == null) throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, "Failed to retrieve Sketch document metadata.");
            return response.Data;
        }

        private async Task<List<SketchColor>> GetShareColorsAsync(string shareId)
        {
            var url = $"{SketchApiBaseUrl}/shares/{shareId}/colors";
            var response = await _httpClient.GetFromJsonAsync<SketchColorsResponse>(url);
            return response?.Data?.Colors ?? new List<SketchColor>();
        }

        #region Sketch API DTOs
        private class SketchDocumentResponse { [JsonPropertyName("data")] public SketchDocument? Data { get; set; } }
        private class SketchDocument { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("share")] public SketchShare Share { get; set; } = new(); }
        private class SketchShare { [JsonPropertyName("id")] public string Id { get; set; } = ""; }
        private class SketchColorsResponse { [JsonPropertyName("data")] public SketchColorsData? Data { get; set; } }
        private class SketchColorsData { [JsonPropertyName("colors")] public List<SketchColor> Colors { get; set; } = new(); }
        private class SketchColor { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("color")] public SketchColorValue Color { get; set; } = new(); }
        private class SketchColorValue { [JsonPropertyName("hex")] public string Hex { get; set; } = ""; }
        #endregion
    }
}