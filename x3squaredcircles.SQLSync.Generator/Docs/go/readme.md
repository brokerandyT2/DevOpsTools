# SQLSync Generator: Go Guide

This guide provides instructions for using the SQLSync Generator with a Go codebase. The discovery mechanism for Go is **source code analysis**, where the tool inspects your `.go` files for special comment directives.

## 1. Understand the Comment Directive

The generator identifies which structs to process by looking for a specific comment directive immediately preceding the `type` definition. There is **no code to create or import**. You simply add a formatted comment.

The required format is `// @<TrackAttribute> [parameters]`. The `@` symbol is critical.

- **Class-level directives** like table and schema name are placed above the `type` definition.
- **Property-level directives** are placed above the specific field within the struct.

## 2. Apply the Directives to Your Structs

Add the comment directives to any `struct` you want the generator to process.

### Basic Example:

This example will generate a table named `user_profile` in the `dbo` schema.

- `ID` will be an `INT`, `NOT NULL`, `PRIMARY KEY`.
- `FullName` will be an `NVARCHAR(255)`, `NULL`.
- `IsActive` will be a `BIT`, `NOT NULL`.

```go
package models

import "github.com/google/uuid"

// @ExportToSQL tableName="user_profile" schemaName="dbo"
type UserProfile struct {
	// @SqlColumn isPrimaryKey="true" isNullable="false" type="INT"
	ID   int
	
	// @SqlColumn type="NVARCHAR(255)"
	FullName string
	
	// @SqlColumn isNullable="false"
	IsActive bool
}
```

## 3. Configure the Generator

In your CI/CD pipeline, you will configure the generator to find your source code and process the structs with comment directives.

### Environment Variables for Go:

| Variable | Description | Example |
|----------|-------------|---------|
| `SQLSYNC_LANGUAGE_GO` | **Required.** Tells the generator to use the Go discovery engine. | `true` |
| `SQLSYNC_TRACK_ATTRIBUTE` | **Required.** The name of the comment directive to look for. | `ExportToSQL` |
| `SQLSYNC_SOURCE_PATHS` | **Required.** A semicolon-separated list of paths to the source code directories. | `/src/my-api/models` |

### Example `docker run` command:

```bash
docker run --rm \
  -v $(pwd):/src \
  -e SQLSYNC_MODE="Generate" \
  -e SQLSYNC_LANGUAGE_GO=true \
  -e SQLSYNC_DATABASE_POSTGRESQL=true \
  -e SQLSYNC_TRACK_ATTRIBUTE="ExportToSQL" \
  -e SQLSYNC_SOURCE_PATHS="/src/my-api/models" \
  -e SQLSYNC_DB_SERVER="my-postgres-db" \
  -e SQLSYNC_DB_NAME="MyDatabase" \
  -e SQLSYNC_AUTH_MODE="Password" \
  -e SQLSYNC_DB_USERNAME="postgres" \
  -e SQLSYNC_DB_PASSWORD_SECRET="my-db-password" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  -e "3SC_VAULT_TYPE=Aws" \
  3sc/sqlsync-generator:latest
```