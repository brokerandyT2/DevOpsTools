using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Models;
using x3squaredcircles.MobileAdapter.Generator.Services;

namespace x3squaredcircles.MobileAdapter.Generator.Discovery
{
    /// <summary>
    /// Discovery engine for JavaScript projects. Analyzes .js source files to find ES6 classes marked for discovery via comments.
    /// </summary>
    public class JavaScriptDiscoveryEngine : IClassDiscoveryEngine
    {
        private readonly ILogger<JavaScriptDiscoveryEngine> _logger;
        private readonly IPlaceholderResolverService _placeholderResolverService;

        public JavaScriptDiscoveryEngine(ILogger<JavaScriptDiscoveryEngine> logger, IPlaceholderResolverService placeholderResolverService)
        {
            _logger = logger;
            _placeholderResolverService = placeholderResolverService;
        }

        public async Task<List<DiscoveredClass>> DiscoverClassesAsync(GeneratorConfiguration config)
        {
            _logger.LogInformation("Starting JavaScript discovery process by parsing source files...");
            var discoveredClasses = new List<DiscoveredClass>();
            var sourcePaths = config.Source.SourcePaths?.Split(';') ?? Array.Empty<string>();

            if (!sourcePaths.Any())
            {
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "JavaScript discovery requires SOURCE_PATHS to be set.");
            }

            foreach (var path in sourcePaths.Where(Directory.Exists))
            {
                var jsFiles = Directory.GetFiles(path, "*.js", SearchOption.AllDirectories);
                _logger.LogDebug("Found {Count} JavaScript files in path: {Path}", jsFiles.Length, path);

                foreach (var file in jsFiles)
                {
                    try
                    {
                        var classesInFile = await ParseJavaScriptFileAsync(file, config);
                        discoveredClasses.AddRange(classesInFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process JavaScript file: {File}", file);
                    }
                }
            }

            return discoveredClasses;
        }

        private async Task<List<DiscoveredClass>> ParseJavaScriptFileAsync(string filePath, GeneratorConfiguration config)
        {
            var classes = new List<DiscoveredClass>();
            var content = await File.ReadAllTextAsync(filePath);
            var cleanContent = RemoveComments(content, keepDocComments: true);

            // JS discovery relies on a JSDoc-style comment marker above the class.
            var trackAttribute = config.TrackAttribute;
            var classRegex = new Regex(
                $@"/\*\*(?<jsdoc>.*?)\*\s*@{trackAttribute}(?:.*)\s*\*/\s*class\s+(?<className>\w+)\s*(?:extends\s+[\w\.]+)?\s*\{{(?<body>(?:[^{{}}]|{{(?<DEPTH>)|}} (?<-DEPTH>))*(?(DEPTH)(?!)))\}}",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture);

            var matches = classRegex.Matches(cleanContent);
            foreach (Match match in matches)
            {
                var className = match.Groups["className"].Value;
                var classBody = match.Groups["body"].Value;
                var jsdoc = match.Groups["jsdoc"].Value;

                _logger.LogDebug("Found tracked JavaScript class '{ClassName}' in file {File}", className, filePath);

                var discoveredClass = new DiscoveredClass
                {
                    Name = className,
                    Namespace = Path.GetFileNameWithoutExtension(filePath),
                    Properties = ExtractProperties(classBody),
                    Methods = ExtractMethods(classBody)
                };

                var metadata = ExtractMetadataFromJsDoc(jsdoc);
                foreach (var item in metadata)
                {
                    var resolvedValue = _placeholderResolverService.ResolvePlaceholders(item.Value);
                    discoveredClass.Metadata[item.Key] = resolvedValue;
                }

                classes.Add(discoveredClass);
            }

            return classes;
        }

        private Dictionary<string, string> ExtractMetadataFromJsDoc(string jsdoc)
        {
            var metadata = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(jsdoc))
            {
                return metadata;
            }

            // Look for custom tags like @targetName {my-target-name}
            var tagRegex = new Regex(@"\*\s*@(?<key>\w+)\s+(?<value>.*?)\s*?(\r\n|\n|\*)", RegexOptions.Compiled);
            var matches = tagRegex.Matches(jsdoc);

            foreach (Match match in matches)
            {
                var key = match.Groups["key"].Value;
                var value = match.Groups["value"].Value.Trim();

                var metadataKey = char.ToUpperInvariant(key[0]) + key.Substring(1);
                metadata[metadataKey] = value;
                _logger.LogTrace("Extracted metadata from JSDoc: {Key} = '{Value}'", metadataKey, value);
            }

            return metadata;
        }

        private List<DiscoveredProperty> ExtractProperties(string classBody)
        {
            var properties = new List<DiscoveredProperty>();

            // Look for property assignments inside the constructor
            var ctorRegex = new Regex(@"constructor\s*\((?:[^)]*)\)\s*\{(?<ctorBody>[^}]*)\}");
            var ctorMatch = ctorRegex.Match(classBody);
            if (ctorMatch.Success)
            {
                var ctorBody = ctorMatch.Groups["ctorBody"].Value;
                var propRegex = new Regex(@"this\.(?<name>\w+)\s*=\s*(?<defaultValue>.*?);", RegexOptions.Multiline);
                var matches = propRegex.Matches(ctorBody);
                foreach (Match match in matches)
                {
                    var name = match.Groups["name"].Value;
                    var defaultValue = match.Groups["defaultValue"].Value.Trim();
                    properties.Add(new DiscoveredProperty
                    {
                        Name = name,
                        Type = InferTypeFromValue(defaultValue)
                    });
                }
            }
            return properties;
        }

        private string InferTypeFromValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "null" || value == "undefined") return "any";
            if (bool.TryParse(value, out _)) return "boolean";
            if (decimal.TryParse(value, out _)) return "number";
            if (value.StartsWith("'") || value.StartsWith("\"") || value.StartsWith("`")) return "string";
            if (value.StartsWith("[")) return "Array<any>";
            if (value.StartsWith("{")) return "object";
            return "any";
        }

        private List<DiscoveredMethod> ExtractMethods(string classBody)
        {
            var methods = new List<DiscoveredMethod>();
            // Regex for method definitions in an ES6 class.
            var methodRegex = new Regex(
                @"^\s*(async\s+)?(?<name>\w+)\s*\((?<params>[^)]*)\)\s*\{",
                RegexOptions.Multiline);

            var matches = methodRegex.Matches(classBody);
            foreach (Match match in matches)
            {
                var methodName = match.Groups["name"].Value;
                if (methodName == "constructor") continue;

                methods.Add(new DiscoveredMethod
                {
                    Name = methodName,
                    ReturnType = "any", // Cannot reliably infer return type
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
                .Select(p => p.Trim().Split('=')[0].Trim()) // Get name, ignore default value
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => new DiscoveredParameter
                {
                    Name = p,
                    Type = "any" // Cannot know parameter types in JS
                }).ToList();
        }

        private string RemoveComments(string source, bool keepDocComments = false)
        {
            var blockCommentPattern = keepDocComments ? @"/\*(?!\*).*?\*/" : @"/\*.*?\*/";
            var lineCommentPattern = @"//.*?\n";

            source = Regex.Replace(source, blockCommentPattern, "", RegexOptions.Singleline);
            source = Regex.Replace(source, lineCommentPattern, "");
            return source;
        }
    }
}