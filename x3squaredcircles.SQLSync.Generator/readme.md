# SQL Schema Generator

[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![License](https://img.shields.io/badge/License-Proprietary-red.svg)](LICENSE)
[![Container](https://img.shields.io/badge/Container-Ready-blue.svg)](Dockerfile)

Automated database schema deployment tool with multi-language entity discovery, comprehensive risk assessment, and 29-phase deployment planning.

## Overview

The SQL Schema Generator analyzes code entities marked with tracking attributes and automatically generates database schema changes with intelligent deployment planning. It supports multiple programming languages, database providers, and provides enterprise-grade features including license management, risk assessment, and approval workflows.

## Key Features

### 🔍 **Multi-Language Entity Discovery**
- **C#**: Full reflection-based analysis with attribute detection
- **Java, Python, JavaScript, TypeScript, Go**: Extensible framework (implementations pending)
- Automatic property-to-column mapping with data type inference
- Relationship discovery and foreign key generation

### 🗄️ **Universal Database Support**
- **SQL Server**: Full T-SQL support with integrated authentication
- **PostgreSQL**: Advanced features including custom types and extensions
- **MySQL**: InnoDB optimizations and character set handling
- **Oracle**: Enterprise features including tablespaces and partitioning
- **SQLite**: Embedded database support with WAL mode

### 📋 **29-Phase Deployment Planning**
Intelligent deployment orchestration with dependency-aware execution:

1. **Pre-deployment Validation** - Environment and prerequisite checks
2. **Database Backup** - Automated backup creation and verification
3-5. **Drop Dependent Objects** - Views, procedures, functions removal
6-8. **Drop Constraints** - Foreign keys, checks, unique constraints
9-12. **Drop Indexes** - Non-clustered and clustered index removal
13. **Drop Columns** - Column removal with data loss warnings
14. **Drop Tables** - Table removal with dependency validation
15-16. **Create Tables/Columns** - New table and column creation
17-19. **Alter Columns** - Data type, nullability, and default changes
20-22. **Create Constraints** - Primary keys, unique, and check constraints
23-25. **Create Indexes** - Clustered, unique, and performance indexes
26. **Create Foreign Keys** - Referential integrity establishment
27-28. **Create Dependent Objects** - Views, procedures, and functions
29. **Post-deployment Validation** - Success verification and cleanup

### ⚖️ **Risk Assessment & Approval Workflows**
- **Safe Operations** (Exit Code 0): Automatic deployment approved
- **Warning Operations** (Exit Code 1): Single approver required
- **Risky Operations** (Exit Code 2): Dual approval required
- Real-time risk factor analysis with mitigation recommendations

### 🏢 **Enterprise Integration**
- **License Management**: Concurrent usage control with burst mode fallback
- **Key Vault Integration**: Azure, AWS, and HashiCorp Vault support
- **CI/CD Pipeline Ready**: Azure DevOps, GitHub Actions, Jenkins compatible
- **Git Integration**: Automatic tagging with customizable patterns

## Quick Start

### Container Usage (Recommended)

```bash
docker run \
  --volume $(pwd):/src \
  --env LANGUAGE_CSHARP=true \
  --env DATABASE_SQLSERVER=true \
  --env TRACK_ATTRIBUTE=ExportToSQL \
  --env LICENSE_SERVER=https://license.company.com \
  --env DATABASE_SERVER=sql.company.com \
  --env DATABASE_NAME=MyDatabase \
  --env ENVIRONMENT=dev \
  --env MODE=validate \
  myregistry.azurecr.io/sql-schema-generator:1.0.0
```

### Azure DevOps Pipeline

```yaml
variables:
  LANGUAGE_CSHARP: true
  DATABASE_SQLSERVER: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  DATABASE_SERVER: $(DatabaseServer)
  DATABASE_NAME: $(DatabaseName)
  ENVIRONMENT: prod
  VERTICAL: Photography
  MODE: execute

resources:
  containers:
  - container: schema_generator
    image: myregistry.azurecr.io/sql-schema-generator:1.0.0
    options: --volume $(Build.SourcesDirectory):/src

jobs:
- job: deploy_schema
  container: schema_generator
  steps:
  - script: /app/sql-schema-generator
    displayName: 'Deploy Database Schema'
```

### GitHub Actions

```yaml
env:
  LANGUAGE_JAVA: true
  DATABASE_POSTGRESQL: true
  TRACK_ATTRIBUTE: Entity
  LICENSE_SERVER: https://license.company.com
  DATABASE_SERVER: postgres.company.com
  DATABASE_NAME: location_db
  ENVIRONMENT: beta
  VERTICAL: Navigation
  MODE: execute

jobs:
  deploy-schema:
    runs-on: ubuntu-latest
    container:
      image: myregistry.azurecr.io/sql-schema-generator:1.0.0
      options: --volume ${{ github.workspace }}:/src
    steps:
      - name: Deploy Database Schema
        run: /app/sql-schema-generator
```

## Configuration

All configuration is provided via environment variables. See [Configuration Documentation](docs/configuration.md) for complete details.

### Required Configuration

```bash
# Language Selection (exactly one)
LANGUAGE_CSHARP=true

# Database Provider (exactly one)
DATABASE_SQLSERVER=true

# Core Settings
TRACK_ATTRIBUTE=ExportToSQL
REPO_URL=https://github.com/company/project
BRANCH=main
LICENSE_SERVER=https://license.company.com

# Database Connection
DATABASE_SERVER=sql.company.com
DATABASE_NAME=MyDatabase
DATABASE_USERNAME=sa
DATABASE_PASSWORD=SecurePassword

# Environment
ENVIRONMENT=dev
MODE=validate
```

### Optional Enhancements

```bash
# Key Vault Integration
VAULT_TYPE=azure
VAULT_URL=https://myvault.vault.azure.net
DATABASE_PASSWORD_VAULT_KEY=sql-password

# Custom Tag Template
TAG_TEMPLATE="{vertical}-{environment}-schema-v{version}"

# Advanced Options
ENABLE_29_PHASE_DEPLOYMENT=true
GENERATE_FK_INDEXES=true
BACKUP_BEFORE_DEPLOYMENT=true
VALIDATION_LEVEL=strict
```

## Entity Marking Examples

### C# Entities
```csharp
[ExportToSQL]
public class User
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; }
    
    [Column("email_address")]
    public string Email { get; set; }
    
    // Navigation properties
    public List<Order> Orders { get; set; }
}

[ExportToSQL]
public class Order
{
    [Key]
    public int Id { get; set; }
    
    [ForeignKey("User")]
    public int UserId { get; set; }
    
    public User User { get; set; }
}
```

### Java Entities (Planned)
```java
@Entity
@ExportToSQL
public class Product {
    @Id
    @GeneratedValue
    private Long id;
    
    @Column(nullable = false, length = 255)
    private String name;
    
    @ManyToOne
    @JoinColumn(name = "category_id")
    private Category category;
}
```

## Output Files

The tool generates comprehensive outputs for CI/CD integration:

| File | Purpose |
|------|---------|
| `schema-analysis.json` | Complete entity discovery and analysis results |
| `deployment-plan.json` | 29-phase deployment plan with risk assessment |
| `validation-report.json` | Schema validation results and recommendations |
| `compiled-deployment.sql` | Executable SQL deployment script |
| `rollback-script.sql` | Emergency rollback procedures |
| `tag-patterns.json` | Git tag patterns for downstream tools |
| `approval-request.json` | Approval workflow data for risky deployments |
| `DEPLOYMENT_SUMMARY.md` | Human-readable change summary |
| `pipeline-tools.log` | CI/CD tool execution tracking |

## Exit Codes & Risk Levels

| Exit Code | Risk Level | Description | Action Required |
|-----------|------------|-------------|-----------------|
| 0 | **Safe** | Low-risk operations | ✅ Auto-deploy approved |
| 1 | **Warning** | Moderate-risk operations | ⚠️ Single approver required |
| 2 | **Risky** | High-risk operations | 🚨 Dual approval required |
| 3 | **Error** | License unavailable | 🔒 License server issue |
| 4-11 | **Error** | Various failure modes | 🛠️ Technical issue resolution |

## License Management

### Normal Operation
- Acquires concurrent license from license server
- Automatic heartbeat maintenance during execution
- Graceful license release on completion

### Burst Mode Fallback
- Automatic activation when licenses unavailable
- Limited monthly usage allowance
- Full analysis capabilities, no deployment restrictions

### No-Operation Mode
- Activates when license server unreachable
- Analysis-only mode with comprehensive reporting
- No database changes or git operations performed

## Multi-Database Deployments

Deploy to multiple databases by running separate executions:

```bash
# Production SQL Server
docker run --env DATABASE_SQLSERVER=true --env DATABASE_SERVER=sql-prod.company.com ...

# Staging PostgreSQL  
docker run --env DATABASE_POSTGRESQL=true --env DATABASE_SERVER=postgres-staging.company.com ...

# Development MySQL
docker run --env DATABASE_MYSQL=true --env DATABASE_SERVER=mysql-dev.company.com ...
```

**Benefits:**
- Isolated failure domains
- Provider-specific optimizations
- Sequential license usage
- Independent rollback strategies

## Security Features

### Authentication Methods
- **Windows Integrated Authentication** - For SQL Server
- **Username/Password** - Direct credentials
- **Key Vault Integration** - Secure credential storage
- **Pipeline Tokens** - Automatic CI/CD authentication

### Data Protection
- Sensitive values masked in logs
- Secure credential transmission
- Encrypted key vault communication
- Audit trail maintenance

## Troubleshooting

### Common Issues

**No Entities Discovered**
```bash
[ERROR] No entities found with attribute: ExportToSQL
```
- Verify entities are marked with tracking attribute
- Ensure assemblies are built and accessible
- Check ASSEMBLY_PATHS configuration

**Database Connection Failed**
```bash
[ERROR] Failed to connect to database: sql.company.com
```
- Verify DATABASE_SERVER and credentials
- Check network connectivity and firewall rules
- Validate key vault secret resolution

**License Server Unavailable**
```bash
[ERROR] Failed to connect to license server
```
- Tool automatically enters NOOP mode
- Analysis completed but no changes applied
- Check LICENSE_SERVER URL and network access

### Debug Mode

Enable detailed logging:
```bash
VERBOSE=true
LOG_LEVEL=DEBUG
SCHEMA_DUMP=true
```

## Development

### Building from Source

```bash
git clone https://github.com/company/sql-schema-generator
cd sql-schema-generator
dotnet restore
dotnet build --configuration Release
dotnet publish --configuration Release --runtime linux-x64 --self-contained
```

### Running Tests

```bash
dotnet test --configuration Release --logger "trx;LogFileName=TestResults.xml"
```

### Container Build

```bash
docker build -t sql-schema-generator:1.0.0 .
docker tag sql-schema-generator:1.0.0 myregistry.azurecr.io/sql-schema-generator:1.0.0
docker push myregistry.azurecr.io/sql-schema-generator:1.0.0
```

## Architecture

### Core Components
- **SqlSchemaOrchestrator** - Main execution workflow
- **EntityDiscoveryService** - Multi-language code analysis
- **SchemaAnalysisService** - Database introspection and target generation
- **RiskAssessmentService** - Comprehensive risk evaluation
- **DeploymentPlanService** - 29-phase planning with dependencies
- **DeploymentExecutionService** - Controlled deployment execution

### Language Analyzers
- **CSharpAnalyzerService** - Reflection-based entity discovery
- **JavaAnalyzerService** - Annotation processing framework
- **PythonAnalyzerService** - AST-based entity extraction
- **Others** - Extensible architecture for additional languages

### Database Providers
- **SqlServerProviderService** - T-SQL with integrated auth
- **PostgreSqlProviderService** - Advanced Postgre