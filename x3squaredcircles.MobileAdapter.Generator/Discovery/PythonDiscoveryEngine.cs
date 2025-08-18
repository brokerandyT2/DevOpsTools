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
    /// Discovery engine for Python projects. Analyzes .py source files to find classes with specified decorators.
    /// </summary>
    public class PythonDiscoveryEngine : IClassDiscoveryEngine
    {
        private readonly ILogger<PythonDiscoveryEngine> _logger;

        public PythonDiscoveryEngine(ILogger<PythonDiscoveryEngine> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredClass>> DiscoverClassesAsync(GeneratorConfiguration config)
        {
            _logger.LogInformation("Starting Python discovery process by parsing source files...");
            var discoveredClasses = new List<DiscoveredClass>();
            var sourcePaths = config.Source.PythonPaths?.Split(';') ?? Array.Empty<string>();

            if (!sourcePaths.Any())
            {
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "Python discovery requires PYTHON_PATHS to be set.");
            }

            foreach (var path in sourcePaths.Where(Directory.Exists))
            {
                var pyFiles = Directory.GetFiles(path, "*.py", SearchOption.AllDirectories);
                _logger.LogDebug("Found {Count} Python files in path: {Path}", pyFiles.Length, path);

                foreach (var file in pyFiles)
                {
                    try
                    {
                        var classesInFile = await ParsePythonFileAsync(file, config);
                        discoveredClasses.AddRange(classesInFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process Python file: {File}", file);
                    }
                }
            }

            return discoveredClasses;
        }

        private async Task<List<DiscoveredClass>> ParsePythonFileAsync(string filePath, GeneratorConfiguration config)
        {
            var classes = new List<DiscoveredClass>();
            var content = await File.ReadAllTextAsync(filePath);
            var cleanContent = RemoveCommentsAndStrings(content);

            // Regex to find classes with the tracking decorator.
            var trackDecorator = config.TrackAttribute;
            var classRegex = new Regex(
                $@"^\@{trackDecorator}(?:\(.*\))?\s*\nclass\s+(?<className>\w+)(?:\(.*\))?:(?<classBody>(?:\n(?:\s{{4,}}|\#.*).*)+)",
                RegexOptions.Multiline | RegexOptions.ExplicitCapture);

            var matches = classRegex.Matches(cleanContent);
            foreach (Match match in matches)
            {
                var className = match.Groups["className"].Value;
                var classBody = match.Groups["classBody"].Value;

                _logger.LogDebug("Found tracked Python class '{ClassName}' in file {File}", className, filePath);

                classes.Add(new DiscoveredClass
                {
                    Name = className,
                    Namespace = Path.GetFileNameWithoutExtension(filePath), // Simplified namespace
                    Properties = ExtractProperties(classBody),
                    Methods = ExtractMethods(classBody)
                });
            }

            return classes;
        }

        private List<DiscoveredProperty> ExtractProperties(string classBody)
        {
            var properties = new List<DiscoveredProperty>();

            // 1. Find properties defined in __init__ (e.g., self.name: str = "default")
            var initRegex = new Regex(@"def\s+__init__\s*\((?<params>.*?)\):(?<initBody>(?:\n(?:\s{8,}}|\#.*).*)+)");
            var initMatch = initRegex.Match(classBody);
            if (initMatch.Success)
            {
                var initBody = initMatch.Groups["initBody"].Value;
                var propRegex = new Regex(@"self\.(?<name>\w+)\s*:\s*(?<type>[\w\[\], \.]+)\s*=");
                var propMatches = propRegex.Matches(initBody);
                foreach (Match match in propMatches)
                {
                    properties.Add(CreateDiscoveredProperty(match.Groups["name"].Value, match.Groups["type"].Value));
                }
            }

            // 2. Find class-level properties with type hints (e.g., name: str)
            var classPropRegex = new Regex(@"^\s{4}(?<name>\w+)\s*:\s*(?<type>[\w\[\], \.]+)");
            var classPropMatches = classPropRegex.Matches(classBody);
            foreach (Match match in classPropMatches)
            {
                // Avoid adding duplicates if already found in __init__
                if (!properties.Any(p => p.Name == match.Groups["name"].Value))
                {
                    properties.Add(CreateDiscoveredProperty(match.Groups["name"].Value, match.Groups["type"].Value));
                }
            }

            return properties;
        }

        private DiscoveredProperty CreateDiscoveredProperty(string name, string type)
        {
            var property = new DiscoveredProperty { Name = name, Type = type.Trim() };
            var listMatch = Regex.Match(type, @"list\[(.*)\]", RegexOptions.IgnoreCase);
            var dictMatch = Regex.Match(type, @"dict\[.*,\s*(.*)\]", RegexOptions.IgnoreCase);

            if (listMatch.Success)
            {
                property.CollectionElementType = listMatch.Groups[1].Value.Trim();
            }
            else if (dictMatch.Success)
            {
                // For dictionaries, we assume the key is string and capture the value type.
                property.CollectionElementType = dictMatch.Groups[1].Value.Trim();
            }

            return property;
        }

        private List<DiscoveredMethod> ExtractMethods(string classBody)
        {
            var methods = new List<DiscoveredMethod>();
            // Regex for method definitions (def method_name(...):)
            var methodRegex = new Regex(
                @"^\s{4}def\s+(?<name>\w+)\s*\((?<params>.*?)\)\s*(?:->\s*(?<returnType>[\w\[\], \.]+))?:",
                RegexOptions.Multiline);

            var matches = methodRegex.Matches(classBody);
            foreach (Match match in matches)
            {
                var methodName = match.Groups["name"].Value;
                // Exclude dunder methods like __init__
                if (methodName.StartsWith("__")) continue;

                methods.Add(new DiscoveredMethod
                {
                    Name = methodName,
                    ReturnType = match.Groups["returnType"].Success ? match.Groups["returnType"].Value.Trim() : "None",
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
                .Where(p => !string.IsNullOrEmpty(p) && p != "self")
                .Select(p =>
                {
                    var parts = p.Split(':');
                    return new DiscoveredParameter
                    {
                        Name = parts[0].Trim(),
                        Type = parts.Length > 1 ? parts[1].Trim().Split('=')[0].Trim() : "Any"
                    };
                }).ToList();
        }

        private string RemoveCommentsAndStrings(string source)
        {
            // This is a simplified implementation. A full parser would be more robust.
            // Remove triple-quoted strings/docstrings
            source = Regex.Replace(source, @"""""(.*?)""""", "", RegexOptions.Singleline);
            source = Regex.Replace(source, @"'''(.*?)'''", "", RegexOptions.Singleline);
            // Remove single-line comments
            source = Regex.Replace(source, @"#.*$", "", RegexOptions.Multiline);
            return source;
        }
    }
}