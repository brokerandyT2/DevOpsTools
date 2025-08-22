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
    public class PenpotConnectorService : IPenpotConnectorService
    {
        private readonly HttpClient _httpClient;
        private readonly IAppLogger _logger;

        public PenpotConnectorService(HttpClient httpClient, IAppLogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config)
        {
            _logger.LogStartPhase("Token Extraction (Penpot)");
            var apiToken = Environment.GetEnvironmentVariable("PENPOT_API_TOKEN");
            if (string.IsNullOrEmpty(apiToken)) throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, "Penpot API token not found in environment.");

            var serverUrl = Environment.GetEnvironmentVariable("TOKENS_PENPOT_SERVER_URL") ?? "https://design.penpot.app";
            _httpClient.BaseAddress = new Uri($"{serverUrl}/api/rpc/http/");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {apiToken}");

            try
            {
                var file = await GetFileAsync(config.Penpot.FileId);
                var colorTokens = file.Data.Colors.Select(c => new DesignTokenModel { Name = c.Name, Type = "color", Value = c.ColorValue }).ToList();
                var typoTokens = file.Data.Typographies.Select(t => new DesignTokenModel { Name = t.Name, Type = "typography", Value = new { fontFamily = t.FontFamily, fontSize = $"{t.FontSize}px" } }).ToList();

                var tokens = colorTokens.Concat(typoTokens).ToList();
                _logger.LogInfo($"✓ Extracted {tokens.Count} tokens ({colorTokens.Count} colors, {typoTokens.Count} typographies) from Penpot.");
                _logger.LogEndPhase("Token Extraction (Penpot)", true);

                return new TokenCollection { Name = $"Penpot Tokens - {file.Data.Name}", Source = "penpot", Tokens = tokens };
            }
            catch (Exception ex)
            {
                _logger.LogError("An unexpected error occurred while extracting tokens from Penpot.", ex);
                throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, $"Penpot token extraction failed: {ex.Message}", ex);
            }
        }

        private async Task<PenpotFileResponse> GetFileAsync(string fileId)
        {
            var payload = new { file_id = fileId };
            var response = await _httpClient.PostAsJsonAsync("get-file", payload);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PenpotFileResponse>() ?? throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, "Failed to parse Penpot file response.");
        }

        #region Penpot API DTOs
        private class PenpotFileResponse { [JsonPropertyName("data")] public PenpotFileData Data { get; set; } = new(); }
        private class PenpotFileData { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("colors")] public List<PenpotColor> Colors { get; set; } = new(); [JsonPropertyName("typographies")] public List<PenpotTypography> Typographies { get; set; } = new(); }
        private class PenpotColor { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("color")] public string ColorValue { get; set; } = ""; }
        private class PenpotTypography { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("font-family")] public string FontFamily { get; set; } = ""; [JsonPropertyName("font-size")] public string FontSize { get; set; } = ""; }
        #endregion
    }
}