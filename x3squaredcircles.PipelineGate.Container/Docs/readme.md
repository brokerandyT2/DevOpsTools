# 3SC Pipeline Gate Controller: Definitive Operations Guide (v1.0)

**Audience:** DevOps Engineers, Platform Engineers, Release Managers, Software Architects

### 1. Tool Description
The 3SC Pipeline Gate Controller is a universal, containerized utility that serves as an intelligent, policy-driven control mechanism for CI/CD pipelines. It evaluates data from external systems, APIs, and files against a configurable set of rules to make a `PASS`, `PAUSE`, or `BREAK` decision, allowing organizations to automate complex approval workflows and enforce governance policies.

The tool is designed as a composable building block. It is called multiple times within a pipeline to perform distinct, single-purpose actions, such as sending a notification, waiting for a human approval, or validating an API's health.

### 2. Core Concepts
*   **Stateless Pipeline Utility:** The tool has no internal state between runs. It is executed for a single purpose and exits. The CI/CD platform (e.g., Azure DevOps, GitHub Actions) is responsible for the overall workflow and for passing context (like a `RunId` or `TicketId`) between different executions of the tool.
*   **Mode-Driven Behavior:** The tool's function is determined by a `GATE_MODE` variable, which selects one of three distinct behaviors: a simple synchronous check (`Basic`), an asynchronous workflow pair (`Advanced`), or a powerful API contract-driven check (`Custom`).
*   **Environment-Driven Configuration:** All behavior is controlled via environment variables. The pipeline definition is the single, explicit source of truth for the tool's behavior.
*   **Unified Evaluation Engine:** The core of the tool is a powerful but safe evaluation engine that can parse responses in both **JSON** and **XML**. It uses industry-standard JSONPath and XPath to traverse data structures, and performs intelligent type casting for comparisons.

### 3. Universal Environment Variables (`3SC_`)
These standard variables control foundational features. A tool-specific variable will always override its `3SC_` counterpart.

| Variable                 | Description                                                 | Example Value(s)                                 |
| ------------------------ | ----------------------------------------------------------- | ------------------------------------------------ |
| `3SC_VAULT_TYPE`         | The type of key vault to use for secrets.                   | `Azure`, `Aws`, `HashiCorp`                      |
| `3SC_VAULT_URL`          | The URL of the key vault instance.                          | `https://my-vault.vault.azure.net`               |
| `3SC_LOG_LEVEL`          | The logging verbosity for the tool.                         | `Debug`, `Information`, `Warning`, `Error`       |
| `3SC_LOG_ENDPOINT_URL`   | A "firehose" endpoint URL for external logging.             | `https://splunk.mycorp.com:8088/services/collector` |
| `3SC_LOG_ENDPOINT_TOKEN` | The authentication token for the external logging endpoint. | *(from secret)*                                  |

### 4. Core Configuration (`GATE_`)
The primary variable determines the tool's behavior for a specific execution.

| Variable        | Required? | Description                                                                |
| --------------- | --------- | -------------------------------------------------------------------------- |
| `GATE_MODE`     | **Yes**   | The operational mode for this run. `Basic`, `Advanced`, or `Custom`.       |
| `GATE_CI_RUN_ID`| No        | A unique identifier for the pipeline run, used for correlation in async workflows. Often provided by the CI system (e.g., `$(Build.BuildId)`). |

---
### Mode 1: `Basic` (Synchronous Check)
For simple, immediate "check this for that" validation.

| Variable                  | Required? | Default | Description                                                                      |
| ------------------------- | --------- | ------- | -------------------------------------------------------------------------------- |
| `GATE_BASIC_URL`          | **Yes**   | (none)  | The full URL of the endpoint to check. Can contain pipeline variables.           |
| `GATE_BASIC_SECRET_NAME`  | No        | (none)  | The name of the secret in the vault holding the Bearer token for authentication. |
| `GATE_BASIC_SUCCESS_EVAL` | **Yes**   | (none)  | The evaluation expression that must be `true` for the gate to `PASS`.            |
| `GATE_BASIC_DEFAULT_ACTION` | No      | `Break` | The action to take (`Break` or `Pause`) if the success condition is not met.     |

---
### Mode 2: `Advanced` (Asynchronous Workflow)
A pair of actions (`Notify` and `WaitFor`) used to manage long-running processes.

| Variable                    | Required? | Description                                                                                                   |
| --------------------------- | --------- | ------------------------------------------------------------------------------------------------------------- |
| `GATE_ADVANCED_ACTION`      | **Yes**   | The sub-mode to execute. Must be `Notify` or `WaitFor`.                                                       |

**For `GATE_ADVANCED_ACTION=Notify`:**
| Variable                  | Required? | Description                                                                                                   |
| ------------------------- | --------- | ------------------------------------------------------------------------------------------------------------- |
| `GATE_ADVANCED_NOTIFY_URL`    | **Yes**   | The webhook URL to send the notification to.                                                                  |
| `GATE_ADVANCED_NOTIFY_PAYLOAD`  | **Yes**   | The JSON payload to send. The tool automatically injects a `context` object with `pipelineRunId`, repo, branch, etc. |

**For `GATE_ADVANCED_ACTION=WaitFor`:**
| Variable                        | Required? | Default | Description                                                                                                           |
| ------------------------------- | --------- | ------- | --------------------------------------------------------------------------------------------------------------------- |
| `GATE_ADVANCED_WAIT_URL`        | **Yes**   | (none)  | The base URL to poll. The tool will automatically append `?runId=<GATE_CI_RUN_ID>` to this URL.                       |
| `GATE_ADVANCED_WAIT_SECRET_NAME`  | No        | (none)  | The name of the secret in the vault holding the Bearer token for authentication.                                      |
| `GATE_ADVANCED_WAIT_SUCCESS_EVAL` | **Yes**   | (none)  | The evaluation expression that must be `true` for the gate to `PASS`.                                                 |
| `GATE_ADVANCED_WAIT_FAILURE_EVAL` | No        | `false` | An optional expression that, if `true`, will immediately `BREAK` the gate.                                            |
| `GATE_ADVANCED_WAIT_DEFAULT_ACTION` | No      | `Pause`   | The action if neither success nor failure is met. `Pause` continues polling. `Break` would stop on the first non-success. |
| `GATE_ADVANCED_WAIT_TIMEOUT_MINUTES` | No    | `30`    | Total time to continue polling before timing out.                                                                     |
| `GATE_ADVANCED_WAIT_POLL_INTERVAL_SECONDS` | No | `15`  | How often to poll the endpoint.                                                                                       |

---
### Mode 3: `Custom` (API Contract-Driven Gate)
For complex, structured interactions with an API defined by a Swagger/OpenAPI specification.

| Variable                        | Required? | Description                                                                                                                                       |
| ------------------------------- | --------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GATE_CUSTOM_SWAGGER_PATH`      | **Yes**   | The local path inside the container (e.g., `/src/docs/api.swagger.json`) to the API specification file.                                             |
| `GATE_CUSTOM_SECRET_NAME`       | No        | The name of the secret in the vault for API authentication.                                                                                       |
| `GATE_CUSTOM_OPERATION_ID`      | **Yes**   | The `operationId` from the Swagger file for the API endpoint to be called.                                                                        |
| `GATE_CUSTOM_PARAM_{LOC}_{NAME}`  | **Maybe** | A parameter to be passed to the operation. `{LOC}` can be `PATH`, `QUERY`, `HEADER`. `{NAME}` is the parameter name from the spec.              |
| `GATE_CUSTOM_PARAM_BODY`        | **Maybe** | The JSON payload for the request body. Can contain `${...}` substitutions for pipeline variables.                                                 |
| `GATE_CUSTOM_SUCCESS_EVAL`      | **Yes**   | The evaluation expression that must be `true` for the gate to `PASS`.                                                                             |
| `GATE_CUSTOM_FAILURE_EVAL`      | No        | An optional expression that, if `true`, will immediately `BREAK` the gate.                                                                        |
| `GATE_CUSTOM_DEFAULT_ACTION`    | No        | `Break`   | The action to take if neither success nor failure conditions are met.                                                                 |

---
### 5. The Evaluation DSL
The `_EVAL` variables use a simple but powerful Domain Specific Language with the format `LHS Operator RHS`.

**LHS (Left-Hand Side): Path Traversal**
*   **For JSON:** `jsonpath($.path.to.value[0])` - Uses standard JSONPath syntax.
*   **For XML:** `xpath(/path/to/value/@attribute)` - Uses standard XPath 1.0 syntax.

**RHS (Right-Hand Side): Typed Literals**
The type of the comparison is determined by the format of the RHS.
*   `'a string'` (in quotes): String comparison.
*   `123` or `-45.6`: Numeric comparison.
*   `true` or `false`: Boolean comparison.
*   `null`: Null check.

**Operators:** `==`, `!=`, `>`, `>=`, `<`, `<=`, `contains`, `not contains`.

**Composition:** You can combine multiple conditions with `AND` or `OR`. Use parentheses `()` for grouping.
`"jsonpath($.riskScore) < 80 AND jsonpath($.approved) == true"`

---
### 6. Control Point: The Ultimate Escape Hatch
For truly complex logic that cannot be expressed in the DSL, the tool provides one powerful, blocking Control Point.

| Variable                    | Description                                                                                                         |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `GATE_CP_BEFORE_DECISION`   | A webhook URL. Invoked just before the tool makes its final decision. The webhook can inspect the full context (API response, initial evaluation) and return a response to **override** the tool's proposed action. |

### 7. CI/CD Integration & Exit Codes
*   **Exit Code 0 (`Pass`):** The success condition was met. The pipeline should proceed.
*   **Exit Code 70 (`Pause`):** A neutral/pending state was reached. The pipeline should typically treat this as a "successful failure" (e.g., mark the build as unstable but don't fail).
*   **Exit Code 71 (`Break`):** A failure condition was met or a critical error occurred. This should fail the pipeline build.

### 8. DX Server Endpoints
Run `docker run --rm -it -p 8080:8080 3sc/pipeline-gate-controller:latest` to access the DX server.
*   **/health**: A simple health check endpoint.
*   **/docs**: Serves this complete operations guide.