# SQL Schema Generator - Go Developer Integration Guide

## Documentation Access

This documentation is always available via HTTP endpoint on port 8080:

```bash
# Access Go documentation from running container
curl http://localhost:8080/docs/go

# Or in browser
http://localhost:8080/docs/go

# Download Go struct tags reference
curl http://localhost:8080/go > sql_tags.go
```

## Overview

The SQL Schema Generator analyzes Go structs marked with tracking struct tags and automatically generates database schema changes with intelligent deployment planning. It uses Go reflection to discover structs, fields, and their SQL metadata tags.

**Key Features:**
- **Reflection-based analysis** of compiled Go binaries
- **Struct tag-based** SQL schema definition
- **GORM compatibility** with standard struct tags
- **Custom SQL struct tags** for advanced schema control
- **29-phase deployment planning** with comprehensive risk assessment
- **Multi-database provider support** (SQL Server, PostgreSQL, MySQL, Oracle, SQLite)

## Quick Start

### Download Go Tags Reference

```bash
# Download the struct tag definitions and helper functions
curl http://localhost:8080/go > sql_tags.go

# Add to your project
cp sql_tags.go internal/schema/
```

### Basic Struct Definition

```go
package models

import (
    "time"
    "github.com/google/uuid"
)

// User represents a user entity
type User struct {
    ID           int       `sql_schema:"export" gorm:"primaryKey;autoIncrement"`
    Name         string    `sql_schema:"type:nvarchar(100);not_null"`
    Email        *string   `sql_schema:"type:nvarchar(255);unique"`
    DepartmentID int       `sql_schema:"foreign_key:departments(id);not_null"`
    CreatedAt    time.Time `sql_schema:"type:datetime2;default:getutcdate()"`
    UpdatedAt    time.Time `sql_schema:"type:datetime2;default:getutcdate()"`
}

// Department represents a department entity
type Department struct {
    ID   int    `sql_schema:"export" gorm:"primaryKey;autoIncrement"`
    Name string `sql_schema:"type:nvarchar(50);not_null;unique"`
    Code string `sql_schema:"type:varchar(10);not_null;unique"`
}

// Order represents an order entity with complex relationships
type Order struct {
    ID          uuid.UUID  `sql_schema:"export" gorm:"type:uuid;primaryKey"`
    OrderNumber string     `sql_schema:"type:nvarchar(20);not_null;unique;index"`
    CustomerID  int        `sql_schema:"foreign_key:customers(id);not_null;index"`
    TotalAmount float64    `sql_schema:"type:decimal(10,2);not_null"`
    Status      string     `sql_schema:"type:nvarchar(20);not_null;default:'Pending'"`
    CreatedAt   time.Time  `sql_schema:"type:datetime2;not_null;default:getutcdate()"`
    
    // Navigation properties (ignored in schema generation)
    Customer *Customer `sql_schema:"ignore" gorm:"foreignKey:CustomerID"`
    Items    []OrderItem `sql_schema:"ignore" gorm:"foreignKey:OrderID"`
}
```

### Container Usage

```bash
# Build your Go project first
go build -o app ./cmd/main.go

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
| `LANGUAGE_GO` | Enable Go analysis | `true` |
| `TRACK_ATTRIBUTE` | Struct tag name to track for schema generation | `sql_schema` |
| `REPO_URL` | Repository URL for context | `https://github.com/company/project` |
| `BRANCH` | Git branch being processed | `main`, `develop` |
| `LICENSE_SERVER` | Licensing server URL | `https://license.company.com` |
| `DATABASE_SQLSERVER` | Target SQL Server (exactly one database provider required) | `true` |
| `DATABASE_SERVER` | Database server hostname | `sql.company.com` |
| `DATABASE_NAME` | Target database name | `MyApplication` |

### Optional Configuration

| Variable | Default | Description | Example |
|----------|---------|-------------|---------|
| `BINARY_PATHS` | Auto-discover | Colon-separated paths to compiled Go binaries | `bin:build/output` |
| `BUILD_OUTPUT_PATH` | `bin` | Primary build output directory | `build/linux-amd64` |
| `GO_MODULE_PATH` | Auto-detect | Go module path for reflection | `github.com/company/myapp` |
| `DATABASE_SCHEMA` | Provider default | Target database schema | `dbo`, `public` |
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

## Go Struct Tag Reference

### Struct-Level Tags

#### `sql_schema:"export"`
Marks a struct for database schema generation.

```go
type Customer struct {
    ID   int    `sql_schema:"export"`
    Name string `sql_schema:"type:nvarchar(100)"`
}

// With description
type Order struct {
    ID int `sql_schema:"export;description:Core business entity"`
    // Fields...
}
```

#### `sql_schema:"table:name"`
Overrides the default table name.

```go
type Customer struct {
    ID   int    `sql_schema:"export;table:tbl_customers"`
    Name string `sql_schema:"type:nvarchar(100)"`
}
```

#### `sql_schema:"schema:name"`
Overrides the default schema name.

```go
type Customer struct {
    ID   int    `sql_schema:"export;schema:sales"`
    Name string `sql_schema:"type:nvarchar(100)"`
}
```

### Field-Level Tags

#### `sql_schema:"type:datatype"`
Specifies the SQL data type for a field.

```go
type Product struct {
    Name        string    `sql_schema:"type:nvarchar(100)"`
    Price       float64   `sql_schema:"type:decimal(10,2)"`
    Description string    `sql_schema:"type:text"`
    CreatedAt   time.Time `sql_schema:"type:datetime2"`
    IsActive    bool      `sql_schema:"type:bit"`
}
```

**Available SQL Data Types:**
- **String**: `nvarchar(n)`, `varchar(n)`, `nvarchar(max)`, `varchar(max)`, `text`, `ntext`
- **Numeric**: `int`, `bigint`, `smallint`, `tinyint`, `decimal(p,s)`, `float`, `real`, `money`, `smallmoney`
- **Date/Time**: `datetime2`, `datetime`, `date`, `time`, `smalldatetime`, `datetimeoffset`
- **Other**: `bit`, `uniqueidentifier`, `binary`, `varbinary`, `varbinary(max)`, `image`, `xml`, `geography`, `geometry`, `timestamp`

#### `sql_schema:"constraints"`
Applies SQL constraints to a field.

```go
type User struct {
    ID       int    `sql_schema:"primary_key;identity"`
    Name     string `sql_schema:"type:nvarchar(100);not_null"`
    Email    string `sql_schema:"type:nvarchar(255);unique;not_null"`
    Username string `sql_schema:"type:nvarchar(50);unique;not_null"`
}
```

**Available Constraints:**
- `not_null` - NOT NULL constraint
- `unique` - UNIQUE constraint
- `primary_key` - PRIMARY KEY constraint
- `identity` - IDENTITY column (auto-increment)
- `check:expression` - CHECK constraint with custom expression

#### `sql_schema:"foreign_key:table(column)"`
Creates a foreign key relationship.

```go
type OrderItem struct {
    ID        int `sql_schema:"primary_key;identity"`
    OrderID   int `sql_schema:"foreign_key:orders(id);not_null"`
    ProductID int `sql_schema:"foreign_key:products(id);not_null"`
    Quantity  int `sql_schema:"type:int;not_null"`
}

// Foreign key with cascade actions
type AuditLog struct {
    ID     int `sql_schema:"primary_key;identity"`
    UserID int `sql_schema:"foreign_key:users(id);on_delete:cascade"`
}
```

**Cascade Actions:**
- `on_delete:cascade` - CASCADE on delete
- `on_delete:set_null` - SET NULL on delete
- `on_update:cascade` - CASCADE on update
- `on_update:restrict` - RESTRICT on update

#### `sql_schema:"column:name"`
Overrides the default column name.

```go
type Customer struct {
    Name  string `sql_schema:"column:customer_name;type:nvarchar(100)"`
    Email string `sql_schema:"column:email_address;type:nvarchar(255)"`
}
```

#### `sql_schema:"ignore"`
Excludes a field from schema generation.

```go
type User struct {
    ID              int    `sql_schema:"export"`
    Name            string `sql_schema:"type:nvarchar(100)"`
    CalculatedField string `sql_schema:"ignore"`
    
    // Navigation properties
    Orders []Order `sql_schema:"ignore"`
}
```

#### `sql_schema:"default:value"`
Sets a default value for the column.

```go
type Order struct {
    Status    string    `sql_schema:"type:nvarchar(20);default:'Pending'"`
    CreatedAt time.Time `sql_schema:"type:datetime2;default:getutcdate()"`
    IsActive  bool      `sql_schema:"type:bit;default:1"`
}
```

### Index Tags

#### `sql_schema:"index"`
Creates an index on a field.

```go
type User struct {
    Email     string    `sql_schema:"type:nvarchar(255);index"`
    Username  string    `sql_schema:"type:nvarchar(50);index:unique"`
    CreatedAt time.Time `sql_schema:"type:datetime2;index:clustered"`
}
```

**Index Types:**
- `index` - Non-clustered index
- `index:unique` - Unique index
- `index:clustered` - Clustered index

## GORM Compatibility

The SQL Schema Generator is compatible with standard GORM struct tags:

```go
type Product struct {
    ID          uint      `sql_schema:"export" gorm:"primaryKey"`
    Name        string    `gorm:"size:100;not null"`
    Code        string    `gorm:"uniqueIndex;size:50"`
    Price       float64   `gorm:"type:decimal(10,2);not null"`
    CategoryID  uint      `gorm:"not null"`
    Category    Category  `gorm:"foreignKey:CategoryID"`
    CreatedAt   time.Time `gorm:"autoCreateTime"`
    UpdatedAt   time.Time `gorm:"autoUpdateTime"`
}

type Category struct {
    ID       uint      `sql_schema:"export" gorm:"primaryKey"`
    Name     string    `gorm:"size:50;not null;uniqueIndex"`
    Products []Product `gorm:"foreignKey:CategoryID"`
}
```

## Advanced Examples

### Complex Entity with Relationships

```go
package models

import (
    "time"
    "github.com/google/uuid"
)

type Order struct {
    ID          uuid.UUID `sql_schema:"export;table:orders;schema:sales" gorm:"type:uuid;primaryKey"`
    OrderNumber string    `sql_schema:"type:nvarchar(20);not_null;unique;index:non_clustered"`
    CustomerID  int       `sql_schema:"foreign_key:customers(id);not_null;index"`
    TotalAmount float64   `sql_schema:"type:decimal(10,2);not_null"`
    Status      string    `sql_schema:"type:nvarchar(20);not_null;default:'Pending'"`
    CreatedAt   time.Time `sql_schema:"type:datetime2;not_null;default:getutcdate()"`
    UpdatedAt   time.Time `sql_schema:"type:datetime2;not_null;default:getutcdate()"`
    
    // Navigation properties (ignored in schema generation)
    Customer  *Customer   `sql_schema:"ignore" gorm:"foreignKey:CustomerID"`
    OrderItems []OrderItem `sql_schema:"ignore" gorm:"foreignKey:OrderID"`
}

type OrderItem struct {
    ID        int     `sql_schema:"export" gorm:"primaryKey;autoIncrement"`
    OrderID   uuid.UUID `sql_schema:"foreign_key:orders(id);not_null;on_delete:cascade"`
    ProductID int     `sql_schema:"foreign_key:products(id);not_null"`
    Quantity  int     `sql_schema:"type:int;not_null;check:quantity > 0"`
    UnitPrice float64 `sql_schema:"type:decimal(10,2);not_null"`
    
    // Navigation properties
    Order   *Order   `sql_schema:"ignore" gorm:"foreignKey:OrderID"`
    Product *Product `sql_schema:"ignore" gorm:"foreignKey:ProductID"`
}

type Customer struct {
    ID          int       `sql_schema:"export" gorm:"primaryKey;autoIncrement"`
    FirstName   string    `sql_schema:"type:nvarchar(50);not_null"`
    LastName    string    `sql_schema:"type:nvarchar(50);not_null"`
    Email       string    `sql_schema:"type:nvarchar(255);unique;not_null;index"`
    Phone       *string   `sql_schema:"type:nvarchar(20)"`
    Address     string    `sql_schema:"type:nvarchar(500)"`
    CreatedAt   time.Time `sql_schema:"type:datetime2;not_null;default:getutcdate()"`
    
    // Navigation properties
    Orders []Order `sql_schema:"ignore" gorm:"foreignKey:CustomerID"`
}
```

### Custom Tracking Tag

```go
// If using a custom tracking tag name
// Set TRACK_ATTRIBUTE=db_entity

type Customer struct {
    ID   int    `db_entity:"export"`
    Name string `db_entity:"type:nvarchar(100);not_null"`
}
```

### Embedded Structs

```go
type BaseModel struct {
    ID        int       `sql_schema:"primary_key;identity"`
    CreatedAt time.Time `sql_schema:"type:datetime2;not_null;default:getutcdate()"`
    UpdatedAt time.Time `sql_schema:"type:datetime2;not_null;default:getutcdate()"`
}

type User struct {
    BaseModel `sql_schema:"embed"`
    Name      string `sql_schema:"export;type:nvarchar(100);not_null"`
    Email     string `sql_schema:"type:nvarchar(255);unique;not_null"`
}
```

## Build Configuration

### Go Module Setup

```go
// go.mod
module github.com/company/myapp

go 1.21

require (
    github.com/google/uuid v1.3.0
    gorm.io/gorm v1.25.0
    gorm.io/driver/sqlserver v1.5.0
)
```

### Build Script

```bash
#!/bin/bash
# build.sh

# Set build environment
export CGO_ENABLED=0
export GOOS=linux
export GOARCH=amd64

# Build the application
go build -o bin/myapp -ldflags="-w -s" ./cmd/main.go

# Build with debug symbols for development
go build -o bin/myapp-debug ./cmd/main.go
```

### Multi-Architecture Build

```bash
# Build for multiple architectures
GOOS=linux GOARCH=amd64 go build -o bin/linux-amd64/myapp ./cmd/main.go
GOOS=windows GOARCH=amd64 go build -o bin/windows-amd64/myapp.exe ./cmd/main.go
GOOS=darwin GOARCH=amd64 go build -o bin/darwin-amd64/myapp ./cmd/main.go
```

## Pipeline Integration

### Azure DevOps

```yaml
variables:
  LANGUAGE_GO: true
  TRACK_ATTRIBUTE: sql_schema
  LICENSE_SERVER: https://license.company.com
  DATABASE_SQLSERVER: true
  DATABASE_SERVER: $(DatabaseServer)
  DATABASE_NAME: $(DatabaseName)
  ENVIRONMENT: prod
  MODE: execute
  BINARY_PATHS: "$(Build.ArtifactStagingDirectory)/bin"
  GO_MODULE_PATH: "github.com/company/myapp"

resources:
  containers:
  - container: schema_generator
    image: myregistry.azurecr.io/sql-schema-generator:1.0.0
    options: --volume $(Build.SourcesDirectory):/src

jobs:
- job: deploy_schema
  container: schema_generator
  steps:
  - task: Go@0
    displayName: 'Build Go Application'
    inputs:
      command: 'build'
      arguments: '-o $(Build.ArtifactStagingDirectory)/bin/myapp ./cmd/main.go'
  
  - script: /app/sql-schema-generator
    displayName: 'Deploy Database Schema'
```

### GitHub Actions

```yaml
env:
  LANGUAGE_GO: true
  TRACK_ATTRIBUTE: sql_schema
  LICENSE_SERVER: https://license.company.com
  DATABASE_SQLSERVER: true
  DATABASE_SERVER: ${{ secrets.DATABASE_SERVER }}
  DATABASE_NAME: MyApplication
  ENVIRONMENT: prod
  MODE: execute
  GO_MODULE_PATH: github.com/company/myapp

jobs:
  deploy-schema:
    runs-on: ubuntu-latest
    container:
      image: myregistry.azurecr.io/sql-schema-generator:1.0.0
      options: --volume ${{ github.workspace }}:/src
    steps:
      - name: Setup Go
        uses: actions/setup-go@v4
        with:
          go-version: '1.21'
      
      - name: Build Application
        run: go build -o bin/myapp ./cmd/main.go
      
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
  "language": "go",
  "module_path": "github.com/company/myapp",
  "structs_discovered": 15,
  "tables_generated": 15,
  "constraints_generated": 23,
  "indexes_generated": 8,
  "deployment_risk_level": "Safe"
}
```

### compiled-deployment.sql
```sql
-- Generated SQL deployment script
CREATE TABLE [dbo].[users] (
    [id] INT IDENTITY(1,1) NOT NULL,
    [name] NVARCHAR(100) NOT NULL,
    [email] NVARCHAR(255) NULL,
    [department_id] INT NOT NULL,
    CONSTRAINT [PK_users] PRIMARY KEY ([id])
);

CREATE INDEX [IX_users_department_id] ON [dbo].[users] ([department_id]);

ALTER TABLE [dbo].[users] 
ADD CONSTRAINT [FK_users_departments_department_id] 
FOREIGN KEY ([department_id]) REFERENCES [dbo].[departments] ([id]);
```

## Best Practices

### Struct Design

1. **Always use the tracking tag** on structs you want in the database
2. **Explicitly define SQL types** for important fields to avoid defaults
3. **Use foreign key tags** for referential integrity
4. **Add indexes** on frequently queried columns
5. **Use embedded structs** for common patterns like audit fields

### Build Management

1. **Use Go modules** for dependency management
2. **Build static binaries** for container compatibility
3. **Include debug symbols** in development builds
4. **Organize by architecture** for multi-platform support

### Schema Organization

1. **Use schemas** to organize related tables
2. **Follow Go naming conventions** that translate well to SQL
3. **Document complex relationships** in code comments
4. **Use consistent struct tag patterns**

### Deployment Safety

1. **Always validate first** with `MODE=validate` before `MODE=execute`
2. **Enable backups** for production deployments
3. **Review risk assessment** output before proceeding
4. **Test in staging** environments first

## Troubleshooting

### Common Issues

**No Structs Discovered:**
```
[ERROR] No structs found with tag: sql_schema
```
*Solution: Ensure structs are marked with tracking tag and binaries are built*

**Binary Load Failure:**
```
[ERROR] Failed to load binary: bin/myapp
```
*Solution: Verify BINARY_PATHS points to compiled binaries and build succeeded*

**Database Connection Failed:**
```
[ERROR] Failed to connect to database: sql.company.com
```
*Solution: Check DATABASE_SERVER, credentials, and network connectivity*

**Module Path Detection Failed:**
```
[ERROR] Failed to detect Go module path
```
*Solution: Set GO_MODULE_PATH environment variable or ensure go.mod exists*

**Reflection Analysis Failed:**
```
[ERROR] Failed to analyze struct: User
```
*Solution: Ensure struct is exported (capitalized) and has proper tags*

### Debug Mode

Enable comprehensive logging:
```bash
VERBOSE=true
LOG_LEVEL=DEBUG
SCHEMA_DUMP=true
```

This outputs:
- Binary loading details
- Struct discovery process
- Reflection analysis results
- SQL generation steps
- Go module analysis

## Integration Examples

### Multi-Module Project

```bash
# Build all modules
for module in api worker scheduler; do
    go build -o bin/$module ./cmd/$module/main.go
done

# Set binary paths to include all modules
BINARY_PATHS="bin/api:bin/worker:bin/scheduler"

# Run schema generator
docker run --env BINARY_PATHS="$BINARY_PATHS" ...
```

### Custom Tag Names

```go
// Use custom tracking tag
type Customer struct {
    ID   int    `my_schema:"export"`
    Name string `my_schema:"type:nvarchar(100)"`
}

// Configure environment
TRACK_ATTRIBUTE=my_schema
```

### GORM Integration

```go
package main

import (
    "gorm.io/gorm"
    "gorm.io/driver/sqlserver"
    "github.com/company/myapp/models"
)

func main() {
    db, err := gorm.Open(sqlserver.Open(dsn), &gorm.Config{})
    if err != nil {
        panic("failed to connect database")
    }

    // GORM auto-migration for runtime
    db.AutoMigrate(&models.User{}, &models.Order{})
    
    // SQL Schema Generator discovers structs via reflection
    // No additional configuration needed
}
```

### Microservices Architecture

```go
// User service
package user

type User struct {
    ID    int    `sql_schema:"export;schema:user_service"`
    Name  string `sql_schema:"type:nvarchar(100)"`
}

// Order service
package order

type Order struct {
    ID     int `sql_schema:"export;schema:order_service"`
    UserID int `sql_schema:"foreign_key:user_service.users(id)"`
}
```

## Support

For issues and questions:
- Enable debug logging (`VERBOSE=true`)
- Verify all required environment variables are set
- Ensure binaries are built and accessible
- Check the generated `schema-analysis.json` for discovery details
- Access documentation at `http://container:8080/docs/go`
- Download tags reference at `http://container:8080/go`