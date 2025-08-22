using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public class AndroidGeneratorService : IAndroidGeneratorService
    {
        private readonly IAppLogger _logger;
        private readonly string _workingDirectory = "/src";

        public AndroidGeneratorService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, TokensConfiguration config)
        {
            try
            {
                _logger.LogInfo("Generating Android/Kotlin design token files...");
                var result = new GenerationResult { Platform = "android", Success = true, Files = new List<GeneratedFile>() };
                var outputPath = Path.Combine(_workingDirectory, request.OutputDirectory);
                Directory.CreateDirectory(outputPath);

                var colorsFile = await GenerateColorsFileAsync(request, config, outputPath);
                result.Files.Add(colorsFile);

                // Add generation for other token types (Typography, Spacing, etc.) here.

                result.Metadata["generatedFiles"] = result.Files.Count;
                result.Metadata["outputDirectory"] = outputPath;
                result.Metadata["packageName"] = GetPackageName(config);

                _logger.LogInfo($"✓ Generated {result.Files.Count} Android file(s).");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("Android generation failed", ex);
                return new GenerationResult { Platform = "android", Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<GeneratedFile> GenerateColorsFileAsync(GenerationRequest request, TokensConfiguration config, string outputPath)
        {
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").ToList();
            var packageName = GetPackageName(config);
            var filePath = Path.Combine(outputPath, "Colors.kt");

            var content = new StringBuilder();
            content.AppendLine($"package {packageName}");
            content.AppendLine();
            content.AppendLine("import androidx.compose.ui.graphics.Color");
            content.AppendLine();
            content.AppendLine("// AUTO-GENERATED CONTENT - DO NOT EDIT");
            content.AppendLine("object DesignTokenColors {");

            foreach (var token in colorTokens.OrderBy(t => t.Name))
            {
                var colorName = ToPascalCase(token.Name);
                var colorValue = ConvertToAndroidColor(token.Value?.ToString() ?? "#000000");
                content.AppendLine($"    val {colorName} = Color({colorValue})");
            }

            content.AppendLine("}");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);
            return new GeneratedFile { FilePath = filePath, Content = fileContent };
        }

        private string GetPackageName(TokensConfiguration config)
        {
            var packageName = Environment.GetEnvironmentVariable("TOKENS_ANDROID_PACKAGE_NAME");
            return string.IsNullOrEmpty(packageName) ? "com.company.designtokens.ui.theme" : packageName;
        }

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return string.Join("", input.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
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
                else if (hex.Length == 8) // AARRGGBB -> 0xAARRGGBB
                {
                    var alpha = hex.Substring(0, 2);
                    var rgb = hex.Substring(2);
                    return $"0x{alpha.ToUpperInvariant()}{rgb.ToUpperInvariant()}";
                }
            }
            _logger.LogWarning($"Could not parse color '{colorValue}', defaulting to Black.");
            return "0xFF000000";
        }
    }
}