using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Models;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    /// <summary>
    /// Represents a single, specific data governance violation found during a scan.
    /// </summary>
    /// <param name="Rule">The validation rule that was triggered.</param>
    /// <param name="Target">The specific location where the violation was found.</param>
    /// <param name="ViolatingValue">A sample of the data that triggered the rule.</param>
    public record Violation(ValidationRule Rule, ScanTarget Target, string ViolatingValue);

    /// <summary>
    /// Represents a single, configured validation rule, either built-in or custom.
    /// </summary>
    public class ValidationRule
    {
        public string Code { get; init; } = string.Empty;
        public string Severity { get; init; } = "error";
        public Regex CompiledRegex { get; init; } = new(".*");
        public string Description { get; init; } = string.Empty;
        public string[] Tags { get; internal set; }
    }

    /// <summary>
    /// Defines the contract for the rules engine that manages and applies validation rules.
    /// </summary>
    public interface IRulesEngineService
    {
        /// <summary>
        /// Initializes the rules engine by loading built-in and custom patterns.
        /// </summary>
        /// <param name="customPatterns">An optional PatternFile containing user-defined rules.</param>
        void Initialize(PatternFile? customPatterns);

        /// <summary>
        /// Applies all loaded validation rules to a given data value.
        /// </summary>
        /// <param name="value">The string value to check.</param>
        /// <returns>The first ValidationRule that matches the value, or null if no match is found.</returns>
        ValidationRule? FindFirstViolation(string value);
    }

    /// <summary>
    /// Implements the data governance rules engine, managing rule sets and performing pattern matching.
    /// </summary>
    public class RulesEngineService : IRulesEngineService
    {
        private readonly ILogger<RulesEngineService> _logger;
        private readonly List<ValidationRule> _rules = new();
        private bool _isInitialized = false;

        public RulesEngineService(ILogger<RulesEngineService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public void Initialize(PatternFile? customPatterns)
        {
            if (_isInitialized)
            {
                _logger.LogWarning("Rules engine is already initialized. Skipping re-initialization.");
                return;
            }

            _logger.LogInformation("Initializing rules engine...");

            LoadBuiltInRules();
            LoadCustomRules(customPatterns);

            _isInitialized = true;
            _logger.LogInformation("✓ Rules engine initialized with {Count} total rules.", _rules.Count);
        }

        /// <inheritdoc />
        public ValidationRule? FindFirstViolation(string value)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("RulesEngineService must be initialized before use.");
            }

            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            // Iterate through the rules and return the first one that matches.
            foreach (var rule in _rules)
            {
                if (rule.CompiledRegex.IsMatch(value))
                {
                    return rule;
                }
            }

            return null;
        }

        private void LoadBuiltInRules()
        {
            _logger.LogDebug("Loading comprehensive library of built-in validation rules...");
            // This method populates the engine with a production-grade set of detectors.
            // Each rule is categorized with tags, which are used by the contextual combination engine.
            // Quasi-Identifiers (QI_*) are intentionally set to 'info' severity to avoid noise;
            // their risk is elevated by the CISO's combination rules.

            _rules.AddRange(new List<ValidationRule>
    {
        #region Direct Identifiers (PII)
        new() {
            Code = "PII_001", Severity = "critical", Description = "Email Address",
            Tags = new[] { "contact", "direct_identifier" },
            CompiledRegex = new Regex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        new() {
            Code = "PII_003", Severity = "critical", Description = "Social Security Number (US)",
            Tags = new[] { "national_id", "direct_identifier" },
            CompiledRegex = new Regex(@"\b(?!000|666|9\d{2})([0-8]\d{2})-?(?!00)(\d{2})-?(?!0000)(\d{4})\b", RegexOptions.Compiled)
        },
        new() {
            Code = "PII_007", Severity = "critical", Description = "National Insurance Number (UK)",
            Tags = new[] { "national_id", "direct_identifier" },
            CompiledRegex = new Regex(@"\b[A-CEGHJ-PR-TW-Z]{2}\s?\d{2}\s?\d{2}\s?\d{2}\s?[A-D]\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        new() {
            Code = "PII_008", Severity = "critical", Description = "Social Insurance Number (Canada)",
            Tags = new[] { "national_id", "direct_identifier" },
            CompiledRegex = new Regex(@"\b\d{3}-\d{3}-\d{3}\b|\b\d{9}\b", RegexOptions.Compiled)
        },
        #endregion

        #region Quasi-Identifiers (QI) - For Contextual Analysis
        new() {
            Code = "QI_001", Severity = "info", Description = "First Name (Common English)",
            Tags = new[] { "name_part" },
            // This is a representative sample, not exhaustive. Designed to seed the contextual engine.
            CompiledRegex = new Regex(@"\b(James|John|Robert|Michael|William|David|Richard|Joseph|Mary|Patricia|Jennifer|Linda|Elizabeth|Susan|Jessica)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        new() {
            Code = "QI_002", Severity = "info", Description = "Last Name (Common English)",
            Tags = new[] { "name_part" },
            CompiledRegex = new Regex(@"\b(Smith|Johnson|Williams|Brown|Jones|Garcia|Miller|Davis|Rodriguez|Martinez|Hernandez|Lopez|Gonzalez)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        new() {
            Code = "QI_003", Severity = "info", Description = "Full Name (First Last)",
            Tags = new[] { "name_full" },
            // Looks for two capitalized words, a common pattern for names.
            CompiledRegex = new Regex(@"\b[A-Z][a-z]+(?:\s|,)\s?[A-Z][a-z]+\b", RegexOptions.Compiled)
        },
        new() {
            Code = "QI_010", Severity = "info", Description = "Date of Birth",
            Tags = new[] { "dob" },
            // Matches common date formats like YYYY-MM-DD, MM/DD/YYYY, DD-Mon-YYYY. Looks for years from 1900-202x.
            CompiledRegex = new Regex(@"\b((?:19|20)\d{2}[-/](?:0[1-9]|1[0-2])[-/](?:0[1-9]|[12]\d|3[01]))\b|\b((?:0[1-9]|1[0-2])\/(?:0[1-9]|[12]\d|3[01])\/(?:19|20)\d{2})\b", RegexOptions.Compiled)
        },
        new() {
            Code = "QI_020", Severity = "info", Description = "Street Address",
            Tags = new[] { "address_part" },
            CompiledRegex = new Regex(@"\b\d{1,5}\s+(?:[A-Z][a-z]+\s+){1,5}(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Court|Ct|Lane|Ln)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        new() {
            Code = "QI_021", Severity = "info", Description = "Zip Code (US)",
            Tags = new[] { "location", "address_part" },
            CompiledRegex = new Regex(@"\b\d{5}(?:-\d{4})?\b", RegexOptions.Compiled)
        },
        new() {
            Code = "QI_022", Severity = "info", Description = "Postal Code (Canada)",
            Tags = new[] { "location", "address_part" },
            CompiledRegex = new Regex(@"\b[A-CEGHJ-NPR-TVXY]\d[A-Z]\s?\d[A-Z]\d\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        new() {
            Code = "QI_023", Severity = "info", Description = "Postcode (UK)",
            Tags = new[] { "location", "address_part" },
            CompiledRegex = new Regex(@"\b([A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        #endregion

        #region Financial Data (PCI/PIFI)
        new() {
            Code = "PCI_001", Severity = "critical", Description = "Credit Card Number",
            Tags = new[] { "financial", "pci" },
            CompiledRegex = new Regex(@"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|6(?:011|5[0-9][0-9])[0-9]{12}|3[47][0-9]{13}|3(?:0[0-5]|[68][0-9])[0-9]{11}|(?:2131|1800|35\d{3})\d{11})\b", RegexOptions.Compiled)
        },
        new() {
            Code = "PIFI_001", Severity = "critical", Description = "IBAN",
            Tags = new[] { "financial", "pifi" },
            CompiledRegex = new Regex(@"\b[A-Z]{2}[0-9]{2}[A-Z0-9]{4}[0-9]{7}([A-Z0-9]?){0,16}\b", RegexOptions.Compiled)
        },
        new() {
            Code = "PIFI_003", Severity = "error", Description = "ABA Routing Number (US)",
            Tags = new[] { "financial", "pifi" },
            CompiledRegex = new Regex(@"\b(0[1-9]|1[0-2]|2[1-9]|3[0-2]|6[1-9]|7[0-2]|80)\d{7}\b", RegexOptions.Compiled)
        },
        #endregion

        #region Security Credentials (SEC)
        new() {
            Code = "SEC_001", Severity = "critical", Description = "AWS Access Key ID",
            Tags = new[] { "secret", "credential" },
            CompiledRegex = new Regex(@"\b(A3T[A-Z0-9]|AKIA|AGPA|AIDA|AROA|AIPA|ANPA|ANVA|ASIA)[A-Z0-9]{16}\b", RegexOptions.Compiled)
        },
        new() {
            Code = "SEC_002", Severity = "critical", Description = "AWS Secret Access Key",
            Tags = new[] { "secret", "credential" },
            CompiledRegex = new Regex(@"(?<![A-Z0-9/+=])[A-Z0-9/+=]{40}(?![A-Z0-9/+=])", RegexOptions.Compiled)
        },
        new() {
            Code = "SEC_003", Severity = "critical", Description = "Azure Client Secret",
            Tags = new[] { "secret", "credential" },
            CompiledRegex = new Regex(@"[A-Za-z0-9_\-\.]{36}~[A-Za-z0-9_\-]{8}", RegexOptions.Compiled)
        },
        new() {
            Code = "SEC_004", Severity = "critical", Description = "Google Cloud API Key",
            Tags = new[] { "secret", "credential" },
            CompiledRegex = new Regex(@"AIza[0-9A-Za-z\-_]{35}", RegexOptions.Compiled)
        },
        new() {
            Code = "SEC_005", Severity = "critical", Description = "Private Key Block",
            Tags = new[] { "secret", "credential", "crypto" },
            CompiledRegex = new Regex(@"-----BEGIN (RSA|EC|PGP|OPENSSH|ENCRYPTED) PRIVATE KEY-----", RegexOptions.Compiled)
        },
        new() {
            Code = "SEC_010", Severity = "critical", Description = "Database Connection String",
            Tags = new[] { "secret", "credential" },
            CompiledRegex = new Regex(@"(?i)(Password|Pwd)=[^;]+;.*(Server|Data Source)=[^;]+;", RegexOptions.Compiled)
        },
        new() {
            Code = "SEC_999", Severity = "error", Description = "Generic Secret Pattern",
            Tags = new[] { "secret", "credential" },
            CompiledRegex = new Regex(@"\b(key|token|secret|password|passwd|pwd|auth|bearer)[\s""':=]+([a-zA-Z0-9_.\-]{16,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        #endregion
        
        #region Protected Health Information (PHI)
        new() {
            Code = "PHI_001", Severity = "error", Description = "ICD-10 Code",
            Tags = new[] { "health", "phi" },
            CompiledRegex = new Regex(@"\b[A-Z][0-9][0-9A-Z](?:\.[0-9A-Z]{1,4})?\b", RegexOptions.Compiled)
        },
        new() {
            Code = "PHI_002", Severity = "error", Description = "National Drug Code (US)",
            Tags = new[] { "health", "phi" },
            CompiledRegex = new Regex(@"\b\d{4,5}-\d{3,4}-\d{1,2}\b", RegexOptions.Compiled)
        },
        new() {
            Code = "PHI_003", Severity = "error", Description = "DEA Number (US)",
            Tags = new[] { "health", "phi" },
            CompiledRegex = new Regex(@"\b[ABCFGHLMPRSTX]\w\d{7}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        },
        #endregion

        #region Network & Device Identifiers
        new() {
            Code = "NET_001", Severity = "warning", Description = "IP Address (v4)",
            Tags = new[] { "location", "network" },
            CompiledRegex = new Regex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b", RegexOptions.Compiled)
        },
        new() {
            Code = "NET_002", Severity = "warning", Description = "MAC Address",
            Tags = new[] { "device", "network" },
            CompiledRegex = new Regex(@"\b(?:[0-9A-F]{2}[:-]){5}(?:[0-9A-F]{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        }
        #endregion
    });
        }

        private void LoadCustomRules(PatternFile? customPatterns)
        {
            if (customPatterns?.Patterns == null || !customPatterns.Patterns.Any())
            {
                _logger.LogDebug("No custom patterns provided to load.");
                return;
            }

            _logger.LogDebug("Loading {Count} custom validation rules.", customPatterns.Patterns.Count);
            foreach (var pattern in customPatterns.Patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern.Code) || string.IsNullOrWhiteSpace(pattern.Regex))
                {
                    _logger.LogWarning("Skipping invalid custom pattern with missing code or regex: {@Pattern}", pattern);
                    continue;
                }

                // Check for duplicate or overridden built-in codes
                if (_rules.Any(r => r.Code.Equals(pattern.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Custom pattern with code '{Code}' overrides an existing built-in rule.", pattern.Code);
                    _rules.RemoveAll(r => r.Code.Equals(pattern.Code, StringComparison.OrdinalIgnoreCase));
                }

                try
                {
                    var validationRule = new ValidationRule
                    {
                        Code = pattern.Code,
                        Severity = pattern.Severity,
                        Description = pattern.Description,
                        CompiledRegex = new Regex(pattern.Regex, RegexOptions.Compiled)
                    };
                    _rules.Add(validationRule);
                }
                catch (ArgumentException ex)
                {
                    _logger.LogError(ex, "Failed to compile regex for custom pattern '{Code}'. The pattern will be skipped.", pattern.Code);
                }
            }
        }
    }
}