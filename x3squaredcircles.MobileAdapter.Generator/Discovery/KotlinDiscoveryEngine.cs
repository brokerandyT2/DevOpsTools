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
    /// Discovery engine for Kotlin projects. Analyzes .kt source files to find classes marked for discovery.
    /// </summary>
    public class KotlinDiscoveryEngine : IClassDiscoveryEngine
    {
        private readonly ILogger<KotlinDiscoveryEngine> _logger;

        public KotlinDiscoveryEngine(ILogger<KotlinDiscoveryEngine> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredClass>> DiscoverClassesAsync(GeneratorConfiguration config)
        {
            _logger.LogInformation("Starting Kotlin discovery process by parsing source files...");
            var discoveredClasses = new List<DiscoveredClass>();
            var sourcePaths = config.Source.SourcePaths?.Split(';') ?? Array.Empty<string>();

            if (!sourcePaths.Any())
            {
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "Kotlin discovery requires SOURCE_PATHS to be set.");
            }

            foreach (var path in sourcePaths.Where(Directory.Exists))
            {
                var kotlinFiles = Directory.GetFiles(path, "*.kt", SearchOption.AllDirectories);
                _logger.LogDebug("Found {Count} Kotlin files in path: {Path}", kotlinFiles.Length, path);

                foreach (var file in kotlinFiles)
                {
                    try
                    {
                        var classesInFile = await ParseKotlinFileAsync(file, config);
                        discoveredClasses.AddRange(classesInFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process Kotlin file: {File}", file);
                    }
                }
            }

            return discoveredClasses;
        }

        private async Task<List<DiscoveredClass>> ParseKotlinFileAsync(string filePath, GeneratorConfiguration config)
        {
            var classes = new List<DiscoveredClass>();
            var content = await File.ReadAllTextAsync(filePath);
            var cleanContent = RemoveComments(content);

            // Regex to find classes (including data classes) annotated with the tracking attribute.
            var trackAttribute = config.TrackAttribute;
            var classRegex = new Regex(
                $@"@{trackAttribute}(?:\(.*\))?\s*(?:public|internal|private)?\s*(?:data\s+)?class\s+(?<className>\w+)\s*(?:\((?<primaryCtorParams>[^)]*)\))?(?:\s*:\s*\w+\s*\([^)]*\))?\s*(?:\{{(?<body>(?:[^{{}}]|{{(?<DEPTH>)|}} (?<-DEPTH>))*(?(DEPTH)(?!)))\}})?",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture);

            var matches = classRegex.Matches(cleanContent);
            if (matches.Count > 0)
            {
                var packageName = ExtractPackageName(cleanContent);
                foreach (Match match in matches)
                {
                    var className = match.Groups["className"].Value;
                    var primaryCtorParams = match.Groups["primaryCtorParams"].Value;
                    var classBody = match.Groups["body"].Success ? match.Groups["body"].Value : string.Empty;

                    _logger.LogDebug("Found tracked Kotlin class '{ClassName}' in file {File}", className, filePath);

                    var properties = ExtractPropertiesFromPrimaryCtor(primaryCtorParams);
                    properties.AddRange(ExtractPropertiesFromBody(classBody));

                    classes.Add(new DiscoveredClass
                    {
                        Name = className,
                        Namespace = packageName,
                        Properties = properties,
                        Methods = ExtractMethods(classBody)
                    });
                }
            }

            return classes;
        }

        private string ExtractPackageName(string content)
        {
            var packageMatch = Regex.Match(content, @"^\s*package\s+([\w\.]+)", RegexOptions.Multiline);
            return packageMatch.Success ? packageMatch.Groups[1].Value : string.Empty;
        }

        private List<DiscoveredProperty> ExtractPropertiesFromPrimaryCtor(string ctorParams)
        {
            if (string.IsNullOrWhiteSpace(ctorParams))
            {
                return new List<DiscoveredProperty>();
            }

            // Handles params like: "val name: String, var age: Int?"
            return ctorParams.Split(',')
                .Select(p => p.Trim())
                .Where(p => p.StartsWith("val ") || p.StartsWith("var "))
                .Select(p =>
                {
                    var parts = p.Split(':');
                    var name = parts[0].Replace("val ", "").Replace("var ", "").Trim();
                    var type = parts.Length > 1 ? parts[1].Trim().Split('=')[0].Trim() : "Any"; // Type is before default value

                    var property = new DiscoveredProperty { Name = name, Type = type };
                    if (type.Contains("<") && type.Contains(">"))
                    {
                        var genericMatch = Regex.Match(type, @"<(.+)>");
                        if (genericMatch.Success)
                        {
                            property.CollectionElementType = genericMatch.Groups[1].Value.Trim();
                        }
                    }
                    return property;
                }).ToList();
        }

        private List<DiscoveredProperty> ExtractPropertiesFromBody(string classBody)
        {
            var properties = new List<DiscoveredProperty>();
            // Regex for properties defined in the class body.
            var propertyRegex = new Regex(
                @"(?:private|public|internal|protected)?\s*(?:lateinit\s+)?(val|var)\s+(?<name>\w+)\s*:\s*(?<type>[\w<>\[\],\s\?]+)");

            var matches = propertyRegex.Matches(classBody);
            foreach (Match match in matches)
            {
                var property = new DiscoveredProperty
                {
                    Name = match.Groups["name"].Value,
                    Type = match.Groups["type"].Value.Trim()
                };

                if (property.Type.Contains("<") && property.Type.Contains(">"))
                {
                    var genericMatch = Regex.Match(property.Type, @"<(.+)>");
                    if (genericMatch.Success)
                    {
                        property.CollectionElementType = genericMatch.Groups[1].Value.Trim();
                    }
                }

                properties.Add(property);
            }
            return properties;
        }

        private List<DiscoveredMethod> ExtractMethods(string classBody)
        {
            var methods = new List<DiscoveredMethod>();
            // Regex for function definitions in Kotlin.
            var methodRegex = new Regex(
                @"(?:public|private|internal|protected|override|suspend)?\s*fun\s+(?:<.*>)?\s*(?<name>\w+)\s*\((?<params>[^)]*)\)\s*:\s*(?<returnType>[\w<>\[\],\s\?]+)");

            var matches = methodRegex.Matches(classBody);
            foreach (Match match in matches)
            {
                methods.Add(new DiscoveredMethod
                {
                    Name = match.Groups["name"].Value,
                    ReturnType = match.Groups["returnType"].Value.Trim(),
                    Parameters = ParseParameters(match.Groups["params"].Value)
                });
            }
            return methods;
        }

        private List<DiscoveredParameter> ParseParameters(string paramsString)
        {
            if (string.IsNullOrWhiteSpace(paramsString))
            {
                return new List<DiscoveredParameter>();
            }

            return paramsString.Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p =>
                {
                    var parts = p.Split(':');
                    return new DiscoveredParameter
                    {
                        Name = parts[0].Trim(),
                        Type = parts.Length > 1 ? parts[1].Trim() : "Any"
                    };
                }).ToList();
        }

        private string RemoveComments(string source)
        {
            var blockComments = @"/\*(.*?)\*/";
            var lineComments = @"//(.*?)\r?\n";
            var strings = @"""((\\[^\n]|[^""\n])*)""";

            return Regex.Replace(source,
                $"{blockComments}|{lineComments}|{strings}",
                me => {
                    if (me.Value.StartsWith("/*") || me.Value.StartsWith("//"))
                        return me.Value.StartsWith("//") ? Environment.NewLine : "";
                    return me.Value;
                },
                RegexOptions.Singleline);
        }
    }
}