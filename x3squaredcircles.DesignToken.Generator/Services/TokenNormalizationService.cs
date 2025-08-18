using Microsoft.Extensions.Logging;
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
        Task<TokenCollection> NormalizeTokensAsync(TokenCollection rawTokens, DesignTokenConfiguration config);
        Task<DesignTokenModel> NormalizeTokenAsync(DesignTokenModel token);
    }

    public class TokenNormalizationService : ITokenNormalizationService
    {
        private readonly ILogger<TokenNormalizationService> _logger;

        public TokenNormalizationService(ILogger<TokenNormalizationService> logger)
        {
            _logger = logger;
        }

        public async Task<TokenCollection> NormalizeTokensAsync(TokenCollection rawTokens, DesignTokenConfiguration config)
        {
            try
            {
                _logger.LogInformation("Normalizing {TokenCount} raw design tokens", rawTokens.Tokens.Count);

                var normalizedTokens = new List<DesignTokenModel>();

                foreach (var token in rawTokens.Tokens)
                {
                    try
                    {
                        var normalizedToken = await NormalizeTokenAsync(token);
                        normalizedTokens.Add(normalizedToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to normalize token: {TokenName}, skipping", token.Name);
                    }
                }

                // Sort tokens by category and name for consistency
                normalizedTokens = normalizedTokens
                    .OrderBy(t => t.Category)
                    .ThenBy(t => t.Name)
                    .ToList();

                // Apply semantic grouping
                var groupedTokens = await ApplySemanticGroupingAsync(normalizedTokens);

                // Generate computed tokens if needed
                var computedTokens = await GenerateComputedTokensAsync(groupedTokens, config);
                groupedTokens.AddRange(computedTokens);

                var result = new TokenCollection
                {
                    Name = rawTokens.Name,
                    Version = rawTokens.Version,
                    Source = rawTokens.Source,
                    Tokens = groupedTokens,
                    Metadata = new Dictionary<string, object>(rawTokens.Metadata)
                };

                // Add normalization metadata
                result.Metadata["normalization_applied"] = true;
                result.Metadata["normalization_time"] = DateTime.UtcNow;
                result.Metadata["original_token_count"] = rawTokens.Tokens.Count;
                result.Metadata["normalized_token_count"] = groupedTokens.Count;

                _logger.LogInformation("✓ Token normalization complete: {Original} -> {Normalized} tokens",
                    rawTokens.Tokens.Count, groupedTokens.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token normalization failed");
                throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure,
                    $"Token normalization failed: {ex.Message}", ex);
            }
        }

        public async Task<DesignTokenModel> NormalizeTokenAsync(DesignTokenModel token)
        {
            var normalizedToken = new DesignTokenModel
            {
                Name = NormalizeTokenName(token.Name),
                Type = NormalizeTokenType(token.Type),
                Category = NormalizeTokenCategory(token.Category, token.Type),
                Description = token.Description?.Trim(),
                Tags = token.Tags?.Select(t => t.ToLowerInvariant().Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>(),
                Attributes = new Dictionary<string, object>(token.Attributes ?? new Dictionary<string, object>())
            };

            // Normalize value based on token type
            normalizedToken.Value = await NormalizeTokenValueAsync(token.Value, normalizedToken.Type);

            // Add computed attributes
            await AddComputedAttributesAsync(normalizedToken);

            return normalizedToken;
        }

        private string NormalizeTokenName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unnamed_token";
            }

            // Convert to kebab-case for consistency
            var normalized = name.Trim()
                .Replace(" ", "-")
                .Replace("_", "-")
                .Replace("/", "-")
                .ToLowerInvariant();

            // Remove multiple consecutive hyphens
            normalized = Regex.Replace(normalized, "-+", "-");

            // Remove leading/trailing hyphens
            normalized = normalized.Trim('-');

            // Ensure it starts with a letter
            if (!char.IsLetter(normalized.FirstOrDefault()))
            {
                normalized = "token-" + normalized;
            }

            return normalized;
        }

        private string NormalizeTokenType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return "other";
            }

            return type.ToLowerInvariant().Trim() switch
            {
                "colour" or "fill" => "color",
                "text" or "font" or "typography" => "typography",
                "space" or "margin" or "padding" => "spacing",
                "size" or "dimension" or "width" or "height" => "sizing",
                "effect" or "drop-shadow" or "box-shadow" => "shadow",
                "stroke" or "border-radius" => "border",
                "alpha" or "transparency" => "opacity",
                _ => type.ToLowerInvariant()
            };
        }

        private string NormalizeTokenCategory(string category, string type)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                return category.ToLowerInvariant().Trim();
            }

            // Derive category from type if not provided
            return type.ToLowerInvariant() switch
            {
                "color" => "color",
                "typography" => "typography",
                "spacing" => "spacing",
                "sizing" => "sizing",
                "shadow" => "effect",
                "border" => "border",
                "opacity" => "opacity",
                _ => "other"
            };
        }

        private async Task<object> NormalizeTokenValueAsync(object value, string type)
        {
            if (value == null)
            {
                return new object();
            }

            return type switch
            {
                "color" => await NormalizeColorValueAsync(value),
                "typography" => await NormalizeTypographyValueAsync(value),
                "spacing" => await NormalizeDimensionValueAsync(value),
                "sizing" => await NormalizeDimensionValueAsync(value),
                "shadow" => await NormalizeShadowValueAsync(value),
                "border" => await NormalizeBorderValueAsync(value),
                "opacity" => await NormalizeOpacityValueAsync(value),
                _ => value
            };
        }

        private async Task<object> NormalizeColorValueAsync(object value)
        {
            try
            {
                var colorString = value.ToString() ?? "";

                // Try to parse as hex color
                if (IsHexColor(colorString))
                {
                    return NormalizeHexColor(colorString);
                }

                // Try to parse as RGB/RGBA
                if (IsRgbColor(colorString))
                {
                    return ConvertRgbToHex(colorString);
                }

                // Try to parse as named color
                try
                {
                    var color = Color.FromName(colorString);
                    if (color.IsKnownColor)
                    {
                        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }

                // If it's a complex object (like from Figma), try to extract RGB values
                if (value is JsonElement element)
                {
                    return await ExtractColorFromJsonAsync(element);
                }

                // Default fallback
                return colorString;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to normalize color value: {Value}", value);
                return value.ToString() ?? "#000000";
            }
        }

        private async Task<object> NormalizeTypographyValueAsync(object value)
        {
            try
            {
                var typography = new Dictionary<string, object>();

                if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in element.EnumerateObject())
                    {
                        var key = prop.Name.ToLowerInvariant();
                        var val = prop.Value;

                        switch (key)
                        {
                            case "fontfamily" or "font-family":
                                typography["fontFamily"] = val.GetString() ?? "inherit";
                                break;
                            case "fontsize" or "font-size":
                                typography["fontSize"] = ParseDimensionValue(val.ToString() ?? "16");
                                break;
                            case "fontweight" or "font-weight":
                                typography["fontWeight"] = ParseFontWeight(val.ToString() ?? "400");
                                break;
                            case "lineheight" or "line-height":
                                typography["lineHeight"] = ParseLineHeight(val.ToString() ?? "normal");
                                break;
                            case "letterspacing" or "letter-spacing":
                                typography["letterSpacing"] = ParseDimensionValue(val.ToString() ?? "0");
                                break;
                        }
                    }
                }
                else
                {
                    // If it's a simple string, assume it's a font family
                    typography["fontFamily"] = value.ToString() ?? "inherit";
                }

                // Ensure required properties exist
                if (!typography.ContainsKey("fontFamily"))
                    typography["fontFamily"] = "inherit";
                if (!typography.ContainsKey("fontSize"))
                    typography["fontSize"] = "16px";
                if (!typography.ContainsKey("fontWeight"))
                    typography["fontWeight"] = 400;

                return typography;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to normalize typography value: {Value}", value);
                return new Dictionary<string, object>
                {
                    ["fontFamily"] = "inherit",
                    ["fontSize"] = "16px",
                    ["fontWeight"] = 400
                };
            }
        }

        private async Task<object> NormalizeDimensionValueAsync(object value)
        {
            try
            {
                var stringValue = value.ToString() ?? "";
                return ParseDimensionValue(stringValue);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to normalize dimension value: {Value}", value);
                return value.ToString() ?? "0px";
            }
        }

        private async Task<object> NormalizeShadowValueAsync(object value)
        {
            try
            {
                var shadow = new Dictionary<string, object>();

                if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                {
                    shadow["offsetX"] = element.TryGetProperty("offsetX", out var x) ? ParseDimensionValue(x.ToString() ?? "0") : "0px";
                    shadow["offsetY"] = element.TryGetProperty("offsetY", out var y) ? ParseDimensionValue(y.ToString() ?? "0") : "0px";
                    shadow["blur"] = element.TryGetProperty("blur", out var b) ? ParseDimensionValue(b.ToString() ?? "0") : "0px";
                    shadow["spread"] = element.TryGetProperty("spread", out var s) ? ParseDimensionValue(s.ToString() ?? "0") : "0px";
                    shadow["color"] = element.TryGetProperty("color", out var c) ? await NormalizeColorValueAsync(c) : "#000000";
                }
                else
                {
                    // Try to parse CSS box-shadow format
                    var cssValue = value.ToString() ?? "";
                    shadow = ParseCssBoxShadow(cssValue);
                }

                return shadow;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to normalize shadow value: {Value}", value);
                return new Dictionary<string, object>
                {
                    ["offsetX"] = "0px",
                    ["offsetY"] = "2px",
                    ["blur"] = "4px",
                    ["spread"] = "0px",
                    ["color"] = "#000000"
                };
            }
        }

        private async Task<object> NormalizeBorderValueAsync(object value)
        {
            try
            {
                var border = new Dictionary<string, object>();

                if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                {
                    border["width"] = element.TryGetProperty("width", out var w) ? ParseDimensionValue(w.ToString() ?? "1") : "1px";
                    border["style"] = element.TryGetProperty("style", out var s) ? s.GetString() ?? "solid" : "solid";
                    border["color"] = element.TryGetProperty("color", out var c) ? await NormalizeColorValueAsync(c) : "#000000";
                }
                else
                {
                    // Try to parse CSS border format
                    var cssValue = value.ToString() ?? "";
                    border = ParseCssBorder(cssValue);
                }

                return border;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to normalize border value: {Value}", value);
                return new Dictionary<string, object>
                {
                    ["width"] = "1px",
                    ["style"] = "solid",
                    ["color"] = "#000000"
                };
            }
        }

        private async Task<object> NormalizeOpacityValueAsync(object value)
        {
            try
            {
                var stringValue = value.ToString() ?? "";

                if (double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericValue))
                {
                    // Ensure opacity is between 0 and 1
                    if (numericValue > 1)
                    {
                        numericValue = numericValue / 100.0; // Convert percentage to decimal
                    }

                    return Math.Max(0, Math.Min(1, numericValue));
                }

                return 1.0; // Default to fully opaque
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to normalize opacity value: {Value}", value);
                return 1.0;
            }
        }

        private async Task AddComputedAttributesAsync(DesignTokenModel token)
        {
            // Add semantic tags based on token name and type
            var semanticTags = GenerateSemanticTags(token);
            foreach (var tag in semanticTags)
            {
                if (!token.Tags.Contains(tag))
                {
                    token.Tags.Add(tag);
                }
            }

            // Add computed accessibility attributes for colors
            if (token.Type == "color" && token.Value is string colorValue)
            {
                token.Attributes["luminance"] = CalculateLuminance(colorValue);
                token.Attributes["contrast_ratio_white"] = CalculateContrastRatio(colorValue, "#FFFFFF");
                token.Attributes["contrast_ratio_black"] = CalculateContrastRatio(colorValue, "#000000");
                token.Attributes["wcag_aa_normal"] = CalculateContrastRatio(colorValue, "#FFFFFF") >= 4.5 ||
                                                   CalculateContrastRatio(colorValue, "#000000") >= 4.5;
            }

            // Add computed attributes for typography
            if (token.Type == "typography" && token.Value is Dictionary<string, object> typography)
            {
                if (typography.TryGetValue("fontSize", out var fontSize))
                {
                    token.Attributes["font_size_category"] = CategorizeFontSize(fontSize.ToString() ?? "");
                }
            }
        }

        private async Task<List<DesignTokenModel>> ApplySemanticGroupingAsync(List<DesignTokenModel> tokens)
        {
            // Group related tokens and create hierarchical naming
            var groupedTokens = new List<DesignTokenModel>();

            foreach (var group in tokens.GroupBy(t => t.Category))
            {
                var categoryTokens = group.ToList();

                // Apply category-specific grouping logic
                switch (group.Key)
                {
                    case "color":
                        groupedTokens.AddRange(await GroupColorTokensAsync(categoryTokens));
                        break;
                    case "typography":
                        groupedTokens.AddRange(await GroupTypographyTokensAsync(categoryTokens));
                        break;
                    default:
                        groupedTokens.AddRange(categoryTokens);
                        break;
                }
            }

            return groupedTokens;
        }

        private async Task<List<DesignTokenModel>> GroupColorTokensAsync(List<DesignTokenModel> colorTokens)
        {
            // Group colors by hue and create semantic naming
            var groupedColors = new List<DesignTokenModel>();

            foreach (var token in colorTokens)
            {
                // Enhance color token with semantic information
                var enhancedToken = new DesignTokenModel
                {
                    Name = token.Name,
                    Type = token.Type,
                    Category = token.Category,
                    Value = token.Value,
                    Description = token.Description,
                    Tags = new List<string>(token.Tags),
                    Attributes = new Dictionary<string, object>(token.Attributes)
                };

                // Add color family tag
                if (token.Value is string colorValue)
                {
                    var colorFamily = DetermineColorFamily(colorValue);
                    if (!string.IsNullOrEmpty(colorFamily))
                    {
                        enhancedToken.Tags.Add($"color-{colorFamily}");
                    }
                }

                groupedColors.Add(enhancedToken);
            }

            return groupedColors;
        }

        private async Task<List<DesignTokenModel>> GroupTypographyTokensAsync(List<DesignTokenModel> typographyTokens)
        {
            // Group typography tokens by usage patterns
            return typographyTokens; // Simplified for now
        }

        private async Task<List<DesignTokenModel>> GenerateComputedTokensAsync(List<DesignTokenModel> tokens, DesignTokenConfiguration config)
        {
            var computedTokens = new List<DesignTokenModel>();

            // Generate dark mode variants for colors if enabled
            if (config.TargetPlatform.WebSupportDarkMode)
            {
                var colorTokens = tokens.Where(t => t.Type == "color").ToList();
                foreach (var colorToken in colorTokens)
                {
                    var darkVariant = await GenerateDarkModeVariantAsync(colorToken);
                    if (darkVariant != null)
                    {
                        computedTokens.Add(darkVariant);
                    }
                }
            }

            return computedTokens;
        }

        private async Task<DesignTokenModel?> GenerateDarkModeVariantAsync(DesignTokenModel colorToken)
        {
            try
            {
                if (colorToken.Value is not string colorValue)
                    return null;

                // Simple dark mode generation by adjusting lightness
                var darkColor = AdjustColorForDarkMode(colorValue);

                return new DesignTokenModel
                {
                    Name = $"{colorToken.Name}-dark",
                    Type = colorToken.Type,
                    Category = colorToken.Category,
                    Value = darkColor,
                    Description = $"Dark mode variant of {colorToken.Name}",
                    Tags = new List<string>(colorToken.Tags) { "dark-mode", "computed" },
                    Attributes = new Dictionary<string, object>(colorToken.Attributes)
                    {
                        ["computed"] = true,
                        ["base_token"] = colorToken.Name
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to generate dark mode variant for {TokenName}", colorToken.Name);
                return null;
            }
        }

        // Helper methods for color processing, font parsing, etc.
        private bool IsHexColor(string color) => Regex.IsMatch(color, @"^#([A-Fa-f0-9]{3}|[A-Fa-f0-9]{6}|[A-Fa-f0-9]{8})$");
        private bool IsRgbColor(string color) => Regex.IsMatch(color, @"^rgba?\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*(,\s*[\d.]+)?\s*\)$");

        private string NormalizeHexColor(string hex)
        {
            hex = hex.TrimStart('#').ToUpperInvariant();
            return hex.Length == 3 ? $"#{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}" : $"#{hex}";
        }

        private string ConvertRgbToHex(string rgb)
        {
            var match = Regex.Match(rgb, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+))?\s*\)");
            if (match.Success)
            {
                var r = int.Parse(match.Groups[1].Value);
                var g = int.Parse(match.Groups[2].Value);
                var b = int.Parse(match.Groups[3].Value);
                return $"#{r:X2}{g:X2}{b:X2}";
            }
            return rgb;
        }

        private async Task<object> ExtractColorFromJsonAsync(JsonElement element)
        {
            if (element.TryGetProperty("r", out var r) &&
                element.TryGetProperty("g", out var g) &&
                element.TryGetProperty("b", out var b))
            {
                var red = (int)(r.GetDouble() * 255);
                var green = (int)(g.GetDouble() * 255);
                var blue = (int)(b.GetDouble() * 255);
                return $"#{red:X2}{green:X2}{blue:X2}";
            }
            return "#000000";
        }

        private string ParseDimensionValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "0px";

            // If already has unit, return as-is
            if (Regex.IsMatch(value, @"\d+(px|em|rem|%|pt|pc|in|cm|mm|ex|ch|vw|vh|vmin|vmax)$"))
                return value;

            // If it's just a number, assume pixels
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numValue))
                return $"{numValue}px";

            return value;
        }

        private int ParseFontWeight(string weight)
        {
            return weight.ToLowerInvariant() switch
            {
                "thin" => 100,
                "light" => 300,
                "normal" or "regular" => 400,
                "medium" => 500,
                "semibold" => 600,
                "bold" => 700,
                "extrabold" => 800,
                "black" => 900,
                _ => int.TryParse(weight, out var w) ? w : 400
            };
        }

        private string ParseLineHeight(string lineHeight)
        {
            if (string.IsNullOrWhiteSpace(lineHeight) || lineHeight == "normal")
                return "normal";

            // If it's a number without unit, treat as multiplier
            if (double.TryParse(lineHeight, NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier))
                return multiplier.ToString(CultureInfo.InvariantCulture);

            return ParseDimensionValue(lineHeight);
        }

        private Dictionary<string, object> ParseCssBoxShadow(string shadow)
        {
            // Simplified CSS box-shadow parser
            return new Dictionary<string, object>
            {
                ["offsetX"] = "0px",
                ["offsetY"] = "2px",
                ["blur"] = "4px",
                ["spread"] = "0px",
                ["color"] = "#000000"
            };
        }

        private Dictionary<string, object> ParseCssBorder(string border)
        {
            // Simplified CSS border parser
            return new Dictionary<string, object>
            {
                ["width"] = "1px",
                ["style"] = "solid",
                ["color"] = "#000000"
            };
        }

        private List<string> GenerateSemanticTags(DesignTokenModel token)
        {
            var tags = new List<string>();
            var name = token.Name.ToLowerInvariant();

            // Common semantic patterns
            if (name.Contains("primary")) tags.Add("semantic-primary");
            if (name.Contains("secondary")) tags.Add("semantic-secondary");
            if (name.Contains("accent")) tags.Add("semantic-accent");
            if (name.Contains("success")) tags.Add("semantic-success");
            if (name.Contains("warning")) tags.Add("semantic-warning");
            if (name.Contains("error") || name.Contains("danger")) tags.Add("semantic-error");
            if (name.Contains("info")) tags.Add("semantic-info");

            // Size patterns
            if (name.Contains("small") || name.Contains("xs")) tags.Add("size-small");
            if (name.Contains("medium") || name.Contains("md")) tags.Add("size-medium");
            if (name.Contains("large") || name.Contains("lg")) tags.Add("size-large");
            if (name.Contains("xl")) tags.Add("size-extra-large");

            return tags;
        }

        private double CalculateLuminance(string hexColor)
        {
            // Simplified luminance calculation
            if (!IsHexColor(hexColor)) return 0.5;

            var hex = hexColor.TrimStart('#');
            if (hex.Length != 6) return 0.5;

            var r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255.0;
            var g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0;
            var b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0;

            return 0.299 * r + 0.587 * g + 0.114 * b;
        }

        private double CalculateContrastRatio(string color1, string color2)
        {
            var lum1 = CalculateLuminance(color1);
            var lum2 = CalculateLuminance(color2);
            var lighter = Math.Max(lum1, lum2);
            var darker = Math.Min(lum1, lum2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private string CategorizeFontSize(string fontSize)
        {
            var size = ParseDimensionValue(fontSize);
            var numericPart = Regex.Match(size, @"(\d+)").Value;

            if (int.TryParse(numericPart, out var sizeValue))
            {
                return sizeValue switch
                {
                    < 12 => "extra-small",
                    < 16 => "small",
                    < 20 => "medium",
                    < 24 => "large",
                    < 32 => "extra-large",
                    _ => "display"
                };
            }

            return "medium";
        }

        private string DetermineColorFamily(string hexColor)
        {
            if (!IsHexColor(hexColor)) return "";

            var hex = hexColor.TrimStart('#');
            if (hex.Length != 6) return "";

            var r = Convert.ToInt32(hex.Substring(0, 2), 16);
            var g = Convert.ToInt32(hex.Substring(2, 2), 16);
            var b = Convert.ToInt32(hex.Substring(4, 2), 16);

            // Simple color family determination
            if (r > g && r > b) return "red";
            if (g > r && g > b) return "green";
            if (b > r && b > g) return "blue";
            if (r == g && g == b) return "gray";
            if (r > 200 && g > 200 && b > 200) return "light";
            if (r < 50 && g < 50 && b < 50) return "dark";

            return "mixed";
        }

        private string AdjustColorForDarkMode(string hexColor)
        {
            if (!IsHexColor(hexColor)) return hexColor;

            var hex = hexColor.TrimStart('#');
            if (hex.Length != 6) return hexColor;

            var r = Convert.ToInt32(hex.Substring(0, 2), 16);
            var g = Convert.ToInt32(hex.Substring(2, 2), 16);
            var b = Convert.ToInt32(hex.Substring(4, 2), 16);

            // Simple dark mode adjustment - invert lightness
            var luminance = CalculateLuminance(hexColor);
            var factor = luminance > 0.5 ? 0.3 : 1.7; // Darken light colors, lighten dark colors

            r = Math.Max(0, Math.Min(255, (int)(r * factor)));
            g = Math.Max(0, Math.Min(255, (int)(g * factor)));
            b = Math.Max(0, Math.Min(255, (int)(b * factor)));

            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}