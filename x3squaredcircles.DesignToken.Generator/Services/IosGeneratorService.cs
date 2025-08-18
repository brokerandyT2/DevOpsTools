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
    public interface IIosGeneratorService
    {
        Task<GenerationResult> GenerateAsync(GenerationRequest request);
    }

    public class IosGeneratorService : IIosGeneratorService
    {
        private readonly ILogger<IosGeneratorService> _logger;
        private readonly string _workingDirectory = "/src";

        public IosGeneratorService(ILogger<IosGeneratorService> logger)
        {
            _logger = logger;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request)
        {
            try
            {
                _logger.LogInformation("Generating iOS/Swift design token files");

                var result = new GenerationResult
                {
                    Platform = "ios",
                    Success = true,
                    Files = new List<GeneratedFile>()
                };

                var outputPath = Path.Combine(_workingDirectory, request.OutputDirectory);
                Directory.CreateDirectory(outputPath);

                // Generate Colors.swift
                var colorsFile = await GenerateColorsFileAsync(request, outputPath);
                result.Files.Add(colorsFile);

                // Generate Typography.swift
                var typographyFile = await GenerateTypographyFileAsync(request, outputPath);
                result.Files.Add(typographyFile);

                // Generate Spacing.swift
                var spacingFile = await GenerateSpacingFileAsync(request, outputPath);
                result.Files.Add(spacingFile);

                // Generate Theme.swift
                var themeFile = await GenerateThemeFileAsync(request, outputPath);
                result.Files.Add(themeFile);

                result.Metadata["generated_files"] = result.Files.Count;
                result.Metadata["output_directory"] = outputPath;
                result.Metadata["module_name"] = GetModuleName(request);

                _logger.LogInformation("✓ Generated {FileCount} iOS files", result.Files.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "iOS generation failed");
                return new GenerationResult
                {
                    Platform = "ios",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<GeneratedFile> GenerateColorsFileAsync(GenerationRequest request, string outputPath)
        {
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").ToList();
            var moduleName = GetModuleName(request);
            var filePath = Path.Combine(outputPath, "Colors.swift");

            var content = new StringBuilder();
            content.AppendLine("import UIKit");
            content.AppendLine("import SwiftUI");
            content.AppendLine();

            // Check for custom sections
            var existingContent = await ReadExistingFileAsync(filePath);
            var customSections = ExtractCustomSections(existingContent);

            if (customSections.Any())
            {
                content.AppendLine("/////////////////////////////////////////");
                content.AppendLine("// Custom Color Extensions - Preserved");
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
            content.AppendLine($"public extension {moduleName} {{");
            content.AppendLine("    struct Colors {");

            foreach (var token in colorTokens.OrderBy(t => t.Name))
            {
                var colorName = ToCamelCase(token.Name);
                var colorValue = ConvertToSwiftColor(token.Value?.ToString() ?? "#000000");

                if (!string.IsNullOrEmpty(token.Description))
                {
                    content.AppendLine($"        /// {token.Description}");
                }
                content.AppendLine($"        public static let {colorName} = Color(hex: \"{colorValue}\")");
            }

            content.AppendLine("    }");
            content.AppendLine("}");
            content.AppendLine();

            // Add Color extension for hex support
            content.AppendLine("// Color extension for hex color support");
            content.AppendLine("extension Color {");
            content.AppendLine("    init(hex: String) {");
            content.AppendLine("        let hex = hex.trimmingCharacters(in: CharacterSet.alphanumerics.inverted)");
            content.AppendLine("        var int: UInt64 = 0");
            content.AppendLine("        Scanner(string: hex).scanHexInt64(&int)");
            content.AppendLine("        let a, r, g, b: UInt64");
            content.AppendLine("        switch hex.count {");
            content.AppendLine("        case 3: // RGB (12-bit)");
            content.AppendLine("            (a, r, g, b) = (255, (int >> 8) * 17, (int >> 4 & 0xF) * 17, (int & 0xF) * 17)");
            content.AppendLine("        case 6: // RGB (24-bit)");
            content.AppendLine("            (a, r, g, b) = (255, int >> 16, int >> 8 & 0xFF, int & 0xFF)");
            content.AppendLine("        case 8: // ARGB (32-bit)");
            content.AppendLine("            (a, r, g, b) = (int >> 24, int >> 16 & 0xFF, int >> 8 & 0xFF, int & 0xFF)");
            content.AppendLine("        default:");
            content.AppendLine("            (a, r, g, b) = (255, 0, 0, 0)");
            content.AppendLine("        }");
            content.AppendLine("        self.init(");
            content.AppendLine("            .sRGB,");
            content.AppendLine("            red: Double(r) / 255,");
            content.AppendLine("            green: Double(g) / 255,");
            content.AppendLine("            blue: Double(b) / 255,");
            content.AppendLine("            opacity: Double(a) / 255");
            content.AppendLine("        )");
            content.AppendLine("    }");
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
            var moduleName = GetModuleName(request);
            var filePath = Path.Combine(outputPath, "Typography.swift");

            var content = new StringBuilder();
            content.AppendLine("import UIKit");
            content.AppendLine("import SwiftUI");
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
            content.AppendLine($"public extension {moduleName} {{");
            content.AppendLine("    struct Typography {");

            foreach (var token in typographyTokens.OrderBy(t => t.Name))
            {
                var styleName = ToCamelCase(token.Name);
                var font = GenerateFont(token);

                if (!string.IsNullOrEmpty(token.Description))
                {
                    content.AppendLine($"        /// {token.Description}");
                }
                content.AppendLine($"        public static let {styleName} = {font}");
            }

            content.AppendLine("    }");
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
            var moduleName = GetModuleName(request);
            var filePath = Path.Combine(outputPath, "Spacing.swift");

            var content = new StringBuilder();
            content.AppendLine("import CoreGraphics");
            content.AppendLine("import SwiftUI");
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
            content.AppendLine($"public extension {moduleName} {{");
            content.AppendLine("    struct Spacing {");

            foreach (var token in spacingTokens.OrderBy(t => t.Name))
            {
                var spacingName = ToCamelCase(token.Name);
                var spacingValue = ConvertToSwiftCGFloat(token.Value?.ToString() ?? "0px");

                if (!string.IsNullOrEmpty(token.Description))
                {
                    content.AppendLine($"        /// {token.Description}");
                }
                content.AppendLine($"        public static let {spacingName}: CGFloat = {spacingValue}");
            }

            content.AppendLine("    }");
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
            var moduleName = GetModuleName(request);
            var themeName = GetThemeName(request);
            var filePath = Path.Combine(outputPath, "Theme.swift");

            var content = new StringBuilder();
            content.AppendLine("import SwiftUI");
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
            content.AppendLine($"public struct {themeName} {{");
            content.AppendLine("    public static let colors = Colors.self");
            content.AppendLine("    public static let typography = Typography.self");
            content.AppendLine("    public static let spacing = Spacing.self");
            content.AppendLine("}");
            content.AppendLine();

            // Generate theme environment
            content.AppendLine("// Environment support for theme");
            content.AppendLine($"private struct {themeName}Key: EnvironmentKey {{");
            content.AppendLine($"    static let defaultValue = {themeName}.self");
            content.AppendLine("}");
            content.AppendLine();

            content.AppendLine("public extension EnvironmentValues {");
            content.AppendLine($"    var {ToCamelCase(themeName)}: {themeName}.Type {{");
            content.AppendLine($"        get {{ self[{themeName}Key.self] }}");
            content.AppendLine($"        set {{ self[{themeName}Key.self] = newValue }}");
            content.AppendLine("    }");
            content.AppendLine("}");
            content.AppendLine();

            // Generate view modifier
            content.AppendLine($"public extension View {{");
            content.AppendLine($"    func {ToCamelCase(themeName)}() -> some View {{");
            content.AppendLine($"        environment(\\.{ToCamelCase(themeName)}, {themeName}.self)");
            content.AppendLine("    }");
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

        private string GetModuleName(GenerationRequest request)
        {
            return request.Platform.IosModuleName ?? "DesignTokens";
        }

        private string GetThemeName(GenerationRequest request)
        {
            return request.Platform.IosThemeName ?? "DesignTokenTheme";
        }

        private string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var words = input.Split('-', '_', ' ');
            if (words.Length == 0) return string.Empty;

            var result = words[0].ToLowerInvariant();
            for (int i = 1; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    result += char.ToUpperInvariant(words[i][0]) + words[i].Substring(1).ToLowerInvariant();
                }
            }

            return result;
        }

        private string ConvertToSwiftColor(string colorValue)
        {
            if (colorValue.StartsWith("#"))
            {
                return colorValue;
            }
            return "#000000";
        }

        private string GenerateFont(DesignTokenModel token)
        {
            if (token.Value is Dictionary<string, object> typography)
            {
                var fontSize = "16";
                var fontWeight = "regular";
                var fontFamily = "system";

                if (typography.TryGetValue("fontSize", out var size))
                {
                    fontSize = ConvertToSwiftCGFloat(size.ToString() ?? "16px");
                }

                if (typography.TryGetValue("fontWeight", out var weight))
                {
                    fontWeight = ConvertToSwiftFontWeight(weight.ToString() ?? "400");
                }

                if (typography.TryGetValue("fontFamily", out var family))
                {
                    fontFamily = ConvertToSwiftFontFamily(family.ToString() ?? "system");
                }

                if (fontFamily == "system")
                {
                    return $"Font.system(size: {fontSize}, weight: .{fontWeight})";
                }
                else
                {
                    return $"Font.custom(\"{fontFamily}\", size: {fontSize})";
                }
            }

            return "Font.system(size: 16, weight: .regular)";
        }

        private string ConvertToSwiftCGFloat(string value)
        {
            if (value.EndsWith("px"))
            {
                var numericPart = value.Replace("px", "");
                if (double.TryParse(numericPart, out var pixels))
                {
                    return pixels.ToString("F1");
                }
            }
            return "16.0";
        }

        private string ConvertToSwiftFontWeight(string weight)
        {
            return weight switch
            {
                "100" => "thin",
                "200" => "ultraLight",
                "300" => "light",
                "400" => "regular",
                "500" => "medium",
                "600" => "semibold",
                "700" => "bold",
                "800" => "heavy",
                "900" => "black",
                _ => "regular"
            };
        }

        private string ConvertToSwiftFontFamily(string family)
        {
            if (string.IsNullOrEmpty(family) || family == "inherit" || family == "system")
            {
                return "system";
            }
            return family;
        }
    }
}