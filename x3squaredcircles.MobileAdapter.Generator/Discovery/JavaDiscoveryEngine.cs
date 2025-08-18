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
    /// Discovery engine for Java projects. Analyzes .java source files to find classes marked for discovery.
    /// </summary>
    public class JavaDiscoveryEngine : IClassDiscoveryEngine
    {
        private readonly ILogger<JavaDiscoveryEngine> _logger;

        public JavaDiscoveryEngine(ILogger<JavaDiscoveryEngine> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredClass>> DiscoverClassesAsync(GeneratorConfiguration config)
        {
            _logger.LogInformation("Starting Java discovery process by parsing source files...");
            var discoveredClasses = new List<DiscoveredClass>();
            var sourcePaths = config.Source.SourcePaths?.Split(';') ?? Array.Empty<string>();

            if (!sourcePaths.Any())
            {
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "Java discovery requires SOURCE_PATHS to be set.");
            }

            foreach (var path in sourcePaths.Where(Directory.Exists))
            {
                var javaFiles = Directory.GetFiles(path, "*.java", SearchOption.AllDirectories);
                _logger.LogDebug("Found {Count} Java files in path: {Path}", javaFiles.Length, path);

                foreach (var file in javaFiles)
                {
                    try
                    {
                        var classesInFile = await ParseJavaFileAsync(file, config);
                        discoveredClasses.AddRange(classesInFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process Java file: {File}", file);
                    }
                }
            }

            return discoveredClasses;
        }

        private async Task<List<DiscoveredClass>> ParseJavaFileAsync(string filePath, GeneratorConfiguration config)
        {
            var classes = new List<DiscoveredClass>();
            var content = await File.ReadAllTextAsync(filePath);
            var cleanContent = RemoveComments(content); // Clean content for more reliable regex matching

            // Regex to find classes annotated with the tracking attribute.
            // It looks for the attribute and then captures the entire class definition that follows.
            var trackAttribute = config.TrackAttribute;
            var classRegex = new Regex(
                $@"@{trackAttribute}(?:\(.*\))?\s*(?:public|private|protected)?\s*(?:final|abstract)?\s*class\s+(?<className>\w+)\s*(?:extends\s+\w+)?\s*(?:implements\s+[\w,\s<>]+)?\s*\{{(?<body>(?:[^{{}}]|{{(?<DEPTH>)|}} (?<-DEPTH>))*(?(DEPTH)(?!)))\}}",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture);

            var matches = classRegex.Matches(cleanContent);
            if (matches.Count > 0)
            {
                var packageName = ExtractPackageName(cleanContent);
                foreach (Match match in matches)
                {
                    var className = match.Groups["className"].Value;
                    var classBody = match.Groups["body"].Value;

                    _logger.LogDebug("Found tracked Java class '{ClassName}' in file {File}", className, filePath);

                    classes.Add(new DiscoveredClass
                    {
                        Name = className,
                        Namespace = packageName,
                        Properties = ExtractProperties(classBody),
                        Methods = ExtractMethods(classBody)
                    });
                }
            }

            return classes;
        }

        private string ExtractPackageName(string content)
        {
            var packageMatch = Regex.Match(content, @"^\s*package\s+([\w\.]+);", RegexOptions.Multiline);
            return packageMatch.Success ? packageMatch.Groups[1].Value : string.Empty;
        }

        private List<DiscoveredProperty> ExtractProperties(string classBody)
        {
            var properties = new List<DiscoveredProperty>();
            // Regex to find field declarations (e.g., "private String name;").
            // It captures access modifiers, type (including generics), and name.
            var propertyRegex = new Regex(
                @"(?:private|public|protected)?\s*(?:static\s+)?(?:final\s+)?(?<type>[\w<>\[\],\s]+)\s+(?<name>\w+)\s*(?:=.*)?;");

            var matches = propertyRegex.Matches(classBody);
            foreach (Match match in matches)
            {
                var type = match.Groups["type"].Value.Trim();
                var name = match.Groups["name"].Value.Trim();

                // Exclude method variables that might look like properties
                if (IsLikelyMethodVariable(match, classBody)) continue;

                var property = new DiscoveredProperty
                {
                    Name = name,
                    Type = type
                };

                if (type.Contains("<") && type.Contains(">"))
                {
                    var genericMatch = Regex.Match(type, @"<([\w,\s]+)>");
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
            // Regex to find method declarations. It's complex to handle various signatures.
            // Captures return type, method name, and parameters.
            var methodRegex = new Regex(
                @"(?:public|private|protected|static|final|synchronized|abstract)\s+(?<returnType>[\w<>\[\],\s]+)\s+(?<name>\w+)\s*\((?<params>[^)]*)\)\s*(?:throws\s+[\w,\s]+)?\s*\{");

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
                    var parts = p.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return new DiscoveredParameter
                    {
                        // Handle cases like "final String name" or "String name"
                        Type = string.Join(" ", parts.Take(parts.Length - 1)),
                        Name = parts.Last()
                    };
                }).ToList();
        }

        private string RemoveComments(string source)
        {
            // Remove block comments /* ... */
            var blockComments = @"/\*(.*?)\*/";
            // Remove line comments // ...
            var lineComments = @"//(.*?)\r?\n";
            // Remove strings to avoid false positives in comments/code
            var strings = @"""((\\[^\n]|[^""\n])*)""";

            return Regex.Replace(source,
                $"{blockComments}|{lineComments}|{strings}",
                me => {
                    if (me.Value.StartsWith("/*") || me.Value.StartsWith("//"))
                        return me.Value.StartsWith("//") ? Environment.NewLine : "";
                    // Keep the string literal but replace its content to avoid messing up line counts/positions if ever needed.
                    return me.Value;
                },
                RegexOptions.Singleline);
        }

        private bool IsLikelyMethodVariable(Match match, string classBody)
        {
            // A simple heuristic to avoid matching local variables inside methods.
            // A more robust solution would require a full AST.
            int matchIndex = match.Index;
            int lastMethodBrace = classBody.LastIndexOf('{', matchIndex);
            int lastSemicolon = classBody.LastIndexOf(';', matchIndex);

            return lastMethodBrace > lastSemicolon;
        }
    }
}