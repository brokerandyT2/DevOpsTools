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
    public class ZeplinConnectorService : IZeplinConnectorService
    {
        private readonly HttpClient _httpClient;
        private readonly IAppLogger _logger;
        private const string ZeplinApiBaseUrl = "https://api.zeplin.dev/v1";

        public ZeplinConnectorService(HttpClient httpClient, IAppLogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config)
        {
            _logger.LogStartPhase("Token Extraction (Zeplin)");
            var apiToken = Environment.GetEnvironmentVariable("ZEPLIN_API_TOKEN");
            if (string.IsNullOrEmpty(apiToken)) throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, "Zeplin API token not found in environment.");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");

            try
            {
                var colors = await GetProjectColorsAsync(config.Zeplin.ProjectId);
                var textStyles = await GetProjectTextStylesAsync(config.Zeplin.ProjectId);

                var colorTokens = colors.Select(c => new DesignTokenModel { Name = c.Name, Type = "color", Value = c.ToHex() });
                var textTokens = textStyles.Select(t => new DesignTokenModel { Name = t.Name, Type = "typography", Value = new { fontFamily = t.FontFamily, fontSize = $"{t.FontSize}px" } });

                var tokens = colorTokens.Concat(textTokens).ToList();
                _logger.LogInfo($"✓ Extracted {tokens.Count} tokens ({colors.Count} colors, {textStyles.Count} text styles) from Zeplin.");
                _logger.LogEndPhase("Token Extraction (Zeplin)", true);

                return new TokenCollection { Name = "Zeplin Design Tokens", Source = "zeplin", Tokens = tokens };
            }
            catch (Exception ex)
            {
                _logger.LogError("An unexpected error occurred while extracting tokens from Zeplin.", ex);
                throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, $"Zeplin token extraction failed: {ex.Message}", ex);
            }
        }

        private async Task<List<ZeplinColor>> GetProjectColorsAsync(string projectId)
        {
            var url = $"{ZeplinApiBaseUrl}/projects/{projectId}/colors";
            return await _httpClient.GetFromJsonAsync<List<ZeplinColor>>(url) ?? new List<ZeplinColor>();
        }

        private async Task<List<ZeplinTextStyle>> GetProjectTextStylesAsync(string projectId)
        {
            var url = $"{ZeplinApiBaseUrl}/projects/{projectId}/text_styles";
            return await _httpClient.GetFromJsonAsync<List<ZeplinTextStyle>>(url) ?? new List<ZeplinTextStyle>();
        }

        #region Zeplin API DTOs
        private class ZeplinColor { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("r")] public int R { get; set; } [JsonPropertyName("g")] public int G { get; set; } [JsonPropertyName("b")] public int B { get; set; } public string ToHex() => $"#{R:X2}{G:X2}{B:X2}"; }
        private class ZeplinTextStyle { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("font_family")] public string FontFamily { get; set; } = ""; [JsonPropertyName("font_size")] public int FontSize { get; set; } }
        #endregion
    }
}