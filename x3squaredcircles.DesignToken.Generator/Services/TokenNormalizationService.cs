using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;
using DesignTokenModel = x3squaredcircles.DesignToken.Generator.Models.DesignToken;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface ITokenNormalizationService
    {
        Task<TokenCollection> NormalizeTokensAsync(TokenCollection rawTokens, TokensConfiguration config);
    }

    public class TokenNormalizationService : ITokenNormalizationService
    {
        private readonly IAppLogger _logger;

        public TokenNormalizationService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<TokenCollection> NormalizeTokensAsync(TokenCollection rawTokens, TokensConfiguration config)
        {
            try
            {
                _logger.LogDebug($"Normalizing {rawTokens.Tokens.Count} raw design tokens...");

                var normalizedTokens = new List<DesignTokenModel>();
                foreach (var token in rawTokens.Tokens)
                {
                    try
                    {
                        var normalized = await NormalizeTokenAsync(token);
                        normalizedTokens.Add(normalized);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to normalize token '{token.Name}', skipping. Reason: {ex.Message}");
                    }
                }

                var groupedTokens = await ApplySemanticGroupingAsync(normalizedTokens);
                var computedTokens = await GenerateComputedTokensAsync(groupedTokens, config);
                groupedTokens.AddRange(computedTokens);

                var result = new TokenCollection
                {
                    Name = rawTokens.Name,
                    Version = rawTokens.Version,
                    Source = rawTokens.Source,
                    Tokens = groupedTokens.OrderBy(t => t.Category).ThenBy(t => t.Name).ToList(),
                    Metadata = new Dictionary<string, object>(rawTokens.Metadata)
                    {
                        ["normalizationTimeUtc"] = DateTime.UtcNow,
                        ["originalTokenCount"] = rawTokens.Tokens.Count,
                        ["finalTokenCount"] = groupedTokens.Count
                    }
                };

                _logger.LogInfo($"✓ Token normalization complete: {rawTokens.Tokens.Count} raw -> {groupedTokens.Count} final tokens");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("Token normalization failed.", ex);
                throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, $"Token normalization failed: {ex.Message}", ex);
            }
        }

        private async Task<DesignTokenModel> NormalizeTokenAsync(DesignTokenModel token)
        {
            var normalizedToken = new DesignTokenModel
            {
                Name = NormalizeTokenName(token.Name),
                Type = NormalizeTokenType(token.Type),
                Category = string.IsNullOrEmpty(token.Category) ? NormalizeTokenType(token.Type) : token.Category.ToLowerInvariant(),
                Description = token.Description?.Trim(),
                Tags = token.Tags?.Select(t => t.ToLowerInvariant().Trim()).Distinct().ToList() ?? new List<string>(),
                Attributes = new Dictionary<string, object>(token.Attributes ?? new Dictionary<string, object>())
            };
            normalizedToken.Value = await NormalizeTokenValueAsync(token.Value, normalizedToken.Type);
            await AddComputedAttributesAsync(normalizedToken);
            return normalizedToken;
        }
        #region Private Normalization & Generation Logic

        private string NormalizeTokenName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed-token";
            var normalized = name.Trim().Replace(" ", "-").Replace("_", "-").Replace("/", "-").ToLowerInvariant();
            normalized = Regex.Replace(normalized, "-+", "-").Trim('-');
            return char.IsLetter(normalized.FirstOrDefault()) ? normalized : "token-" + normalized;
        }

        private string NormalizeTokenType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "other";
            return type.ToLowerInvariant().Trim() switch
            {
                "colour" or "fill" => "color",
                "text" or "font" => "typography",
                "space" => "spacing",
                "size" or "dimension" => "sizing",
                "effect" => "shadow",
                "stroke" => "border",
                _ => type.ToLowerInvariant()
            };
        }

        private async Task<object> NormalizeTokenValueAsync(object value, string type)
        {
            if (value is JsonElement element)
            {
                if (type == "color" && element.ValueKind == JsonValueKind.Object) return await ExtractColorFromJsonAsync(element);
                if (type == "typography" && element.ValueKind == JsonValueKind.Object) return NormalizeTypographyValue(element);
            }
            var sValue = value.ToString() ?? "";
            if (type == "color")
            {
                var hex = sValue.TrimStart('#').ToUpperInvariant();
                if (hex.Length == 3) return $"#{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
                if (hex.Length == 6 || hex.Length == 8) return $"#{hex}";
            }
            return value;
        }

        private async Task<string> ExtractColorFromJsonAsync(JsonElement element)
        {
            if (element.TryGetProperty("r", out var r) && element.TryGetProperty("g", out var g) && element.TryGetProperty("b", out var b))
            {
                var red = (int)(r.GetDouble() * 255);
                var green = (int)(g.GetDouble() * 255);
                var blue = (int)(b.GetDouble() * 255);
                var alpha = element.TryGetProperty("a", out var a) ? (int)(a.GetDouble() * 255) : 255;
                if (alpha < 255) return $"#{red:X2}{green:X2}{blue:X2}{alpha:X2}";
                return $"#{red:X2}{green:X2}{blue:X2}";
            }
            return "#000000";
        }

        private object NormalizeTypographyValue(JsonElement element)
        {
            var typography = new Dictionary<string, object>();
            if (element.TryGetProperty("fontFamily", out var fam)) typography["fontFamily"] = fam.GetString() ?? "inherit";
            if (element.TryGetProperty("fontSize", out var size)) typography["fontSize"] = $"{size.GetDouble()}px";
            if (element.TryGetProperty("fontWeight", out var weight)) typography["fontWeight"] = weight.GetInt32();
            return typography;
        }

        private async Task AddComputedAttributesAsync(DesignTokenModel token)
        {
            if (token.Type == "color" && token.Value is string colorValue && colorValue.StartsWith("#"))
            {
                token.Attributes["luminance"] = CalculateLuminance(colorValue);
                token.Attributes["isDark"] = (double)token.Attributes["luminance"] < 0.5;
            }
        }

        private async Task<List<DesignTokenModel>> ApplySemanticGroupingAsync(List<DesignTokenModel> tokens)
        {
            foreach (var token in tokens)
            {
                var semanticTags = GenerateSemanticTags(token.Name);
                foreach (var tag in semanticTags)
                {
                    if (!token.Tags.Contains(tag)) token.Tags.Add(tag);
                }
            }
            return tokens;
        }

        private List<string> GenerateSemanticTags(string name)
        {
            var tags = new List<string>();
            if (name.Contains("primary")) tags.Add("semantic-primary");
            if (name.Contains("secondary")) tags.Add("semantic-secondary");
            if (name.Contains("success")) tags.Add("semantic-success");
            if (name.Contains("warning")) tags.Add("semantic-warning");
            if (name.Contains("error") || name.Contains("danger")) tags.Add("semantic-error");
            if (name.Contains("info")) tags.Add("semantic-info");
            return tags;
        }

        private async Task<List<DesignTokenModel>> GenerateComputedTokensAsync(List<DesignTokenModel> tokens, TokensConfiguration config)
        {
            var computedTokens = new List<DesignTokenModel>();
            if (config.TargetPlatform == "web") // Simplified: only for web
            {
                var colorTokens = tokens.Where(t => t.Type == "color").ToList();
                foreach (var colorToken in colorTokens)
                {
                    var darkVariant = GenerateDarkModeVariant(colorToken);
                    if (darkVariant != null) computedTokens.Add(darkVariant);
                }
            }
            return computedTokens;
        }

        private DesignTokenModel? GenerateDarkModeVariant(DesignTokenModel colorToken)
        {
            if (colorToken.Value is not string colorValue || !colorValue.StartsWith("#")) return null;

            var darkColor = InvertColor(colorValue);
            return new DesignTokenModel
            {
                Name = $"{colorToken.Name}-dark",
                Type = colorToken.Type,
                Category = colorToken.Category,
                Value = darkColor,
                Description = $"Dark mode variant of {colorToken.Name}",
                Tags = new List<string>(colorToken.Tags) { "dark-mode", "computed" },
                Attributes = new Dictionary<string, object>(colorToken.Attributes) { ["baseToken"] = colorToken.Name }
            };
        }

        private string InvertColor(string hex)
        {
            try
            {
                var color = ColorTranslator.FromHtml(hex);
                var r = 255 - color.R;
                var g = 255 - color.G;
                var b = 255 - color.B;
                return ColorTranslator.ToHtml(Color.FromArgb(color.A, r, g, b));
            }
            catch
            {
                return "#FFFFFF";
            }
        }

        private double CalculateLuminance(string hexColor)
        {
            try
            {
                var color = ColorTranslator.FromHtml(hexColor);
                float r = color.R / 255.0f;
                float g = color.G / 255.0f;
                float b = color.B / 255.0f;
                return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
            }
            catch
            {
                return 0.0;
            }
        }

        #endregion
    }
}