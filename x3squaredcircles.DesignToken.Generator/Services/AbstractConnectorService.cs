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
    public class AbstractConnectorService : IAbstractConnectorService
    {
        private readonly HttpClient _httpClient;
        private readonly IAppLogger _logger;
        private const string AbstractApiBaseUrl = "https://api.goabstract.com";

        public AbstractConnectorService(HttpClient httpClient, IAppLogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config)
        {
            _logger.LogStartPhase("Token Extraction (Abstract)");
            var apiToken = Environment.GetEnvironmentVariable("ABSTRACT_API_TOKEN");
            if (string.IsNullOrEmpty(apiToken)) throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, "Abstract API token not found in environment.");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Abstract-Api-Version", "17");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");

            try
            {
                var library = await GetLibraryAsync(config.Abstract.ProjectId);
                var colors = library.Assets.Where(a => a.LayerTypeName == "color").Select(a => new DesignTokenModel
                {
                    Name = a.Name,
                    Type = "color",
                    Value = a.Color.Hex
                }).ToList();

                _logger.LogInfo($"✓ Extracted {colors.Count} color assets from Abstract library '{library.Name}'.");
                _logger.LogEndPhase("Token Extraction (Abstract)", true);

                return new TokenCollection { Name = $"Abstract Library - {library.Name}", Source = "abstract", Tokens = colors };
            }
            catch (Exception ex)
            {
                _logger.LogError("An unexpected error occurred while extracting tokens from Abstract.", ex);
                throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, $"Abstract token extraction failed: {ex.Message}", ex);
            }
        }

        private async Task<AbstractLibrary> GetLibraryAsync(string libraryId)
        {
            var url = $"{AbstractApiBaseUrl}/libraries/{libraryId}/assets";
            var response = await _httpClient.GetFromJsonAsync<AbstractLibraryResponse>(url);
            return response?.Library ?? throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, "Failed to retrieve Abstract library assets.");
        }

        #region Abstract API DTOs
        private class AbstractLibraryResponse { [JsonPropertyName("library")] public AbstractLibrary? Library { get; set; } }
        private class AbstractLibrary { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("assets")] public List<AbstractAsset> Assets { get; set; } = new(); }
        private class AbstractAsset { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("layerTypeName")] public string LayerTypeName { get; set; } = ""; [JsonPropertyName("color")] public AbstractColor Color { get; set; } = new(); }
        private class AbstractColor { [JsonPropertyName("hex")] public string Hex { get; set; } = ""; }
        #endregion
    }
}