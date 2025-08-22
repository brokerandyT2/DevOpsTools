# SQLSync Generator: Python Guide

This guide provides instructions for using the SQLSync Generator with a Python codebase. The discovery mechanism for Python is **source code analysis**, where the tool inspects your `.py` files for specific decorators.

## 1. Get the Tracking Decorators

The generator identifies which classes to process by looking for specific decorators. **You must define these decorators in your Python project.** This approach ensures the generator has zero compile-time dependencies on your code.

You can copy the required Python decorator functions from the link below, or download the file directly from the tool's DX Server. Place this code in a new file within your project (e.g., `sql_decorators.py`). The module name is not important, but the function names are.

### Download Helper File:

- `http://localhost:8080/Code/python/DSL.txt` (when the container is running)

## 2. Apply the Decorators to Your Entities

Apply the `@export_to_sql` decorator to any class you want the generator to process, and use `@sql_column` on properties with type hints to provide specific database mapping details.

### Basic Example:

This example will generate a table named `user_profile` in the `dbo` schema.

- `id` will be an `INT`, `NOT NULL`, `PRIMARY KEY`.
- `full_name` will be an `NVARCHAR(255)`, `NULL`.
- `is_active` will be a `BIT`, `NOT NULL`.

```python
# Assumes the decorators from DSL.txt are in your project
from .sql_decorators import export_to_sql, sql_column

@export_to_sql(table_name="user_profile", schema_name="dbo")
class UserProfile:
    
    @sql_column(is_primary_key=True, is_nullable=False, type="INT")
    id: int

    @sql_column(type="NVARCHAR(255)")
    full_name: str

    @sql_column(is_nullable=False)
    is_active: bool
```

## 3. Configure the Generator

In your CI/CD pipeline, configure the generator to find your source files and process the decorated classes.

### Environment Variables for Python:

| Variable | Description | Example |
|----------|-------------|---------|
| `SQLSYNC_LANGUAGE_PYTHON` | **Required.** Tells the generator to use the Python discovery engine. | `true` |
| `SQLSYNC_TRACK_ATTRIBUTE` | **Required.** The name of the primary decorator function to look for. | `export_to_sql` |
| `SQLSYNC_SOURCE_PATHS` | **Required.** A semicolon-separated list of paths to the source code directories. | `/src/my_api/models` |

### Example `docker run` command:

```bash
docker run --rm \
  -v $(pwd):/src \
  -e SQLSYNC_MODE="Generate" \
  -e SQLSYNC_LANGUAGE_PYTHON=true \
  -e SQLSYNC_DATABASE_MYSQL=true \
  -e SQLSYNC_TRACK_ATTRIBUTE="export_to_sql" \
  -e SQLSYNC_SOURCE_PATHS="/src/my_api/models" \
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