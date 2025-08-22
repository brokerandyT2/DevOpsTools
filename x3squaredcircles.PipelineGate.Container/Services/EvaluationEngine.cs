using JsonPath;

using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using x3squaredcircles.PipelineGate.Container.Models;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    public class EvaluationEngine : IEvaluationEngine
    {
        private readonly ILogger<EvaluationEngine> _logger;
        private static readonly Regex ConditionRegex = new Regex(
            @"\s*(?<lhs>.+?)\s*(?<op>==|!=|>|>=|<|<=|contains|not contains)\s*(?<rhs>.+)\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public EvaluationEngine(ILogger<EvaluationEngine> logger)
        {
            _logger = logger;
        }

        public async Task<bool> EvaluateConditionAsync(string condition, string responseContent, string contentType)
        {
            if (string.IsNullOrWhiteSpace(condition) || string.IsNullOrWhiteSpace(responseContent))
            {
                return false;
            }

            _logger.LogDebug("Evaluating condition: {Condition}", condition);

            try
            {
                var match = ConditionRegex.Match(condition);
                if (!match.Success)
                {
                    throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Invalid condition syntax: '{condition}'");
                }

                var lhsExpression = match.Groups["lhs"].Value.Trim();
                var op = match.Groups["op"].Value.Trim().ToLowerInvariant();
                var rhsLiteral = match.Groups["rhs"].Value.Trim();

                var extractedValue = await ExtractValueFromResponseAsync(lhsExpression, responseContent, contentType);

                return CompareValues(extractedValue, op, rhsLiteral);
            }
            catch (PipelineGateException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate condition '{Condition}'.", condition);
                throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Failed to evaluate condition: '{condition}'", ex);
            }
        }

        private async Task<object> ExtractValueFromResponseAsync(string expression, string content, string contentType)
        {
            var formatMatch = Regex.Match(expression, @"^(jsonpath|xpath)\((.+)\)$", RegexOptions.IgnoreCase);
            if (!formatMatch.Success)
            {
                throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Invalid path expression. Must start with 'jsonpath(...)' or 'xpath(...)'. Expression: {expression}");
            }

            var format = formatMatch.Groups[1].Value.ToLowerInvariant();
            var path = formatMatch.Groups[2].Value;

            return format switch
            {
                "jsonpath" => ExtractJsonPathValue(path, content),
                "xpath" => ExtractXPathValue(path, content),
                _ => throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Unsupported format '{format}' in expression.")
            };
        }

        private object ExtractJsonPathValue(string path, string jsonContent)
        {
            try
            {
                var selector = new JsonPathSelector(path);
                var result = selector.Select(jsonContent).FirstOrDefault();
                _logger.LogDebug("JSONPath '{Path}' extracted value: {Value}", path, result ?? "null");
                return result;
            }
            catch (Exception ex)
            {
                throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Error evaluating JSONPath expression '{path}'.", ex);
            }
        }

        private object ExtractXPathValue(string path, string xmlContent)
        {
            try
            {
                using var stringReader = new StringReader(xmlContent);
                var doc = new XPathDocument(stringReader);
                var nav = doc.CreateNavigator();
                var result = nav.Evaluate(path);

                if (result is XPathNodeIterator iterator)
                {
                    if (iterator.MoveNext())
                    {
                        var value = iterator.Current.Value;
                        _logger.LogDebug("XPath '{Path}' extracted value: {Value}", path, value);
                        return value;
                    }
                    return null;
                }

                _logger.LogDebug("XPath '{Path}' extracted value: {Value}", path, result ?? "null");
                return result;
            }
            catch (Exception ex)
            {
                throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Error evaluating XPath expression '{path}'.", ex);
            }
        }

        private bool CompareValues(object lhsValue, string op, string rhsLiteral)
        {
            // Determine type from RHS literal
            if (bool.TryParse(rhsLiteral, out var rhsBool))
            {
                var lhsBool = Convert.ToBoolean(lhsValue);
                _logger.LogDebug("Comparing as Boolean: {LHS} {Operator} {RHS}", lhsBool, op, rhsBool);
                return op switch
                {
                    "==" => lhsBool == rhsBool,
                    "!=" => lhsBool != rhsBool,
                    _ => throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Operator '{op}' is not valid for Boolean comparison.")
                };
            }

            if (decimal.TryParse(rhsLiteral, out var rhsDecimal))
            {
                var lhsDecimal = Convert.ToDecimal(lhsValue);
                _logger.LogDebug("Comparing as Decimal: {LHS} {Operator} {RHS}", lhsDecimal, op, rhsDecimal);
                return op switch
                {
                    "==" => lhsDecimal == rhsDecimal,
                    "!=" => lhsDecimal != rhsDecimal,
                    ">" => lhsDecimal > rhsDecimal,
                    ">=" => lhsDecimal >= rhsDecimal,
                    "<" => lhsDecimal < rhsDecimal,
                    "<=" => lhsDecimal <= rhsDecimal,
                    _ => throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Operator '{op}' is not valid for Numeric comparison.")
                };
            }

            // Default to string comparison
            var rhsString = rhsLiteral.Trim('\'', '"');
            var lhsString = lhsValue?.ToString() ?? string.Empty;
            _logger.LogDebug("Comparing as String: '{LHS}' {Operator} '{RHS}'", lhsString, op, rhsString);
            return op switch
            {
                "==" => lhsString.Equals(rhsString, StringComparison.Ordinal),
                "!=" => !lhsString.Equals(rhsString, StringComparison.Ordinal),
                "contains" => lhsString.Contains(rhsString, StringComparison.Ordinal),
                "not contains" => !lhsString.Contains(rhsString, StringComparison.Ordinal),
                _ => throw new PipelineGateException(GateExitCode.EvaluationFailure, $"Operator '{op}' is not valid for String comparison.")
            };
        }
    }
}