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
    public class GoAnalyzerService : ILanguageAnalyzerService
    {
        private readonly IAppLogger _logger;

        private static readonly Regex DataConsumerRegex = new(
            @"//\s*@DataConsumer\s+serviceName\s*=\s*[""'](?<serviceName>[\w-]+)[""']\s*\ntype\s+(?<typeName>\w+)\s+struct",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TriggerRegex = new(
            @"//\s*@Trigger\s+type\s*=\s*[""'](?<triggerType>\w+)[""'](?:\s+name\s*=\s*[""'](?<triggerName>[^""']+)[""'])?.*\s*\nfunc\s+\(s\s+\*\w+\)\s+(?<methodName>\w+)\s*\((?<params>.*?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public GoAnalyzerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory)
        {
            _logger.LogInfo("Analyzing Go source code for DataConsumers...");
            var blueprints = new List<ServiceBlueprint>();
            var goFiles = Directory.GetFiles(sourceDirectory, "*.go", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("_test.go"));

            foreach (var file in goFiles)
            {
                try
                {
                    var fileContent = await File.ReadAllTextAsync(file);
                    var consumerMatches = DataConsumerRegex.Matches(fileContent);

                    foreach (Match consumerMatch in consumerMatches)
                    {
                        var serviceName = consumerMatch.Groups["serviceName"].Value;
                        var typeName = consumerMatch.Groups["typeName"].Value;
                        var blueprint = new ServiceBlueprint { ServiceName = serviceName };

                        // Go file is the scope, find all methods for this struct
                        var triggerMatches = TriggerRegex.Matches(fileContent);

                        foreach (Match triggerMatch in triggerMatches)
                        {
                            var triggerMethod = new TriggerMethod
                            {
                                MethodName = triggerMatch.Groups["methodName"].Value,
                                Triggers = new List<TriggerDefinition>
                                {
                                    new()
                                    {
                                        Type = triggerMatch.Groups["triggerType"].Value,
                                        Name = triggerMatch.Groups["triggerName"].Success ? triggerMatch.Groups["triggerName"].Value : string.Empty
                                    }
                                },
                                Parameters = ParseGoParameters(triggerMatch.Groups["params"].Value)
                            };
                            blueprint.TriggerMethods.Add(triggerMethod);
                        }

                        if (blueprint.TriggerMethods.Any())
                        {
                            blueprints.Add(blueprint);
                            _logger.LogDebug($"Discovered Go DataConsumer: {blueprint.ServiceName} in type {typeName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to analyze Go file '{file}': {ex.Message}");
                }
            }
            _logger.LogInfo($"✓ Go analysis complete. Found {blueprints.Count} services.");
            return blueprints;
        }

        private List<ParameterDefinition> ParseGoParameters(string paramString)
        {
            if (string.IsNullOrWhiteSpace(paramString)) return new List<ParameterDefinition>();

            return paramString.Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select((p, index) => {
                    var parts = p.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    return new ParameterDefinition
                    {
                        Name = parts.Length > 0 ? parts[0].Trim() : $"param{index}",
                        TypeFullName = parts.Length > 1 ? parts[1].Trim() : "interface{}",
                        IsPayload = index == 0
                    };
                }).ToList();
        }
    }
}