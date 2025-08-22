# SQLSync Generator: JavaScript Guide

This guide provides instructions for using the SQLSync Generator with a JavaScript (ES6+) codebase. The discovery mechanism for JavaScript is **source code analysis**, where the tool inspects your `.js` files for classes preceded by a special JSDoc comment block.

## 1. Understand the JSDoc Directives

The generator identifies which classes to process by looking for a specific tag within a JSDoc comment block: `@ExportToSQL`. **There is no code to create or import.** You simply add a formatted comment.

- **Class-level directives** like `@tableName` and `@schemaName` are placed in the JSDoc block above the `class` definition.
- **Property-level directives** are placed in a JSDoc block above the specific property assignment within the `constructor`.

## 2. Apply the JSDoc Blocks to Your Entities

Add the JSDoc blocks to any ES6 `class` you want the generator to process.

### Basic Example:

This example will generate a table named `user_profile` in the `dbo` schema.

- `id` will be an `INT`, `NOT NULL`, `PRIMARY KEY`.
- `fullName` will be an `NVARCHAR(255)`, `NULL`.
- `isActive` will be a `BIT`, `NOT NULL`.

```javascript
/**
 * Represents a user's public profile.
 * @ExportToSQL
 * @tableName user_profile
 * @schemaName dbo
 */
class UserProfile {
    constructor() {
        /**
         * @SqlColumn isPrimaryKey=true isNullable=false type=INT
         */
        this.id = 0;

        /**
         * @SqlColumn type=NVARCHAR(255)
         */
        this.fullName = "";

        /**
         * @SqlColumn isNullable=false
         */
        this.isActive = false;
    }
}
```

## 3. Configure the Generator

In your CI/CD pipeline, you will configure the generator to find your source files and process the classes with the JSDoc directives.

### Environment Variables for JavaScript:

| Variable | Description | Example |
|----------|-------------|---------|
| `SQLSYNC_LANGUAGE_JAVASCRIPT` | **Required.** Tells the generator to use the JavaScript discovery engine. | `true` |
| `SQLSYNC_TRACK_ATTRIBUTE` | **Required.** The name of the JSDoc tag to look for. | `ExportToSQL` |
| `SQLSYNC_SOURCE_PATHS` | **Required.** A semicolon-separated list of paths to the source code directories. | `/src/my-api/src/models` |

### Example `docker run` command:

```bash
docker run --rm \
  -v $(pwd):/src \
  -e SQLSYNC_MODE="Generate" \
  -e SQLSYNC_LANGUAGE_JAVASCRIPT=true \
  -e SQLSYNC_DATABASE_MYSQL=true \
  -e SQLSYNC_TRACK_ATTRIBUTE="ExportToSQL" \
  -e SQLSYNC_SOURCE_PATHS="/src/my-api/src/models" \
  -e SQLSYNC_DB_SERVER="my-mysql-db" \
  -e SQLSYNC_DB_NAME="MyDatabase" \
  -e SQLSYNC_AUTH_MODE="Password" \
  -e SQLSYNC_DB_USERNAME="root" \
  -e SQLSYNC_DB_PASSWORD_SECRET="my-db-password" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  -e "3SC_VAULT_TYPE=Azure" \
  -e "3SC_VAULT_URL=https://my-vault.vault.azure.net" \
  3sc/sqlsync-generator:latest
```