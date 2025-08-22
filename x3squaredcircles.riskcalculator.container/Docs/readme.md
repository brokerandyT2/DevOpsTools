# 3SC Risk Calculator: Definitive Operations Guide (v1.0)

**Audience:** DevOps Engineers, Platform Engineers, Team Leads, Software Architects

### 1. Tool Description

The 3SC Risk Calculator is a language-agnostic, containerized command-line tool that automates the detection of emerging risk in a software project. It operates on the principle that **change velocity is a direct predictor of entropy and risk**. By analyzing the patterns of change in a Git repository, it identifies "hotspots"—areas of the codebase experiencing unusually high or accelerating activity—and provides actionable intelligence to engineering teams.

The tool is designed for seamless CI/CD integration. It maintains its own state within the repository, allowing for highly efficient delta analysis on every run. It is configured entirely via environment variables and provides Control Points (webhooks) for deep integration with external governance and monitoring systems.

### 2. Core Concepts

*   **Velocity → Entropy → Risk:** The tool does not parse code for quality. It operates on the fundamental principle that the faster a specific area of code is changing, the higher the probability of defects, regressions, or architectural decay.
*   **Ranking-Based Alerting:** The tool's primary output is a ranked list of the riskiest areas. It alerts on **relative shifts in this ranking**, not on absolute risk scores. An area that is consistently a hotspot is a known quantity; an area that suddenly *becomes* a hotspot is an emerging risk that requires attention.
*   **Leaf Directory Granularity:** Analysis is performed on the bottom-most ("leaf") directories that contain source code. This provides a meaningful, component-level view of risk without the noise and performance cost of analyzing every individual file.
*   **Self-Contained State:** The tool manages its own state by committing a `change-analysis.json` file back to the repository and using a `change-analysis-last-run` Git tag. This enables lightning-fast delta analysis on subsequent runs.

### 3. Universal Environment Variables (`3SC_`)

These standard variables control foundational features. A tool-specific variable (e.g., `RISKCALC_VERBOSE`) will always override its `3SC_` counterpart.

| Variable                 | Description                                                 | Example Value(s)                                 |
| ------------------------ | ----------------------------------------------------------- | ------------------------------------------------ |
| `3SC_LICENSE_SERVER`     | The URL of the 3SC license validation server.               | `https://license.3sc.io`                         |
| `3SC_LOG_ENDPOINT_URL`   | A "firehose" endpoint URL for external logging.             | `https://splunk.mycorp.com:8088/services/collector` |
| `3SC_LOG_ENDPOINT_TOKEN` | The authentication token for the external logging endpoint. | *(from secret)*                                  |
| `3SC_LOG_LEVEL`          | The logging verbosity for the tool.                         | `Debug`, `Information`, `Warning`, `Error`       |
| `3SC_VAULT_TYPE`         | The type of key vault to use for secrets.                   | `Azure`, `Aws`, `HashiCorp`                      |
| `3SC_VAULT_URL`          | The URL of the key vault instance.                          | `https://my-vault.vault.azure.net`               |

### 4. Core Configuration (`RISKCALC_`)

| Variable                        | Required? | Default       | Description                                                                                                                              |
| ------------------------------- | --------- | ------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `RISKCALC_REPO_URL`             | **Yes**   | (none)        | The clone URL of the Git repository. Often provided by the CI system.                                                                    |
| `RISKCALC_BRANCH`               | **Yes**   | (none)        | The name of the current Git branch being processed. Often provided by the CI system.                                                     |
| `RISKCALC_ALERT_THRESHOLD`      | No        | `2`           | Alert the pipeline (exit code 70) if a tracked area moves up this many positions or more in the risk rankings.                            |
| `RISKCALC_FAIL_THRESHOLD`       | No        | `5`           | Fail the pipeline (exit code 71) if a tracked area moves up this many positions or more in the risk rankings.                             |
| `RISKCALC_ALERT_ON_NEW_ENTRIES` | No        | `true`        | If `true`, trigger an alert if a new, previously untracked area enters the risk rankings above the minimum percentile.                   |
| `RISKCALC_MINIMUM_PERCENTILE`   | No        | `70`          | The minimum risk percentile an area must reach to be included in the active rankings and alerting. (Range: 0-100).                   |
| `RISKCALC_EXCLUDED_AREAS`       | No        | (none)        | A semicolon-separated list of directory paths (from the repo root) to exclude from analysis (e.g., `src/Generated;Tests/Mocks`).       |
| `RISKCALC_PAT_SECRET_NAME`      | **Yes**   | (none)        | The name/key of the secret in the configured vault that holds a Git PAT with push access. **Required** for committing analysis files.     |
| `RISKCALC_VERBOSE`              | No        | `false`       | If `true`, enables detailed `VERBOSE` output, including full rankings and blast radius analysis. Overrides `3SC_LOG_LEVEL`. |

### 5. Control Point Configuration

Control Points are webhooks invoked at key stages for deep integration and governance.

| Variable                        | Type         | Description                                                                                                                              |
| ------------------------------- | ------------ | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `RISKCALC_CP_GITANALYSIS_AFTER` | **Blocking** | Invoked after Git analysis is complete. The webhook can inspect the file changes and abort if they violate policy. Payload: `GitDelta`.        |
| `RISKCALC_CP_RISKANALYSIS_AFTER`| **Blocking** | Invoked after risk calculation. The webhook can inspect the "before" and "after" state of the risk rankings and abort. Payload: `AnalysisState`. |
| `RISKCALC_CP_DECISION_BEFORE`   | **Blocking** | The final gate. Invoked before the PASS/ALERT/FAIL decision is finalized. The webhook can override the decision. Payload: `RiskAnalysisResult`. |

### 6. CI/CD Integration

The tool communicates its findings via exit codes, enabling automated pipeline decisions.

*   **Exit Code 0 (`Pass`):** No significant risk pattern changes were detected. The pipeline can proceed.
*   **Exit Code 70 (`Alert`):** Risk patterns have shifted. This should be treated as a warning. The pipeline could be paused for human review or trigger notifications.
*   **Exit Code 71 (`Fail`):** Critical risk pattern changes were detected. This should fail the pipeline build and require immediate intervention.

### 7. Appendix: Pipeline Integration Example (Azure DevOps)

```yaml
stages:
- stage: Risk_Analysis
  displayName: 'Calculate Code Risk'
  jobs:
  - job:
    pool:
      vmImage: 'ubuntu-latest'
    steps:
    - checkout: self
      fetchDepth: 0 # Required to analyze full git history
      persistCredentials: true # Allows tool to push updates

    - task: Bash@3
      displayName: 'Run 3SC Risk Calculator'
      continueOnError: true # Allow the task to 'fail' with an ALERT exit code
      env:
        # --- Universal Vars ---
        "3SC_LICENSE_SERVER": $(LicenseServer)
        "3SC_VAULT_TYPE": "Azure"
        "3SC_VAULT_URL": $(KeyVaultUrl)
        # Assumes service connection provides AZURE_CLIENT_ID, etc.
        
        # --- Core Config ---
        "RISKCALC_REPO_URL": $(Build.Repository.Uri)
        "RISKCALC_BRANCH": $(Build.SourceBranchName)
        "RISKCALC_PAT_SECRET_NAME": "github-pat-for-riskcalc" # Secret name in Key Vault
        "RISKCALC_ALERT_THRESHOLD": "3"

      script: |
        docker run --rm \
          -v $(Build.SourcesDirectory):/src \
          --env-file <(env | grep -E 'RISKCALC_|3SC_|AZURE_') \
          3sc/risk-calculator:latest
        
        # Capture the exit code from the container run
        export EXIT_CODE=$?
        echo "Risk Calculator exited with code $EXIT_CODE"
        
        # Set pipeline variables based on exit code
        if [ $EXIT_CODE -eq 70 ]; then
          echo "##vso[task.setvariable variable=RiskDecision;isOutput=true]Alert"
        elif [ $EXIT_CODE -eq 71 ]; then
          echo "##vso[task.setvariable variable=RiskDecision;isOutput=true]Fail"
        else
          echo "##vso[task.setvariable variable=RiskDecision;isOutput=true]Pass"
        fi
        
        # Exit with the original code to correctly reflect status
        exit $EXIT_CODE

    # This is a subsequent step in the same job
    - task: Bash@3
      displayName: 'Evaluate Risk Decision'
      condition: succeededOrFailed() # Run this step even if the previous one 'failed'
      inputs:
        targetType: 'inline'
        script: |
          if [ "$(RiskCalculator.RiskDecision)" == "Fail" ]; then
            echo "##vso[task.logissue type=error]Critical risk shift detected. Build failed."
            echo "##vso[task.complete result=Failed;]"
          elif [ "$(RiskCalculator.RiskDecision)" == "Alert" ]; then
            echo "##vso[task.logissue type=warning]Risk pattern shift detected. Manual review is recommended."
            # This allows the pipeline to continue but flags it as unstable.
            echo "##vso[task.complete result=SucceededWithIssues;]"
          else
            echo "No significant risk detected. Build can proceed."
          fi