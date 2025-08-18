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
    /// Discovery engine for TypeScript projects. Analyzes .ts source files to find classes with specified decorators.
    /// </summary>
    public class TypeScriptDiscoveryEngine : IClassDiscoveryEngine
    {
        private readonly ILogger<TypeScriptDiscoveryEngine> _logger;

        public TypeScriptDiscoveryEngine(ILogger<TypeScriptDiscoveryEngine> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredClass>> DiscoverClassesAsync(GeneratorConfiguration config)
        {
            _logger.LogInformation("Starting TypeScript discovery process by parsing source files...");
            var discoveredClasses = new List<DiscoveredClass>();
            var sourcePaths = config.Source.SourcePaths?.Split(';') ?? Array.Empty<string>();

            if (!sourcePaths.Any())
            {
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "TypeScript discovery requires SOURCE_PATHS to be set.");
            }

            foreach (var path in sourcePaths.Where(Directory.Exists))
            {
                var tsFiles = Directory.GetFiles(path, "*.ts", SearchOption.AllDirectories)
                                       .Where(f => !f.EndsWith(".d.ts")) // Exclude declaration files
                                       .ToList();

                _logger.LogDebug("Found {Count} TypeScript files in path: {Path}", tsFiles.Count, path);

                foreach (var file in tsFiles)
                {
                    try
                    {
                        var classesInFile = await ParseTypeScriptFileAsync(file, config);
                        discoveredClasses.AddRange(classesInFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process TypeScript file: {File}", file);
                    }
                }
            }

            return discoveredClasses;
        }

        private async Task<List<DiscoveredClass>> ParseTypeScriptFileAsync(string filePath, GeneratorConfiguration config)
        {
            var classes = new List<DiscoveredClass>();
            var content = await File.ReadAllTextAsync(filePath);
            var cleanContent = RemoveComments(content);

            // Regex to find exported classes annotated with the tracking decorator.
            var trackAttribute = config.TrackAttribute;
            var classRegex = new Regex(
                 $@"@{trackAttribute}(?:\(.*\))?\s*export\s+class\s+(?<className>\w+)\s*(?:extends\s+[\w\.<>]+)?\s*(?:implements\s+[\w,\s<>]+)?\s*\{{(?<body>(?:[^{{}}]|{{(?<DEPTH>)|}} (?<-DEPTH>))*(?(DEPTH)(?!)))\}}",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture);

            var matches = classRegex.Matches(cleanContent);
            foreach (Match match in matches)
            {
                var className = match.Groups["className"].Value;
                var classBody = match.Groups["body"].Value;

                _logger.LogDebug("Found tracked TypeScript class '{ClassName}' in file {File}", className, filePath);

                classes.Add(new DiscoveredClass
                {
                    Name = className,
                    Namespace = ExtractNamespace(cleanContent) ?? Path.GetFileNameWithoutExtension(filePath),
                    Properties = ExtractProperties(classBody),
                    Methods = ExtractMethods(classBody)
                });
            }

            return classes;
        }

        private string ExtractNamespace(string content)
        {
            var namespaceMatch = Regex.Match(content, @"^\s*namespace\s+([\w\.]+)", RegexOptions.Multiline);
            return namespaceMatch.Success ? namespaceMatch.Groups[1].Value : null;
        }

        private List<DiscoveredProperty> ExtractProperties(string classBody)
        {
            var properties = new List<DiscoveredProperty>();
            // Regex for public properties with type annotations. Catches formats like:
            // public name: string;
            // name: string = 'default';
            // public readonly id?: number;
            var propertyRegex = new Regex(
                @"^\s*(?:public|private|protected|readonly)?\s+(?<name>\w+)(?<isOptional>\?)?\s*:\s*(?<type>[\w<>\[\]\|\s\.]+);",
                RegexOptions.Multiline);

            var matches = propertyRegex.Matches(classBody);
            foreach (Match match in matches)
            {
                var name = match.Groups["name"].Value;
                var type = match.Groups["type"].Value.Trim();

                var property = new DiscoveredProperty
                {
                    Name = name,
                    Type = type
                };

                // Check for array types like string[] or Array<string>
                var arrayMatch = Regex.Match(type, @"Array<(?<elementType>.+)>|(?<elementType>.+)\[\]");
                if (arrayMatch.Success)
                {
                    property.CollectionElementType = arrayMatch.Groups["elementType"].Value.Trim();
                }

                properties.Add(property);
            }
            return properties;
        }

        private List<DiscoveredMethod> ExtractMethods(string classBody)
        {
            var methods = new List<DiscoveredMethod>();
            // Regex for public method definitions.
            var methodRegex = new Regex(
                 @"^\s*(?:public|private|protected|async)?\s+(?<name>\w+)\s*\((?<params>[^)]*)\)\s*:\s*(?<returnType>[\w<>\[\]\|\s\.]+)",
                RegexOptions.Multiline);

            var matches = methodRegex.Matches(classBody);
            foreach (Match match in matches)
            {
                var returnType = match.Groups["returnType"].Value.Trim();
                // Handle Promises
                var promiseMatch = Regex.Match(returnType, @"Promise<(?<type>.+)>");
                if (promiseMatch.Success)
                {
                    returnType = promiseMatch.Groups["type"].Value.Trim();
                }

                methods.Add(new DiscoveredMethod
                {
                    Name = match.Groups["name"].Value,
                    ReturnType = returnType,
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
                        Name = parts[0].Trim().Replace("?", ""), // remove optional marker
                        Type = parts.Length > 1 ? parts[1].Trim() : "any"
                    };
                }).ToList();
        }

        private string RemoveComments(string source)
        {
            var blockComments = @"/\*(.*?)\*/";
            var lineComments = @"//(.*?)\r?\n";
            var strings = @"((""|'|`)(?:\\.|[^\\])*?\2)";

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