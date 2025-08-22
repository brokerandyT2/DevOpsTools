# 3SC Design Token Generator: Definitive Operations Guide (v2.0)

**Part 1 of 4: Overview, Core Concepts, and Universal Configuration**

**Audience:** DevOps Engineers, Platform Engineers, Design System Leads

## 1. Tool Description

The 3SC Design Token Generator is a polyglot, containerized command-line tool that automates the entire design token pipeline. It connects to a source design platform (e.g., Figma, Sketch), extracts raw design data, normalizes it into a standardized Design Token format, and generates platform-specific style files (e.g., Swift for iOS, Kotlin for Android, CSS/SCSS for Web).

The tool is designed for CI/CD automation, configured entirely via environment variables, and provides a suite of stage-specific Control Points (webhooks) to allow for deep integration into enterprise workflows, governance, and approval processes. Its core value is enforcing brand consistency and accelerating development by creating a single, automated source of truth for an organization's visual style.

## 2. Core Concepts

### Pipeline as a Component
The tool is not a standalone application but a component designed to be executed within a CI/CD pipeline. It assumes it is running in a checked-out Git repository.

### Environment-Driven Configuration
All behavior is controlled via environment variables. There are no configuration files. This makes the pipeline definition the single, explicit source of truth for how the tool will behave in a given environment.

### Stage-Specific Control Points
The tool's workflow is broken into distinct stages (e.g., Extract, Generate, Commit). It can invoke external webhooks at the start or end of these stages, allowing for fine-grained monitoring, approval gates, and custom logic.

### Preservation of Custom Code
The platform-specific code generators are designed to find and preserve manually-written code inside special comment blocks. This allows developers to safely extend the auto-generated style files without their work being overwritten on subsequent runs.

### Polyglot Container
The official `3sc/design-token-generator` Docker image is a self-contained environment with all necessary runtimes (Java, Python, Node.js) pre-installed to provide a true "it just works" experience.

## 3. Universal Environment Variables

These are the standard, cross-tool variables that control foundational features like licensing, logging, and vault access. A tool-specific variable (e.g., `TOKENS_VERBOSE`) will always override its `3SC_` counterpart.

| Variable | Description | Example Value(s) |
|----------|-------------|------------------|
| `3SC_LICENSE_SERVER` | The URL of the 3SC license validation server | `https://license.3sc.io` |
| `3SC_LICENSE_TIMEOUT` | Timeout in seconds for contacting the license server | `300` |
| `3SC_LOG_ENDPOINT_URL` | A "firehose" endpoint URL for external logging (e.g., Splunk) | `https://splunk.mycorp.com:8088/services/collector` |
| `3SC_LOG_ENDPOINT_TOKEN` | The authentication token for the external logging endpoint | `(from secret)` |
| `3SC_CONTROL_POINT_TIMEOUT_SECONDS` | Timeout in seconds for all blocking Control Point webhook calls | `60` |
| `3SC_CONTROL_POINT_TIMEOUT_ACTION` | Action to take on a Control Point timeout | `fail`, `continue` |
| `3SC_LOG_LEVEL` | The logging verbosity for all tools | `debug`, `info`, `warn`, `error` |
| `3SC_VERBOSE` | Boolean (true/false). Enables maximum logging output | `true` |
| `3SC_NO_OP` | Boolean (true/false). If true, the tool performs a dry run | `true` |
| `3SC_VAULT_TYPE` | The type of key vault to use for secrets | `azure`, `aws`, `gcp`, `hashicorp` |
| `3SC_VAULT_URL` | The URL of the key vault instance | `https://my-vault.vault.azure.net` |
| `3SC_AZURE_CLIENT_ID` | Azure Service Principal Client ID for vault auth | `(from secret)` |
| `3SC_AZURE_CLIENT_SECRET` | Azure Service Principal Client Secret | `(from secret)` |
| `3SC_AZURE_TENANT_ID` | Azure Tenant ID | `(from secret)` |
| `3SC_AWS_REGION` | AWS Region for vault and other services | `us-east-1` |
| `3SC_AWS_ACCESS_KEY_ID` | AWS Access Key ID for authentication | `(from secret)` |
| `3SC_AWS_SECRET_ACCESS_KEY` | AWS Secret Access Key | `(from secret)` |
| `3SC_HASHICORP_TOKEN` | The authentication token for HashiCorp Vault | `(from secret)` |
| `3SC_GCP_SERVICE_ACCOUNT_KEY_JSON` | The JSON content of the GCP service account key | `(from secret)` |

## 4. Core Configuration

These variables define the what and where of the generation process.

| Variable | Required? | Description | Example Value |
|----------|-----------|-------------|---------------|
| `TOKENS_DESIGN_PLATFORM` | Yes | The source design platform to extract tokens from | `figma`, `sketch`, `zeplin`, `adobe-xd`, `abstract`, `penpot` |
| `TOKENS_TARGET_PLATFORM` | Yes | The target development platform to generate code for | `ios`, `android`, `web` |
| `TOKENS_REPO_URL` | Yes | The clone URL of the Git repository the tool is operating in | `https://github.com/my-org/my-mobile-app.git` |
| `TOKENS_BRANCH` | Yes | The name of the current Git branch being processed | `main`, `develop` |
| `TOKENS_COMMAND` | No | The command to execute. Currently supports sync (default) | `sync` |
| `TOKENS_NO_OP` | No | If true, the tool performs a full analysis but does not write any files or make git changes. Overrides `3SC_NO_OP` | `true` |
| `TOKENS_VALIDATE_ONLY` | No | If true, performs configuration validation and token extraction but skips file generation and git operations | `true` |

## 5. Design Platform Configuration

You must provide the configuration for the specific `TOKENS_DESIGN_PLATFORM` you have selected.

### Figma

| Variable | Required? | Description | Example Value |
|----------|-----------|-------------|---------------|
| `TOKENS_FIGMA_URL` | Yes | The full URL to the Figma file to be analyzed | `https://www.figma.com/design/fileId/project-name` |
| `TOKENS_FIGMA_TOKEN_SECRET_NAME` | Yes | The name/key of the secret in your configured key vault that holds the Figma Personal Access Token | `figma-personal-access-token` |

### Sketch

| Variable | Required? | Description | Example Value |
|----------|-----------|-------------|---------------|
| `TOKENS_SKETCH_WORKSPACE_ID` | Yes | The ID of your Sketch workspace | `a1b2c3d4-e5f6-7890-1234-567890abcdef` |
| `TOKENS_SKETCH_DOCUMENT_ID` | Yes | The ID of the specific Sketch document to analyze | `z9y8x7w6-v5u4-t3s2-r1q0-p9o8n7m6l5k4` |
| `TOKENS_SKETCH_TOKEN_SECRET_NAME` | Yes | The name/key of the secret in your key vault that holds the Sketch API Token | `sketch-api-token` |

### Zeplin

| Variable | Required? | Description | Example Value |
|----------|-----------|-------------|---------------|
| `TOKENS_ZEPLIN_PROJECT_ID` | Yes | The ID of the Zeplin project to extract tokens from | `60a1b2c3d4e5f60017a1b2c3` |
| `TOKENS_ZEPLIN_TOKEN_SECRET_NAME` | Yes | The name/key of the secret in your key vault that holds the Zeplin Personal Access Token | `zeplin-pat` |

*(This pattern continues for Adobe XD, Abstract, and Penpot)*

## 6. Target Platform Configuration

These variables control the output format for the specific `TOKENS_TARGET_PLATFORM` you have selected.

### iOS

| Variable | Description | Example Value |
|----------|-------------|---------------|
| `TOKENS_IOS_OUTPUT_DIR` | The directory (relative to the repo root) where Swift files will be written. Default: `UI/iOS/style/` | `MyApp/Sources/Theme/Generated` |
| `TOKENS_IOS_MODULE_NAME` | The name of the Swift struct used to namespace the tokens. Default: `DesignTokens` | `MyBrandTheme` |

### Android

| Variable | Description | Example Value |
|----------|-------------|---------------|
| `TOKENS_ANDROID_OUTPUT_DIR` | The directory where Kotlin files will be written. Default: `UI/Android/style/` | `app/src/main/java/com/myorg/myapp/theme` |
| `TOKENS_ANDROID_PACKAGE_NAME` | The Kotlin package name for the generated files | `com.myorg.myapp.theme.generated` |

### Web

| Variable | Description | Example Value |
|----------|-------------|---------------|
| `TOKENS_WEB_OUTPUT_DIR` | The directory where CSS/SCSS/JS files will be written. Default: `UI/Web/style/` | `src/styles/generated` |
| `TOKENS_WEB_TEMPLATE` | The output format for the web tokens. Default: `vanilla` | `tailwind`, `bootstrap`, `material` |

## 7. Git Operations Configuration

These variables control how the tool interacts with your Git repository.

| Variable | Description | Example Value |
|----------|-------------|---------------|
| `TOKENS_GIT_AUTO_COMMIT` | If true, the tool will automatically stage and commit any changed or new token files | `true` |
| `TOKENS_GIT_COMMIT_MESSAGE` | The commit message to use for the automatic commit | `chore(design): update design tokens from Figma` |
| `TOKENS_PAT_SECRET_NAME` | The name/key of the secret in your key vault that holds a Git PAT token with push access. Required for push/tag operations | `github-repo-pat` |

## 8. Control Point Configuration

Control Points are the primary mechanism for integrating the Design Token Generator into a larger enterprise workflow. They are webhooks that the tool invokes at key stages of its execution. This allows for fine-grained monitoring, custom validation, and interactive approval gates.

All Control Points are configured via environment variables. A blocking Control Point will halt the pipeline if its endpoint returns a non-2xx HTTP status code, unless `TOKENS_CP_TIMEOUT_ACTION` is set to `continue`.

| Variable | Type | Description |
|----------|------|-------------|
| `TOKENS_CP_ON_RUN_START` | Blocking | Invoked at the absolute beginning of a run, after initial configuration parsing but before any significant action. Ideal for pre-flight validation. |
| `TOKENS_CP_ON_EXTRACT_SUCCESS` | Non-blocking | Invoked after successfully extracting and normalizing tokens from the source design platform. |
| `TOKENS_CP_ON_EXTRACT_FAILURE` | Non-blocking | Invoked if the tool fails to connect to or parse data from the source design platform. |
| `TOKENS_CP_ON_GENERATE_SUCCESS` | Non-blocking | Invoked after successfully generating all platform-specific code files. |
| `TOKENS_CP_ON_GENERATE_FAILURE` | Non-blocking | Invoked if the code generation process fails. |
| `TOKENS_CP_BEFORE_COMMIT` | Blocking | Critical approval gate. Invoked after file generation but before git commit is run. An external system can return a non-2xx status to veto the commit. |
| `TOKENS_CP_ON_COMMIT_SUCCESS` | Non-blocking | Invoked after a successful local git commit operation. |
| `TOKENS_CP_ON_RUN_SUCCESS` | Non-blocking | Invoked at the very end of a completely successful run. |
| `TOKENS_CP_ON_RUN_FAILURE` | Non-blocking | The final webhook invoked at the end of any failed run, capturing any error not caught by a more specific failure hook. |

## 9. Control Point Payloads & Responses

All Control Points receive a POST request with a standard JSON body. Blocking webhooks can return an "enhanced" JSON response to provide feedback to the tool.

### Standard Payload Structure

This is the base structure for all Control Point events.

```json
{
  "eventType": "Extract_OnSuccess",
  "toolVersion": "2.0.0",
  "timestampUtc": "2024-05-21T18:30:00.123Z",
  "status": "Success",
  "errorMessage": null,
  "metadata": {
    // Stage-specific metadata goes here
  }
}
```

### Stage-Specific Metadata Examples

**Event: `Extract_OnSuccess`**
The metadata object contains details about the extraction.

```json
"metadata": {
  "source": "figma",
  "tokenCount": 247,
  "figmaFileId": "aBcDeFg12345",
  "originalTokenCount": 310,
  "finalTokenCount": 247
}
```

**Event: `Generate_OnSuccess`**
The metadata object contains a list of the file paths that were created or modified.

```json
"metadata": {
  "filesGenerated": 3,
  "outputDirectory": "UI/iOS/style/",
  "generatedFiles": [
    "/src/UI/iOS/style/Colors.swift",
    "/src/UI/iOS/style/Typography.swift",
    "/src/UI/iOS/style/Spacing.swift"
  ]
}
```

**Event: `Commit_OnSuccess`**
The metadata object contains the SHA of the new commit.

```json
"metadata": {
  "commitHash": "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
  "tagCreated": "tokens/ios/v1.2.3"
}
```

**Event: `Extract_OnFailure`**
The errorMessage field is populated with the error that caused the failure.

```json
{
  "eventType": "Extract_OnFailure",
  "status": "Failure",
  "errorMessage": "Figma API error (403): Invalid personal access token.",
  "metadata": {
     "source": "figma"
  }
}
```

### Blocking Webhook Enhancement

The `BEFORE_COMMIT` webhook is special. An external system can return a 2xx status to approve the commit, or a non-2xx status to reject it. When rejecting, it can optionally return a JSON body to provide a reason for the failure, which will be logged by the tool.

**Example Rejection Response (HTTP 400 Bad Request):**

```json
{
  "approved": false,
  "reason": "Accessibility check failed: Color 'brand-primary-weak' has a contrast ratio of 1.2, which is below the required 4.5.",
  "validationReportUrl": "https://my-ci-server.com/build/123/artifacts/accessibility-report.html"
}
```

The tool will log the reason and fail the build, preventing the commit.

## 10. Appendix: Pipeline Integration Example (Azure DevOps)

This example YAML demonstrates how to integrate the Design Token Generator into a multi-stage Azure DevOps pipeline. It shows how to run the tool in a container, pass secrets, and use the generated files as an artifact for a subsequent build job.

```yaml
variables:
  # Define a variable group with your secrets and environment-specific settings
  - group: DesignToken-Prod-Vars

stages:
- stage: Generate_Tokens
  displayName: 'Generate Design Tokens'
  jobs:
  - job:
    pool:
      vmImage: 'ubuntu-latest'
    steps:
    - checkout: self
      persistCredentials: true # Allows the tool to use the pipeline's Git token

    - task: Bash@3
      displayName: 'Run 3SC Design Token Generator'
      env:
        # --- Map secrets from the variable group ---
        TOKENS_FIGMA_TOKEN_SECRET_NAME: $(FigmaTokenSecretName)
        TOKENS_VAULT_TYPE: $(VaultType)
        TOKENS_VAULT_URL: $(VaultUrl)
        TOKENS_AZURE_CLIENT_ID: $(AzureClientId)
        TOKENS_AZURE_CLIENT_SECRET: $(AzureClientSecret)
        TOKENS_AZURE_TENANT_ID: $(AzureTenantId)
        
        # --- Standard Configuration ---
        TOKENS_DESIGN_PLATFORM: 'figma'
        TOKENS_TARGET_PLATFORM: 'ios'
        TOKENS_REPO_URL: $(Build.Repository.Uri)
        TOKENS_BRANCH: $(Build.SourceBranchName)
        TOKENS_GIT_AUTO_COMMIT: 'true'
        TOKENS_FIGMA_URL: 'https://www.figma.com/design/your-file-id/your-project'
        
        # --- Control Point Example ---
        TOKENS_CP_ON_RUN_FAILURE: 'https://hooks.slack.com/services/...'

      script: |
        # Run the tool from the official Docker image.
        # It operates on the code in the current directory, which is mounted into /src.
        docker run --rm \
          -v $(Build.SourcesDirectory):/src \
          -w /src \
          --env-file <(env | grep -E 'TOKENS_|3SC_') \
          3sc/design-token-generator:latest

    - task: PublishBuildArtifacts@1
      displayName: 'Publish Generated Token Source'
      inputs:
        PathtoPublish: '$(Build.SourcesDirectory)/UI/iOS/style'
        ArtifactName: 'iOSDesignTokens'

- stage: Build_iOS_App
  displayName: 'Build iOS Application'
  dependsOn: Generate_Tokens
  jobs:
  - job:
    pool:
      vmImage: 'macOS-latest'
    steps:
    - task: DownloadBuildArtifacts@1
      displayName: 'Download Generated Design Tokens'
      inputs:
        buildType: 'current'
        downloadType: 'single'
        artifactName: 'iOSDesignTokens'
        downloadPath: '$(System.ArtifactsDirectory)'

    # - task: Xcode@5
    #   displayName: 'Build the iOS App'
    #   inputs:
    #     # The Xcode build will now automatically include the newly
    #     # generated Swift files from the artifact.
    #     actions: 'build'
    #     # ... other Xcode build settings
```

## 11. DX Server Endpoints

For local development and debugging, the container runs a lightweight web server on port 8080. You can run the container locally and expose this port to access documentation.

**Example Local Run:**

```bash
docker run --rm -it -p 8080:8080 3sc/design-token-generator:latest
```

You can then access `http://localhost:8080` in your browser.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | Redirects to `/docs` |
| GET | `/health` | A simple health check endpoint. Returns `{"status": "healthy"}` |
| GET | `/docs` | Serves the main tool documentation (this guide) |
| GET | `/docs/android` | Serves the Android-specific generation documentation and examples |
| GET | `/docs/ios` | Serves the iOS-specific generation documentation and examples |
| GET | `/docs/web` | Serves the Web-specific generation documentation and examples |