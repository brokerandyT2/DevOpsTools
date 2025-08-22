using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public class WebGeneratorService : IWebGeneratorService
    {
        private readonly IAppLogger _logger;
        private readonly string _workingDirectory = "/src";

        public WebGeneratorService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, TokensConfiguration config)
        {
            try
            {
                var template = config.Web.Template;
                _logger.LogInfo($"Generating Web/{template} design token files...");
                var result = new GenerationResult { Platform = "web", Success = true, Files = new List<GeneratedFile>() };
                var outputPath = Path.Combine(_workingDirectory, request.OutputDirectory);
                Directory.CreateDirectory(outputPath);

                // This switch statement now correctly handles all supported web templates.
                switch (template.ToLowerInvariant())
                {
                    case "tailwind":
                        result.Files.Add(await GenerateTailwindConfigFileAsync(request, outputPath));
                        break;
                    case "bootstrap":
                        result.Files.Add(await GenerateBootstrapVariablesAsync(request, outputPath));
                        break;
                    case "material":
                        result.Files.Add(await GenerateMaterialThemeFileAsync(request, outputPath));
                        break;
                    case "vanilla":
                    default:
                        result.Files.Add(await GenerateVanillaCssAsync(request, outputPath));
                        break;
                }

                result.Metadata["generatedFiles"] = result.Files.Count;
                result.Metadata["outputDirectory"] = outputPath;
                result.Metadata["template"] = template;

                _logger.LogInfo($"✓ Generated {result.Files.Count} Web file(s).");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("Web generation failed", ex);
                return new GenerationResult { Platform = "web", Success = false, ErrorMessage = ex.Message };
            }
        }

        #region Private Template Generators

        private async Task<GeneratedFile> GenerateVanillaCssAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "tokens.css");
            var content = new StringBuilder();
            content.AppendLine("/* AUTO-GENERATED STYLES - DO NOT EDIT */");
            content.AppendLine(":root {");
            foreach (var token in request.Tokens.Tokens.OrderBy(t => t.Name))
            {
                var cssVar = $"--{ToKebabCase(token.Name)}";
                content.AppendLine($"  {cssVar}: {token.Value?.ToString() ?? ""};");
            }
            content.AppendLine("}");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);
            return new GeneratedFile { FilePath = filePath, Content = fileContent };
        }

        private async Task<GeneratedFile> GenerateTailwindConfigFileAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "tailwind.tokens.js");
            var content = new StringBuilder();
            content.AppendLine("// AUTO-GENERATED TAILWIND TOKENS - DO NOT EDIT");
            content.AppendLine("module.exports = {");
            content.AppendLine("  theme: {");
            content.AppendLine("    extend: {");

            var colors = request.Tokens.Tokens.Where(t => t.Type == "color").ToList();
            if (colors.Any())
            {
                content.AppendLine("      colors: {");
                foreach (var token in colors) content.AppendLine($"        '{ToKebabCase(token.Name)}': '{token.Value}',");
                content.AppendLine("      },");
            }

            var spacing = request.Tokens.Tokens.Where(t => t.Type == "spacing" || t.Type == "sizing").ToList();
            if (spacing.Any())
            {
                content.AppendLine("      spacing: {");
                foreach (var token in spacing) content.AppendLine($"        '{ToKebabCase(token.Name)}': '{token.Value}',");
                content.AppendLine("      },");
            }

            content.AppendLine("    }");
            content.AppendLine("  }");
            content.AppendLine("};");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);
            return new GeneratedFile { FilePath = filePath, Content = fileContent };
        }

        private async Task<GeneratedFile> GenerateBootstrapVariablesAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "_variables.scss");
            var content = new StringBuilder();
            content.AppendLine("// AUTO-GENERATED BOOTSTRAP VARIABLES - DO NOT EDIT");
            content.AppendLine();

            foreach (var token in request.Tokens.Tokens.OrderBy(t => t.Name))
            {
                var scssVar = $"${ToKebabCase(token.Name)}";
                content.AppendLine($"{scssVar}: {token.Value?.ToString() ?? ""};");
            }

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);
            return new GeneratedFile { FilePath = filePath, Content = fileContent };
        }

        private async Task<GeneratedFile> GenerateMaterialThemeFileAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "material-theme.js");
            var content = new StringBuilder();
            content.AppendLine("// AUTO-GENERATED MATERIAL DESIGN THEME");
            content.AppendLine("import { createTheme } from '@mui/material/styles';");
            content.AppendLine();
            content.AppendLine("export const designTokenTheme = createTheme({");

            var colors = request.Tokens.Tokens.Where(t => t.Type == "color").ToList();
            if (colors.Any())
            {
                content.AppendLine("  palette: {");
                foreach (var token in colors)
                {
                    var materialName = MapToMaterialColorName(token.Name);
                    if (!string.IsNullOrEmpty(materialName))
                    {
                        content.AppendLine($"    {materialName}: {{ main: '{token.Value}' }},");
                    }
                }
                content.AppendLine("  },");
            }

            content.AppendLine("});");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);
            return new GeneratedFile { FilePath = filePath, Content = fileContent };
        }

        #endregion

        #region Private Helpers

        private string ToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("_", "-").ToLowerInvariant();
        }

        private string MapToMaterialColorName(string tokenName)
        {
            var name = tokenName.ToLowerInvariant();
            if (name.Contains("primary")) return "primary";
            if (name.Contains("secondary")) return "secondary";
            if (name.Contains("error")) return "error";
            if (name.Contains("warning")) return "warning";
            if (name.Contains("info")) return "info";
            if (name.Contains("success")) return "success";
            return string.Empty;
        }

        #endregion
    }
}