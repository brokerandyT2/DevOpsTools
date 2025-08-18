using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.Discovery
{
    /// <summary>
    /// Discovery engine for Go projects. Analyzes .go source files to find structs marked for discovery via comments.
    /// </summary>
    public class GoDiscoveryEngine : IClassDiscoveryEngine
    {
        private readonly ILogger<GoDiscoveryEngine> _logger;

        public GoDiscoveryEngine(ILogger<GoDiscoveryEngine> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredClass>> DiscoverClassesAsync(GeneratorConfiguration config)
        {
            _logger.LogInformation("Starting Go discovery process by parsing source files...");
            var discoveredClasses = new List<DiscoveredClass>();
            var sourcePaths = config.Source.SourcePaths?.Split(';') ?? Array.Empty<string>();

            if (!sourcePaths.Any())
            {
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "Go discovery requires SOURCE_PATHS to be set.");
            }

            foreach (var path in sourcePaths.Where(Directory.Exists))
            {
                var goFiles = Directory.GetFiles(path, "*.go", SearchOption.AllDirectories)
                                       .Where(f => !f.EndsWith("_test.go")) // Exclude test files
                                       .ToList();

                _logger.LogDebug("Found {Count} Go files in path: {Path}", goFiles.Count, path);

                foreach (var file in goFiles)
                {
                    try
                    {
                        var classesInFile = await ParseGoFileAsync(file, config);
                        discoveredClasses.AddRange(classesInFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process Go file: {File}", file);
                    }
                }
            }

            return discoveredClasses;
        }

        private async Task<List<DiscoveredClass>> ParseGoFileAsync(string filePath, GeneratorConfiguration config)
        {
            var classes = new List<DiscoveredClass>();
            var content = await File.ReadAllTextAsync(filePath);

            // Go discovery relies on a comment marker above the struct.
            var trackAttribute = config.TrackAttribute;
            var structRegex = new Regex(
                $@"//\s*\@{trackAttribute}\s*\ntype\s+(?<structName>\w+)\s+struct\s*\{{(?<body>.*?)\}}",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture);

            var matches = structRegex.Matches(content);
            if (matches.Count > 0)
            {
                var packageName = ExtractPackageName(content);
                foreach (Match match in matches)
                {
                    var structName = match.Groups["structName"].Value;
                    var structBody = match.Groups["body"].Value;

                    _logger.LogDebug("Found tracked Go struct '{StructName}' in file {File}", structName, filePath);

                    classes.Add(new DiscoveredClass
                    {
                        Name = structName,
                        Namespace = packageName,
                        // Go structs primarily have properties (fields). Methods are receivers and handled differently.
                        // For adapter generation, we focus on the data structure.
                        Properties = ExtractFields(structBody),
                        Methods = new List<DiscoveredMethod>() // Methods are extracted if needed in the future
                    });
                }
            }

            return classes;
        }

        private string ExtractPackageName(string content)
        {
            var packageMatch = Regex.Match(content, @"^\s*package\s+([\w\.]+)");
            return packageMatch.Success ? packageMatch.Groups[1].Value : string.Empty;
        }

        private List<DiscoveredProperty> ExtractFields(string structBody)
        {
            var properties = new List<DiscoveredProperty>();
            // Regex to find struct fields. Catches formats like:
            // Name string `json:"name"`
            // Age  int    `json:"age,omitempty"`
            var fieldRegex = new Regex(
                @"^\s*(?<name>\w+)\s+(?<type>[\w\.\*\[\]]+)",
                RegexOptions.Multiline);

            var matches = fieldRegex.Matches(structBody);
            foreach (Match match in matches)
            {
                var name = match.Groups["name"].Value;
                var type = match.Groups["type"].Value.Trim();

                var property = new DiscoveredProperty
                {
                    Name = name,
                    Type = type
                };

                // Check for slice types like []string or []*User
                var sliceMatch = Regex.Match(type, @"\[\](?<elementType>\*?\w+(?:\.\w+)?)");
                if (sliceMatch.Success)
                {
                    property.CollectionElementType = sliceMatch.Groups["elementType"].Value;
                }

                properties.Add(property);
            }
            return properties;
        }
    }
}