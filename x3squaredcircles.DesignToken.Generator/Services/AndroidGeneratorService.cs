using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;
using DesignTokenModel = x3squaredcircles.DesignToken.Generator.Models.DesignToken;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IAndroidGeneratorService
    {
        Task<GenerationResult> GenerateAsync(GenerationRequest request);
    }

    public class AndroidGeneratorService : IAndroidGeneratorService
    {
        private readonly ILogger<AndroidGeneratorService> _logger;
        private readonly string _workingDirectory = "/src";

        public AndroidGeneratorService(ILogger<AndroidGeneratorService> logger)
        {
            _logger = logger;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request)
        {
            try
            {
                _logger.LogInformation("Generating Android/Kotlin design token files");

                var result = new GenerationResult
                {
                    Platform = "android",
                    Success = true,
                    Files = new List<GeneratedFile>()
                };

                var outputPath = Path.Combine(_workingDirectory, request.OutputDirectory);
                Directory.CreateDirectory(outputPath);

                // Generate Colors.kt
                var colorsFile = await GenerateColorsFileAsync(request, outputPath);
                result.Files.Add(colorsFile);

                // Generate Typography.kt
                var typographyFile = await GenerateTypographyFileAsync(request, outputPath);
                result.Files.Add(typographyFile);

                // Generate Spacing.kt
                var spacingFile = await GenerateSpacingFileAsync(request, outputPath);
                result.Files.Add(spacingFile);

                // Generate Theme.kt
                var themeFile = await GenerateThemeFileAsync(request, outputPath);
                result.Files.Add(themeFile);

                result.Metadata["generated_files"] = result.Files.Count;
                result.Metadata["output_directory"] = outputPath;
                result.Metadata["package_name"] = GetPackageName(request);

                _logger.LogInformation("✓ Generated {FileCount} Android files", result.Files.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Android generation failed");
                return new GenerationResult
                {
                    Platform = "android",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<GeneratedFile> GenerateColorsFileAsync(GenerationRequest request, string outputPath)
        {
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").ToList();
            var packageName = GetPackageName(request);
            var filePath = Path.Combine(outputPath, "Colors.kt");

            var content = new StringBuilder();
            content.AppendLine($"package {packageName}");
            content.AppendLine();
            content.AppendLine("import androidx.compose.ui.graphics.Color");
            content.AppendLine();

            // Check for custom sections
            var existingContent = await ReadExistingFileAsync(filePath);
            var customSections = ExtractCustomSections(existingContent);

            if (customSections.Any())
            {
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// Custom Color Definitions - Preserved");
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine();
                foreach (var section in customSections)
                {
                    content.AppendLine(section.Content);
                }
                content.AppendLine();
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// End Custom Section");
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine();
            }

            content.AppendLine("// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT");
            content.AppendLine("object DesignTokenColors {");

            foreach (var token in colorTokens.OrderBy(t => t.Name))
            {
                var colorName = ToPascalCase(token.Name);
                var colorValue = ConvertToAndroidColor(token.Value?.ToString() ?? "#000000");

                if (!string.IsNullOrEmpty(token.Description))
                {
                    content.AppendLine($"    /** {token.Description} */");
                }
                content.AppendLine($"    val {colorName} = Color({colorValue})");
            }

            content.AppendLine("}");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent,
                HasCustomSections = customSections.Any(),
                PreservedSections = customSections
            };
        }

        private async Task<GeneratedFile> GenerateTypographyFileAsync(GenerationRequest request, string outputPath)
        {
            var typographyTokens = request.Tokens.Tokens.Where(t => t.Type == "typography").ToList();
            var packageName = GetPackageName(request);
            var filePath = Path.Combine(outputPath, "Typography.kt");

            var content = new StringBuilder();
            content.AppendLine($"package {packageName}");
            content.AppendLine();
            content.AppendLine("import androidx.compose.ui.text.TextStyle");
            content.AppendLine("import androidx.compose.ui.text.font.FontFamily");
            content.AppendLine("import androidx.compose.ui.text.font.FontWeight");
            content.AppendLine("import androidx.compose.ui.unit.sp");
            content.AppendLine();

            var existingContent = await ReadExistingFileAsync(filePath);
            var customSections = ExtractCustomSections(existingContent);

            if (customSections.Any())
            {
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// Custom Typography - Preserved");
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine();
                foreach (var section in customSections)
                {
                    content.AppendLine(section.Content);
                }
                content.AppendLine();
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// End Custom Section");
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine();
            }

            content.AppendLine("// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT");
            content.AppendLine("object DesignTokenTypography {");

            foreach (var token in typographyTokens.OrderBy(t => t.Name))
            {
                var styleName = ToPascalCase(token.Name);
                var style = GenerateTextStyle(token);

                if (!string.IsNullOrEmpty(token.Description))
                {
                    content.AppendLine($"    /** {token.Description} */");
                }
                content.AppendLine($"    val {styleName} = {style}");
            }

            content.AppendLine("}");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent,
                HasCustomSections = customSections.Any(),
                PreservedSections = customSections
            };
        }

        private async Task<GeneratedFile> GenerateSpacingFileAsync(GenerationRequest request, string outputPath)
        {
            var spacingTokens = request.Tokens.Tokens.Where(t => t.Type == "spacing" || t.Type == "sizing").ToList();
            var packageName = GetPackageName(request);
            var filePath = Path.Combine(outputPath, "Spacing.kt");

            var content = new StringBuilder();
            content.AppendLine($"package {packageName}");
            content.AppendLine();
            content.AppendLine("import androidx.compose.ui.unit.dp");
            content.AppendLine();

            var existingContent = await ReadExistingFileAsync(filePath);
            var customSections = ExtractCustomSections(existingContent);

            if (customSections.Any())
            {
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// Custom Spacing - Preserved");
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine();
                foreach (var section in customSections)
                {
                    content.AppendLine(section.Content);
                }
                content.AppendLine();
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// End Custom Section");
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine();
            }

            content.AppendLine("// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT");
            content.AppendLine("object DesignTokenSpacing {");

            foreach (var token in spacingTokens.OrderBy(t => t.Name))
            {
                var spacingName = ToPascalCase(token.Name);
                var spacingValue = ConvertToDpValue(token.Value?.ToString() ?? "0px");

                if (!string.IsNullOrEmpty(token.Description))
                {
                    content.AppendLine($"    /** {token.Description} */");
                }
                content.AppendLine($"    val {spacingName} = {spacingValue}.dp");
            }

            content.AppendLine("}");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent,
                HasCustomSections = customSections.Any(),
                PreservedSections = customSections
            };
        }

        private async Task<GeneratedFile> GenerateThemeFileAsync(GenerationRequest request, string outputPath)
        {
            var packageName = GetPackageName(request);
            var themeName = GetThemeName(request);
            var filePath = Path.Combine(outputPath, "Theme.kt");

            var content = new StringBuilder();
            content.AppendLine($"package {packageName}");
            content.AppendLine();
            content.AppendLine("import androidx.compose.material3.MaterialTheme");
            content.AppendLine("import androidx.compose.material3.lightColorScheme");
            content.AppendLine("import androidx.compose.material3.darkColorScheme");
            content.AppendLine("import androidx.compose.runtime.Composable");
            content.AppendLine();

            var existingContent = await ReadExistingFileAsync(filePath);
            var customSections = ExtractCustomSections(existingContent);

            if (customSections.Any())
            {
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// Custom Theme Components - Preserved");
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine();
                foreach (var section in customSections)
                {
                    content.AppendLine(section.Content);
                }
                content.AppendLine();
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// End Custom Section");
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine();
            }

            content.AppendLine("// AUTO-GENERATED CONTENT BELOW - DO NOT EDIT");

            // Generate color schemes
            content.AppendLine("private val LightColorScheme = lightColorScheme(");
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").ToList();
            foreach (var token in colorTokens.Take(3)) // Limit for example
            {
                var materialColorName = MapToMaterialColorName(token.Name);
                if (!string.IsNullOrEmpty(materialColorName))
                {
                    content.AppendLine($"    {materialColorName} = DesignTokenColors.{ToPascalCase(token.Name)},");
                }
            }
            content.AppendLine(")");
            content.AppendLine();

            content.AppendLine("private val DarkColorScheme = darkColorScheme(");
            foreach (var token in colorTokens.Take(3)) // Limit for example
            {
                var materialColorName = MapToMaterialColorName(token.Name);
                if (!string.IsNullOrEmpty(materialColorName))
                {
                    content.AppendLine($"    {materialColorName} = DesignTokenColors.{ToPascalCase(token.Name)},");
                }
            }
            content.AppendLine(")");
            content.AppendLine();

            // Generate theme composable
            content.AppendLine("@Composable");
            content.AppendLine($"fun {themeName}(");
            content.AppendLine("    darkTheme: Boolean = false,");
            content.AppendLine("    content: @Composable () -> Unit");
            content.AppendLine(") {");
            content.AppendLine("    val colorScheme = if (darkTheme) DarkColorScheme else LightColorScheme");
            content.AppendLine();
            content.AppendLine("    MaterialTheme(");
            content.AppendLine("        colorScheme = colorScheme,");
            content.AppendLine("        content = content");
            content.AppendLine("    )");
            content.AppendLine("}");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent,
                HasCustomSections = customSections.Any(),
                PreservedSections = customSections
            };
        }

        private async Task<string> ReadExistingFileAsync(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    return await File.ReadAllTextAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read existing file: {FilePath}", filePath);
            }
            return string.Empty;
        }

        private List<CustomSection> ExtractCustomSections(string content)
        {
            var sections = new List<CustomSection>();
            if (string.IsNullOrEmpty(content))
                return sections;

            var lines = content.Split('\n');
            var inCustomSection = false;
            var currentSection = new StringBuilder();
            var startLine = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Contains("/////////////////////////////////////////") &&
                    i + 1 < lines.Length &&
                    lines[i + 1].Contains("Preserved"))
                {
                    inCustomSection = true;
                    startLine = i;
                    continue;
                }

                if (inCustomSection && line.Contains("End Custom Section"))
                {
                    sections.Add(new CustomSection
                    {
                        Name = "Custom",
                        Content = currentSection.ToString().Trim(),
                        StartLine = startLine,
                        EndLine = i
                    });
                    currentSection.Clear();
                    inCustomSection = false;
                    continue;
                }

                if (inCustomSection)
                {
                    currentSection.AppendLine(line);
                }
            }

            return sections;
        }

        private string GetPackageName(GenerationRequest request)
        {
            return request.Platform.AndroidPackageName ?? "com.company.designtokens.ui.theme";
        }

        private string GetThemeName(GenerationRequest request)
        {
            return request.Platform.AndroidThemeName ?? "DesignTokenTheme";
        }

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return string.Join("", input.Split('-', '_', ' ')
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant()));
        }

        private string ConvertToAndroidColor(string colorValue)
        {
            if (colorValue.StartsWith("#"))
            {
                var hex = colorValue.TrimStart('#');
                if (hex.Length == 6)
                {
                    return $"0xFF{hex.ToUpperInvariant()}";
                }
                else if (hex.Length == 8)
                {
                    return $"0x{hex.ToUpperInvariant()}";
                }
            }
            return "0xFF000000";
        }

        private string GenerateTextStyle(DesignTokenModel token)
        {
            var style = new StringBuilder("TextStyle(");

            if (token.Value is Dictionary<string, object> typography)
            {
                var properties = new List<string>();

                if (typography.TryGetValue("fontSize", out var fontSize))
                {
                    var dpValue = ConvertToDpValue(fontSize.ToString() ?? "16px");
                    properties.Add($"fontSize = {dpValue}.sp");
                }

                if (typography.TryGetValue("fontWeight", out var fontWeight))
                {
                    var weight = ConvertToFontWeight(fontWeight.ToString() ?? "400");
                    properties.Add($"fontWeight = FontWeight.{weight}");
                }

                if (typography.TryGetValue("fontFamily", out var fontFamily))
                {
                    var family = fontFamily.ToString();
                    if (family != "inherit")
                    {
                        properties.Add($"fontFamily = FontFamily.{family}");
                    }
                }

                style.Append(string.Join(", ", properties));
            }

            style.Append(")");
            return style.ToString();
        }

        private string ConvertToDpValue(string value)
        {
            if (value.EndsWith("px"))
            {
                var numericPart = value.Replace("px", "");
                if (double.TryParse(numericPart, out var pixels))
                {
                    return ((int)pixels).ToString();
                }
            }
            return "16";
        }

        private string ConvertToFontWeight(string weight)
        {
            return weight switch
            {
                "100" => "Thin",
                "200" => "ExtraLight",
                "300" => "Light",
                "400" => "Normal",
                "500" => "Medium",
                "600" => "SemiBold",
                "700" => "Bold",
                "800" => "ExtraBold",
                "900" => "Black",
                _ => "Normal"
            };
        }

        private string MapToMaterialColorName(string tokenName)
        {
            var name = tokenName.ToLowerInvariant();
            if (name.Contains("primary")) return "primary";
            if (name.Contains("secondary")) return "secondary";
            if (name.Contains("surface")) return "surface";
            if (name.Contains("background")) return "background";
            if (name.Contains("error")) return "error";
            return string.Empty;
        }
    }
}