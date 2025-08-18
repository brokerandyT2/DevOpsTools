using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;
using DesignTokenModel = x3squaredcircles.DesignToken.Generator.Models.DesignToken;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IFigmaConnectorService
    {
        Task<TokenCollection> ExtractTokensAsync(DesignTokenConfiguration config);
    }

    public class FigmaConnectorService : IFigmaConnectorService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FigmaConnectorService> _logger;
        private const string FigmaApiBaseUrl = "https://api.figma.com/v1";

        public FigmaConnectorService(HttpClient httpClient, ILogger<FigmaConnectorService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractTokensAsync(DesignTokenConfiguration config)
        {
            try
            {
                _logger.LogInformation("Extracting design tokens from Figma file: {FigmaUrl}", config.DesignPlatform.FigmaUrl);

                // Get API token from environment (set by KeyVaultService)
                var apiToken = Environment.GetEnvironmentVariable("FIGMA_API_TOKEN");
                if (string.IsNullOrEmpty(apiToken))
                {
                    throw new DesignTokenException(DesignTokenExitCode.AuthenticationFailure,
                        "Figma API token not found. Ensure FIGMA_TOKEN_VAULT_KEY is configured and key vault access is working.");
                }

                // Configure HTTP client
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-Figma-Token", apiToken);

                // Extract file ID from URL
                var fileId = ExtractFileIdFromUrl(config.DesignPlatform.FigmaUrl);
                if (string.IsNullOrEmpty(fileId))
                {
                    throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration,
                        "Invalid Figma URL format. Expected format: https://www.figma.com/design/{fileId}/...");
                }

                // Get file metadata
                var fileInfo = await GetFileInfoAsync(fileId, config.DesignPlatform.FigmaVersionId);

                // Extract design tokens from file
                var tokens = await ExtractDesignTokensFromFileAsync(fileInfo, config);

                _logger.LogInformation("✓ Extracted {TokenCount} design tokens from Figma", tokens.Count);

                return new TokenCollection
                {
                    Name = fileInfo.Name ?? "Figma Design Tokens",
                    Version = "1.0.0", // Could be derived from Figma version
                    Source = "figma",
                    Tokens = tokens,
                    Metadata = new Dictionary<string, object>
                    {
                        ["figma_file_id"] = fileId,
                        ["figma_file_name"] = fileInfo.Name ?? "",
                        ["figma_version"] = config.DesignPlatform.FigmaVersionId ?? "latest",
                        ["extraction_time"] = DateTime.UtcNow
                    }
                };
            }
            catch (DesignTokenException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract tokens from Figma");
                throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure,
                    $"Figma token extraction failed: {ex.Message}", ex);
            }
        }

        private string ExtractFileIdFromUrl(string figmaUrl)
        {
            try
            {
                var uri = new Uri(figmaUrl);
                var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // Expected format: /design/{fileId}/... or /file/{fileId}/...
                if (pathSegments.Length >= 2 &&
                    (pathSegments[0].Equals("design", StringComparison.OrdinalIgnoreCase) ||
                     pathSegments[0].Equals("file", StringComparison.OrdinalIgnoreCase)))
                {
                    return pathSegments[1];
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<FigmaFileInfo> GetFileInfoAsync(string fileId, string? versionId = null)
        {
            try
            {
                var url = $"{FigmaApiBaseUrl}/files/{fileId}";
                if (!string.IsNullOrEmpty(versionId))
                {
                    url += $"?version={versionId}";
                }

                _logger.LogDebug("Fetching Figma file info: {Url}", url);

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Figma API error ({response.StatusCode}): {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var fileResponse = JsonSerializer.Deserialize<FigmaFileResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (fileResponse?.Document == null)
                {
                    throw new Exception("Invalid Figma file response - missing document");
                }

                return new FigmaFileInfo
                {
                    Name = fileResponse.Name,
                    Document = fileResponse.Document,
                    Styles = fileResponse.Styles ?? new Dictionary<string, FigmaStyle>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Figma file info for {FileId}", fileId);
                throw;
            }
        }

        private async Task<List<DesignTokenModel>> ExtractDesignTokensFromFileAsync(FigmaFileInfo fileInfo, DesignTokenConfiguration config)
        {
            var tokens = new List<DesignTokenModel>();

            // Extract tokens from Figma styles (colors, text styles, etc.)
            tokens.AddRange(await ExtractStyleTokensAsync(fileInfo.Styles));

            // Extract tokens from local variables (if available)
            tokens.AddRange(await ExtractVariableTokensAsync(fileInfo));

            // Extract tokens from specific nodes if FigmaNodeId is specified
            if (!string.IsNullOrEmpty(config.DesignPlatform.FigmaNodeId))
            {
                tokens.AddRange(await ExtractNodeTokensAsync(fileInfo.Document, config.DesignPlatform.FigmaNodeId));
            }

            return tokens;
        }

        private async Task<List<DesignTokenModel>> ExtractStyleTokensAsync(Dictionary<string, FigmaStyle> styles)
        {
            var tokens = new List<DesignTokenModel>();

            foreach (var style in styles)
            {
                try
                {
                    var token = ConvertStyleToToken(style.Key, style.Value);
                    if (token != null)
                    {
                        tokens.Add(token);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert Figma style to token: {StyleId}", style.Key);
                }
            }

            return tokens;
        }

        private async Task<List<DesignTokenModel>> ExtractVariableTokensAsync(FigmaFileInfo fileInfo)
        {
            var tokens = new List<DesignTokenModel>();

            // Figma variables are a newer feature - would need to call variables API
            // For now, return empty list as this requires additional API calls
            _logger.LogDebug("Variable token extraction not implemented - would require additional Figma API calls");

            return tokens;
        }

        private async Task<List<DesignTokenModel>> ExtractNodeTokensAsync(FigmaNode document, string nodeId)
        {
            var tokens = new List<DesignTokenModel>();

            // Find the specific node and extract design tokens from it
            var targetNode = FindNodeById(document, nodeId);
            if (targetNode != null)
            {
                tokens.AddRange(ExtractTokensFromNode(targetNode));
            }

            return tokens;
        }

        private FigmaNode? FindNodeById(FigmaNode node, string nodeId)
        {
            if (node.Id == nodeId)
            {
                return node;
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    var found = FindNodeById(child, nodeId);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private List<DesignTokenModel> ExtractTokensFromNode(FigmaNode node)
        {
            var tokens = new List<DesignTokenModel>();

            // Extract color tokens from fills
            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Type == "SOLID" && fill.Color != null)
                    {
                        tokens.Add(new DesignTokenModel
                        {
                            Name = $"{node.Name}_fill".Replace(" ", "_").ToLowerInvariant(),
                            Type = "color",
                            Category = "color",
                            Value = ConvertFigmaColorToHex(fill.Color),
                            Attributes = new Dictionary<string, object>
                            {
                                ["opacity"] = fill.Opacity ?? 1.0,
                                ["source_node"] = node.Id ?? ""
                            }
                        });
                    }
                }
            }

            // Extract text style tokens
            if (node.Style != null)
            {
                tokens.Add(new DesignTokenModel
                {
                    Name = $"{node.Name}_typography".Replace(" ", "_").ToLowerInvariant(),
                    Type = "typography",
                    Category = "typography",
                    Value = new Dictionary<string, object>
                    {
                        ["fontFamily"] = node.Style.FontFamily ?? "inherit",
                        ["fontSize"] = node.Style.FontSize ?? 16,
                        ["fontWeight"] = node.Style.FontWeight ?? 400,
                        ["lineHeight"] = node.Style.LineHeightPx?.ToString() ?? "normal"
                    },
                    Attributes = new Dictionary<string, object>
                    {
                        ["source_node"] = node.Id ?? ""
                    }
                });
            }

            // Recursively extract from children
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    tokens.AddRange(ExtractTokensFromNode(child));
                }
            }

            return tokens;
        }

        private DesignTokenModel? ConvertStyleToToken(string styleId, FigmaStyle style)
        {
            switch (style.StyleType?.ToUpperInvariant())
            {
                case "FILL":
                    return new DesignTokenModel
                    {
                        Name = SanitizeTokenName(style.Name ?? styleId),
                        Type = "color",
                        Category = "color",
                        Value = "#000000", // Would need additional API call to get actual color
                        Description = style.Description,
                        Attributes = new Dictionary<string, object>
                        {
                            ["figma_style_id"] = styleId,
                            ["figma_style_type"] = style.StyleType ?? ""
                        }
                    };

                case "TEXT":
                    return new DesignTokenModel
                    {
                        Name = SanitizeTokenName(style.Name ?? styleId),
                        Type = "typography",
                        Category = "typography",
                        Value = new Dictionary<string, object>
                        {
                            ["fontFamily"] = "inherit",
                            ["fontSize"] = 16,
                            ["fontWeight"] = 400
                        },
                        Description = style.Description,
                        Attributes = new Dictionary<string, object>
                        {
                            ["figma_style_id"] = styleId,
                            ["figma_style_type"] = style.StyleType ?? ""
                        }
                    };

                case "EFFECT":
                    return new DesignTokenModel
                    {
                        Name = SanitizeTokenName(style.Name ?? styleId),
                        Type = "shadow",
                        Category = "effect",
                        Value = new Dictionary<string, object>
                        {
                            ["offsetX"] = 0,
                            ["offsetY"] = 2,
                            ["blur"] = 4,
                            ["color"] = "rgba(0,0,0,0.1)"
                        },
                        Description = style.Description,
                        Attributes = new Dictionary<string, object>
                        {
                            ["figma_style_id"] = styleId,
                            ["figma_style_type"] = style.StyleType ?? ""
                        }
                    };

                default:
                    _logger.LogDebug("Unknown Figma style type: {StyleType}", style.StyleType);
                    return null;
            }
        }

        private string ConvertFigmaColorToHex(FigmaColor color)
        {
            var r = (int)Math.Round((color.R ?? 0) * 255);
            var g = (int)Math.Round((color.G ?? 0) * 255);
            var b = (int)Math.Round((color.B ?? 0) * 255);

            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private string SanitizeTokenName(string name)
        {
            return name.Replace(" ", "_")
                      .Replace("-", "_")
                      .Replace("/", "_")
                      .ToLowerInvariant();
        }

        // Figma API response models
        private class FigmaFileResponse
        {
            public string? Name { get; set; }
            public FigmaNode? Document { get; set; }
            public Dictionary<string, FigmaStyle>? Styles { get; set; }
        }

        private class FigmaFileInfo
        {
            public string? Name { get; set; }
            public FigmaNode Document { get; set; } = new();
            public Dictionary<string, FigmaStyle> Styles { get; set; } = new();
        }

        private class FigmaNode
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Type { get; set; }
            public List<FigmaNode>? Children { get; set; }
            public List<FigmaFill>? Fills { get; set; }
            public FigmaTextStyle? Style { get; set; }
        }

        private class FigmaFill
        {
            public string? Type { get; set; }
            public FigmaColor? Color { get; set; }
            public double? Opacity { get; set; }
        }

        private class FigmaColor
        {
            public double? R { get; set; }
            public double? G { get; set; }
            public double? B { get; set; }
            public double? A { get; set; }
        }

        private class FigmaTextStyle
        {
            public string? FontFamily { get; set; }
            public double? FontSize { get; set; }
            public int? FontWeight { get; set; }
            public double? LineHeightPx { get; set; }
        }

        private class FigmaStyle
        {
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? StyleType { get; set; }
        }
    }
}