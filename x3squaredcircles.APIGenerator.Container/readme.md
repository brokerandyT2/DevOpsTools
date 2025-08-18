# API Generator Container

A containerized tool that automatically generates and deploys cloud APIs from code entities across multiple languages and cloud providers.

## Overview

The API Generator discovers entities marked with tracking attributes in your codebase and automatically generates complete API projects with CRUD operations, then deploys them to your chosen cloud platform.

## Features

- **Multi-language support**: C#, Java, Python, JavaScript, TypeScript, Go
- **Multi-cloud deployment**: Azure, AWS, Google Cloud, Oracle Cloud
- **External template system**: Fetch templates from Git repositories
- **Licensing integration**: Automatic license management with NOOP mode
- **Key vault integration**: Secure secret management across cloud providers
- **Tag template system**: Flexible deployment tagging with token replacement
- **Entity discovery**: Automatic detection of entities with tracking attributes
- **Pipeline integration**: Works with Azure DevOps, GitHub Actions, Jenkins

## Quick Start

### Docker

```bash
docker run \
  --volume $(pwd):/src \
  --env LANGUAGE_CSHARP=true \
  --env CLOUD_AZURE=true \
  --env TRACK_ATTRIBUTE=ExportToSQL \
  --env LICENSE_SERVER=https://license.company.com \
  --env TEMPLATE_REPO=https://github.com/company/api-templates \
  --env AZURE_SUBSCRIPTION=12345678-1234-1234-1234-123456789012 \
  --env AZURE_RESOURCE_GROUP=rg-api-prod \
  --env AZURE_REGION=eastus \
  myregistry.azurecr.io/api-generator:1.0.0
```

### Azure DevOps Pipeline

```yaml
variables:
  LANGUAGE_CSHARP: true
  CLOUD_AZURE: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  TEMPLATE_REPO: https://github.com/company/api-templates
  AZURE_SUBSCRIPTION: $(AzureSubscriptionId)
  AZURE_RESOURCE_GROUP: rg-api-prod
  AZURE_REGION: eastus

resources:
  containers:
  - container: api_generator
    image: myregistry.azurecr.io/api-generator:1.0.0
    options: --volume $(Build.SourcesDirectory):/src

jobs:
- job: generate_api
  container: api_generator
  steps:
  - script: /app/api-generator
```

### GitHub Actions

```yaml
env:
  LANGUAGE_JAVA: true
  CLOUD_AWS: true
  TRACK_ATTRIBUTE: Entity
  LICENSE_SERVER: https://license.company.com
  TEMPLATE_REPO: https://github.com/company/api-templates
  AWS_REGION: us-east-1

jobs:
  generate-api:
    runs-on: ubuntu-latest
    container:
      image: myregistry.azurecr.io/api-generator:1.0.0
      options: --volume ${{ github.workspace }}:/src
    steps:
      - name: Generate and deploy API
        run: /app/api-generator
```

## Configuration

All configuration is provided via environment variables. See the [Configuration Documentation](docs/configuration.md) for complete details.

### Required Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `LANGUAGE_*` | Language selection (exactly one) | `LANGUAGE_CSHARP=true` |
| `CLOUD_*` | Cloud provider (exactly one) | `CLOUD_AZURE=true` |
| `TRACK_ATTRIBUTE` | Attribute to track for API generation | `ExportToSQL` |
| `REPO_URL` | Repository URL for context | `https://github.com/company/project` |
| `BRANCH` | Git branch being processed | `main` |
| `LICENSE_SERVER` | Licensing server URL | `https://license.company.com` |
| `TEMPLATE_REPO` | Template repository URL | `https://github.com/company/templates` |

### Supported Languages

- **C#** (`LANGUAGE_CSHARP=true`) - ASP.NET Core APIs
- **Java** (`LANGUAGE_JAVA=true`) - Spring Boot APIs  
- **Python** (`LANGUAGE_PYTHON=true`) - FastAPI APIs
- **JavaScript** (`LANGUAGE_JAVASCRIPT=true`) - Express.js APIs
- **TypeScript** (`LANGUAGE_TYPESCRIPT=true`) - Express.js with TypeScript
- **Go** (`LANGUAGE_GO=true`) - Gorilla Mux APIs

### Supported Cloud Providers

- **Azure** (`CLOUD_AZURE=true`) - Function Apps, App Service
- **AWS** (`CLOUD_AWS=true`) - Lambda, API Gateway
- **Google Cloud** (`CLOUD_GCP=true`) - Cloud Run, Cloud Functions
- **Oracle Cloud** (`CLOUD_ORACLE=true`) - Functions

## Entity Discovery

Mark your entities with the configured tracking attribute:

### C# Example
```csharp
[ExportToSQL]
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}
```

### Java Example
```java
@Entity
public class Customer {
    private Long id;
    private String name;
    private String email;
    // getters and setters
}
```

### Python Example
```python
@api_endpoint
class Customer:
    def __init__(self):
        self.id = None
        self.name = None
        self.email = None
```

## Templates

Templates are fetched from external Git repositories and support token replacement:

### Template Structure
```
templates/
├── minimal/
│   ├── template.json
│   ├── main.bicep
│   └── src/
├── custom-admin/
│   ├── template.json
│   ├── cloudformation.yaml
│   └── lambda/
└── public-api/
    ├── template.json
    ├── cloudbuild.yaml
    └── app/
```

### Token Replacement

Templates support token replacement with curly braces:

- `{project-name}` - Generated project name
- `{version}` - API version
- `{entity-name}` - Current entity name
- `{namespace}` - Generated namespace
- `{cloud}` - Target cloud provider
- `{deployment-tag}` - Generated deployment tag

Example template file:
```yaml
apiVersion: serving.knative.dev/v1
kind: Service
metadata:
  name: {project-name}
spec:
  template:
    spec:
      containers:
      - image: gcr.io/{gcp-project-id}/{project-name}:{version}
```

## Tag Templates

Customize deployment tags using flexible token patterns:

```bash
# Simple versioning
TAG_TEMPLATE="api/{version}"
# Result: api/1.2.3

# Cloud-specific with date
TAG_TEMPLATE="{cloud}-api-{version}-{date}"
# Result: azure-api-1.2.3-2025-01-15

# Complete context
TAG_TEMPLATE="{repo}/{cloud}/{template-path}/v{version}"
# Result: my-project/aws/minimal/v1.2.3
```

### Supported Tokens

- `{branch}` - Git branch name
- `{repo}` - Repository name  
- `{version}` - API version
- `{major}`, `{minor}`, `{patch}` - Version components
- `{date}` - Current date (YYYY-MM-DD)
- `{datetime}` - Current datetime
- `{commit-hash}` - Git commit hash
- `{build-number}` - CI/CD build number
- `{user}` - User who triggered pipeline
- `{cloud}` - Target cloud provider
- `{template-path}` - Template path

## Licensing

The tool integrates with a licensing server for usage tracking:

### License Behavior

| Scenario | Behavior |
|----------|----------|
| License available | Normal execution |
| License expired | **Automatic NOOP mode** (analysis only) |
| License server unreachable | Wait and retry with timeout |
| Burst capacity exceeded | Wait for available license |

### NOOP Mode

When licenses are expired, the tool automatically enters NOOP mode:
- Performs full analysis and entity discovery
- Validates templates and configuration  
- Generates tag patterns and metadata
- **Does not generate code or deploy**
- Writes analysis results to `analysis-results.json`

## Key Vault Integration

Securely manage secrets across cloud providers:

### Azure Key Vault
```bash
VAULT_TYPE=azure
VAULT_URL=https://myvault.vault.azure.net
AZURE_CLIENT_ID=12345678-1234-1234-1234-123456789012
AZURE_CLIENT_SECRET=your-secret
AZURE_TENANT_ID=87654321-4321-4321-4321-210987654321
```

### AWS Secrets Manager
```bash
VAULT_TYPE=aws
AWS_REGION=us-east-1
AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
```

### HashiCorp Vault
```bash
VAULT_TYPE=hashicorp
VAULT_URL=https://vault.company.com:8200
VAULT_TOKEN=s.1234567890abcdef
```

## Output Files

The tool generates several output files:

| File | Purpose |
|------|---------|
| `pipeline-tools.log` | Execution log (append-only) |
| `api-metadata.json` | Detailed generation results |
| `deployment-info.json` | Cloud deployment details |
| `tag-patterns.json` | Generated tag patterns |
| `analysis-results.json` | NOOP mode analysis results |

## Error Codes

| Exit Code | Description |
|-----------|-------------|
| 0 | Success |
| 1 | Invalid configuration |
| 2 | License unavailable |
| 3 | Authentication failure |
| 4 | Repository access failure |
| 5 | Entity discovery failure |
| 6 | Template fetch/validation failure |
| 7 | API generation failure |
| 8 | Deployment failure |
| 9 | Key vault access failure |
| 10 | Cloud provider authentication failure |

## Troubleshooting

### Common Issues

**No entities found**
```
[ERROR] No entities found with attribute: ExportToSQL
```
Solution: Ensure entities are marked with the configured `TRACK_ATTRIBUTE`.

**No language specified**
```
[ERROR] No language specified. Set exactly one: LANGUAGE_CSHARP, LANGUAGE_JAVA, etc.
```
Solution: Set exactly one language environment variable to `true`.

**Template repository unreachable**
```
[ERROR] Failed to fetch templates from https://github.com/company/api-templates
```
Solution: Verify `TEMPLATE_REPO` URL and ensure PAT tokens have access.

### Debug Mode

Enable detailed logging:
```bash
VERBOSE=true
LOG_LEVEL=DEBUG
```

This outputs:
- Complete environment configuration (sensitive values masked)
- Template fetching and validation details
- Entity discovery process
- Cloud deployment steps
- Detailed execution timing

## Building

### Prerequisites
- .NET 6 SDK
- Docker (optional)

### Build Commands
```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run
dotnet run

# Publish single-file executable
dotnet publish -c Release -r linux-x64 --self-contained true

# Build Docker image
docker build -t api-generator .
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

## License

Copyright © x3squaredcircles 2025. All rights reserved.