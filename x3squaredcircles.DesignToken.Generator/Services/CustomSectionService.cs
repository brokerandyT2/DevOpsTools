using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface ICustomSectionService
    {
        Task<List<CustomSection>> ExtractCustomSectionsAsync(string filePath, string platform);
        Task<string> MergeCustomSectionsAsync(string generatedContent, List<CustomSection> customSections, string platform, string mergeStrategy);
        Task SaveCustomSectionsInventoryAsync(List<GeneratedFile> files, string outputDirectory);
        Task<bool> HasCustomSectionConflictsAsync(List<CustomSection> existing, List<CustomSection> incoming);
    }

    public class CustomSectionService : ICustomSectionService
    {
        private readonly ILogger<CustomSectionService> _logger;

        public CustomSectionService(ILogger<CustomSectionService> logger)
        {
            _logger = logger;
        }

        public async Task<List<CustomSection>> ExtractCustomSectionsAsync(string filePath, string platform)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogDebug("File does not exist, no custom sections to extract: {FilePath}", filePath);
                    return new List<CustomSection>();
                }

                var content = await File.ReadAllTextAsync(filePath);
                return ExtractCustomSectionsByPlatform(content, platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract custom sections from: {FilePath}", filePath);
                return new List<CustomSection>();
            }
        }

        public async Task<string> MergeCustomSectionsAsync(string generatedContent, List<CustomSection> customSections, string platform, string mergeStrategy)
        {
            try
            {
                if (!customSections.Any())
                {
                    _logger.LogDebug("No custom sections to merge");
                    return generatedContent;
                }

                _logger.LogInformation("Merging {SectionCount} custom sections using strategy: {Strategy}",
                    customSections.Count, mergeStrategy);

                return mergeStrategy.ToLowerInvariant() switch
                {
                    "preserve-custom" => await PreserveCustomMergeAsync(generatedContent, customSections, platform),
                    "overwrite" => generatedContent, // Just return generated content, ignoring custom sections
                    "prompt" => await PromptMergeAsync(generatedContent, customSections, platform),
                    _ => await PreserveCustomMergeAsync(generatedContent, customSections, platform)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to merge custom sections");
                throw new DesignTokenException(DesignTokenExitCode.CustomSectionConflict,
                    $"Custom section merge failed: {ex.Message}", ex);
            }
        }

        public async Task SaveCustomSectionsInventoryAsync(List<GeneratedFile> files, string outputDirectory)
        {
            try
            {
                var inventory = new
                {
                    generated_at = DateTime.UtcNow,
                    total_files = files.Count,
                    files_with_custom_sections = files.Count(f => f.HasCustomSections),
                    files = files.Select(f => new
                    {
                        file_path = f.FilePath,
                        has_custom_sections = f.HasCustomSections,
                        custom_sections = f.PreservedSections.Select(s => new
                        {
                            name = s.Name,
                            start_line = s.StartLine,
                            end_line = s.EndLine,
                            content_length = s.Content.Length
                        }).ToList()
                    }).ToList()
                };

                var inventoryPath = Path.Combine(outputDirectory, "custom-sections.json");
                var json = JsonSerializer.Serialize(inventory, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                await File.WriteAllTextAsync(inventoryPath, json);
                _logger.LogInformation("✓ Saved custom sections inventory: {InventoryPath}", inventoryPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save custom sections inventory");
                throw;
            }
        }

        public async Task<bool> HasCustomSectionConflictsAsync(List<CustomSection> existing, List<CustomSection> incoming)
        {
            try
            {
                foreach (var incomingSection in incoming)
                {
                    var existingSection = existing.FirstOrDefault(e => e.Name == incomingSection.Name);
                    if (existingSection != null)
                    {
                        // Check if content has changed
                        var existingHash = ComputeContentHash(existingSection.Content);
                        var incomingHash = ComputeContentHash(incomingSection.Content);

                        if (existingHash != incomingHash)
                        {
                            _logger.LogWarning("Custom section conflict detected: {SectionName}", incomingSection.Name);
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check for custom section conflicts");
                return true; // Assume conflict if we can't determine
            }
        }

        private List<CustomSection> ExtractCustomSectionsByPlatform(string content, string platform)
        {
            return platform.ToLowerInvariant() switch
            {
                "android" => ExtractKotlinCustomSections(content),
                "ios" => ExtractSwiftCustomSections(content),
                "web" => ExtractCssCustomSections(content),
                _ => new List<CustomSection>()
            };
        }

        private List<CustomSection> ExtractKotlinCustomSections(string content)
        {
            var sections = new List<CustomSection>();
            var lines = content.Split('\n');
            var inCustomSection = false;
            var currentSection = new StringBuilder();
            var startLine = 0;
            var sectionName = "Custom";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Contains("/////////////////////////////////////////") &&
                    i + 1 < lines.Length &&
                    lines[i + 1].Contains("Preserved"))
                {
                    inCustomSection = true;
                    startLine = i;
                    sectionName = ExtractSectionName(lines[i + 1]) ?? "Custom";
                    continue;
                }

                if (inCustomSection && line.Contains("End Custom Section"))
                {
                    sections.Add(new CustomSection
                    {
                        Name = sectionName,
                        Content = currentSection.ToString().Trim(),
                        StartLine = startLine,
                        EndLine = i
                    });
                    currentSection.Clear();
                    inCustomSection = false;
                    continue;
                }

                if (inCustomSection && !line.Contains("/////////////////////////////////////////"))
                {
                    currentSection.AppendLine(line);
                }
            }

            return sections;
        }

        private List<CustomSection> ExtractSwiftCustomSections(string content)
        {
            var sections = new List<CustomSection>();
            var lines = content.Split('\n');
            var inCustomSection = false;
            var currentSection = new StringBuilder();
            var startLine = 0;
            var sectionName = "Custom";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Contains("/////////////////////////////////////////") &&
                    i + 1 < lines.Length &&
                    lines[i + 1].Contains("Preserved"))
                {
                    inCustomSection = true;
                    startLine = i;
                    sectionName = ExtractSectionName(lines[i + 1]) ?? "Custom";
                    continue;
                }

                if (inCustomSection && line.Contains("End Custom Section"))
                {
                    sections.Add(new CustomSection
                    {
                        Name = sectionName,
                        Content = currentSection.ToString().Trim(),
                        StartLine = startLine,
                        EndLine = i
                    });
                    currentSection.Clear();
                    inCustomSection = false;
                    continue;
                }

                if (inCustomSection && !line.Contains("/////////////////////////////////////////"))
                {
                    currentSection.AppendLine(line);
                }
            }

            return sections;
        }

        private List<CustomSection> ExtractCssCustomSections(string content)
        {
            var sections = new List<CustomSection>();
            var lines = content.Split('\n');
            var inCustomSection = false;
            var currentSection = new StringBuilder();
            var startLine = 0;
            var sectionName = "Custom";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Contains("/**********************************/") &&
                    i + 1 < lines.Length &&
                    lines[i + 1].Contains("Preserved"))
                {
                    inCustomSection = true;
                    startLine = i;
                    sectionName = ExtractSectionName(lines[i + 1]) ?? "Custom";
                    continue;
                }

                if (inCustomSection && line.Contains("End Custom Section"))
                {
                    sections.Add(new CustomSection
                    {
                        Name = sectionName,
                        Content = currentSection.ToString().Trim(),
                        StartLine = startLine,
                        EndLine = i
                    });
                    currentSection.Clear();
                    inCustomSection = false;
                    continue;
                }

                if (inCustomSection && !line.Contains("/**********************************/"))
                {
                    currentSection.AppendLine(line);
                }
            }

            return sections;
        }

        private async Task<string> PreserveCustomMergeAsync(string generatedContent, List<CustomSection> customSections, string platform)
        {
            var content = new StringBuilder();
            var lines = generatedContent.Split('\n');
            bool insertedCustomSections = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Look for the first import/using statement or after the first comment block
                if (!insertedCustomSections && ShouldInsertCustomSectionsHere(line, platform))
                {
                    // Insert custom sections
                    foreach (var section in customSections)
                    {
                        InsertCustomSectionForPlatform(content, section, platform);
                        content.AppendLine();
                    }
                    insertedCustomSections = true;
                }

                content.AppendLine(line);
            }

            return content.ToString();
        }

        private async Task<string> PromptMergeAsync(string generatedContent, List<CustomSection> customSections, string platform)
        {
            // In a real implementation, this would prompt the user or use a conflict resolution strategy
            // For now, fall back to preserve-custom
            _logger.LogWarning("Prompt merge strategy not implemented, falling back to preserve-custom");
            return await PreserveCustomMergeAsync(generatedContent, customSections, platform);
        }

        private bool ShouldInsertCustomSectionsHere(string line, string platform)
        {
            return platform.ToLowerInvariant() switch
            {
                "android" => line.StartsWith("import ") || line.StartsWith("package "),
                "ios" => line.StartsWith("import ") || line.StartsWith("//"),
                "web" => line.StartsWith("@import") || line.StartsWith("/*") || line.StartsWith(":root"),
                _ => false
            };
        }

        private void InsertCustomSectionForPlatform(StringBuilder content, CustomSection section, string platform)
        {
            switch (platform.ToLowerInvariant())
            {
                case "android":
                    content.AppendLine("/////////////////////////////////////////");
                    content.AppendLine($"// {section.Name} - Preserved");
                    content.AppendLine("/////////////////////////////////////////");
                    content.AppendLine();
                    content.AppendLine(section.Content);
                    content.AppendLine();
                    content.AppendLine("/////////////////////////////////////////");
                    content.AppendLine("// End Custom Section");
                    content.AppendLine("/////////////////////////////////////////");
                    break;

                case "ios":
                    content.AppendLine("/////////////////////////////////////////");
                    content.AppendLine($"// {section.Name} - Preserved");
                    content.AppendLine("/////////////////////////////////////////");
                    content.AppendLine();
                    content.AppendLine(section.Content);
                    content.AppendLine();
                    content.AppendLine("/////////////////////////////////////////");
                    content.AppendLine("// End Custom Section");
                    content.AppendLine("/////////////////////////////////////////");
                    break;

                case "web":
                    content.AppendLine("/**********************************/");
                    content.AppendLine($"/* {section.Name} - Preserved */");
                    content.AppendLine("/**********************************/");
                    content.AppendLine();
                    content.AppendLine(section.Content);
                    content.AppendLine();
                    content.AppendLine("/**********************************/");
                    content.AppendLine("/* End Custom Section */");
                    content.AppendLine("/**********************************/");
                    break;
            }
        }

        private string? ExtractSectionName(string commentLine)
        {
            try
            {
                // Extract section name from comment line like "// Custom Colors - Preserved"
                var parts = commentLine.Split('-');
                if (parts.Length >= 2)
                {
                    var namePart = parts[0].Trim();
                    // Remove comment markers
                    namePart = namePart.Replace("//", "").Replace("/*", "").Replace("*/", "").Trim();
                    return namePart;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract section name from: {CommentLine}", commentLine);
            }

            return null;
        }

        private string ComputeContentHash(string content)
        {
            // Simple hash for content comparison
            return content.GetHashCode().ToString();
        }
    }
}