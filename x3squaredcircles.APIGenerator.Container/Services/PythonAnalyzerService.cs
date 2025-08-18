using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Implements source code analysis for Python using regular expressions.
    /// </summary>
    public class PythonAnalyzerService : ILanguageAnalyzerService
    {
        private readonly IAppLogger _logger;

        // Regex to find a class decorated with @data_consumer
        private static readonly Regex DataConsumerRegex = new(
            @"@data_consumer\(service_name\s*=\s*[""'](?<serviceName>\w+)[""']\)\s*\nclass\s+(?<className>\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Regex to find methods decorated with @trigger within a class body
        private static readonly Regex TriggerRegex = new(
            @"@trigger\(type\s*=\s*TriggerType\.(?<triggerType>\w+),\s*name\s*=\s*[""'](?<triggerName>.*?)[""']\)\s*\ndef\s+(?<methodName>\w+)\(self,\s*(?<payloadName>\w+):\s*(?<payloadType>\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        public PythonAnalyzerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory)
        {
            _logger.LogInfo("Analyzing Python source code for data_consumers...");
            var blueprints = new List<ServiceBlueprint>();
            var pythonFiles = Directory.GetFiles(sourceDirectory, "*.py", SearchOption.AllDirectories);

            if (pythonFiles.Length == 0)
            {
                _logger.LogWarning("No Python source files found in the provided directory.");
                return blueprints;
            }

            foreach (var file in pythonFiles)
            {
                try
                {
                    var fileContent = await File.ReadAllTextAsync(file);
                    var consumerMatches = DataConsumerRegex.Matches(fileContent);

                    foreach (Match consumerMatch in consumerMatches)
                    {
                        var blueprint = new ServiceBlueprint
                        {
                            ServiceName = consumerMatch.Groups["serviceName"].Value,
                            Metadata = new GenerationMetadata()
                        };

                        // Simplified: Assume the class body is the rest of the file for this example
                        var triggerMatches = TriggerRegex.Matches(fileContent);

                        foreach (Match triggerMatch in triggerMatches)
                        {
                            var triggerMethod = new TriggerMethod
                            {
                                MethodName = triggerMatch.Groups["methodName"].Value,
                                ReturnType = "None", // Python default
                                Triggers = new List<TriggerDefinition>
                                {
                                    new()
                                    {
                                        Type = triggerMatch.Groups["triggerType"].Value,
                                        Name = triggerMatch.Groups["triggerName"].Value
                                    }
                                },
                                Parameters = new List<ParameterDefinition>
                                {
                                    new()
                                    {
                                        Name = triggerMatch.Groups["payloadName"].Value,
                                        TypeFullName = triggerMatch.Groups["payloadType"].Value,
                                        IsPayload = true
                                    }
                                }
                            };
                            blueprint.TriggerMethods.Add(triggerMethod);
                        }

                        if (blueprint.TriggerMethods.Any())
                        {
                            blueprints.Add(blueprint);
                            _logger.LogDebug($"Discovered Python DataConsumer: {blueprint.ServiceName} with {blueprint.TriggerMethods.Count} trigger(s).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to analyze Python file '{file}': {ex.Message}");
                }
            }

            _logger.LogInfo($"✓ Python analysis complete. Found {blueprints.Count} services to generate.");
            return blueprints;
        }
    }
}