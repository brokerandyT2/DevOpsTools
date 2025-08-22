# 3SC API Assembler

The 3SC API Assembler is a polyglot, containerized CLI tool that automates the creation, building, and deployment of cloud-native API "shims." It enables developers to focus on business logic in their language of choice, while the Assembler handles the generation of the necessary boilerplate, infrastructure configuration, and deployment manifests for any supported cloud platform.

## Core Philosophy: URN-Driven Generation

The Assembler is built on a simple yet powerful principle: decoupling a developer's logical intent from the physical deployment target. This is achieved through an **Event URN (Uniform Resource Name)**.

Instead of using platform-specific SDKs directly in business logic, a developer uses a single `[EventSource]` attribute with a structured URN string.

**URN Format:** `cloud:service:{placeholder-for-resource}:action`

| Part | Description | Example |
|------|-------------|---------|
| **cloud** | The target cloud provider | `aws`, `azure`, `gcp` |
| **service** | The specific cloud service | `s3`, `sqs`, `apigateway`, `servicebus` |
| **resource** | A logical, placeholder name for the resource | `{customerUploadsBucket}` |
| **action** | The specific event or method | `ObjectCreated:Put`, `GET` |

**Example:**
A developer wants to trigger a function when a file is uploaded.

- **For AWS:** `[EventSource("aws:s3:{customerUploadsBucket}:ObjectCreated:Put")]`
- **For Azure:** `[EventSource("azure:storage:{uploadsContainer}:Microsoft.Storage.BlobCreated")]`

The business logic remains the same, but the URN declaratively defines the event source. The placeholder `{customerUploadsBucket}` is resolved at deployment time from a pipeline variable.

## The Definitive Workflow

The Assembler operates via a series of simple commands, designed to be run in a CI/CD pipeline.

1. **`discover-vars`**: Scans the business logic, finds all placeholders (e.g., `{customerUploadsBucket}`), and outputs a list. This allows the pipeline to validate that all required variables are present before proceeding.
2. **`generate`**: Reads the business logic, parses the `[EventSource]` attributes, and generates a complete, buildable source code project for the API shim in the specified output directory.
3. **`build`**: (Placeholder) Takes a generated source project and compiles/packages it into a deployable artifact (e.g., a ZIP file or a container image).
4. **`deploy`**: (Placeholder) Takes a built artifact and deploys it to the target cloud environment.

## Quick Start

This example runs the `generate` command, mounting the current directory as the source and specifying an output path.

```bash
docker run --rm -it \
  -v $(pwd):/src \
  -w /src \
  --env ASSEMBLER_COMMAND=generate \
  --env ASSEMBLER_SOURCES=./my-business-logic/src \
  --env ASSEMBLER_LIBS=./my-business-logic/artifacts/my-logic.dll \
  --env ASSEMBLER_TARGET_LANGUAGE=csharp \
  --env ASSEMBLER_CLOUD_PROVIDER=aws \
  --env ASSEMBLER_OUTPUT_PATH=./generated \
  3sc/assembler:latest
```

## Configuration

All configuration is provided via environment variables.

### Key Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `ASSEMBLER_COMMAND` | The command to execute (discover-vars, generate, etc.) | `generate` |
| `ASSEMBLER_SOURCES` | Semicolon-separated path(s) to the business logic source code | `./src/MyProject.Logic` |
| `ASSEMBLER_LIBS` | Semicolon-separated path(s) to the compiled business logic libraries | `./artifacts/MyProject.Logic.dll` |
| `ASSEMBLER_TARGET_LANGUAGE` | The language for the generated shim | `csharp`, `python`, `java` |
| `ASSEMBLER_CLOUD_PROVIDER` | The target cloud platform | `aws`, `azure`, `gcp` |
| `ASSEMBLER_OUTPUT_PATH` | The directory where generated source code will be placed | `./generated-shims` |

### Placeholder Resolution

Placeholders in an `[EventSource]` URN are resolved from environment variables that must follow a specific convention: `ASSEMBLER_CUSTOM_{PLACEHOLDER_NAME}`.

- **Developer Code:** `[EventSource("aws:s3:{customerUploadsBucket}:ObjectCreated:Put")]`
- **Placeholder:** `{customerUploadsBucket}`
- **Required Pipeline Variable:** `ASSEMBLER_CUSTOM_CUSTOMERUPLOADSBUCKET`

The value of this variable (e.g., `prod-uploads-bucket-ue1`) is provided by the CI/CD environment.

## Developer Experience: The DSL

Developers use a simple set of attributes/decorators to mark up their business logic. The `[EventSource]` attribute is the most important.

### Example (C#):

```csharp
using _3SC.Assembler.Attributes;

[FunctionHandler]
[DeploymentGroup("FileUploads", "v1")]
public class FileProcessingHandler
{
    [EventSource("aws:s3:{customerUploadsBucket}:ObjectCreated:Put")]
    public async Task HandleNewFile(S3Event s3Event, IFileMetadataService metadataService)
    {
        // Business logic here...
    }
}
```

The syntax varies by language (e.g., Python decorators), but the URN principle is the same. See the language-specific DSL files for details.

## Control Points: The Enterprise Escape Hatch

The Assembler is designed to be extensible. **Control Points** are sanctioned "escape hatches" that allow you to override or augment the tool's behavior by calling out to an external webhook (e.g., a custom API, a ServiceNow endpoint, another serverless function).

When a Control Point is configured, the Assembler makes a synchronous POST request to your specified URL with a JSON payload containing context about the event.

### Available Control Points

| Environment Variable | Command | Purpose |
|----------------------|---------|---------|
| `ASSEMBLER_CP_ON_STARTUP` | All | **Pre-flight Check:** A blocking call made before any command logic runs. Use it for dynamic configuration, license checks, or pre-flight validation. |
| `ASSEMBLER_CP_ON_SUCCESS` | All | **Notification:** A non-blocking call made after a command (generate, build, etc.) succeeds. Use it for success notifications or telemetry. |
| `ASSEMBLER_CP_ON_FAILURE` | All | **Incident Response:** A non-blocking call made after a command fails. Use it to create tickets, send alerts, or trigger rollbacks. |
| `ASSEMBLER_CP_DEPLOYMENT_TOOL` | deploy | **Override:** A blocking call that completely replaces the Assembler's built-in deployment logic. The external endpoint is given the deployment context and is expected to perform the deployment itself. |
| `ASSEMBLER_LOG_ENDPOINT_URL` | All | **Firehose Logging:** A generic, non-blocking endpoint that receives a copy of every log message generated by the tool. Ideal for shipping logs to Splunk, Datadog, or a custom logging service. |

### Configuration

Control Points are configured via environment variables.

```bash
# Example of setting up Control Points in a Docker command
docker run --rm -it \
  -v $(pwd):/src \
  -w /src \
  # --- Control Point Configuration ---
  --env ASSEMBLER_CP_ON_FAILURE="https://my-pagerduty-webhook.com/incident" \
  --env ASSEMBLER_CP_DEPLOYMENT_TOOL="https://my-custom-spinnaker-deployer.com/api/v1/deploy" \
  --env ASSEMBLER_LOG_ENDPOINT_URL="https://splunk.mycorp.com:8088/services/collector/event" \
  --env ASSEMBLER_LOG_ENDPOINT_TOKEN="my-splunk-hec-token" \
  # --- Other Assembler Configuration ---
  --env ASSEMBLER_COMMAND=deploy \
  --env ASSEMBLER_GROUP=OrderProcessor-v1 \
  --env ASSEMBLER_ARTIFACT_PATH=./artifacts/OrderProcessor-v1.zip \
  3sc/assembler:latest
```

### Payload Example for ON_FAILURE:

```json
{
  "EventType": "OnFailure",
  "Command": "deploy",
  "Status": "Failure",
  "ErrorMessage": "The external deployment override service reported a failure.",
  "ToolVersion": "6.0.0",
  "TimestampUtc": "2024-01-01T12:00:00.000Z"
}
```

## The Container Environment

The 3sc/assembler container is a self-contained, polyglot environment. To provide a true "it just works" experience, it comes with all necessary runtimes pre-installed.

- .NET 8 SDK
- OpenJDK 17 JRE
- Python 3.11
- Node.js 20 (LTS)
- Git

### DX Server

When running, the container exposes a simple web server on port 8080 for documentation and health checks.

- `GET /health`: Returns a simple health status.
- `GET /docs`: Serves this documentation.
- `GET /docs/{language}`: Serves language-specific DSL documentation.

## Building the Tool

### Prerequisites

- .NET 8 SDK
- Docker

### Build Commands

```bash
# Restore, build, and publish the .NET application
dotnet publish -c Release

# Build the final Docker image
docker build -t 3sc/assembler:latest .
```