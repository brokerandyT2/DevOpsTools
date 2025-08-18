# Version Detective Container - Developer Integration Guide

## Documentation Access

This documentation is always available via HTTP endpoint on port 8080:

```bash
# Access documentation from running container
curl http://localhost:8080/docs

# Or in browser
http://localhost:8080/docs
```

## Overview

Version Detective Container is a containerized tool that automatically calculates semantic version increments by analyzing code changes in your repository. It tracks specific attributes/annotations in your source code and applies semantic versioning rules based on detected changes.

**Key Features:**
- Multi-language support (C#, Java, Python, JavaScript, TypeScript, Go)
- Automated semantic versioning based on code analysis
- Git tag generation with customizable templates
- Integration with licensing servers and key vaults
- Pipeline execution tracking via shared log files
- Comprehensive output metadata for downstream tools
- Built-in documentation server on port 8080

## Quick Start

### Basic Usage

```bash
# Pass entire environment to container via .env file
docker run --rm \
  -p 8080:8080 \
  --volume $(pwd):/src \
  --env-file .env \
  myregistry.azurecr.io/version-detective:latest

# Or pass all current environment variables
docker run --rm \
  -p 8080:8080 \
  --volume $(pwd):/src \
  $(env | sed 's/^/--env /') \
  myregistry.azurecr.io/version-detective:latest

# Documentation is automatically available at:
# http://localhost:8080/docs
```

### Pipeline Integration

**Azure DevOps:**
```yaml
# Set ALL configuration variables at pipeline level
variables:
  LANGUAGE_CSHARP: true
  TRACK_ATTRIBUTE: ExportToSQL
  LICENSE_SERVER: https://license.company.com
  TAG_TEMPLATE: "release/{version}"
  REPO_URL: $(Build.Repository.Uri)
  BRANCH: $(Build.SourceBranchName)
  # ... all other configuration variables as needed

resources:
  containers:
  - container: version_detective
    image: myregistry.azurecr.io/version-detective:latest
    options: --volume $(Build.SourcesDirectory):/src
    ports:
    - 8080:8080

jobs:
- job: calculate_versions
  container: version_detective
  # Container automatically inherits ALL pipeline variables as environment variables
  steps:
  - script: /app/version-detective
```

**GitHub Actions:**
```yaml
# Set ALL configuration variables at workflow level
env:
  LANGUAGE_JAVA: true
  TRACK_ATTRIBUTE: Entity
  LICENSE_SERVER: https://license.company.com
  REPO_URL: ${{ github.repository }}
  BRANCH: ${{ github.ref_name }}
  # ... all other configuration variables as needed

jobs:
  version-calculation:
    runs-on: ubuntu-latest
    container:
      image: myregistry.azurecr.io/version-detective:latest
      options: --volume ${{ github.workspace }}:/src
      ports:
        - 8080:8080
    # Container automatically inherits ALL workflow environment variables
    steps:
      - name: Calculate versions
        run: /app/version-detective
```

## Complete Configuration Reference

All configuration is provided via environment variables. The container receives all environment variables and parses what it needs.

### Required Configuration

| Variable | Description | Example |
|----------|-------------|---------|
| `LANGUAGE_CSHARP` | Enable C# analysis (exactly one language required) | `true` |
| `LANGUAGE_JAVA` | Enable Java analysis (exactly one language required) | `true` |
| `LANGUAGE_PYTHON` | Enable Python analysis (exactly one language required) | `true` |
| `LANGUAGE_JAVASCRIPT` | Enable JavaScript analysis (exactly one language required) | `true` |
| `LANGUAGE_TYPESCRIPT` | Enable TypeScript analysis (exactly one language required) | `true` |
| `LANGUAGE_GO` | Enable Go analysis (exactly one language required) | `true` |
| `TRACK_ATTRIBUTE` | Attribute/annotation name to track for versioning | `ExportToSQL`, `Entity`, `version_track` |
| `REPO_URL` | Repository URL for context | `https://github.com/company/project` |
| `BRANCH` | Git branch being analyzed | `main`, `develop`, `feature/api-v2` |
| `LICENSE_SERVER` | URL of licensing server | `https://license.company.com` |

### Optional Configuration

| Variable | Default | Description | Example |
|----------|---------|-------------|---------|
| `TOOL_NAME` | Assembly name | Name to report to license server | Defaults to executable assembly name |
| `LICENSE_TIMEOUT` | `300` | Seconds to wait for license server response | `300` |
| `LICENSE_RETRY_INTERVAL` | `30` | Seconds between license server retry attempts | `30` |
| `TAG_TEMPLATE` | `{branch}/{repo}/semver/{version}` | Custom tag pattern using supported tokens | `release/{version}`, `v{version}-{date}` |
| `MARKETING_TAG_TEMPLATE` | `{branch}/{repo}/marketing/{version}` | Marketing version tag pattern | `marketing/v{version}` |
| `FROM` | Latest semver tag | Specific commit/tag to analyze changes from | `v1.0.0`, `abc123def` |
| `MODE` | `pr` | Operation mode | `pr` (analyze only), `deploy` (apply changes) |
| `VALIDATE_ONLY` | `false` | Validation-only mode | `true`, `false` |
| `NO_OP` | `false` | No-operation mode (analyze and report only) | `true`, `false` |
| `DLL_PATHS` | (empty) | Colon-separated paths to compiled assemblies | `bin/Release:build/libs:target/classes` |
| `BUILD_OUTPUT_PATH` | (empty) | Primary build output directory | `bin/Release/net8.0` |
| `VERBOSE` | `false` | Enable detailed logging | `true`, `false` |
| `LOG_LEVEL` | `INFO` | Logging level | `DEBUG`, `INFO`, `WARN`, `ERROR` |

### Authentication Configuration

| Variable | Default | Description | Example |
|----------|---------|-------------|---------|
| `PAT_TOKEN` | (empty) | Personal Access Token for git operations | `ghp_xxxxxxxxxxxx` |
| `PAT_SECRET_NAME` | (empty) | Key vault reference for PAT | `github-pat-prod` |

**Automatic Pipeline Authentication:**
The tool automatically detects and uses platform tokens:
- Azure DevOps: `System.AccessToken`
- GitHub Actions: `GITHUB_TOKEN`
- Jenkins: `GIT_TOKEN`, `SCM_TOKEN`

### Key Vault Configuration

**Azure Key Vault:**
| Variable | Description | Example |
|----------|-------------|---------|
| `VAULT_TYPE` | Must be `azure` | `azure` |
| `VAULT_URL` | Azure Key Vault URL | `https://myvault.vault.azure.net` |
| `AZURE_CLIENT_ID` | Service principal client ID | `12345678-1234-1234-1234-123456789012` |
| `AZURE_CLIENT_SECRET` | Service principal secret | `your-secret` |
| `AZURE_TENANT_ID` | Azure tenant ID | `87654321-4321-4321-4321-210987654321` |

**AWS Secrets Manager:**
| Variable | Description | Example |
|----------|-------------|---------|
| `VAULT_TYPE` | Must be `aws` | `aws` |
| `AWS_REGION` | AWS region | `us-east-1` |
| `AWS_ACCESS_KEY_ID` | AWS access key | `AKIAIOSFODNN7EXAMPLE` |
| `AWS_SECRET_ACCESS_KEY` | AWS secret key | `wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY` |

**HashiCorp Vault:**
| Variable | Description | Example |
|----------|-------------|---------|
| `VAULT_TYPE` | Must be `hashicorp` | `hashicorp` |
| `VAULT_URL` | Vault server URL | `https://vault.company.com:8200` |
| `VAULT_TOKEN` | Vault authentication token | `s.1234567890abcdef` |

## Language Selection

**Mutually exclusive** - set exactly one to `true`, all others to `false` or leave empty:

```bash
# C# Analysis
LANGUAGE_CSHARP=true

# Java Analysis  
LANGUAGE_JAVA=true

# Python Analysis
LANGUAGE_PYTHON=true

# JavaScript Analysis
LANGUAGE_JAVASCRIPT=true

# TypeScript Analysis
LANGUAGE_TYPESCRIPT=true

# Go Analysis
LANGUAGE_GO=true
```

## Tag Template Configuration

Customize how version tags are generated using supported tokens:

```bash
# Simple versioning
TAG_TEMPLATE="v{version}"
# Result: v1.2.3

# Detailed with branch and date
TAG_TEMPLATE="{branch}/release-{version}-{date}"
# Result: main/release-1.2.3-2025-01-15

# Build-specific
TAG_TEMPLATE="build-{build-number}/v{version}"
# Result: build-1234/v1.2.3

# Repository-specific with commit
TAG_TEMPLATE="{repo}-{version}-{commit-hash}"
# Result: my-project-1.2.3-a1b2c3d

# Marketing version
MARKETING_TAG_TEMPLATE="marketing/v{version}"
# Result: marketing/v1.2.0
```

**Available Tokens:**
- `{version}` - Calculated semantic version (1.2.3)
- `{major}`, `{minor}`, `{patch}` - Individual version components
- `{branch}` - Git branch name
- `{repo}` - Repository name
- `{date}` - Current date (YYYY-MM-DD)
- `{datetime}` - Current datetime (YYYY-MM-DD-HHMMSS)
- `{commit-hash}` - Short git commit hash (7 chars)
- `{commit-hash-full}` - Full git commit hash
- `{build-number}` - CI/CD build number
- `{user}` - User who triggered the pipeline

## How It Works

### 1. Code Analysis

The tool analyzes your source code to find entities marked with your specified tracking attribute:

**C# Example:**
```csharp
[ExportToSQL]
public class Customer
{
    public string Name { get; set; }
    public string Email { get; set; }
}
```

**Java Example:**
```java
@Entity
public class Customer {
    private String name;
    private String email;
}
```

**Python Example:**
```python
@version_track
class Customer:
    name: str
    email: str
```

### 2. Change Detection

The tool compares the current code against a baseline (previous version tag or specified commit) to detect:

- **Major Changes** (Breaking): New entities, removed entities, removed properties, type changes
- **Minor Changes** (Features): New methods, new properties (non-breaking additions)
- **Patch Changes** (Fixes): Bug fixes, performance improvements, documentation updates

### 3. Version Calculation

Applies semantic versioning rules:
- **Major**: Increment for breaking changes (1.0.0 → 2.0.0)
- **Minor**: Increment for new features (1.0.0 → 1.1.0)
- **Patch**: Increment for fixes (1.0.0 → 1.0.1)

### 4. Output Generation

Creates files in your mounted volume (`/src`):

- **`pipeline-tools.log`**: Execution record for tool chaining
- **`version-metadata.json`**: Detailed analysis results
- **`tag-patterns.json`**: Generated tag patterns for downstream tools

## Output Files

### pipeline-tools.log

Shared execution log for tool chaining:
```
version-detective=1.0.0
other-tool=2.1.0
final-tool=1.5.0
```

Format: `tool-name=tool-version` (with optional `(BURST)` indicator)

### version-metadata.json

Comprehensive analysis results:
```json
{
  "tool_name": "version-detective",
  "tool_version": "1.0.0",
  "execution_time": "2025-01-15T14:30:22Z",
  "language": "csharp",
  "repository": "https://github.com/company/project",
  "branch": "main",
  "current_commit": "a1b2c3d4e5f6789012345678901234567890abcd",
  "baseline_commit": "previous-commit-hash",
  "version_calculation": {
    "current_version": "1.0.0",
    "new_semantic_version": "1.1.0",
    "new_marketing_version": "1.1.0",
    "has_major_changes": false,
    "minor_changes": 2,
    "patch_changes": 1,
    "reasoning": "New functionality added: 2 new methods, 1 new property"
  },
  "tag_templates": {
    "semantic_tag": "release/1.1.0",
    "marketing_tag": "marketing/1.1.0"
  },
  "license_used": true,
  "burst_mode_used": false,
  "mode": "pr"
}
```

### tag-patterns.json

Generated tag patterns:
```json
{
  "semantic_tag": "release/1.1.0",
  "marketing_tag": "marketing/1.1.0",
  "generated_at": "2025-01-15T14:30:22Z",
  "token_values": {
    "version": "1.1.0",
    "branch": "main",
    "repo": "my-project",
    "date": "2025-01-15"
  }
}
```

## Licensing Behavior

The tool integrates with a licensing server to manage usage:

| Scenario | Behavior | Pipeline Impact |
|----------|----------|-----------------|
| License available | Normal execution | Analysis + deployment (if `MODE=deploy`) |
| License expired | Automatic NOOP mode | Analysis only, no changes applied |
| License server unreachable | Wait and retry | Configurable timeout, then fail |
| Burst capacity exceeded | Wait for license | Configurable wait, then fail |

**Burst Mode:** When regular licenses are exhausted, the tool can use burst capacity. This is indicated in logs and output files with `(BURST)` markers.

## Error Codes

| Exit Code | Description |
|-----------|-------------|
| 0 | Success |
| 1 | Invalid configuration |
| 2 | License unavailable |
| 3 | Authentication failure |
| 4 | Repository access failure |
| 5 | Build artifacts not found |
| 6 | Git operation failure |
| 7 | Tag template validation failure |
| 8 | Key vault access failure |

## Best Practices

### Pipeline Integration

1. **Environment Variables**: Set all configuration at the pipeline level - containers automatically inherit ALL variables
2. **Volume Mounting**: Always mount your source directory to `/src`
3. **Error Handling**: Check exit codes and handle licensing failures gracefully
4. **Logging**: Enable `VERBOSE=true` for debugging, but use `INFO` for production

### Version Tracking

1. **Consistent Attributes**: Use the same tracking attribute across your entire codebase
2. **Meaningful Changes**: Only mark entities that represent meaningful API changes
3. **Documentation**: Document your tracking strategy for team members

### Tag Templates

1. **Consistency**: Use consistent tag patterns across projects
2. **Descriptiveness**: Include branch and date for feature branches
3. **Downstream Compatibility**: Ensure your tag patterns work with deployment tools

### Security

1. **Key Vaults**: Use key vaults for sensitive tokens, not environment variables
2. **Least Privilege**: Grant minimal necessary permissions to pipeline tokens
3. **Rotation**: Regularly rotate PAT tokens and vault credentials

## Troubleshooting

### Common Issues

**No Language Selected:**
```
[ERROR] No language specified. Set exactly one: LANGUAGE_CSHARP, LANGUAGE_JAVA, etc.
```
*Solution: Set exactly one language environment variable to `true`*

**Invalid Tag Template:**
```
[ERROR] Unknown token: {unknown-token}
```
*Solution: Use only supported tokens from the reference table*

**License Server Unavailable:**
```
[ERROR] Failed to connect to license server
```
*Solution: Verify `LICENSE_SERVER` URL and network connectivity*

**Git Authentication Failure:**
```
[ERROR] Git authentication failed
```
*Solution: Ensure `PAT_TOKEN` is set or pipeline has repository permissions*

### Debug Mode

Enable comprehensive logging:
```bash
VERBOSE=true
LOG_LEVEL=DEBUG
```

This outputs:
- Complete environment configuration (with sensitive values masked)
- Detailed execution steps
- Git operations and file analysis
- Version calculation reasoning

## Integration Examples

### Multi-Stage Pipeline

```yaml
# Stage 1: Version Calculation
- stage: VersionCalculation
  variables:
    LANGUAGE_CSHARP: true
    TRACK_ATTRIBUTE: ExportToSQL
    LICENSE_SERVER: https://license.company.com
    # ... all other variables
  jobs:
  - job: calculate_version
    container: version_detective
    steps:
    - script: /app/version-detective

# Stage 2: Use Calculated Version
- stage: Build
  dependsOn: VersionCalculation
  jobs:
  - job: build_application
    steps:
    - script: |
        # Read calculated version
        VERSION=$(cat tag-patterns.json | jq -r '.semantic_tag')
        echo "Building version: $VERSION"
```

### Tool Chaining

```bash
# Tool 1: Version Detective
docker run --env-file .env --volume $(pwd):/src version-detective

# Tool 2: Read execution history
docker run --env-file .env --volume $(pwd):/src another-tool
# Can read pipeline-tools.log to see version-detective ran

# Tool 3: Use version metadata
docker run --env-file .env --volume $(pwd):/src deployment-tool
# Can read version-metadata.json for deployment decisions
```

## Support

For issues and questions:
- Check exit codes and error messages
- Enable debug logging (`VERBOSE=true`)
- Verify all required environment variables are set
- Ensure proper volume mounting to `/src`
- Check licensing server connectivity and credentials
- Access documentation at `http://container:8080/docs`