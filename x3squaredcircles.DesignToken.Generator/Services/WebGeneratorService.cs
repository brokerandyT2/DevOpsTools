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
    public interface IWebGeneratorService
    {
        Task<GenerationResult> GenerateAsync(GenerationRequest request);
    }

    public class WebGeneratorService : IWebGeneratorService
    {
        private readonly ILogger<WebGeneratorService> _logger;
        private readonly string _workingDirectory = "/src";

        public WebGeneratorService(ILogger<WebGeneratorService> logger)
        {
            _logger = logger;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request)
        {
            try
            {
                _logger.LogInformation("Generating Web/{Template} design token files", request.Platform.WebTemplate);

                var result = new GenerationResult
                {
                    Platform = "web",
                    Success = true,
                    Files = new List<GeneratedFile>()
                };

                var outputPath = Path.Combine(_workingDirectory, request.OutputDirectory);
                Directory.CreateDirectory(outputPath);

                var template = request.Platform.WebTemplate.ToLowerInvariant();

                switch (template)
                {
                    case "vanilla":
                        await GenerateVanillaCssAsync(request, outputPath, result);
                        break;
                    case "tailwind":
                        await GenerateTailwindConfigAsync(request, outputPath, result);
                        break;
                    case "bootstrap":
                        await GenerateBootstrapScssAsync(request, outputPath, result);
                        break;
                    case "material":
                        await GenerateMaterialThemeAsync(request, outputPath, result);
                        break;
                    default:
                        await GenerateVanillaCssAsync(request, outputPath, result);
                        break;
                }

                result.Metadata["generated_files"] = result.Files.Count;
                result.Metadata["output_directory"] = outputPath;
                result.Metadata["template"] = template;
                result.Metadata["css_prefix"] = GetCssPrefix(request);

                _logger.LogInformation("✓ Generated {FileCount} Web files", result.Files.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Web generation failed");
                return new GenerationResult
                {
                    Platform = "web",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task GenerateVanillaCssAsync(GenerationRequest request, string outputPath, GenerationResult result)
        {
            // Generate tokens.css
            var tokensFile = await GenerateTokensCssAsync(request, outputPath);
            result.Files.Add(tokensFile);

            // Generate theme.css
            var themeFile = await GenerateThemeCssAsync(request, outputPath);
            result.Files.Add(themeFile);

            // Generate utilities.css
            var utilitiesFile = await GenerateUtilitiesCssAsync(request, outputPath);
            result.Files.Add(utilitiesFile);
        }

        private async Task GenerateTailwindConfigAsync(GenerationRequest request, string outputPath, GenerationResult result)
        {
            var configFile = await GenerateTailwindConfigFileAsync(request, outputPath);
            result.Files.Add(configFile);

            var cssFile = await GenerateTailwindCssFileAsync(request, outputPath);
            result.Files.Add(cssFile);
        }

        private async Task GenerateBootstrapScssAsync(GenerationRequest request, string outputPath, GenerationResult result)
        {
            var variablesFile = await GenerateBootstrapVariablesAsync(request, outputPath);
            result.Files.Add(variablesFile);

            var customFile = await GenerateBootstrapCustomAsync(request, outputPath);
            result.Files.Add(customFile);
        }

        private async Task GenerateMaterialThemeAsync(GenerationRequest request, string outputPath, GenerationResult result)
        {
            var themeFile = await GenerateMaterialThemeFileAsync(request, outputPath);
            result.Files.Add(themeFile);

            var cssFile = await GenerateMaterialCssFileAsync(request, outputPath);
            result.Files.Add(cssFile);
        }

        private async Task<GeneratedFile> GenerateTokensCssAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "tokens.css");
            var prefix = GetCssPrefix(request);

            var content = new StringBuilder();

            var existingContent = await ReadExistingFileAsync(filePath);
            var customSections = ExtractCustomSections(existingContent);

            if (customSections.Any())
            {
                content.AppendLine("/**********************************/");
                content.AppendLine("/* Custom CSS Variables - Preserved */");
                content.AppendLine("/**********************************/");
                content.AppendLine();
                foreach (var section in customSections)
                {
                    content.AppendLine(section.Content);
                }
                content.AppendLine();
                content.AppendLine("/**********************************/");
                content.AppendLine("/* End Custom Section */");
                content.AppendLine("/**********************************/");
                content.AppendLine();
            }

            content.AppendLine("/* AUTO-GENERATED STYLES - DO NOT EDIT */");
            content.AppendLine(":root {");

            // Generate color tokens
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").OrderBy(t => t.Name);
            foreach (var token in colorTokens)
            {
                var cssVar = ToCssVariable(token.Name, prefix);
                var colorValue = token.Value?.ToString() ?? "#000000";
                content.AppendLine($"  {cssVar}: {colorValue};");
            }

            // Generate typography tokens
            var typographyTokens = request.Tokens.Tokens.Where(t => t.Type == "typography").OrderBy(t => t.Name);
            foreach (var token in typographyTokens)
            {
                if (token.Value is Dictionary<string, object> typography)
                {
                    var baseName = ToCssVariableName(token.Name, prefix);

                    if (typography.TryGetValue("fontFamily", out var fontFamily))
                    {
                        content.AppendLine($"  --{baseName}-font-family: {fontFamily};");
                    }
                    if (typography.TryGetValue("fontSize", out var fontSize))
                    {
                        content.AppendLine($"  --{baseName}-font-size: {fontSize};");
                    }
                    if (typography.TryGetValue("fontWeight", out var fontWeight))
                    {
                        content.AppendLine($"  --{baseName}-font-weight: {fontWeight};");
                    }
                    if (typography.TryGetValue("lineHeight", out var lineHeight))
                    {
                        content.AppendLine($"  --{baseName}-line-height: {lineHeight};");
                    }
                }
            }

            // Generate spacing tokens
            var spacingTokens = request.Tokens.Tokens.Where(t => t.Type == "spacing" || t.Type == "sizing").OrderBy(t => t.Name);
            foreach (var token in spacingTokens)
            {
                var cssVar = ToCssVariable(token.Name, prefix);
                var spacingValue = token.Value?.ToString() ?? "0px";
                content.AppendLine($"  {cssVar}: {spacingValue};");
            }

            content.AppendLine("}");

            // Generate dark mode variants if enabled
            if (request.Platform.WebSupportDarkMode)
            {
                content.AppendLine();
                content.AppendLine("@media (prefers-color-scheme: dark) {");
                content.AppendLine("  :root {");

                var darkColorTokens = request.Tokens.Tokens.Where(t => t.Type == "color" && t.Name.EndsWith("-dark"));
                foreach (var token in darkColorTokens)
                {
                    var baseName = token.Name.Replace("-dark", "");
                    var cssVar = ToCssVariable(baseName, prefix);
                    var colorValue = token.Value?.ToString() ?? "#000000";
                    content.AppendLine($"    {cssVar}: {colorValue};");
                }

                content.AppendLine("  }");
                content.AppendLine("}");
            }

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

        private async Task<GeneratedFile> GenerateThemeCssAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "theme.css");
            var prefix = GetCssPrefix(request);

            var content = new StringBuilder();

            var existingContent = await ReadExistingFileAsync(filePath);
            var customSections = ExtractCustomSections(existingContent);

            if (customSections.Any())
            {
                content.AppendLine("/**********************************/");
                content.AppendLine("/* Custom Theme Styles - Preserved */");
                content.AppendLine("/**********************************/");
                content.AppendLine();
                foreach (var section in customSections)
                {
                    content.AppendLine(section.Content);
                }
                content.AppendLine();
                content.AppendLine("/**********************************/");
                content.AppendLine("/* End Custom Section */");
                content.AppendLine("/**********************************/");
                content.AppendLine();
            }

            content.AppendLine("/* AUTO-GENERATED THEME STYLES - DO NOT EDIT */");

            // Generate utility classes for colors
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").OrderBy(t => t.Name);
            foreach (var token in colorTokens)
            {
                var className = ToKebabCase(token.Name);
                var cssVar = ToCssVariable(token.Name, prefix);

                content.AppendLine($".{prefix}text-{className} {{");
                content.AppendLine($"  color: var({cssVar});");
                content.AppendLine("}");
                content.AppendLine();

                content.AppendLine($".{prefix}bg-{className} {{");
                content.AppendLine($"  background-color: var({cssVar});");
                content.AppendLine("}");
                content.AppendLine();
            }

            // Generate typography classes
            var typographyTokens = request.Tokens.Tokens.Where(t => t.Type == "typography").OrderBy(t => t.Name);
            foreach (var token in typographyTokens)
            {
                var className = ToKebabCase(token.Name);
                var baseName = ToCssVariableName(token.Name, prefix);

                content.AppendLine($".{prefix}text-{className} {{");
                content.AppendLine($"  font-family: var(--{baseName}-font-family);");
                content.AppendLine($"  font-size: var(--{baseName}-font-size);");
                content.AppendLine($"  font-weight: var(--{baseName}-font-weight);");
                content.AppendLine($"  line-height: var(--{baseName}-line-height);");
                content.AppendLine("}");
                content.AppendLine();
            }

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

        private async Task<GeneratedFile> GenerateUtilitiesCssAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "utilities.css");
            var prefix = GetCssPrefix(request);

            var content = new StringBuilder();
            content.AppendLine("/* AUTO-GENERATED UTILITY CLASSES - DO NOT EDIT */");

            // Generate spacing utilities
            var spacingTokens = request.Tokens.Tokens.Where(t => t.Type == "spacing").OrderBy(t => t.Name);
            foreach (var token in spacingTokens)
            {
                var className = ToKebabCase(token.Name);
                var cssVar = ToCssVariable(token.Name, prefix);

                content.AppendLine($".{prefix}m-{className} {{ margin: var({cssVar}); }}");
                content.AppendLine($".{prefix}mt-{className} {{ margin-top: var({cssVar}); }}");
                content.AppendLine($".{prefix}mr-{className} {{ margin-right: var({cssVar}); }}");
                content.AppendLine($".{prefix}mb-{className} {{ margin-bottom: var({cssVar}); }}");
                content.AppendLine($".{prefix}ml-{className} {{ margin-left: var({cssVar}); }}");
                content.AppendLine($".{prefix}p-{className} {{ padding: var({cssVar}); }}");
                content.AppendLine($".{prefix}pt-{className} {{ padding-top: var({cssVar}); }}");
                content.AppendLine($".{prefix}pr-{className} {{ padding-right: var({cssVar}); }}");
                content.AppendLine($".{prefix}pb-{className} {{ padding-bottom: var({cssVar}); }}");
                content.AppendLine($".{prefix}pl-{className} {{ padding-left: var({cssVar}); }}");
                content.AppendLine();
            }

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent
            };
        }

        private async Task<GeneratedFile> GenerateTailwindConfigFileAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "tailwind.config.js");

            var content = new StringBuilder();
            content.AppendLine("// AUTO-GENERATED TAILWIND CONFIG - DO NOT EDIT");
            content.AppendLine("module.exports = {");
            content.AppendLine("  theme: {");

            if (request.Platform.TailwindExtendTheme)
            {
                content.AppendLine("    extend: {");
            }

            // Generate colors
            content.AppendLine("      colors: {");
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").OrderBy(t => t.Name);
            foreach (var token in colorTokens)
            {
                var colorName = ToTailwindName(token.Name);
                var colorValue = token.Value?.ToString() ?? "#000000";
                content.AppendLine($"        '{colorName}': '{colorValue}',");
            }
            content.AppendLine("      },");

            // Generate spacing
            content.AppendLine("      spacing: {");
            var spacingTokens = request.Tokens.Tokens.Where(t => t.Type == "spacing").OrderBy(t => t.Name);
            foreach (var token in spacingTokens)
            {
                var spacingName = ToTailwindName(token.Name);
                var spacingValue = token.Value?.ToString() ?? "0px";
                content.AppendLine($"        '{spacingName}': '{spacingValue}',");
            }
            content.AppendLine("      },");

            // Generate font sizes
            content.AppendLine("      fontSize: {");
            var typographyTokens = request.Tokens.Tokens.Where(t => t.Type == "typography").OrderBy(t => t.Name);
            foreach (var token in typographyTokens)
            {
                if (token.Value is Dictionary<string, object> typography && typography.TryGetValue("fontSize", out var fontSize))
                {
                    var fontName = ToTailwindName(token.Name);
                    content.AppendLine($"        '{fontName}': '{fontSize}',");
                }
            }
            content.AppendLine("      },");

            if (request.Platform.TailwindExtendTheme)
            {
                content.AppendLine("    },");
            }

            content.AppendLine("  },");
            content.AppendLine("};");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent
            };
        }

        private async Task<GeneratedFile> GenerateTailwindCssFileAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "tailwind.css");

            var content = new StringBuilder();
            content.AppendLine("@tailwind base;");
            content.AppendLine("@tailwind components;");
            content.AppendLine("@tailwind utilities;");
            content.AppendLine();
            content.AppendLine("/* AUTO-GENERATED CUSTOM STYLES */");
            content.AppendLine("@layer components {");

            var typographyTokens = request.Tokens.Tokens.Where(t => t.Type == "typography").OrderBy(t => t.Name);
            foreach (var token in typographyTokens)
            {
                var className = ToKebabCase(token.Name);
                content.AppendLine($"  .text-{className} {{");

                if (token.Value is Dictionary<string, object> typography)
                {
                    if (typography.TryGetValue("fontSize", out var fontSize))
                        content.AppendLine($"    font-size: {fontSize};");
                    if (typography.TryGetValue("fontWeight", out var fontWeight))
                        content.AppendLine($"    font-weight: {fontWeight};");
                    if (typography.TryGetValue("lineHeight", out var lineHeight))
                        content.AppendLine($"    line-height: {lineHeight};");
                }

                content.AppendLine("  }");
            }

            content.AppendLine("}");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent
            };
        }

        private async Task<GeneratedFile> GenerateBootstrapVariablesAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "_variables.scss");

            var content = new StringBuilder();
            content.AppendLine("// AUTO-GENERATED BOOTSTRAP VARIABLES - DO NOT EDIT");
            content.AppendLine();

            // Generate color variables
            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").OrderBy(t => t.Name);
            foreach (var token in colorTokens)
            {
                var scssVar = ToScssVariable(token.Name);
                var colorValue = token.Value?.ToString() ?? "#000000";
                content.AppendLine($"{scssVar}: {colorValue};");
            }

            content.AppendLine();

            // Generate spacing variables
            var spacingTokens = request.Tokens.Tokens.Where(t => t.Type == "spacing").OrderBy(t => t.Name);
            foreach (var token in spacingTokens)
            {
                var scssVar = ToScssVariable(token.Name);
                var spacingValue = token.Value?.ToString() ?? "0px";
                content.AppendLine($"{scssVar}: {spacingValue};");
            }

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent
            };
        }

        private async Task<GeneratedFile> GenerateBootstrapCustomAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "custom.scss");

            var content = new StringBuilder();
            content.AppendLine("// Import design token variables");
            content.AppendLine("@import 'variables';");
            content.AppendLine();
            content.AppendLine("// Import Bootstrap");
            content.AppendLine($"@import '~bootstrap@{request.Platform.BootstrapVersion}/scss/bootstrap';");
            content.AppendLine();
            content.AppendLine("// AUTO-GENERATED CUSTOM STYLES");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent
            };
        }

        private async Task<GeneratedFile> GenerateMaterialThemeFileAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "material-theme.js");

            var content = new StringBuilder();
            content.AppendLine("// AUTO-GENERATED MATERIAL DESIGN THEME");
            content.AppendLine("import { createTheme } from '@mui/material/styles';");
            content.AppendLine();
            content.AppendLine("export const designTokenTheme = createTheme({");
            content.AppendLine("  palette: {");

            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").OrderBy(t => t.Name);
            foreach (var token in colorTokens.Take(5))
            {
                var materialName = MapToMaterialColorName(token.Name);
                if (!string.IsNullOrEmpty(materialName))
                {
                    var colorValue = token.Value?.ToString() ?? "#000000";
                    content.AppendLine($"    {materialName}: '{colorValue}',");
                }
            }

            content.AppendLine("  },");
            content.AppendLine("  typography: {");

            var typographyTokens = request.Tokens.Tokens.Where(t => t.Type == "typography").OrderBy(t => t.Name);
            foreach (var token in typographyTokens.Take(3))
            {
                var materialName = MapToMaterialTypographyName(token.Name);
                if (!string.IsNullOrEmpty(materialName) && token.Value is Dictionary<string, object> typography)
                {
                    content.AppendLine($"    {materialName}: {{");
                    if (typography.TryGetValue("fontSize", out var fontSize))
                        content.AppendLine($"      fontSize: '{fontSize}',");
                    if (typography.TryGetValue("fontWeight", out var fontWeight))
                        content.AppendLine($"      fontWeight: {fontWeight},");
                    content.AppendLine("    },");
                }
            }

            content.AppendLine("  },");
            content.AppendLine("});");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent
            };
        }

        private async Task<GeneratedFile> GenerateMaterialCssFileAsync(GenerationRequest request, string outputPath)
        {
            var filePath = Path.Combine(outputPath, "material.css");

            var content = new StringBuilder();
            content.AppendLine("/* AUTO-GENERATED MATERIAL DESIGN CSS VARIABLES */");
            content.AppendLine(":root {");

            var colorTokens = request.Tokens.Tokens.Where(t => t.Type == "color").OrderBy(t => t.Name);
            foreach (var token in colorTokens)
            {
                var cssVar = $"--md-{ToKebabCase(token.Name)}";
                var colorValue = token.Value?.ToString() ?? "#000000";
                content.AppendLine($"  {cssVar}: {colorValue};");
            }

            content.AppendLine("}");

            var fileContent = content.ToString();
            await File.WriteAllTextAsync(filePath, fileContent);

            return new GeneratedFile
            {
                FilePath = filePath,
                Content = fileContent
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

                if (line.Contains("/**********************************/") &&
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

        private string GetCssPrefix(GenerationRequest request)
        {
            var prefix = request.Platform.WebCssPrefix;
            if (string.IsNullOrEmpty(prefix))
                return string.Empty;

            return prefix.EndsWith("-") ? prefix : prefix + "-";
        }

        private string ToCssVariable(string name, string prefix)
        {
            return $"--{ToCssVariableName(name, prefix)}";
        }

        private string ToCssVariableName(string name, string prefix)
        {
            var kebabName = ToKebabCase(name);
            return string.IsNullOrEmpty(prefix) ? kebabName : $"{prefix.TrimEnd('-')}-{kebabName}";
        }

        private string ToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input.Replace("_", "-").ToLowerInvariant();
        }

        private string ToTailwindName(string input)
        {
            return ToKebabCase(input);
        }

        private string ToScssVariable(string name)
        {
            return $"${ToKebabCase(name)}";
        }

        private string MapToMaterialColorName(string tokenName)
        {
            var name = tokenName.ToLowerInvariant();
            if (name.Contains("primary")) return "primary";
            if (name.Contains("secondary")) return "secondary";
            if (name.Contains("error")) return "error";
            if (name.Contains("warning")) return "warning";
            if (name.Contains("info")) return "info";
            return string.Empty;
        }

        private string MapToMaterialTypographyName(string tokenName)
        {
            var name = tokenName.ToLowerInvariant();
            if (name.Contains("h1") || name.Contains("heading1")) return "h1";
            if (name.Contains("h2") || name.Contains("heading2")) return "h2";
            if (name.Contains("body") || name.Contains("paragraph")) return "body1";
            return string.Empty;
        }
    }
}