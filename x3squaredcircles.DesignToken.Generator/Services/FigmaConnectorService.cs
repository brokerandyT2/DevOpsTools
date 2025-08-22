using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;
using DesignTokenModel = x3squaredcircles.DesignToken.Generator.Models.DesignToken;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public class FigmaConnectorService : IFigmaConnectorService
    {
        private readonly HttpClient _httpClient;
        private readonly IAppLogger _logger;
        private const string FigmaApiBaseUrl = "https://api.figma.com/v1";

        public FigmaConnectorService(HttpClient httpClient, IAppLogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config)
        {
            try
            {
                _logger.LogInfo($"Extracting design tokens from Figma file: {config.Figma.Url}");
                var apiToken = Environment.GetEnvironmentVariable("FIGMA_API_TOKEN");
                if (string.IsNullOrEmpty(apiToken)) throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, "Figma API token not found in environment.");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-Figma-Token", apiToken);

                var fileId = ExtractFileIdFromUrl(config.Figma.Url);
                if (string.IsNullOrEmpty(fileId)) throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, "Invalid Figma URL format.");

                var fileTask = GetFigmaFileContentAsync(fileId);
                var variablesTask = GetFigmaVariablesAsync(fileId);
                await Task.WhenAll(fileTask, variablesTask);

                var figmaFile = await fileTask;
                var figmaVariables = await variablesTask;

                if (figmaFile == null) throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, "Failed to retrieve Figma file content.");

                var variableTokens = ConvertVariablesToTokens(figmaVariables);
                var styleTokens = ConvertStylesToTokens(figmaFile);

                var finalTokens = new Dictionary<string, DesignTokenModel>();
                foreach (var token in styleTokens) { finalTokens[token.Name] = token; }
                foreach (var token in variableTokens) { finalTokens[token.Name] = token; }

                _logger.LogInfo($"✓ Extracted {finalTokens.Count} total unique tokens from Figma ({variableTokens.Count} from Variables, {styleTokens.Count} from legacy Styles).");

                return new TokenCollection
                {
                    Name = figmaFile.RootElement.GetProperty("name").GetString() ?? "Figma Design Tokens",
                    Source = "figma",
                    Tokens = finalTokens.Values.ToList(),
                    Metadata = new Dictionary<string, object> { ["figmaFileId"] = fileId }
                };
            }
            catch (DesignTokenException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError("An unexpected error occurred while extracting tokens from Figma.", ex);
                throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, $"Figma token extraction failed: {ex.Message}", ex);
            }
        }

        private async Task<JsonDocument?> GetFigmaFileContentAsync(string fileId)
        {
            var url = $"{FigmaApiBaseUrl}/files/{fileId}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Figma API error (files endpoint) ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}", null);
                return null;
            }
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        }

        private async Task<FigmaVariablesResponse?> GetFigmaVariablesAsync(string fileId)
        {
            var url = $"{FigmaApiBaseUrl}/files/{fileId}/variables/local";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Figma API error (variables endpoint) ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}", null);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<FigmaVariablesResponse>();
        }

        private string ExtractFileIdFromUrl(string figmaUrl)
        {
            try
            {
                var uri = new Uri(figmaUrl);
                var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (pathSegments.Length >= 2 && (pathSegments[0] == "design" || pathSegments[0] == "file")) return pathSegments[1];
                return string.Empty;
            }
            catch { return string.Empty; }
        }

        private List<DesignTokenModel> ConvertStylesToTokens(JsonDocument? figmaFile)
        {
            var tokens = new List<DesignTokenModel>();
            if (figmaFile == null || !figmaFile.RootElement.TryGetProperty("styles", out var stylesElement)) return tokens;

            foreach (var style in stylesElement.EnumerateObject())
            {
                var name = style.Value.GetProperty("name").GetString();
                if (string.IsNullOrEmpty(name)) continue;

                var token = new DesignTokenModel { Name = name, Type = style.Value.GetProperty("styleType").GetString()?.ToLowerInvariant() ?? "other" };
                tokens.Add(token);
            }
            return tokens;
        }

        private List<DesignTokenModel> ConvertVariablesToTokens(FigmaVariablesResponse? response)
        {
            var tokens = new List<DesignTokenModel>();
            if (response?.Meta?.Variables == null) return tokens;

            foreach (var variable in response.Meta.Variables.Values)
            {
                var modeId = variable.ValuesByMode.Keys.FirstOrDefault();
                if (modeId == null || !variable.ValuesByMode.TryGetValue(modeId, out var value)) continue;

                var token = variable.ResolvedType switch
                {
                    "COLOR" => ConvertColorVariableToToken(variable, value),
                    "FLOAT" => ConvertFloatVariableToToken(variable, value),
                    _ => null
                };
                if (token != null) tokens.Add(token);
            }
            return tokens;
        }

        private DesignTokenModel ConvertColorVariableToToken(FigmaVariable variable, object value)
        {
            var color = JsonSerializer.Deserialize<FigmaColorValue>(JsonSerializer.Serialize(value));
            var hex = $"#{ToHex(color.R)}{ToHex(color.G)}{ToHex(color.B)}";
            return new DesignTokenModel { Name = variable.Name, Type = "color", Value = hex, Description = variable.Description };
        }

        private DesignTokenModel ConvertFloatVariableToToken(FigmaVariable variable, object value)
        {
            var floatValue = (float)JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value)).GetDouble();
            var type = variable.Name.Contains("spacing", StringComparison.OrdinalIgnoreCase) ? "spacing" : "sizing";
            return new DesignTokenModel { Name = variable.Name, Type = type, Value = $"{floatValue}px", Description = variable.Description };
        }

        private string ToHex(float val) => ((int)(val * 255)).ToString("X2");

        #region Figma API DTOs
        private class FigmaVariablesResponse { [JsonPropertyName("meta")] public FigmaVariablesMeta? Meta { get; set; } }
        private class FigmaVariablesMeta { [JsonPropertyName("variables")] public Dictionary<string, FigmaVariable>? Variables { get; set; } }
        private class FigmaVariable { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("description")] public string Description { get; set; } = ""; [JsonPropertyName("resolvedType")] public string ResolvedType { get; set; } = ""; [JsonPropertyName("valuesByMode")] public Dictionary<string, object> ValuesByMode { get; set; } = new(); }
        private class FigmaColorValue { [JsonPropertyName("r")] public float R { get; set; } [JsonPropertyName("g")] public float G { get; set; } [JsonPropertyName("b")] public float B { get; set; } [JsonPropertyName("a")] public float A { get; set; } }
        #endregion
    }
}