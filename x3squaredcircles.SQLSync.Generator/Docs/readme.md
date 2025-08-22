# 3SC SQLSync Generator: Definitive Operations Guide

**Audience:** DevOps Engineers, Platform Engineers, Database Administrators, Lead Developers, Software Architects

### 1. Tool Description

The 3SC SQLSync Generator is a polyglot, containerized command-line tool that automates database schema lifecycle management. It bridges the gap between application code and the database by treating your object-oriented entities as the single source of truth for the database schema.

The tool connects to your source code, discovers developer-annotated classes (the "target schema"), compares them against a live database (the "current schema"), and generates a safe, multi-phase SQL deployment script to synchronize them. It is designed for CI/CD automation, configured entirely via environment variables, and provides a comprehensive suite of Control Points (webhooks) for deep integration into enterprise workflows, policy enforcement, and release governance.

Its core value is creating a reliable, repeatable, and GitOps-driven process for evolving database schemas in lockstep with the application code that depends on them.

### 2. Core Concepts

*   **Code as the Source of Truth:** Your application's data models (e.g., C# classes, Java classes) are the blueprint. You use language-idiomatic constructs (Attributes, Decorators) to declare how these entities should map to database tables, columns, and relationships.
*   **Environment-Driven Configuration:** All behavior is controlled via environment variables. There are no configuration files. This makes the pipeline definition the single, explicit source of truth for the tool's behavior.
*   **Generate vs. Deploy Lifecycle:** The tool operates in two distinct modes. `Generate` mode is for analysis and pull requests, producing a plan and script without execution. `Deploy` mode performs the full lifecycle, including backup, execution, and tagging.
*   **Ecosystem-Aware Authentication:** The tool abstracts database connectivity. It can connect to on-premises, bare-metal instances using username/password, or to cloud PaaS offerings (Azure SQL, AWS RDS) using their native identity-based authentication mechanisms.
*   **Stage-Specific Control Points:** The tool's workflow is broken into distinct stages (e.g., Discovery, Validation, Risk Assessment, Execution). It can invoke external webhooks during these stages, allowing for fine-grained monitoring, policy-as-code enforcement, and interactive approval gates.

### 3. Universal Environment Variables (`3SC_`)

These are the standard, cross-tool variables that control foundational features. A tool-specific variable (e.g., `SQLSYNC_VERBOSE`) will always override its `3SC_` counterpart.

| Variable                 | Description                                                 | Example Value(s)                                 |
| ------------------------ | ----------------------------------------------------------- | ------------------------------------------------ |
| `3SC_LICENSE_SERVER`     | The URL of the 3SC license validation server.               | `https://license.3sc.io`                         |
| `3SC_LOG_ENDPOINT_URL`   | A "firehose" endpoint URL for external logging (e.g., Splunk). | `https://splunk.mycorp.com:8088/services/collector` |
| `3SC_LOG_ENDPOINT_TOKEN` | The authentication token for the external logging endpoint. | *(from secret)*                                  |
| `3SC_LOG_LEVEL`          | The logging verbosity for all tools.                        | `Debug`, `Information`, `Warning`, `Error`       |
| `3SC_VERBOSE`            | Boolean (`true`/`false`). Enables maximum logging output.   | `true`                                           |
| `3SC_NO_OP`              | Boolean (`true`/`false`). If true, the tool performs a dry run. | `true`                                           |
| `3SC_VAULT_TYPE`         | The type of key vault to use for secrets.                   | `Azure`, `Aws`, `HashiCorp`                      |
| `3SC_VAULT_URL`          | The URL of the key vault instance.                          | `https://my-vault.vault.azure.net`               |

### 4. Core Configuration (`SQLSYNC_`)

| Variable                    | Required? | Description                                                                                                                   | Example Value                        |
| --------------------------- | --------- | ----------------------------------------------------------------------------------------------------------------------------- | ------------------------------------ |
| `SQLSYNC_MODE`              | Yes       | The operational mode. `Generate` is for analysis and PR checks. `Deploy` executes the changes.                                  | `Generate`                           |
| `SQLSYNC_LANGUAGE_<LANG>`   | Yes       | A boolean (`true`) flag to select the source language. Only one can be set. `<LANG>` is `CSHARP`, `JAVA`, `PYTHON`, `TYPESCRIPT`, `GO`. | `SQLSYNC_LANGUAGE_CSHARP=true`       |
| `SQLSYNC_DATABASE_<DB>`     | Yes       | A boolean (`true`) flag to select the database provider. Only one can be set. `<DB>` is `SQLSERVER`, `POSTGRESQL`, `MYSQL`, etc.  | `SQLSYNC_DATABASE_SQLSERVER=true`    |
| `SQLSYNC_TRACK_ATTRIBUTE`   | Yes       | The case-sensitive name of the Attribute, Decorator, or Comment Directive the tool should look for in the source code.        | `ExportToSQL`                        |
| `SQLSYNC_REPO_URL`          | Yes       | The clone URL of the Git repository. Often provided by the CI system.                                                         | `https://github.com/my-org/my-api.git` |
| `SQLSYNC_BRANCH`            | Yes       | The name of the current Git branch. Often provided by the CI system.                                                          | `main`                               |
| `SQLSYNC_DB_SERVER`         | Yes       | The server hostname or IP address of the target database.                                                                     | `mydb.database.windows.net`          |
| `SQLSYNC_DB_NAME`           | Yes       | The name of the target database.                                                                                              | `BillingDB`                          |
| `SQLSYNC_AUTH_MODE`         | Yes       | The authentication strategy to use. `Password` is for standard credentials. `AzureMsi` uses Azure Managed Identity.              | `Password`, `AzureMsi`               |

### 5. DX Server Endpoints

While running, the container exposes a **Developer Experience (DX) Server** on port `8080`.

*   **/health**: A simple health check endpoint.
*   **/docs**: The main documentation page, which provides links to language-specific guides and helper file downloads.

You can access it at `http://localhost:8080/docs` when running the container with the port mapped (`-p 8080:8080`).

### 6. Language-Specific Guides

For detailed instructions on how to implement the schema definition DSL in your project's source language, including code examples and helper file downloads, please see the guides below. **You will need to create your own attribute/decorator classes as shown in these guides.**

*   [C# Guide](./csharp/readme.md)
*   [Java Guide](./java/readme.md)
*   [Python Guide](./python/readme.md)
*   [TypeScript Guide](./typescript/readme.md)
*   [Go Guide](./go/readme.md)
*   [JavaScript Guide](./javascript/readme.md)