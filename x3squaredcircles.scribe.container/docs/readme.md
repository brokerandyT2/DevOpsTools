# 3SC Scribe

**Version: 5.0 (Definitive Ecosystem)**

*The Automated Historian for your CI/CD Pipeline*

---

## Overview & Core Principle

The 3SC Scribe serves as the designated, automated historian for CI/CD pipeline runs. Its singular purpose is to create the final, authoritative release artifact as a standardized, convention-based assembler—not a configurable templating engine.

The Scribe's value comes from its rigid consistency, ability to create a forensically sound audit trail, and the clear, unambiguous voice it provides for the entire 3SC toolchain. It is designed to be "environment-aware," providing rich data when online while degrading gracefully to a forensically useful state in fully air-gapped environments.

The output is a comprehensive, multi-page Markdown artifact designed for the **Auditor** persona, structured to answer three critical release questions:

- **WHY** it was deployed (business justification via work items)
- **WHAT** was deployed (code changes via commit history)
- **HOW** it was deployed (verifiable tool evidence)

## Usage

The Scribe runs as a containerized step at the end of a CI/CD pipeline. It reads the shared workspace to discover evidence, environment variables for configuration, and a Git repository for commit history.

### Example Docker Command

```bash
docker run --rm \
  -v $(pwd):/workspace \
  -w /workspace \
  -e SCRIBE_WIKI_ROOT_PATH="/workspace/MyProject.wiki" \
  -e SCRIBE_APP_NAME="MyWebApp" \
  -e SCRIBE_WI_URL="https://my-jira.atlassian.net" \
  -e SCRIBE_WI_PAT="$JIRA_PAT_SECRET" \
  x3squaredcircles/scribe:5.0
```

## The Forensic Logging Protocol

The Scribe's integrity is built on a system-wide forensic logging mandate, serving as the final consumer of logs that all other 3SC tools must produce.

**The Artifact:** A `pipeline-log.json` file must be created in the workspace root.

**The Mandate:** Every other 3SC tool (Gate, Forge, Codex, etc.) must append a JSON entry to this file upon execution.

**The Scribe's Role:** The Scribe parses this file to enrich its reports with exact tool versions and configurations used to produce each piece of evidence, creating a non-repudiable audit trail.

If this file is not present, the Scribe will still function but will be unable to provide configuration-level forensic details on its sub-pages.

## Output Artifact Structure

The Scribe produces a single, hierarchically named root directory. This structure is hardcoded and non-configurable to ensure consistency across all projects.

**Path Template:** `[SCRIBE_WIKI_ROOT_PATH]/RELEASES/[SCRIBE_APP_NAME]/[Version]-[Date]/`

**Example Final Path:** `/src/wiki/RELEASES/MyWebApp/1.5.0-20231027/`

Within this final directory, the standardized multi-page Markdown artifact is created:

```
1.5.0-20231027/
├── 1_-_Work_Items.md
├── 2_-_Risk_Analysis.md
├── 3_-_Build_Manifest.md
├── ... (and all other 1:1 tool pages) ...
└── attachments/
    ├── pipeline-log.json
    ├── risk-analysis.json
    ├── build-manifest.xml
    └── ... (all other raw artifacts) ...
```

## Configuration (Environment Variables)

The Scribe's behavior is controlled by a minimal, well-defined set of environment variables:

| Variable | Required | Description | Example Value(s) |
|----------|----------|-------------|------------------|
| `SCRIBE_WIKI_ROOT_PATH` | Yes | Base path where the `/RELEASES` directory will be created | `/workspace/MyProject.wiki` |
| `SCRIBE_APP_NAME` | Yes | Application name used to create the sub-folder | `MyWebApp` |
| `SCRIBE_WORK_ITEM_STYLE` | No | Controls format of the `Work_Items.md` page (defaults to `list`) | `list`, `categorized` |
| `SCRIBE_WI_URL` | No | Triggers smart detection override for work item provider | `https://mycompany.atlassian.net` |
| `SCRIBE_WI_PAT` | No | Personal Access Token for override work item provider | *(from pipeline secret)* |
| `SCRIBE_WI_PROVIDER` | No | Final escape hatch if smart detection fails | `jira`, `azuredevops`, `github` |

## Work Item Provider Logic: The "Smart Detection Cascade"

The Scribe uses an intelligent system to determine the work item provider, prioritizing a zero-config experience:

### 1. Default ("Just Works")
If no `SCRIBE_WI_*` variables are set, the Scribe attempts to use ambient CI/CD variables (e.g., Azure DevOps' `SYSTEM_COLLECTIONURI` and `SYSTEM_ACCESSTOKEN`) for the "local" provider associated with the repository.

### 2. Smart Detection Override
If `SCRIBE_WI_URL` and `SCRIBE_WI_PAT` are present, the Scribe initiates a detection cascade:

**A) PAT Fingerprinting:** Analyzes the PAT for unique prefixes (e.g., `ghp_` for GitHub) to instantly identify the provider.

**B) API Handshake:** If the PAT has no known prefix, it makes small, authenticated calls to known server endpoints for Jira and Azure DevOps. The first successful "handshake" wins.

### 3. Explicit Override
If, and only if, both smart detection steps fail, the Scribe looks for a `SCRIBE_WI_PROVIDER` variable as the final, authoritative instruction.

### 4. Graceful Degradation (Air-Gap Support)
If an online connection is required but fails, this is not a fatal error. The Scribe logs a clear warning and generates a "Data Unavailable" report containing only the raw work item IDs parsed from Git commit messages.

## Service Endpoint

When run as a service, the Scribe exposes a single endpoint for documentation:

**GET /docs** - Returns this README.md file.