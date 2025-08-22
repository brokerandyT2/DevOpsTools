using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public class IosGeneratorService : IIosGeneratorService
    {
        private readonly IAppLogger _logger;
        private readonly string _workingDirectory = "/src";

        public IosGeneratorService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, TokensConfiguration config)
        {
            try
            {
                _logger.LogInfo("Generating iOS/Swift design token files...");
                var result = new GenerationResult { Platform = "ios", Success = true, Files = new List<GeneratedFile>() };
                var outputPath = Path.Combine(_workingDirectory, request.OutputDirectory);
                Directory.CreateDirectory(outputPath);

                var colorsFile = await GenerateColorsFileAsync(request, config, outputPath);
                result.Files.Add(colorsFile);

                // Add generation for other token types (Typography, Spacing, etc.) here.

                result.Metadata["generatedFiles"] = result.Files.Count;
                result.Metadata["outputDirectory"] = outputPath;
                result.Metadata["moduleName"] = GetModuleName(config);

                _logger.LogInfo($"✓ Generated {result.Files.Count} iOS file(s).");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("iOS generation failed", ex);
                return new GenerationResult { Platform = "ios", Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<GeneratedFile> GenerateColorsFileAsync(GenerationRequest request, TokensConfiguration config, string outputPath)
        {
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").ToList();
            var moduleName = GetModuleName(config);
            var filePath = Path.Combine(outputPath, "Colors.swift");

            var content = new StringBuilder();
            content.AppendLine("import SwiftUI");
            content.AppendLine();
            content.AppendLine("// AUTO-GENERATED CONTENT - DO NOT EDIT");
            content.AppendLine($"public struct {moduleName}Colors {{");

            foreach (var token in colorTokens.OrderBy(t => t.Name))
            {
                var colorName = ToCamelCase(token.Name);
                var hexValue = token.Value?.ToString() ?? "#000000";
                content.AppendLine($"    public static let {colorName} = Color(hex: \"{hexValue}\")");
            }

            content.AppendLine("}");
            content.AppendLine();
            content.AppendLine(GetSwiftHexColorExtension());

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);
            return new GeneratedFile { FilePath = filePath, Content = fileContent };
        }

        private string GetModuleName(TokensConfiguration config)
        {
            var moduleName = Environment.GetEnvironmentVariable("TOKENS_IOS_MODULE_NAME");
            return string.IsNullOrEmpty(moduleName) ? "DesignTokens" : moduleName;
        }

        private string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var words = input.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return string.Empty;
            var firstWord = words[0].ToLowerInvariant();
            var otherWords = words.Skip(1).Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant());
            return firstWord + string.Concat(otherWords);
        }

        private string GetSwiftHexColorExtension()
        {
            return @"
// Color extension for hex color support
private extension Color {
    init(hex: String) {
        let hex = hex.trimmingCharacters(in: CharacterSet.alphanumerics.inverted)
        var int: UInt64 = 0
        Scanner(string: hex).scanHexInt64(&int)
        let a, r, g, b: UInt64
        switch hex.count {
        case 3: // RGB (12-bit)
            (a, r, g, b) = (255, (int >> 8) * 17, (int >> 4 & 0xF) * 17, (int & 0xF) * 17)
        case 6: // RGB (24-bit)
            (a, r, g, b) = (255, int >> 16, int >> 8 & 0xFF, int & 0xFF)
        case 8: // ARGB (32-bit)
            (a, r, g, b) = (int >> 24, int >> 16 & 0xFF, int >> 8 & 0xFF, int & 0xFF)
        default:
            (a, r, g, b) = (255, 0, 0, 0)
        }
        self.init(
            .sRGB,
            red: Double(r) / 255,
            green: Double(g) / 255,
            blue: Double(b) / 255,
            opacity: Double(a) / 255
        )
    }
}";
        }
    }
}