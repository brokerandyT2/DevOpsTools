# SQLSync Generator: C# Guide

This guide provides instructions for using the SQLSync Generator with a C# codebase. The primary discovery mechanism for C# is **assembly analysis**, where the tool inspects the compiled `.dll` files of your project.

## 1. Get the Tracking Attributes

The generator identifies which classes to process by looking for specific attributes. **You must define these attributes in your C# project.** This approach ensures the generator has zero compile-time dependencies on your code.

You can copy the required C# attribute classes from the link below, or download the file directly from the tool's DX Server. Place this code in a new file within your project (e.g., `SqlSyncAttributes.cs`). The namespace is not important, but the class names and property names are.

### Download Helper File:

- `http://localhost:8080/Code/csharp/DSL.txt` (when the container is running)

## 2. Apply the Attributes to Your Entities

Apply the `[ExportToSQL]` attribute to any class you want the generator to process, and use `[SqlColumn]` on properties to provide specific database mapping details.

### Basic Example:

This example will generate a table named `user_profile` in the `dbo` schema.

- `Id` will be an `INT`, `NOT NULL`, `PRIMARY KEY`.
- `FullName` will be an `NVARCHAR(255)`, `NULL`.
- `IsActive` will be a `BIT`, `NOT NULL`.

```csharp
// Assumes the attributes from DSL.txt are in your project
using SqlSync.Attributes;

[ExportToSQL(TableName = "user_profile", SchemaName = "dbo")]
public class UserProfile
{
    [SqlColumn(IsPrimaryKey = true, IsNullable = false, Type = "INT")]
    public int Id { get; set; }

    [SqlColumn(Type = "NVARCHAR(255)")]
    public string FullName { get; set; }

    [SqlColumn(IsNullable = false)]
    public bool IsActive { get; set; }
}
```

## 3. Configure the Generator

In your CI/CD pipeline, configure the generator to find your compiled assemblies and process the attributed classes.

### Environment Variables for C#:

| Variable | Description | Example |
|----------|-------------|---------|
| `SQLSYNC_LANGUAGE_CSHARP` | **Required.** Tells the generator to use the C# discovery engine. | `true` |
| `SQLSYNC_TRACK_ATTRIBUTE` | **Required.** The name of the primary attribute class to look for. | `ExportToSQL` |
| `SQLSYNC_ASSEMBLY_PATH` | **Required.** Path inside the container to the directory containing your compiled project DLLs. | `/src/MyProject.Api/bin/Release/net8.0` |

### Example `docker run` command:

```bash
docker run --rm \
  -v $(pwd):/src \
  -e SQLSYNC_MODE="Generate" \
  -e SQLSYNC_LANGUAGE_CSHARP=true \
  -e SQLSYNC_DATABASE_SQLSERVER=true \
  -e SQLSYNC_TRACK_ATTRIBUTE="ExportToSQL" \
  -e SQLSYNC_ASSEMBLY_PATH="/src/MyApi/bin/Release/net8.0" \
  -e SQLSYNC_DB_SERVER="localhost" \
  -e SQLSYNC_DB_NAME="MyDatabase" \
  -e SQLSYNC_AUTH_MODE="Password" \
  -e SQLSYNC_DB_USERNAME="sa" \
  -e SQLSYNC_DB_PASSWORD_SECRET="my-db-password" \
  -e "3SC_LICENSE_SERVER=https://licensing.3sc.com" \
  -e "3SC_VAULT_TYPE=Azure" \
  -e "3SC_VAULT_URL=https://my-vault.vault.azure.net" \
  3sc/sqlsync-generator:latest
```