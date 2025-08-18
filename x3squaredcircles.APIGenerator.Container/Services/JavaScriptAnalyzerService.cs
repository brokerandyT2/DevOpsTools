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
    public class JavaScriptAnalyzerService : ILanguageAnalyzerService
    {
        private readonly IAppLogger _logger;

        private static readonly Regex DataConsumerRegex = new(
            @"/\*\*\s*@DataConsumer\s+serviceName\s*=\s*[""'](?<serviceName>[\w-]+)[""']\s*\*/\s*(?:export\s+)?class\s+(?<className>\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TriggerRegex = new(
            @"/\*\*\s*@Trigger\s+type\s*=\s*[""'](?<triggerType>\w+)[""'](?:\s+name\s*=\s*[""'](?<triggerName>[^""']+)[""'])?.*\s*\*/\s*(?<methodName>\w+)\s*\((?<params>.*?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public JavaScriptAnalyzerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory)
        {
            _logger.LogInfo("Analyzing JavaScript source code for DataConsumers...");
            var blueprints = new List<ServiceBlueprint>();
            var jsFiles = Directory.GetFiles(sourceDirectory, "*.js", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".spec.js") && !f.EndsWith(".test.js"));

            foreach (var file in jsFiles)
            {
                try
                {
                    var fileContent = await File.ReadAllTextAsync(file);
                    var consumerMatches = DataConsumerRegex.Matches(fileContent);

                    foreach (Match consumerMatch in consumerMatches)
                    {
                        var serviceName = consumerMatch.Groups["serviceName"].Value;
                        var className = consumerMatch.Groups["className"].Value;
                        var blueprint = new ServiceBlueprint { ServiceName = serviceName };

                        var classBody = GetBlockContent(fileContent, consumerMatch.Index);
                        if (string.IsNullOrEmpty(classBody)) continue;

                        var triggerMatches = TriggerRegex.Matches(classBody);
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
                                Parameters = ParseJsParameters(triggerMatch.Groups["params"].Value)
                            };
                            blueprint.TriggerMethods.Add(triggerMethod);
                        }

                        if (blueprint.TriggerMethods.Any())
                        {
                            blueprints.Add(blueprint);
                            _logger.LogDebug($"Discovered JavaScript DataConsumer: {blueprint.ServiceName} in class {className}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to analyze JavaScript file '{file}': {ex.Message}");
                }
            }
            _logger.LogInfo($"✓ JavaScript analysis complete. Found {blueprints.Count} services.");
            return blueprints;
        }

        private List<ParameterDefinition> ParseJsParameters(string paramString)
        {
            if (string.IsNullOrWhiteSpace(paramString)) return new List<ParameterDefinition>();

            return paramString.Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select((p, index) => new ParameterDefinition
                {
                    Name = p,
                    TypeFullName = "any", // Type is not available in JS syntax
                    IsPayload = index == 0
                }).ToList();
        }

        private string GetBlockContent(string text, int startIndex)
        {
            int braceCount = 0;
            int blockStartIndex = text.IndexOf('{', startIndex);
            if (blockStartIndex == -1) return string.Empty;

            for (int i = blockStartIndex; i < text.Length; i++)
            {
                if (text[i] == '{') braceCount++;
                else if (text[i] == '}') braceCount--;

                if (braceCount == 0 && i > blockStartIndex)
                {
                    return text.Substring(blockStartIndex + 1, i - blockStartIndex - 1);
                }
            }
            return string.Empty;
        }
    }
}