# 3SC Conduit: The Universal Service Synthesizer

**Version:** 1.0  
**Project Codename:** Phoenix  

---

## 1. Overview

**3SC Conduit** is a "forward-only," source-to-source transformation engine. Its purpose is to generate the complete, human-readable source code for a governed, testable, and deployable infrastructure **shim**.

It works by reading your high-level business logic from a source repository and generating the low-level infrastructure plumbing (event listeners, HTTP triggers, DI containers, etc.) required to make that logic run as a robust, event-driven service in the cloud.

The core philosophy is simple: **We generate the plumbing, you own the code.**

## 2. The Definitive Workflow

Conduit is built around a definitive, three-repository GitOps workflow that ensures traceability, testability, and a clean separation of concerns.

#### **Repository A: Business Logic Repo**
*   **What it is:** The source of truth for your business logic. Developers write their C#, Java, or Python code here.
*   **The DX:** Developers create a class (e.g., `OrderProcessor`) and mark it with a `[DataConsumer]` attribute. They write a public method (e.g., `HandleNewOrder`) and mark it with a `[Trigger]` attribute to define the event that should invoke it.
*   **The Pipeline:** This repo's CI/CD pipeline compiles the code, runs unit tests, and—most importantly—runs **`3SC Keystone`** to apply a semantic version tag (e.g., `v1.2.5`) to the commit.

#### **Repository B: The Conduit "Bridge" Pipeline**
*   **What it is:** A dedicated pipeline whose sole job is to run the `3SC Conduit` tool.
*   **The Trigger:** It is triggered by the new tag (`v1.2.5`) being pushed to the Business Logic Repo.
*   **The Action:** It runs the `datalink` container, providing it with the locations of the three repositories. Conduit then:
    1.  Finds the `v1.2.5` tag in the Business Logic Repo.
    2.  Clones the source code and the corresponding test code.
    3.  Generates the complete source code for the infrastructure shim.
    4.  Assembles a new test harness using your existing tests.
    5.  Runs the new test harness to validate the integration.
    6.  Commits the generated shim source code and test harness skeleton to the Destination Repo and applies the same `v1.2.5` tag.

#### **Repository C: The Shim Destination Repo**
*   **What it is:** The final, version-controlled home for the generated shim's source code.
*   **The Trigger:** This repo has its own CI/CD pipeline that is triggered when a new tag (e.g., `v1.2.5`) is pushed to it by Conduit.
*   **The Action:** This pipeline's only job is to **build, package, and deploy** the source code that is already in its repository. The developer has full control over this final step.

## 3. Configuration (Environment Variables)

Conduit is configured entirely via environment variables in the "Bridge" pipeline.

| Variable | Description | Example |
|---|---|---|
| `DATALINK_BUSINESS_LOGIC_REPO` | **Required.** The full Git URL to the repository containing your business logic. | `https://github.com/my-org/MyCompany.Logic` |
| `DATALINK_DESTINATION_REPO` | **Required.** The full Git URL to the repository where the generated shim code will be committed. | `https://github.com/my-org/MyCompany.Shim.OrderProcessor` |
| `DATALINK_DESTINATION_REPO_PAT` | **Required.** A Personal Access Token with write access to the destination repository. | `ghp_...` |
| `DATALINK_TEST_HARNESS_REPO` | **Optional.** The Git URL to the repository containing your tests. If omitted, the test assembly step is skipped. | `https://github.com/my-org/MyCompany.Logic.Tests` |
| `DATALINK_VERSION_TAG_PATTERN`| The pattern used to find the latest version tag in the business logic repo. | `v*` |
| `DATALINK_GENERATE_TEST_HARNESS`| If `true` (the default), the test harness skeleton will be generated. Set to `false` to disable. | `true` |
| `DATALINK_CONTINUE_ON_TEST_FAILURE` | If `true`, Conduit will commit the shim even if the assembled test harness fails. **Use with extreme caution.** | `false` |
| `DATALINK_LOG_LEVEL` | Sets the logging verbosity. Options: `Debug`, `Info`, `Warning`, `Error`. | `Info` |

---

## 4. The Developer Experience: Attributes (DSL)

To define a service, you use a simple set of attributes in your business logic source code. You can download these attribute definitions from the running container at the `/code/{language}` endpoint.

*   `[DataConsumer(ServiceName = "...")]`
    *   **Placement:** Class
    *   **Purpose:** Marks a class as a container for trigger methods. The `ServiceName` becomes the name of the generated project.

*   `[Trigger(Type = ..., Name = "...")]`
    *   **Placement:** Method
    *   **Purpose:** Marks a method as an event handler. The `Type` (e.g., `Http`, `AwsSqsQueue`) and `Name` (e.g., a URL route or queue name) define the event source. The method's first parameter is the DTO.

*   `[Requires(Handler = typeof(...), Method = "...")]`
    *   **Placement:** Method
    *   **Purpose:** Defines a pre-processing gate (e.g., authentication) that must pass before the trigger method is executed. Points to a specific class and method that returns a boolean.

*   `[RequiresLogger(Handler = typeof(...), Action = ...)]`
    *   **Placement:** Method
    *   **Purpose:** Injects a logging hook. The `Action` enum (`OnInbound`, `OnError`) specifies when the hook is called. Points to a class and method that implements the logging logic.

*   `[RequiresResultsLogger(..., Variable = "...")]`
    *   **Placement:** Method
    *   **Purpose:** Injects trace logging to capture the state of a specific local variable within your method for deep debugging.

For language-specific examples of how to use these attributes, see the documentation served from the running container at `/docs/{language}`.