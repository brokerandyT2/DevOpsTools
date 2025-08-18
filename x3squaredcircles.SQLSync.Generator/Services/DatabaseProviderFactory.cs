using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient; // Changed from System.Data.SqlClient
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

// Resolve namespace conflicts
using SqlSchemaColumn = x3squaredcircles.SQLSync.Generator.Models.SchemaColumn;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IDatabaseProviderFactory
    {
        Task<IDatabaseProvider> GetProviderAsync(string providerName);
    }

    public interface IDatabaseProvider
    {
        Task<DatabaseSchema> GetCurrentSchemaAsync(SqlSchemaConfiguration config);
        Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config);
        Task<bool> TestConnectionAsync(SqlSchemaConfiguration config);
        string BuildConnectionString(SqlSchemaConfiguration config);
        Task ExecuteSqlAsync(string sql, SqlSchemaConfiguration config);
        Task<DataTable> ExecuteQueryAsync(string sql, SqlSchemaConfiguration config);
    }

    public class DatabaseProviderFactory : IDatabaseProviderFactory
    {
        private readonly ISqlServerProviderService _sqlServerProvider;
        private readonly IPostgreSqlProviderService _postgreSqlProvider;
        private readonly IMySqlProviderService _mySqlProvider;
        private readonly IOracleProviderService _oracleProvider;
        private readonly ISqliteProviderService _sqliteProvider;
        private readonly ILogger<DatabaseProviderFactory> _logger;

        public DatabaseProviderFactory(
            ISqlServerProviderService sqlServerProvider,
            IPostgreSqlProviderService postgreSqlProvider,
            IMySqlProviderService mySqlProvider,
            IOracleProviderService oracleProvider,
            ISqliteProviderService sqliteProvider,
            ILogger<DatabaseProviderFactory> logger)
        {
            _sqlServerProvider = sqlServerProvider;
            _postgreSqlProvider = postgreSqlProvider;
            _mySqlProvider = mySqlProvider;
            _oracleProvider = oracleProvider;
            _sqliteProvider = sqliteProvider;
            _logger = logger;
        }

        public async Task<IDatabaseProvider> GetProviderAsync(string providerName)
        {
            _logger.LogDebug("Getting database provider for: {ProviderName}", providerName);

            return providerName.ToLowerInvariant() switch
            {
                "sqlserver" => _sqlServerProvider,
                "postgresql" => _postgreSqlProvider,
                "mysql" => _mySqlProvider,
                "oracle" => _oracleProvider,
                "sqlite" => _sqliteProvider,
                _ => throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration,
                    $"Unsupported database provider: {providerName}")
            };
        }
    }

    // Base provider class with common functionality
    public abstract class BaseDatabaseProvider : IDatabaseProvider
    {
        protected readonly ILogger _logger;

        protected BaseDatabaseProvider(ILogger logger)
        {
            _logger = logger;
        }

        public abstract Task<DatabaseSchema> GetCurrentSchemaAsync(SqlSchemaConfiguration config);
        public abstract Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config);
        public abstract string BuildConnectionString(SqlSchemaConfiguration config);

        public virtual async Task<bool> TestConnectionAsync(SqlSchemaConfiguration config)
        {
            try
            {
                using var connection = await CreateConnectionAsync(config);
                await connection.OpenAsync();
                return connection.State == ConnectionState.Open;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection test failed");
                return false;
            }
        }

        public virtual async Task ExecuteSqlAsync(string sql, SqlSchemaConfiguration config)
        {
            using var connection = await CreateConnectionAsync(config);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = config.Database.CommandTimeoutSeconds;

            await command.ExecuteNonQueryAsync();
        }

        public virtual async Task<DataTable> ExecuteQueryAsync(string sql, SqlSchemaConfiguration config)
        {
            using var connection = await CreateConnectionAsync(config);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = config.Database.CommandTimeoutSeconds;

            var dataTable = new DataTable();
            using var reader = await command.ExecuteReaderAsync();
            dataTable.Load(reader);

            return dataTable;
        }

        protected virtual async Task<DbConnection> CreateConnectionWithRetryAsync(string connectionString, Func<string, DbConnection> connectionFactory, SqlSchemaConfiguration config)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= config.Database.RetryAttempts; attempt++)
            {
                try
                {
                    var connection = connectionFactory(connectionString);
                    return connection;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning("Connection attempt {Attempt} failed: {Message}", attempt, ex.Message);

                    if (attempt < config.Database.RetryAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(config.Database.RetryIntervalSeconds * attempt));
                    }
                }
            }

            throw new SqlSchemaException(SqlSchemaExitCode.DatabaseConnectionFailure,
                $"Failed to create database connection after {config.Database.RetryAttempts} attempts: {lastException?.Message}", lastException);
        }
    }

    // SQL Server Provider
    public interface ISqlServerProviderService : IDatabaseProvider { }

    public class SqlServerProviderService : BaseDatabaseProvider, ISqlServerProviderService
    {
        public SqlServerProviderService(ILogger<SqlServerProviderService> logger) : base(logger) { }

        public override async Task<DatabaseSchema> GetCurrentSchemaAsync(SqlSchemaConfiguration config)
        {
            var schema = new DatabaseSchema
            {
                DatabaseName = config.Database.DatabaseName,
                Provider = "sqlserver",
                AnalysisTime = DateTime.UtcNow,
                Tables = new List<SchemaTable>(),
                Views = new List<SchemaView>(),
                Indexes = new List<SchemaIndex>(),
                Constraints = new List<SchemaConstraint>(),
                Procedures = new List<SchemaProcedure>(),
                Functions = new List<SchemaFunction>(),
                Metadata = new Dictionary<string, object>
                {
                    ["server"] = config.Database.Server,
                    ["database"] = config.Database.DatabaseName,
                    ["schema"] = config.Database.Schema ?? "dbo"
                }
            };

            await LoadTablesAsync(schema, config);
            await LoadConstraintsAsync(schema, config);
            await LoadIndexesAsync(schema, config);
            await LoadViewsAsync(schema, config);
            await LoadProceduresAsync(schema, config);
            await LoadFunctionsAsync(schema, config);

            return schema;
        }

        public override async Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config)
        {
            var connectionString = BuildConnectionString(config);
            return await CreateConnectionWithRetryAsync(connectionString, cs => new SqlConnection(cs), config);
        }

        public override string BuildConnectionString(SqlSchemaConfiguration config)
        {
            return config.Database.BuildConnectionString();
        }

        private async Task LoadTablesAsync(DatabaseSchema schema, SqlSchemaConfiguration config)
        {
            var sql = @"
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    c.name AS ColumnName,
                    ty.name AS DataType,
                    c.max_length,
                    c.precision,
                    c.scale,
                    c.is_nullable,
                    c.is_identity,
                    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_primary_key,
                    dc.definition AS default_value
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN sys.columns c ON t.object_id = c.object_id
                INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM sys.index_columns ic
                    INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_primary_key = 1
                ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
                LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                WHERE s.name = @SchemaName
                ORDER BY s.name, t.name, c.column_id";

            var schemaName = config.Database.Schema ?? "dbo";
            var dataTable = await ExecuteQueryWithParametersAsync(sql, config, new Dictionary<string, object>
            {
                ["@SchemaName"] = schemaName
            });

            var tablesDict = new Dictionary<string, SchemaTable>();

            foreach (DataRow row in dataTable.Rows)
            {
                var tableKey = $"{row["SchemaName"]}.{row["TableName"]}";

                if (!tablesDict.TryGetValue(tableKey, out var table))
                {
                    table = new SchemaTable
                    {
                        Name = row["TableName"].ToString()!,
                        Schema = row["SchemaName"].ToString()!,
                        Columns = new List<SqlSchemaColumn>(),
                        Indexes = new List<SchemaIndex>(),
                        Constraints = new List<SchemaConstraint>(),
                        Metadata = new Dictionary<string, object>()
                    };
                    tablesDict[tableKey] = table;
                    schema.Tables.Add(table);
                }

                var column = new SqlSchemaColumn
                {
                    Name = row["ColumnName"].ToString()!,
                    DataType = FormatSqlServerDataType(row),
                    IsNullable = (bool)row["is_nullable"],
                    IsPrimaryKey = Convert.ToBoolean(row["is_primary_key"]),
                    IsIdentity = (bool)row["is_identity"],
                    MaxLength = row["max_length"] != DBNull.Value ? (int?)Convert.ToInt32(row["max_length"]) : null,
                    Precision = row["precision"] != DBNull.Value ? (int?)Convert.ToInt32(row["precision"]) : null,
                    Scale = row["scale"] != DBNull.Value ? (int?)Convert.ToInt32(row["scale"]) : null,
                    DefaultValue = row["default_value"]?.ToString(),
                    Metadata = new Dictionary<string, object>
                    {
                        ["system_type"] = row["DataType"].ToString()!
                    }
                };

                table.Columns.Add(column);
            }
        }

        private async Task LoadConstraintsAsync(DatabaseSchema schema, SqlSchemaConfiguration config)
        {
            var sql = @"
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    c.name AS ConstraintName,
                    c.type AS ConstraintType,
                    col.name AS ColumnName,
                    ref_s.name AS ReferencedSchema,
                    ref_t.name AS ReferencedTable,
                    ref_col.name AS ReferencedColumn,
                    fk.delete_referential_action_desc AS DeleteAction,
                    fk.update_referential_action_desc AS UpdateAction,
                    cc.definition AS CheckDefinition
                FROM sys.objects c
                INNER JOIN sys.schemas s ON c.schema_id = s.schema_id
                INNER JOIN sys.tables t ON c.parent_object_id = t.object_id
                LEFT JOIN sys.key_constraints kc ON c.object_id = kc.object_id
                LEFT JOIN sys.foreign_keys fk ON c.object_id = fk.object_id
                LEFT JOIN sys.check_constraints cc ON c.object_id = cc.object_id
                LEFT JOIN sys.index_columns ic ON kc.unique_index_id = ic.index_id AND ic.object_id = t.object_id
                LEFT JOIN sys.columns col ON ic.column_id = col.column_id AND ic.object_id = col.object_id
                LEFT JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                LEFT JOIN sys.columns ref_col ON fkc.referenced_column_id = ref_col.column_id AND fkc.referenced_object_id = ref_col.object_id
                LEFT JOIN sys.tables ref_t ON fkc.referenced_object_id = ref_t.object_id
                LEFT JOIN sys.schemas ref_s ON ref_t.schema_id = ref_s.schema_id
                WHERE c.type IN ('PK', 'UQ', 'F', 'C') AND s.name = @SchemaName
                ORDER BY s.name, t.name, c.name";

            var schemaName = config.Database.Schema ?? "dbo";
            var dataTable = await ExecuteQueryWithParametersAsync(sql, config, new Dictionary<string, object>
            {
                ["@SchemaName"] = schemaName
            });

            var constraintsDict = new Dictionary<string, SchemaConstraint>();

            foreach (DataRow row in dataTable.Rows)
            {
                var constraintKey = $"{row["SchemaName"]}.{row["ConstraintName"]}";

                if (!constraintsDict.TryGetValue(constraintKey, out var constraint))
                {
                    constraint = new SchemaConstraint
                    {
                        Name = row["ConstraintName"].ToString()!,
                        Type = MapSqlServerConstraintType(row["ConstraintType"].ToString()!),
                        TableName = row["TableName"].ToString()!,
                        Schema = row["SchemaName"].ToString()!,
                        Columns = new List<string>(),
                        ReferencedColumns = new List<string>(),
                        ReferencedTable = row["ReferencedTable"]?.ToString(),
                        ReferencedSchema = row["ReferencedSchema"]?.ToString(),
                        OnDeleteAction = row["DeleteAction"]?.ToString() ?? "NO_ACTION",
                        OnUpdateAction = row["UpdateAction"]?.ToString() ?? "NO_ACTION",
                        CheckExpression = row["CheckDefinition"]?.ToString(),
                        Metadata = new Dictionary<string, object>()
                    };
                    constraintsDict[constraintKey] = constraint;
                    schema.Constraints.Add(constraint);
                }

                if (row["ColumnName"] != DBNull.Value && !string.IsNullOrEmpty(row["ColumnName"].ToString()))
                {
                    var columnName = row["ColumnName"].ToString()!;
                    if (!constraint.Columns.Contains(columnName))
                    {
                        constraint.Columns.Add(columnName);
                    }
                }

                if (row["ReferencedColumn"] != DBNull.Value && !string.IsNullOrEmpty(row["ReferencedColumn"].ToString()))
                {
                    var refColumnName = row["ReferencedColumn"].ToString()!;
                    if (!constraint.ReferencedColumns.Contains(refColumnName))
                    {
                        constraint.ReferencedColumns.Add(refColumnName);
                    }
                }
            }
        }

        private async Task LoadIndexesAsync(DatabaseSchema schema, SqlSchemaConfiguration config)
        {
            var sql = @"
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    i.name AS IndexName,
                    i.is_unique,
                    i.type_desc AS IndexType,
                    c.name AS ColumnName,
                    ic.key_ordinal,
                    i.filter_definition
                FROM sys.indexes i
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND s.name = @SchemaName
                ORDER BY s.name, t.name, i.name, ic.key_ordinal";

            var schemaName = config.Database.Schema ?? "dbo";
            var dataTable = await ExecuteQueryWithParametersAsync(sql, config, new Dictionary<string, object>
            {
                ["@SchemaName"] = schemaName
            });

            var indexesDict = new Dictionary<string, SchemaIndex>();

            foreach (DataRow row in dataTable.Rows)
            {
                var indexKey = $"{row["SchemaName"]}.{row["IndexName"]}";

                if (!indexesDict.TryGetValue(indexKey, out var index))
                {
                    index = new SchemaIndex
                    {
                        Name = row["IndexName"].ToString()!,
                        TableName = row["TableName"].ToString()!,
                        Schema = row["SchemaName"].ToString()!,
                        Columns = new List<string>(),
                        IsUnique = (bool)row["is_unique"],
                        IsClustered = row["IndexType"].ToString()!.Contains("CLUSTERED"),
                        FilterExpression = row["filter_definition"]?.ToString(),
                        Metadata = new Dictionary<string, object>
                        {
                            ["index_type"] = row["IndexType"].ToString()!
                        }
                    };
                    indexesDict[indexKey] = index;
                    schema.Indexes.Add(index);
                }

                if (row["ColumnName"] != DBNull.Value)
                {
                    index.Columns.Add(row["ColumnName"].ToString()!);
                }
            }
        }

        private async Task LoadViewsAsync(DatabaseSchema schema, SqlSchemaConfiguration config)
        {
            var sql = @"
                SELECT 
                    s.name AS SchemaName,
                    v.name AS ViewName,
                    m.definition AS ViewDefinition
                FROM sys.views v
                INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
                INNER JOIN sys.sql_modules m ON v.object_id = m.object_id
                WHERE s.name = @SchemaName";

            var schemaName = config.Database.Schema ?? "dbo";
            var dataTable = await ExecuteQueryWithParametersAsync(sql, config, new Dictionary<string, object>
            {
                ["@SchemaName"] = schemaName
            });

            foreach (DataRow row in dataTable.Rows)
            {
                var view = new SchemaView
                {
                    Name = row["ViewName"].ToString()!,
                    Schema = row["SchemaName"].ToString()!,
                    Definition = row["ViewDefinition"]?.ToString() ?? string.Empty,
                    Columns = new List<SqlSchemaColumn>(),
                    Metadata = new Dictionary<string, object>()
                };

                schema.Views.Add(view);
            }
        }

        private async Task LoadProceduresAsync(DatabaseSchema schema, SqlSchemaConfiguration config)
        {
            var sql = @"
                SELECT 
                    s.name AS SchemaName,
                    p.name AS ProcedureName,
                    m.definition AS ProcedureDefinition
                FROM sys.procedures p
                INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
                INNER JOIN sys.sql_modules m ON p.object_id = m.object_id
                WHERE s.name = @SchemaName";

            var schemaName = config.Database.Schema ?? "dbo";
            var dataTable = await ExecuteQueryWithParametersAsync(sql, config, new Dictionary<string, object>
            {
                ["@SchemaName"] = schemaName
            });

            foreach (DataRow row in dataTable.Rows)
            {
                var procedure = new SchemaProcedure
                {
                    Name = row["ProcedureName"].ToString()!,
                    Schema = row["SchemaName"].ToString()!,
                    Definition = row["ProcedureDefinition"]?.ToString() ?? string.Empty,
                    Parameters = new List<SchemaParameter>(),
                    Metadata = new Dictionary<string, object>()
                };

                schema.Procedures.Add(procedure);
            }
        }

        private async Task LoadFunctionsAsync(DatabaseSchema schema, SqlSchemaConfiguration config)
        {
            var sql = @"
                SELECT 
                    s.name AS SchemaName,
                    f.name AS FunctionName,
                    m.definition AS FunctionDefinition,
                    t.name AS ReturnType
                FROM sys.objects f
                INNER JOIN sys.schemas s ON f.schema_id = s.schema_id
                INNER JOIN sys.sql_modules m ON f.object_id = m.object_id
                LEFT JOIN sys.types t ON f.type = t.user_type_id
                WHERE f.type IN ('FN', 'IF', 'TF') AND s.name = @SchemaName";

            var schemaName = config.Database.Schema ?? "dbo";
            var dataTable = await ExecuteQueryWithParametersAsync(sql, config, new Dictionary<string, object>
            {
                ["@SchemaName"] = schemaName
            });

            foreach (DataRow row in dataTable.Rows)
            {
                var function = new SchemaFunction
                {
                    Name = row["FunctionName"].ToString()!,
                    Schema = row["SchemaName"].ToString()!,
                    Definition = row["FunctionDefinition"]?.ToString() ?? string.Empty,
                    ReturnType = row["ReturnType"]?.ToString() ?? "sql_variant",
                    Parameters = new List<SchemaParameter>(),
                    Metadata = new Dictionary<string, object>()
                };

                schema.Functions.Add(function);
            }
        }

        private async Task<DataTable> ExecuteQueryWithParametersAsync(string sql, SqlSchemaConfiguration config, Dictionary<string, object> parameters)
        {
            using var connection = await CreateConnectionAsync(config);
            await connection.OpenAsync();

            using var command = new SqlCommand(sql, (SqlConnection)connection);
            command.CommandTimeout = config.Database.CommandTimeoutSeconds;

            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }

            var dataTable = new DataTable();
            using var reader = await command.ExecuteReaderAsync();
            dataTable.Load(reader);

            return dataTable;
        }

        private string FormatSqlServerDataType(DataRow row)
        {
            var dataType = row["DataType"].ToString()!;
            var maxLength = row["max_length"] != DBNull.Value ? Convert.ToInt32(row["max_length"]) : 0;
            var precision = row["precision"] != DBNull.Value ? Convert.ToInt32(row["precision"]) : 0;
            var scale = row["scale"] != DBNull.Value ? Convert.ToInt32(row["scale"]) : 0;

            return dataType.ToUpperInvariant() switch
            {
                "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" => maxLength == -1 ? $"{dataType}(MAX)" : $"{dataType}({maxLength})",
                "DECIMAL" or "NUMERIC" => $"{dataType}({precision},{scale})",
                "FLOAT" => precision > 0 ? $"{dataType}({precision})" : dataType,
                _ => dataType
            };
        }

        private string MapSqlServerConstraintType(string type)
        {
            return type switch
            {
                "PK" => "PK",
                "UQ" => "UQ",
                "F" => "FK",
                "C" => "CK",
                _ => type
            };
        }
    }

    // PostgreSQL Provider (simplified implementation)
    public interface IPostgreSqlProviderService : IDatabaseProvider { }

    public class PostgreSqlProviderService : BaseDatabaseProvider, IPostgreSqlProviderService
    {
        public PostgreSqlProviderService(ILogger<PostgreSqlProviderService> logger) : base(logger) { }

        public override async Task<DatabaseSchema> GetCurrentSchemaAsync(SqlSchemaConfiguration config)
        {
            return new DatabaseSchema
            {
                DatabaseName = config.Database.DatabaseName,
                Provider = "postgresql",
                AnalysisTime = DateTime.UtcNow,
                Tables = new List<SchemaTable>(),
                Views = new List<SchemaView>(),
                Indexes = new List<SchemaIndex>(),
                Constraints = new List<SchemaConstraint>(),
                Procedures = new List<SchemaProcedure>(),
                Functions = new List<SchemaFunction>(),
                Metadata = new Dictionary<string, object>()
            };
        }

        public override async Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config)
        {
            var connectionString = BuildConnectionString(config);
            return await CreateConnectionWithRetryAsync(connectionString, cs => new NpgsqlConnection(cs), config);
        }

        public override string BuildConnectionString(SqlSchemaConfiguration config)
        {
            return config.Database.BuildConnectionString();
        }
    }

    // MySQL Provider (simplified implementation)
    public interface IMySqlProviderService : IDatabaseProvider { }

    public class MySqlProviderService : BaseDatabaseProvider, IMySqlProviderService
    {
        public MySqlProviderService(ILogger<MySqlProviderService> logger) : base(logger) { }

        public override async Task<DatabaseSchema> GetCurrentSchemaAsync(SqlSchemaConfiguration config)
        {
            return new DatabaseSchema
            {
                DatabaseName = config.Database.DatabaseName,
                Provider = "mysql",
                AnalysisTime = DateTime.UtcNow,
                Tables = new List<SchemaTable>(),
                Views = new List<SchemaView>(),
                Indexes = new List<SchemaIndex>(),
                Constraints = new List<SchemaConstraint>(),
                Procedures = new List<SchemaProcedure>(),
                Functions = new List<SchemaFunction>(),
                Metadata = new Dictionary<string, object>()
            };
        }

        public override async Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config)
        {
            var connectionString = BuildConnectionString(config);
            return await CreateConnectionWithRetryAsync(connectionString, cs => new MySqlConnection(cs), config);
        }

        public override string BuildConnectionString(SqlSchemaConfiguration config)
        {
            return config.Database.BuildConnectionString();
        }
    }

    // Oracle Provider (simplified implementation)
    public interface IOracleProviderService : IDatabaseProvider { }

    public class OracleProviderService : BaseDatabaseProvider, IOracleProviderService
    {
        public OracleProviderService(ILogger<OracleProviderService> logger) : base(logger) { }

        public override async Task<DatabaseSchema> GetCurrentSchemaAsync(SqlSchemaConfiguration config)
        {
            return new DatabaseSchema
            {
                DatabaseName = config.Database.DatabaseName,
                Provider = "oracle",
                AnalysisTime = DateTime.UtcNow,
                Tables = new List<SchemaTable>(),
                Views = new List<SchemaView>(),
                Indexes = new List<SchemaIndex>(),
                Constraints = new List<SchemaConstraint>(),
                Procedures = new List<SchemaProcedure>(),
                Functions = new List<SchemaFunction>(),
                Metadata = new Dictionary<string, object>()
            };
        }

        public override async Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config)
        {
            var connectionString = BuildConnectionString(config);
            return await CreateConnectionWithRetryAsync(connectionString, cs => new OracleConnection(cs), config);
        }

        public override string BuildConnectionString(SqlSchemaConfiguration config)
        {
            return config.Database.BuildConnectionString();
        }
    }

    // SQLite Provider (simplified implementation)
    public interface ISqliteProviderService : IDatabaseProvider { }

    public class SqliteProviderService : BaseDatabaseProvider, ISqliteProviderService
    {
        public SqliteProviderService(ILogger<SqliteProviderService> logger) : base(logger) { }

        public override async Task<DatabaseSchema> GetCurrentSchemaAsync(SqlSchemaConfiguration config)
        {
            return new DatabaseSchema
            {
                DatabaseName = config.Database.DatabaseName,
                Provider = "sqlite",
                AnalysisTime = DateTime.UtcNow,
                Tables = new List<SchemaTable>(),
                Views = new List<SchemaView>(),
                Indexes = new List<SchemaIndex>(),
                Constraints = new List<SchemaConstraint>(),
                Procedures = new List<SchemaProcedure>(),
                Functions = new List<SchemaFunction>(),
                Metadata = new Dictionary<string, object>()
            };
        }

        public override async Task<DbConnection> CreateConnectionAsync(SqlSchemaConfiguration config)
        {
            var connectionString = BuildConnectionString(config);
            return await CreateConnectionWithRetryAsync(connectionString, cs => new SqliteConnection(cs), config);
        }

        public override string BuildConnectionString(SqlSchemaConfiguration config)
        {
            return config.Database.BuildConnectionString();
        }
    }
}