using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface ICustomSectionService
    {
        // Method signatures have been simplified and standardized.
        Task<List<CustomSection>> ExtractCustomSectionsAsync(string filePath);
        Task<string> MergeCustomSectionsAsync(string generatedContent, List<CustomSection> customSections);
        Task SaveCustomSectionsInventoryAsync(List<GeneratedFile> files, string outputDirectory);
    }

    // Model moved here for better cohesion with its primary service.
    public class CustomSection
    {
        public string Name { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class CustomSectionService : ICustomSectionService
    {
        private readonly IAppLogger _logger;

        // Regex to find start and end markers for custom sections.
        private static readonly Regex _startRegex = new Regex(@"\/\/\/\s*CUSTOM_SECTION_START:\s*(?<name>[\w-]+)\s*\/\/\/");
        private static readonly Regex _endRegex = new Regex(@"\/\/\/\s*CUSTOM_SECTION_END:\s*(?<name>[\w-]+)\s*\/\/\/");

        public CustomSectionService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<CustomSection>> ExtractCustomSectionsAsync(string filePath)
        {
            var sections = new List<CustomSection>();
            if (!File.Exists(filePath))
            {
                _logger.LogDebug($"File not found, no custom sections to extract: {filePath}");
                return sections;
            }

            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var matches = _startRegex.Matches(content);

                foreach (Match startMatch in matches)
                {
                    var sectionName = startMatch.Groups["name"].Value;
                    var endPattern = @$"\/\/\/\s*CUSTOM_SECTION_END:\s*{Regex.Escape(sectionName)}\s*\/\/\/";
                    var endMatch = Regex.Match(content, endPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));

                    if (endMatch.Success)
                    {
                        var startIndex = startMatch.Index + startMatch.Length;
                        var length = endMatch.Index - startIndex;
                        var sectionContent = content.Substring(startIndex, length).Trim();

                        sections.Add(new CustomSection { Name = sectionName, Content = sectionContent });
                        _logger.LogDebug($"Extracted custom section '{sectionName}' from {Path.GetFileName(filePath)}");
                    }
                    else
                    {
                        _logger.LogWarning($"Found start of custom section '{sectionName}' but no matching end in {filePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to extract custom sections from: {filePath}", ex);
            }
            return sections;
        }

        public Task<string> MergeCustomSectionsAsync(string generatedContent, List<CustomSection> customSections)
        {
            if (!customSections.Any())
            {
                return Task.FromResult(generatedContent);
            }

            var contentBuilder = new StringBuilder(generatedContent);
            _logger.LogInfo($"Merging {customSections.Count} preserved custom sections.");

            // A simple merge strategy: append all custom sections to the end of the file.
            // A more complex strategy could use named markers in the generated template.
            contentBuilder.AppendLine();
            contentBuilder.AppendLine();

            foreach (var section in customSections)
            {
                contentBuilder.AppendLine($"/// CUSTOM_SECTION_START: {section.Name} ///");
                contentBuilder.AppendLine(section.Content);
                contentBuilder.AppendLine($"/// CUSTOM_SECTION_END: {section.Name} ///");
                contentBuilder.AppendLine();
            }

            return Task.FromResult(contentBuilder.ToString());
        }

        public async Task SaveCustomSectionsInventoryAsync(List<GeneratedFile> files, string outputDirectory)
        {
            try
            {
                var inventory = files
                    .Where(f => f.Content.Contains("CUSTOM_SECTION_START"))
                    .Select(f => new {
                        filePath = f.FilePath,
                        sections = ExtractCustomSectionsFromString(f.Content).Select(s => s.Name).ToList()
                    });

                if (!inventory.Any())
                {
                    _logger.LogDebug("No custom sections found in generated files. Skipping inventory.");
                    return;
                }

                var inventoryPath = Path.Combine(outputDirectory, "custom-sections.json");
                var json = JsonSerializer.Serialize(new { generatedAt = DateTime.UtcNow, files = inventory },
                    new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(inventoryPath, json);
                _logger.LogInfo($"✓ Saved custom sections inventory: {inventoryPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to save custom sections inventory.", ex);
            }
        }

        private List<CustomSection> ExtractCustomSectionsFromString(string content)
        {
            // This is a helper for the inventory, mirrors the file-based extraction.
            var sections = new List<CustomSection>();
            var matches = _startRegex.Matches(content);
            foreach (Match startMatch in matches)
            {
                sections.Add(new CustomSection { Name = startMatch.Groups["name"].Value });
            }
            return sections;
        }
    }
}