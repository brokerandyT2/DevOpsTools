# SQLSync Generator: TypeScript Guide

This guide provides instructions for using the SQLSync Generator with a TypeScript codebase. The discovery mechanism for TypeScript is **source code analysis**, where the tool inspects your `.ts` files for specific decorators.

## 1. Get the Tracking Decorators

The generator identifies which classes to process by looking for specific decorators. **You must define these decorators in your TypeScript project.** This approach ensures the generator has zero compile-time dependencies on your code.

You can copy the required TypeScript decorator functions from the link below, or download the file directly from the tool's DX Server. Place this code in a new file within your project (e.g., `sql-decorators.ts`). The module name is not important, but the exported function names are.

### Download Helper File:

- `http://localhost:8080/Code/typescript/DSL.txt` (when the container is running)

## 2. Apply the Decorators to Your Entities

Apply the `@ExportToSQL()` decorator to any `export`ed class you want the generator to process, and use `@SqlColumn()` on properties to provide specific database mapping details.

### Basic Example:

This example will generate a table named `user_profile` in the `dbo` schema.

- `id` will be an `INT`, `NOT NULL`, `PRIMARY KEY`.
- `fullName` will be an `NVARCHAR(255)`, `NULL`.
- `isActive` will be a `BIT`, `NOT NULL`.

```typescript
// Assumes the decorators from DSL.txt are in your project
import { ExportToSQL, SqlColumn } from './sql-decorators';

@ExportToSQL({ tableName: "user_profile", schemaName: "dbo" })
export class UserProfile {
    
    @SqlColumn({ isPrimaryKey: true, isNullable: false, type: "INT" })
    id: number;

    @SqlColumn({ type: "NVARCHAR(255)" })
    fullName: string;

    @SqlColumn({ isNullable: false })
    isActive: boolean;
}
```

## 3. Configure the Generator

In your CI/CD pipeline, configure the generator to find your source files and process the decorated classes.

### Environment Variables for TypeScript:

| Variable | Description | Example |
|----------|-------------|---------|
| `SQLSYNC_LANGUAGE_TYPESCRIPT` | **Required.** Tells the generator to use the TypeScript discovery engine. | `true` |
| `SQLSYNC_TRACK_ATTRIBUTE` | **Required.** The name of the primary decorator function to look for. | `ExportToSQL` |
| `SQLSYNC_SOURCE_PATHS` | **Required.** A semicolon-separated list of paths to the source code directories. | `/src/my-api/src/models` |

### Example `docker run` command:

```bash
docker run --rm \
  -v $(pwd):/src \
  -e SQLSYNC_MODE="Generate" \
  -e SQLSYNC_LANGUAGE_TYPESCRIPT=true \
  -e SQLSYNC_DATABASE_POSTGRESQL=true \
  -e SQLSYNC_TRACK_ATTRIBUTE="ExportToSQL" \
  -e SQLSYNC_SOURCE_PATHS="/src/my-api/src/models" \
  -e SQLSYNC_DB_SERVER="my-postgres-db" \
  -e SQLSYNC_DB_NAME="MyDatabase" \
  -e SQLSYNC_AUTH_MODE="Password" \
  -e SQLSYNC_DB_USERNAME="postgres" \
  -e SQLSYNC_DB_PASSWORD_SECRET="my-db-password" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  -e "3SC_VAULT_TYPE=Aws" \
  3sc/sqlsync-generator:latest
```