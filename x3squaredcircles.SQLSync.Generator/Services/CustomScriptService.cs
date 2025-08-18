using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ICustomScriptService
    {
        Task<List<CustomScript>> DiscoverCustomScriptsAsync(SqlSchemaConfiguration config);
        Task<CustomScriptValidationResult> ValidateCustomScriptsAsync(List<CustomScript> scripts, SqlSchemaConfiguration config);
        Task<List<DeploymentOperation>> ConvertScriptsToOperationsAsync(List<CustomScript> scripts, SqlSchemaConfiguration config);
    }

    public class CustomScriptService : ICustomScriptService
    {
        private readonly ILogger<CustomScriptService> _logger;
        private readonly string _workingDirectory = "/src";

        // Dangerous SQL keywords that require risk assessment
        private readonly HashSet<string> _riskyKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DROP", "DELETE", "TRUNCATE", "ALTER", "UPDATE", "EXEC", "EXECUTE",
            "xp_", "sp_", "BULK", "OPENROWSET", "OPENDATASOURCE", "SHUTDOWN",
            "RESTORE", "BACKUP", "DBCC", "KILL", "WAITFOR"
        };

        // SQL statement types for classification
        private readonly Dictionary<string, string> _statementTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CREATE"] = "CREATE",
            ["ALTER"] = "ALTER",
            ["DROP"] = "DROP",
            ["INSERT"] = "INSERT",
            ["UPDATE"] = "UPDATE",
            ["DELETE"] = "DELETE",
            ["TRUNCATE"] = "TRUNCATE",
            ["GRANT"] = "GRANT",
            ["REVOKE"] = "REVOKE",
            ["EXEC"] = "EXECUTE",
            ["EXECUTE"] = "EXECUTE"
        };

        public CustomScriptService(ILogger<CustomScriptService> logger)
        {
            _logger = logger;
        }

        public async Task<List<CustomScript>> DiscoverCustomScriptsAsync(SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Discovering custom SQL scripts");

                var scripts = new List<CustomScript>();
                var scriptsPath = DetermineScriptsPath(config);

                if (string.IsNullOrEmpty(scriptsPath) || !Directory.Exists(scriptsPath))
                {
                    _logger.LogInformation("No custom scripts directory found: {ScriptsPath}", scriptsPath ?? "not configured");
                    return scripts;
                }

                var sqlFiles = Directory.GetFiles(scriptsPath, "*.sql", SearchOption.AllDirectories);
                _logger.LogInformation("Found {FileCount} SQL files in {ScriptsPath}", sqlFiles.Length, scriptsPath);

                foreach (var sqlFile in sqlFiles)
                {
                    try
                    {
                        var script = await ProcessSqlFileAsync(sqlFile, config);
                        if (script != null)
                        {
                            scripts.Add(script);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process SQL file: {SqlFile}", sqlFile);
                    }
                }

                // Sort scripts by execution order
                scripts = scripts.OrderBy(s => s.ExecutionOrder)
                                .ThenBy(s => s.FileName)
                                .ToList();

                _logger.LogInformation("✓ Discovered {ScriptCount} custom scripts", scripts.Count);
                return scripts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover custom scripts");
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure,
                    $"Failed to discover custom scripts: {ex.Message}", ex);
            }
        }

        public async Task<CustomScriptValidationResult> ValidateCustomScriptsAsync(List<CustomScript> scripts, SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Validating {ScriptCount} custom scripts", scripts.Count);

                var result = new CustomScriptValidationResult
                {
                    IsValid = true,
                    Scripts = scripts,
                    Errors = new List<CustomScriptError>(),
                    Warnings = new List<CustomScriptWarning>(),
                    ValidationTime = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object>
                    {
                        ["total_scripts"] = scripts.Count,
                        ["database_provider"] = config.Database.GetSelectedProvider()
                    }
                };

                foreach (var script in scripts)
                {
                    await ValidateIndividualScriptAsync(script, result, config);
                }

                // Validate script dependencies and execution order
                await ValidateScriptDependenciesAsync(scripts, result, config);

                // Check for potential conflicts with generated schema
                await ValidateSchemaConflictsAsync(scripts, result, config);

                result.IsValid = !result.Errors.Any();

                if (result.IsValid)
                {
                    _logger.LogInformation("✓ Custom script validation passed: {ScriptCount} scripts, {WarningCount} warnings",
                        scripts.Count, result.Warnings.Count);
                }
                else
                {
                    _logger.LogWarning("❌ Custom script validation failed: {ErrorCount} errors, {WarningCount} warnings",
                        result.Errors.Count, result.Warnings.Count);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom script validation failed");
                throw new SqlSchemaException(SqlSchemaExitCode.SchemaValidationFailure,
                    $"Custom script validation failed: {ex.Message}", ex);
            }
        }

        public async Task<List<DeploymentOperation>> ConvertScriptsToOperationsAsync(List<CustomScript> scripts, SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Converting {ScriptCount} custom scripts to deployment operations", scripts.Count);

                var operations = new List<DeploymentOperation>();

                foreach (var script in scripts.OrderBy(s => s.ExecutionOrder))
                {
                    var operation = new DeploymentOperation
                    {
                        Type = "CUSTOM_SCRIPT",
                        ObjectName = script.Name,
                        Schema = script.TargetSchema ?? config.Database.Schema ?? GetDefaultSchema(config.Database.GetSelectedProvider()),
                        SqlCommand = script.Content,
                        RollbackCommand = script.RollbackScript,
                        RiskLevel = script.RiskLevel,
                        Dependencies = script.Dependencies.ToList(),
                        Properties = new Dictionary<string, object>
                        {
                            ["script_type"] = script.ScriptType,
                            ["file_path"] = script.FilePath,
                            ["execution_order"] = script.ExecutionOrder,
                            ["estimated_duration"] = script.EstimatedDuration,
                            ["requires_transaction"] = script.RequiresTransaction,
                            ["can_retry"] = script.CanRetry,
                            ["description"] = script.Description,
                            ["author"] = script.Author,
                            ["version"] = script.Version
                        }
                    };

                    operations.Add(operation);
                }

                _logger.LogInformation("✓ Converted {ScriptCount} scripts to {OperationCount} deployment operations",
                    scripts.Count, operations.Count);

                return operations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert custom scripts to operations");
                throw new SqlSchemaException(SqlSchemaExitCode.SchemaValidationFailure,
                    $"Failed to convert custom scripts to operations: {ex.Message}", ex);
            }
        }

        private string DetermineScriptsPath(SqlSchemaConfiguration config)
        {
            // Use configured scripts path or common default locations
            if (!string.IsNullOrEmpty(config.SchemaAnalysis.ScriptsPath))
            {
                return Path.IsPathRooted(config.SchemaAnalysis.ScriptsPath)
                    ? config.SchemaAnalysis.ScriptsPath
                    : Path.Combine(_workingDirectory, config.SchemaAnalysis.ScriptsPath);
            }

            // Try common script directory names
            var commonPaths = new[]
            {
                "SqlScripts", "Scripts", "sql", "database", "db", "migrations", "Database/Scripts"
            };

            foreach (var path in commonPaths)
            {
                var fullPath = Path.Combine(_workingDirectory, path);
                if (Directory.Exists(fullPath))
                {
                    _logger.LogDebug("Found scripts directory: {ScriptsPath}", fullPath);
                    return fullPath;
                }
            }

            return string.Empty;
        }

        private async Task<CustomScript?> ProcessSqlFileAsync(string filePath, SqlSchemaConfiguration config)
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogDebug("Skipping empty SQL file: {FilePath}", filePath);
                    return null;
                }

                var fileName = Path.GetFileName(filePath);
                var script = new CustomScript
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    FileName = fileName,
                    FilePath = filePath,
                    Content = content,
                    ScriptType = DetermineScriptType(content, fileName),
                    RiskLevel = AssessScriptRiskLevel(content),
                    EstimatedDuration = EstimateExecutionDuration(content),
                    RequiresTransaction = ShouldUseTransaction(content),
                    CanRetry = CanScriptBeRetried(content),
                    Dependencies = ExtractDependencies(content),
                    Metadata = new Dictionary<string, object>
                    {
                        ["file_size"] = new FileInfo(filePath).Length,
                        ["line_count"] = content.Split('\n').Length,
                        ["statement_count"] = CountSqlStatements(content)
                    }
                };

                // Parse script header for metadata
                ParseScriptHeader(content, script);

                // Determine execution order from filename or header
                script.ExecutionOrder = DetermineExecutionOrder(fileName, script);

                // Set target schema
                script.TargetSchema = ExtractTargetSchema(content, config);

                _logger.LogDebug("Processed custom script: {ScriptName} (Type: {ScriptType}, Risk: {RiskLevel})",
                    script.Name, script.ScriptType, script.RiskLevel);

                return script;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process SQL file: {FilePath}", filePath);
                return null;
            }
        }

        private string DetermineScriptType(string content, string fileName)
        {
            var upperContent = content.ToUpperInvariant();
            var upperFileName = fileName.ToUpperInvariant();

            // Check filename patterns first
            if (upperFileName.Contains("PROCEDURE") || upperFileName.Contains("PROC"))
                return "PROCEDURE";
            if (upperFileName.Contains("FUNCTION") || upperFileName.Contains("FUNC"))
                return "FUNCTION";
            if (upperFileName.Contains("VIEW"))
                return "VIEW";
            if (upperFileName.Contains("INDEX"))
                return "INDEX";
            if (upperFileName.Contains("TRIGGER"))
                return "TRIGGER";
            if (upperFileName.Contains("DATA") || upperFileName.Contains("SEED"))
                return "DATA";
            if (upperFileName.Contains("MIGRATION") || upperFileName.Contains("MIGRATE"))
                return "MIGRATION";

            // Check content patterns
            if (upperContent.Contains("CREATE PROCEDURE") || upperContent.Contains("CREATE OR REPLACE PROCEDURE"))
                return "PROCEDURE";
            if (upperContent.Contains("CREATE FUNCTION") || upperContent.Contains("CREATE OR REPLACE FUNCTION"))
                return "FUNCTION";
            if (upperContent.Contains("CREATE VIEW") || upperContent.Contains("CREATE OR REPLACE VIEW"))
                return "VIEW";
            if (upperContent.Contains("CREATE INDEX") || upperContent.Contains("CREATE UNIQUE INDEX"))
                return "INDEX";
            if (upperContent.Contains("CREATE TRIGGER"))
                return "TRIGGER";
            if (upperContent.Contains("INSERT INTO") && !upperContent.Contains("CREATE"))
                return "DATA";

            // Check for DDL vs DML
            if (Regex.IsMatch(upperContent, @"\b(CREATE|ALTER|DROP)\b"))
                return "DDL";
            if (Regex.IsMatch(upperContent, @"\b(INSERT|UPDATE|DELETE)\b"))
                return "DML";

            return "CUSTOM";
        }

        private RiskLevel AssessScriptRiskLevel(string content)
        {
            var upperContent = content.ToUpperInvariant();

            // Check for risky keywords
            foreach (var riskyKeyword in _riskyKeywords)
            {
                if (Regex.IsMatch(upperContent, @"\b" + Regex.Escape(riskyKeyword.ToUpperInvariant()) + @"\b"))
                {
                    return RiskLevel.Risky;
                }
            }

            // Check for warning-level operations
            if (Regex.IsMatch(upperContent, @"\b(ALTER|UPDATE|GRANT|REVOKE)\b"))
                return RiskLevel.Warning;

            // Check for potentially problematic patterns
            if (upperContent.Contains("WHERE") && upperContent.Contains("UPDATE") && !upperContent.Contains("WHERE"))
                return RiskLevel.Risky; // UPDATE without WHERE clause

            if (upperContent.Contains("DELETE") && !upperContent.Contains("WHERE"))
                return RiskLevel.Risky; // DELETE without WHERE clause

            if (Regex.IsMatch(upperContent, @"WHILE\s*\("))
                return RiskLevel.Warning; // Loops can be dangerous

            // Safe operations: CREATE, INSERT with specific targets, SELECT
            return RiskLevel.Safe;
        }

        private TimeSpan EstimateExecutionDuration(string content)
        {
            var statementCount = CountSqlStatements(content);
            var lines = content.Split('\n').Length;

            // Simple heuristic based on content analysis
            var baseTime = TimeSpan.FromSeconds(5); // Base execution time
            var perStatementTime = TimeSpan.FromSeconds(2);
            var perLineTime = TimeSpan.FromMilliseconds(100);

            // Check for potentially slow operations
            var upperContent = content.ToUpperInvariant();
            var slowOperationMultiplier = 1.0;

            if (upperContent.Contains("CREATE INDEX"))
                slowOperationMultiplier = 3.0; // Indexes can be slow
            if (upperContent.Contains("ALTER TABLE") && upperContent.Contains("ADD COLUMN"))
                slowOperationMultiplier = 2.0; // Table alterations
            if (upperContent.Contains("UPDATE") || upperContent.Contains("DELETE"))
                slowOperationMultiplier = 2.5; // Data modifications
            if (upperContent.Contains("BULK INSERT") || upperContent.Contains("MERGE"))
                slowOperationMultiplier = 4.0; // Bulk operations

            var estimatedTime = baseTime +
                               TimeSpan.FromTicks((long)(perStatementTime.Ticks * statementCount * slowOperationMultiplier)) +
                               TimeSpan.FromTicks((long)(perLineTime.Ticks * lines));

            return estimatedTime;
        }

        private bool ShouldUseTransaction(string content)
        {
            var upperContent = content.ToUpperInvariant();

            // Scripts that should NOT use transactions
            if (upperContent.Contains("CREATE INDEX") || upperContent.Contains("DROP INDEX"))
                return false; // Some databases don't allow index operations in transactions
            if (upperContent.Contains("BACKUP") || upperContent.Contains("RESTORE"))
                return false; // Backup operations
            if (upperContent.Contains("DBCC") || upperContent.Contains("CHECKPOINT"))
                return false; // Maintenance operations

            // Scripts that SHOULD use transactions
            if (upperContent.Contains("INSERT") || upperContent.Contains("UPDATE") || upperContent.Contains("DELETE"))
                return true; // Data modifications
            if (upperContent.Contains("ALTER TABLE") || upperContent.Contains("DROP TABLE"))
                return true; // Structure changes

            // Multiple statements should use transactions
            return CountSqlStatements(content) > 1;
        }

        private bool CanScriptBeRetried(string content)
        {
            var upperContent = content.ToUpperInvariant();

            // Scripts that cannot be safely retried
            if (upperContent.Contains("INSERT") && !upperContent.Contains("IF NOT EXISTS"))
                return false; // Inserts without existence checks
            if (upperContent.Contains("CREATE") && !upperContent.Contains("IF NOT EXISTS"))
                return false; // Creates without existence checks
            if (upperContent.Contains("DROP") && !upperContent.Contains("IF EXISTS"))
                return false; // Drops without existence checks
            if (upperContent.Contains("ALTER TABLE ADD") && !upperContent.Contains("IF NOT EXISTS"))
                return false; // Column additions without checks

            // Scripts with idempotent patterns can be retried
            if (upperContent.Contains("IF NOT EXISTS") || upperContent.Contains("IF EXISTS"))
                return true;
            if (upperContent.Contains("MERGE") || upperContent.Contains("UPSERT"))
                return true;

            return false;
        }

        private List<string> ExtractDependencies(string content)
        {
            var dependencies = new List<string>();
            var upperContent = content.ToUpperInvariant();

            // Look for table/object references
            var tableMatches = Regex.Matches(content, @"\b(FROM|JOIN|INTO|UPDATE|REFERENCES)\s+([a-zA-Z_][a-zA-Z0-9_]*\.)?([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
            foreach (Match match in tableMatches)
            {
                var tableName = match.Groups[3].Value;
                if (!string.IsNullOrEmpty(tableName) && !dependencies.Contains(tableName, StringComparer.OrdinalIgnoreCase))
                {
                    dependencies.Add(tableName);
                }
            }

            // Look for procedure/function calls
            var procMatches = Regex.Matches(content, @"\bEXEC(UTE)?\s+([a-zA-Z_][a-zA-Z0-9_]*\.)?([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
            foreach (Match match in procMatches)
            {
                var procName = match.Groups[3].Value;
                if (!string.IsNullOrEmpty(procName) && !dependencies.Contains(procName, StringComparer.OrdinalIgnoreCase))
                {
                    dependencies.Add(procName);
                }
            }

            return dependencies;
        }

        private int CountSqlStatements(string content)
        {
            // Remove comments and string literals first
            var cleanContent = RemoveCommentsAndStrings(content);

            // Count semicolons as statement separators
            var statements = cleanContent.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                       .Where(s => !string.IsNullOrWhiteSpace(s))
                                       .Count();

            return Math.Max(1, statements); // At least 1 statement
        }

        private string RemoveCommentsAndStrings(string sql)
        {
            var result = new StringBuilder();
            var inSingleQuote = false;
            var inDoubleQuote = false;
            var inBlockComment = false;
            var inLineComment = false;

            for (int i = 0; i < sql.Length; i++)
            {
                var current = sql[i];
                var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

                if (!inSingleQuote && !inDoubleQuote && !inBlockComment && !inLineComment)
                {
                    if (current == '\'' && next != '\'')
                    {
                        inSingleQuote = true;
                        continue;
                    }
                    if (current == '"')
                    {
                        inDoubleQuote = true;
                        continue;
                    }
                    if (current == '/' && next == '*')
                    {
                        inBlockComment = true;
                        i++; // Skip next character
                        continue;
                    }
                    if (current == '-' && next == '-')
                    {
                        inLineComment = true;
                        i++; // Skip next character
                        continue;
                    }
                }

                if (inSingleQuote && current == '\'' && (i == 0 || sql[i - 1] != '\\'))
                {
                    inSingleQuote = false;
                    continue;
                }

                if (inDoubleQuote && current == '"' && (i == 0 || sql[i - 1] != '\\'))
                {
                    inDoubleQuote = false;
                    continue;
                }

                if (inBlockComment && current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++; // Skip next character
                    continue;
                }

                if (inLineComment && (current == '\n' || current == '\r'))
                {
                    inLineComment = false;
                }

                if (!inSingleQuote && !inDoubleQuote && !inBlockComment && !inLineComment)
                {
                    result.Append(current);
                }
            }

            return result.ToString();
        }

        private void ParseScriptHeader(string content, CustomScript script)
        {
            var lines = content.Split('\n').Take(20); // Check first 20 lines for header

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || (!trimmedLine.StartsWith("--") && !trimmedLine.StartsWith("/*")))
                    continue;

                // Remove comment markers
                var cleanLine = trimmedLine.TrimStart('-', '/', '*').Trim();

                if (cleanLine.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                {
                    script.Description = cleanLine.Substring(12).Trim();
                }
                else if (cleanLine.StartsWith("Author:", StringComparison.OrdinalIgnoreCase))
                {
                    script.Author = cleanLine.Substring(7).Trim();
                }
                else if (cleanLine.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                {
                    script.Version = cleanLine.Substring(8).Trim();
                }
                else if (cleanLine.StartsWith("Order:", StringComparison.OrdinalIgnoreCase) ||
                        cleanLine.StartsWith("ExecutionOrder:", StringComparison.OrdinalIgnoreCase))
                {
                    var orderStr = cleanLine.Contains(':') ? cleanLine.Split(':')[1].Trim() : "";
                    if (int.TryParse(orderStr, out var order))
                    {
                        script.ExecutionOrder = order;
                    }
                }
                else if (cleanLine.StartsWith("Schema:", StringComparison.OrdinalIgnoreCase))
                {
                    script.TargetSchema = cleanLine.Substring(7).Trim();
                }
                else if (cleanLine.StartsWith("Rollback:", StringComparison.OrdinalIgnoreCase))
                {
                    var rollbackFile = cleanLine.Substring(9).Trim();
                    script.RollbackScript = LoadRollbackScriptAsync(rollbackFile, script.FilePath).Result;
                }
            }
        }

        private async Task<string?> LoadRollbackScriptAsync(string rollbackFile, string originalScriptPath)
        {
            try
            {
                var scriptDirectory = Path.GetDirectoryName(originalScriptPath);
                var rollbackPath = Path.Combine(scriptDirectory!, rollbackFile);

                if (File.Exists(rollbackPath))
                {
                    return await File.ReadAllTextAsync(rollbackPath);
                }

                _logger.LogWarning("Rollback script not found: {RollbackPath}", rollbackPath);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load rollback script: {RollbackFile}", rollbackFile);
                return null;
            }
        }

        private int DetermineExecutionOrder(string fileName, CustomScript script)
        {
            // If already set from header, use that
            if (script.ExecutionOrder > 0)
                return script.ExecutionOrder;

            // Try to extract order from filename (e.g., "001_create_tables.sql")
            var match = Regex.Match(fileName, @"^(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var fileOrder))
            {
                return fileOrder;
            }

            // Default ordering by script type
            return script.ScriptType switch
            {
                "DDL" => 100,
                "PROCEDURE" => 200,
                "FUNCTION" => 250,
                "VIEW" => 300,
                "TRIGGER" => 400,
                "INDEX" => 500,
                "DATA" => 600,
                "DML" => 700,
                "MIGRATION" => 800,
                _ => 900
            };
        }

        private string? ExtractTargetSchema(string content, SqlSchemaConfiguration config)
        {
            // Look for USE statement
            var useMatch = Regex.Match(content, @"\bUSE\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
            if (useMatch.Success)
            {
                return useMatch.Groups[1].Value;
            }

            // Look for schema-qualified object names
            var schemaMatch = Regex.Match(content, @"\b([a-zA-Z_][a-zA-Z0-9_]*)\.[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.IgnoreCase);
            if (schemaMatch.Success)
            {
                var schemaName = schemaMatch.Groups[1].Value;
                if (!schemaName.Equals("dbo", StringComparison.OrdinalIgnoreCase) &&
                    !schemaName.Equals("sys", StringComparison.OrdinalIgnoreCase))
                {
                    return schemaName;
                }
            }

            // Default to configured schema
            return config.Database.Schema;
        }

        private async Task ValidateIndividualScriptAsync(CustomScript script, CustomScriptValidationResult result, SqlSchemaConfiguration config)
        {
            // Validate script content
            if (string.IsNullOrWhiteSpace(script.Content))
            {
                result.Errors.Add(new CustomScriptError
                {
                    ScriptName = script.Name,
                    ErrorCode = "EMPTY_SCRIPT",
                    Message = $"Script '{script.Name}' is empty or contains only whitespace",
                    Severity = "Error"
                });
                return;
            }

            // Check for dangerous operations
            await ValidateDangerousOperationsAsync(script, result, config);

            // Validate SQL syntax (basic)
            await ValidateBasicSyntaxAsync(script, result, config);

            // Check for provider compatibility
            await ValidateProviderCompatibilityAsync(script, result, config);

            // Validate dependencies
            await ValidateScriptDependenciesIndividualAsync(script, result, config);
        }

        private async Task ValidateDangerousOperationsAsync(CustomScript script, CustomScriptValidationResult result, SqlSchemaConfiguration config)
        {
            var upperContent = script.Content.ToUpperInvariant();

            // Check for extremely dangerous operations
            var dangerousPatterns = new[]
            {
                (@"\bDROP\s+DATABASE\b", "DROP DATABASE operations are not allowed"),
                (@"\bSHUTDOWN\b", "SHUTDOWN commands are not allowed"),
                (@"\bFORMAT\b", "FORMAT operations are not allowed"),
                (@"\bxp_cmdshell\b", "xp_cmdshell is not allowed for security reasons"),
                (@"\bOPENROWSET\b", "OPENROWSET operations require review"),
                (@"\bBULK\s+INSERT\b", "BULK INSERT operations require validation")
            };

            foreach (var (pattern, message) in dangerousPatterns)
            {
                if (Regex.IsMatch(upperContent, pattern))
                {
                    result.Errors.Add(new CustomScriptError
                    {
                        ScriptName = script.Name,
                        ErrorCode = "DANGEROUS_OPERATION",
                        Message = $"Script '{script.Name}': {message}",
                        Severity = "Error"
                    });
                }
            }

            // Check for warning-level operations
            var warningPatterns = new[]
            {
                (@"\bUPDATE\b.*\bWHERE\b", "UPDATE operations should be carefully reviewed"),
                (@"\bDELETE\b.*\bWHERE\b", "DELETE operations should be carefully reviewed"),
                (@"\bTRUNCATE\b", "TRUNCATE operations cause data loss"),
                (@"\bALTER\s+TABLE\b", "ALTER TABLE operations should be reviewed")
            };

            foreach (var (pattern, message) in warningPatterns)
            {
                if (Regex.IsMatch(upperContent, pattern))
                {
                    result.Warnings.Add(new CustomScriptWarning
                    {
                        ScriptName = script.Name,
                        WarningCode = "RISKY_OPERATION",
                        Message = $"Script '{script.Name}': {message}",
                        Severity = "Warning"
                    });
                }
            }
        }

        private async Task ValidateBasicSyntaxAsync(CustomScript script, CustomScriptValidationResult result, SqlSchemaConfiguration config)
        {
            // Basic syntax validation
            var content = script.Content;

            // Check for balanced parentheses
            var openParens = content.Count(c => c == '(');
            var closeParens = content.Count(c => c == ')');
            if (openParens != closeParens)
            {
                result.Warnings.Add(new CustomScriptWarning
                {
                    ScriptName = script.Name,
                    WarningCode = "UNBALANCED_PARENTHESES",
                    Message = $"Script '{script.Name}' has unbalanced parentheses",
                    Severity = "Warning"
                });
            }

            // Check for unclosed quotes
            var singleQuotes = content.Count(c => c == '\'');
            if (singleQuotes % 2 != 0)
            {
                result.Warnings.Add(new CustomScriptWarning
                {
                    ScriptName = script.Name,
                    WarningCode = "UNCLOSED_QUOTES",
                    Message = $"Script '{script.Name}' may have unclosed string literals",
                    Severity = "Warning"
                });
            }

            // Check for common SQL mistakes
            var upperContent = content.ToUpperInvariant();
            if (upperContent.Contains("SELECT *") && !upperContent.Contains("COUNT(*)"))
            {
                result.Warnings.Add(new CustomScriptWarning
                {
                    ScriptName = script.Name,
                    WarningCode = "SELECT_STAR",
                    Message = $"Script '{script.Name}' uses SELECT * which may impact performance",
                    Severity = "Info"
                });
            }
        }

        private async Task ValidateProviderCompatibilityAsync(CustomScript script, CustomScriptValidationResult result, SqlSchemaConfiguration config)
        {
            var provider = config.Database.GetSelectedProvider();
            var content = script.Content.ToUpperInvariant();

            var incompatibleFeatures = provider switch
            {
                "mysql" => new[] { "IDENTITY", "UNIQUEIDENTIFIER", "NVARCHAR", "[", "]" },
                "postgresql" => new[] { "IDENTITY", "UNIQUEIDENTIFIER", "[", "]", "NVARCHAR" },
                "sqlite" => new[] { "IDENTITY", "STORED PROCEDURE", "FUNCTION", "TRIGGER" },
                "oracle" => new[] { "IDENTITY", "UNIQUEIDENTIFIER", "NVARCHAR", "[", "]" },
                _ => Array.Empty<string>()
            };

            foreach (var feature in incompatibleFeatures)
            {
                if (content.Contains(feature.ToUpperInvariant()))
                {
                    result.Warnings.Add(new CustomScriptWarning
                    {
                        ScriptName = script.Name,
                        WarningCode = "PROVIDER_INCOMPATIBILITY",
                        Message = $"Script '{script.Name}' uses '{feature}' which may not be compatible with {provider}",
                        Severity = "Warning"
                    });
                }
            }
        }

        private async Task ValidateScriptDependenciesIndividualAsync(CustomScript script, CustomScriptValidationResult result, SqlSchemaConfiguration config)
        {
            // This would ideally check if referenced objects exist in the target schema
            // For now, just warn about common missing dependencies

            if (script.Dependencies.Any())
            {
                foreach (var dependency in script.Dependencies)
                {
                    // Log dependency for later validation
                    result.Warnings.Add(new CustomScriptWarning
                    {
                        ScriptName = script.Name,
                        WarningCode = "DEPENDENCY_REFERENCE",
                        Message = $"Script '{script.Name}' references object '{dependency}' - ensure it exists",
                        Severity = "Info"
                    });
                }
            }
        }

        private async Task ValidateScriptDependenciesAsync(List<CustomScript> scripts, CustomScriptValidationResult result, SqlSchemaConfiguration config)
        {
            // Check for circular dependencies
            var dependencyGraph = scripts.ToDictionary(s => s.Name, s => s.Dependencies);
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            foreach (var script in scripts)
            {
                if (HasCircularDependency(script.Name, dependencyGraph, visited, recursionStack))
                {
                    result.Errors.Add(new CustomScriptError
                    {
                        ScriptName = script.Name,
                        ErrorCode = "CIRCULAR_DEPENDENCY",
                        Message = $"Circular dependency detected involving script '{script.Name}'",
                        Severity = "Error"
                    });
                }
            }
        }

        private bool HasCircularDependency(string scriptName, Dictionary<string, List<string>> graph,
            HashSet<string> visited, HashSet<string> recursionStack)
        {
            if (recursionStack.Contains(scriptName))
                return true;

            if (visited.Contains(scriptName))
                return false;

            visited.Add(scriptName);
            recursionStack.Add(scriptName);

            if (graph.ContainsKey(scriptName))
            {
                foreach (var dependency in graph[scriptName])
                {
                    if (HasCircularDependency(dependency, graph, visited, recursionStack))
                        return true;
                }
            }

            recursionStack.Remove(scriptName);
            return false;
        }

        private async Task ValidateSchemaConflictsAsync(List<CustomScript> scripts, CustomScriptValidationResult result, SqlSchemaConfiguration config)
        {
            // Check for potential conflicts with auto-generated schema
            // This is a placeholder for more sophisticated conflict detection

            foreach (var script in scripts)
            {
                if (script.ScriptType == "DDL" && script.Content.ToUpperInvariant().Contains("CREATE TABLE"))
                {
                    result.Warnings.Add(new CustomScriptWarning
                    {
                        ScriptName = script.Name,
                        WarningCode = "POTENTIAL_SCHEMA_CONFLICT",
                        Message = $"Script '{script.Name}' creates tables - ensure no conflicts with generated schema",
                        Severity = "Warning"
                    });
                }
            }
        }

        private string GetDefaultSchema(string provider)
        {
            return provider switch
            {
                "sqlserver" => "dbo",
                "postgresql" => "public",
                "mysql" => "",
                "oracle" => "SYSTEM",
                "sqlite" => "",
                _ => "dbo"
            };
        }
    }

    // Model classes for custom scripts
    public class CustomScript
    {
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string ScriptType { get; set; } = string.Empty;
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Safe;
        public int ExecutionOrder { get; set; } = 0;
        public string? TargetSchema { get; set; }
        public TimeSpan EstimatedDuration { get; set; } = TimeSpan.Zero;
        public bool RequiresTransaction { get; set; } = true;
        public bool CanRetry { get; set; } = false;
        public List<string> Dependencies { get; set; } = new List<string>();
        public string? RollbackScript { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? Version { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class CustomScriptValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<CustomScript> Scripts { get; set; } = new List<CustomScript>();
        public List<CustomScriptError> Errors { get; set; } = new List<CustomScriptError>();
        public List<CustomScriptWarning> Warnings { get; set; } = new List<CustomScriptWarning>();
        public DateTime ValidationTime { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class CustomScriptError
    {
        public string ScriptName { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
    }

    public class CustomScriptWarning
    {
        public string ScriptName { get; set; } = string.Empty;
        public string WarningCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
    }
}