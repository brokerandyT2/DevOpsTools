# SQLSync Generator: Java Guide

This guide provides instructions for using the SQLSync Generator with a Java codebase. The discovery mechanism for Java is **source code analysis**, where the tool inspects your `.java` files for specific annotations.

## 1. Get the Tracking Annotations

The generator identifies which classes to process by looking for specific annotations. **You must define these annotations in your Java project.** This approach ensures the generator has zero compile-time dependencies on your code.

You can copy the required Java annotation classes from the link below, or download the file directly from the tool's DX Server. Place this code in a new file within your project (e.g., `SqlSyncAnnotations.java`). The package is not important, but the annotation names and method names are.

### Download Helper File:

- `http://localhost:8080/Code/java/DSL.txt` (when the container is running)

## 2. Apply the Annotations to Your Entities

Apply the `@ExportToSQL` annotation to any class you want the generator to process, and use `@SqlColumn` on fields to provide specific database mapping details.

### Basic Example:

This example will generate a table named `user_profile` in the `dbo` schema.

- `id` will be an `INT`, `NOT NULL`, `PRIMARY KEY`.
- `fullName` will be an `NVARCHAR(255)`, `NULL`.
- `isActive` will be a `BIT`, `NOT NULL`.

```java
// Assumes the annotations from DSL.txt are in your project
package com.mycompany.models;

import com.mycompany.annotations.*; // Your package here

@ExportToSQL(tableName = "user_profile", schemaName = "dbo")
public class UserProfile {

    @SqlColumn(isPrimaryKey = true, isNullable = false, type = "INT")
    private int id;

    @SqlColumn(type = "NVARCHAR(255)")
    private String fullName;

    @SqlColumn(isNullable = false)
    private boolean isActive;

    // Getters and setters...
}
```

## 3. Configure the Generator

In your CI/CD pipeline, configure the generator to find your source files and process the annotated classes.

### Environment Variables for Java:

| Variable | Description | Example |
|----------|-------------|---------|
| `SQLSYNC_LANGUAGE_JAVA` | **Required.** Tells the generator to use the Java discovery engine. | `true` |
| `SQLSYNC_TRACK_ATTRIBUTE` | **Required.** The name of the primary annotation class to look for. | `ExportToSQL` |
| `SQLSYNC_SOURCE_PATHS` | **Required.** A semicolon-separated list of paths to the source code directories. | `/src/my-api/src/main/java` |

### Example `docker run` command:

```bash
docker run --rm \
  -v $(pwd):/src \
  -e SQLSYNC_MODE="Generate" \
  -e SQLSYNC_LANGUAGE_JAVA=true \
  -e SQLSYNC_DATABASE_POSTGRESQL=true \
  -e SQLSYNC_TRACK_ATTRIBUTE="ExportToSQL" \
  -e SQLSYNC_SOURCE_PATHS="/src/my-api/src/main/java" \
  -e SQLSYNC_DB_SERVER="my-postgres-db" \
  -e SQLSYNC_DB_NAME="MyDatabase" \
  -e SQLSYNC_AUTH_MODE="Password" \
  -e SQLSYNC_DB_USERNAME="postgres" \
  -e SQLSYNC_DB_PASSWORD_SECRET="my-db-password" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  -e "3SC_VAULT_TYPE=Aws" \
  3sc/sqlsync-generator:latest
```