# SQL Schema Generator - C# Developer Integration Guide

## Documentation Access

This documentation is always available via HTTP endpoint on port 8080:

```bash
# Access C# documentation from running container
curl http://localhost:8080/docs/csharp

# Or in browser
http://localhost:8080/docs/csharp

# Download C# attributes file
curl http://localhost:8080/csharp > SqlAttributes.cs
```

## Overview

The SQL Schema Generator analyzes C# entities marked with tracking attributes and automatically generates database schema changes with intelligent deployment planning. It uses .NET reflection to discover entities, properties, and their SQL metadata attributes.

**Key Features:**
- **Reflection-based analysis** of compiled C# assemblies
- **Strongly-typed attributes** for SQL schema definition
- **Entity Framework compatibility** with standard data annotations
- **Custom SQL attributes** for advanced schema control
- **29-phase deployment planning** with comprehensive risk assessment
- **Multi-database provider support** (SQL Server, PostgreSQL, MySQL, Oracle, SQLite)

## Quick Start

### Download C# Attributes

```bash
# Download the strongly-typed attribute definitions
curl http://localhost:8080/csharp > SqlAttributes.cs

# Add to your project
dotnet add reference SqlAttributes.cs
```

### Basic Entity Definition

```csharp
using SqlSchemaAttributes;

[ExportToSQL]
public class User
{
    [SqlConstraints(SqlConstraint.PrimaryKey, SqlConstraint.Identity)]
    public int Id { get; set; }

    [SqlType(SqlDataType.NVarChar, 100)]
    [SqlConstraints(SqlConstraint.NotNull)]
    public string Name { get; set; } = string.Empty;

    [SqlType(SqlDataType.NVarChar, 255)]
    public string? Email { get; set; }

    [SqlForeignKeyTo<Department>()]
    public int DepartmentId { get; set; }
}

[ExportToSQL]
public class Department
{
    [SqlConstraints(SqlConstraint.PrimaryKey, SqlConstraint.Identity)]
    public int Id { get; set; }

    [SqlType(SqlDataType.NVarChar, 50)]
    [SqlConstraints(SqlConstraint.NotNull, SqlConstraint.Unique)]
    public string Name { get; set; } = string.Empty;
}
```

### Container Usage

```bash
# Build your C# project first
dotnet build --configuration Release

# Run SQL Schema Generator
docker run --rm \
  -p 8080:8080 \
  --volume $(pwd):/src \
  --env-file .env \
  myregistry.azurecr.io/sql-schema-generator:latest
```

## Complete Configuration Reference

All configuration is provided via environment variables:

### Required Configuration

| Variable | Description | Example |
|----------|-------------|---------|
| `LANGUAGE_CSHARP` | Enable C# analysis | `true` |
| `TRACK_ATTRIBUTE` | Attribute name to track for schema generation | `ExportToSQL` |
| `REPO_URL` | Repository URL for context | `https://github.com/company/project` |
| `BRANCH` | Git branch being processed | `main`, `develop` |
| `LICENSE_SERVER` | Licensing server URL | `https://license.company.com` |
| `DATABASE_SQLSERVER` | Target SQL Server (exactly one database provider required) | `true` |
| `DATABASE_SERVER` | Database server hostname | `sql.company.com` |
| `DATABASE_NAME` | Target database name | `MyApplication` |

### Optional Configuration

| Variable | Default | Description | Example |
|----------|---------|-------------|---------|
| `ASSEMBLY_PATHS` | Auto-discover | Colon-separated paths to compiled assemblies | `bin/Release:build/output` |
| `BUILD_OUTPUT_PATH` | `bin/Release` | Primary build output directory | `bin/Release/net9.0` |
| `DATABASE_SCHEMA` | Provider default | Target database schema | `dbo`, `application` |
| `MODE` | `validate` | Operation mode | `validate`, `execute` |
| `ENVIRONMENT` | `dev` | Deployment environment | `dev`, `beta`, `prod` |
| `VERTICAL` | (empty) | Business vertical for beta environments | `Photography`, `Navigation` |
| `VALIDATE_ONLY` | `false` | Validation-only mode | `true`, `false` |
| `GENERATE_INDEXES` | `true` | Auto-generate performance indexes | `false` |
| `GENERATE_FK_INDEXES` | `true` | Auto-generate foreign key indexes | `false` |
| `BACKUP_BEFORE_DEPLOYMENT` | `false` | Create backup before changes | `true` |

### Authentication Configuration

| Variable | Description | Example |
|----------|-------------|---------|
| `DATABASE_USERNAME` | Database username (if not using integrated auth) | `sa`, `app_user` |
| `DATABASE_PASSWORD` | Database password | `SecurePassword123` |
| `DATABASE_USE_INTEGRATED_AUTH` | Use Windows integrated authentication | `true`, `false` |
| `PAT_TOKEN` | Personal Access Token for git operations | `ghp_xxxxxxxxxxxx` |

## C# Attribute Reference

### Class-Level Attributes

#### `[ExportToSQL]`
Marks a class for database schema generation.

```csharp
[ExportToSQL]
public class Customer
{
    // Properties...
}

[ExportToSQL("Core business entity")]
public class Order
{
    // Properties...
}
```

#### `[SqlTable]`
Overrides the default table name.

```csharp
[ExportToSQL]
[SqlTable("tbl_customers")]
public class Customer
{
    // Properties...
}
```

#### `[SqlSchema]`
Overrides the default schema name.

```csharp
[ExportToSQL]
[SqlSchema("sales")]
public class Customer
{
    // Properties...
}
```

### Property-Level Attributes

#### `[SqlType]`
Specifies the SQL data type for a property.

```csharp
[SqlType(SqlDataType.NVarChar, 100)]
public string Name { get; set; }

[SqlType(SqlDataType.Decimal, 10, 2)]  // DECIMAL(10,2)
public decimal Price { get; set; }

[SqlType(SqlDataType.DateTime2)]
public DateTime CreatedAt { get; set; }
```

**Available SQL Data Types:**
- **String**: `NVarChar`, `VarChar`, `NVarCharMax`, `VarCharMax`, `Text`, `NText`
- **Numeric**: `Int`, `BigInt`, `SmallInt`, `TinyInt`, `Decimal`, `Float`, `Real`, `Money`, `SmallMoney`
- **Date/Time**: `DateTime2`, `DateTime`, `Date`, `Time`, `SmallDateTime`, `DateTimeOffset`
- **Other**: `Bit`, `UniqueIdentifier`, `Binary`, `VarBinary`, `VarBinaryMax`, `Image`, `Xml`, `Geography`, `Geometry`, `Timestamp`

#### `[SqlConstraints]`
Applies SQL constraints to a property.

```csharp
[SqlConstraints(SqlConstraint.NotNull)]
public string Name { get; set; }

[SqlConstraints(SqlConstraint.PrimaryKey, SqlConstraint.Identity)]
public int Id { get; set; }

[SqlConstraints(SqlConstraint.Unique)]
public string Email { get; set; }
```

**Available Constraints:**
- `NotNull` - NOT NULL constraint
- `Unique` - UNIQUE constraint
- `PrimaryKey` - PRIMARY KEY constraint
- `Identity` - IDENTITY column (auto-increment)
- `Check` - CHECK constraint (use with custom expression)

#### `[SqlForeignKeyTo<T>]`
Creates a strongly-typed foreign key relationship.

```csharp
// Basic foreign key to Id column
[SqlForeignKeyTo<Department>()]
public int DepartmentId { get; set; }

// Foreign key to specific column
[SqlForeignKeyTo<User>(u => u.UserId)]
public int CreatedByUserId { get; set; }

// Foreign key with cascade actions
[SqlForeignKeyTo<Category>()]
public int CategoryId { get; set; }
```

**Cascade Actions:**
```csharp
public class OrderItem
{
    [SqlForeignKeyTo<Order>()]
    public int OrderId { get; set; }
    
    // Configure cascade behavior in the foreign key attribute
    // OnDelete and OnUpdate actions are inferred from the relationship
}
```

#### `[SqlColumn]`
Overrides the default column name.

```csharp
[SqlColumn("customer_name")]
public string Name { get; set; }

[SqlColumn("email_address")]
public string Email { get; set; }
```

#### `[SqlIgnore]`
Excludes a property from schema generation.

```csharp
[SqlIgnore]
public string InternalCalculation { get; set; }

[SqlIgnore("Cached value, not persisted")]
public decimal CachedTotal { get; set; }
```

### Index Attributes

#### `[SqlIndex]`
Creates an index on a property.

```csharp
[SqlIndex]
public string Email { get; set; }

[SqlIndex(SqlIndexType.Unique)]
public string Username { get; set; }

[SqlIndex(SqlIndexType.Clustered)]
public DateTime CreatedAt { get; set; }
```

## Entity Framework Compatibility

The SQL Schema Generator is compatible with standard Entity Framework data annotations:

```csharp
[ExportToSQL]
public class Product
{
    [Key]  // Recognized as primary key
    public int Id { get; set; }

    [Required]  // Recognized as NOT NULL
    [MaxLength(100)]  // Recognized as length constraint
    public string Name { get; set; }

    [Column("product_code")]  // Recognized as custom column name
    public string Code { get; set; }

    [ForeignKey("Category")]  // Recognized as foreign key
    public int CategoryId { get; set; }
    
    public Category Category { get; set; }  // Navigation property
}
```

## Advanced Examples

### Complex Entity with Relationships

```csharp
[ExportToSQL]
[SqlTable("orders")]
[SqlSchema("sales")]
public class Order
{
    [SqlConstraints(SqlConstraint.PrimaryKey, SqlConstraint.Identity)]
    public int Id { get; set; }

    [SqlType(SqlDataType.NVarChar, 20)]
    [SqlConstraints(SqlConstraint.NotNull, SqlConstraint.Unique)]
    [SqlIndex(SqlIndexType.NonClustered)]
    public string OrderNumber { get; set; } = string.Empty;

    [SqlForeignKeyTo<Customer>()]
    [SqlIndex]  // Performance index on foreign key
    public int CustomerId { get; set; }

    [SqlType(SqlDataType.DateTime2)]
    [SqlConstraints(SqlConstraint.NotNull)]
    [SqlDefault(SqlDefaultValue.GetUtcDate)]
    public DateTime CreatedAt { get; set; }

    [SqlType(SqlDataType.Decimal, 10, 2)]
    [SqlConstraints(SqlConstraint.NotNull)]
    public decimal TotalAmount { get; set; }

    [SqlType(SqlDataType.NVarChar, 20)]
    [SqlConstraints(SqlConstraint.NotNull)]
    public string Status { get; set; } = "Pending";

    // Navigation properties (ignored in schema generation)
    public Customer Customer { get; set; }
    public List<OrderItem> OrderItems { get; set; } = new();
}

[ExportToSQL]
public class OrderItem
{
    [SqlConstraints(SqlConstraint.PrimaryKey, SqlConstraint.Identity)]
    public int Id { get; set; }

    [SqlForeignKeyTo<Order>()]
    public int OrderId { get; set; }

    [SqlForeignKeyTo<Product>()]
    public int ProductId { get; set; }

    [SqlType(SqlDataType.Int)]
    [SqlConstraints(SqlConstraint.NotNull)]
    public int Quantity { get; set; }

    [SqlType(SqlDataType.Decimal, 10, 2)]
    [SqlConstraints(SqlConstraint.NotNull)]
    public decimal UnitPrice { get; set; }
}
```

### Custom Tracking Attribute

```csharp
// If using a custom tracking attribute name
// Set TRACK_ATTRIBUTE=DatabaseEntity

[DatabaseEntity]
public class Customer
{
    [SqlConstraints(SqlConstraint.PrimaryKey, SqlConstraint.Identity)]
    public int Id { get; set; }
    
    // Properties...
}
```

## Pipeline Integration

### Azure DevOps

```yaml
variables:
  LANGUAGE_CSHARP: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  DATABASE_SQLSERVER: true
  DATABASE_SERVER: $(DatabaseServer)
  DATABASE_NAME: $(DatabaseName)
  ENVIRONMENT: prod
  MODE: execute
  ASSEMBLY_PATHS: "$(Build.ArtifactStagingDirectory)"

resources:
  containers:
  - container: schema_generator
    image: myregistry.azurecr.io/sql-schema-generator:1.0.0
    options: --volume $(Build.SourcesDirectory):/src

jobs:
- job: deploy_schema
  container: schema_generator
  steps:
  - task: DotNetCoreCLI@2
    displayName: 'Build Application'
    inputs:
      command: 'build'
      configuration: 'Release'
  
  - script: /app/sql-schema-generator
    displayName: 'Deploy Database Schema'
```

### GitHub Actions

```yaml
env:
  LANGUAGE_CSHARP: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  DATABASE_SQLSERVER: true
  DATABASE_SERVER: ${{ secrets.DATABASE_SERVER }}
  DATABASE_NAME: MyApplication
  ENVIRONMENT: prod
  MODE: execute

jobs:
  deploy-schema:
    runs-on: ubuntu-latest
    container:
      image: myregistry.azurecr.io/sql-schema-generator:1.0.0
      options: --volume ${{ github.workspace }}:/src
    steps:
      - name: Build Application
        run: dotnet build --configuration Release
      
      - name: Deploy Database Schema
        run: /app/sql-schema-generator
```

## Output Files

The tool generates several output files in your mounted volume:

### pipeline-tools.log
```
sql-schema-generator=1.0.0
```

### schema-analysis.json
```json
{
  "tool_name": "sql-schema-generator",
  "language": "csharp",
  "entities_discovered": 15,
  "tables_generated": 15,
  "constraints_generated": 23,
  "indexes_generated": 8,
  "deployment_risk_level": "Safe"
}
```

### compiled-deployment.sql
```sql
-- Generated SQL deployment script
CREATE TABLE [dbo].[Users] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(255) NULL,
    [DepartmentId] INT NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_Users_DepartmentId] ON [dbo].[Users] ([DepartmentId]);

ALTER TABLE [dbo].[Users] 
ADD CONSTRAINT [FK_Users_Departments_DepartmentId] 
FOREIGN KEY ([DepartmentId]) REFERENCES [dbo].[Departments] ([Id]);
```

## Best Practices

### Entity Design

1. **Always use the tracking attribute** on entities you want in the database
2. **Explicitly define SQL types** for important properties to avoid defaults
3. **Use foreign key attributes** for referential integrity
4. **Add indexes** on frequently queried columns

### Assembly Management

1. **Build in Release mode** for production deployments
2. **Include all dependent assemblies** in the ASSEMBLY_PATHS
3. **Ensure assemblies are accessible** to the container

### Schema Organization

1. **Use schemas** to organize related tables
2. **Follow consistent naming conventions** across entities
3. **Document complex relationships** in code comments

### Deployment Safety

1. **Always validate first** with `MODE=validate` before `MODE=execute`
2. **Enable backups** for production deployments
3. **Review risk assessment** output before proceeding
4. **Test in staging** environments first

## Troubleshooting

### Common Issues

**No Entities Discovered:**
```
[ERROR] No entities found with attribute: ExportToSQL
```
*Solution: Ensure entities are marked with `[ExportToSQL]` and assemblies are built*

**Assembly Load Failure:**
```
[ERROR] Failed to load assembly: MyApp.dll
```
*Solution: Verify ASSEMBLY_PATHS points to compiled assemblies and dependencies*

**Database Connection Failed:**
```
[ERROR] Failed to connect to database: sql.company.com
```
*Solution: Check DATABASE_SERVER, credentials, and network connectivity*

**Missing Tracking Attribute:**
```
[ERROR] No entities found with attribute: MyCustomAttribute
```
*Solution: Verify TRACK_ATTRIBUTE matches your attribute name exactly*

### Debug Mode

Enable comprehensive logging:
```bash
VERBOSE=true
LOG_LEVEL=DEBUG
SCHEMA_DUMP=true
```

This outputs:
- Assembly loading details
- Entity discovery process
- Reflection analysis results
- SQL generation steps

## Integration Examples

### Multi-Project Solution

```bash
# Build entire solution
dotnet build --configuration Release

# Set assembly paths to include all projects
ASSEMBLY_PATHS="ProjectA/bin/Release:ProjectB/bin/Release:Shared/bin/Release"

# Run schema generator
docker run --env ASSEMBLY_PATHS="$ASSEMBLY_PATHS" ...
```

### Custom Attribute Names

```csharp
// Use custom tracking attribute
[MyCustomEntity]
public class Customer { }

// Configure environment
TRACK_ATTRIBUTE=MyCustomEntity
```

### Entity Framework Integration

```csharp
public class ApplicationDbContext : DbContext
{
    // EF DbSets for runtime
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Order> Orders { get; set; }
    
    // SQL Schema Generator will discover entities via reflection
    // No additional configuration needed
}
```

## Support

For issues and questions:
- Enable debug logging (`VERBOSE=true`)
- Verify all required environment variables are set
- Ensure assemblies are built and accessible
- Check the generated `schema-analysis.json` for discovery details
- Access documentation at `http://container:8080/docs/csharp`
- Download attributes at `http://container:8080/csharp`