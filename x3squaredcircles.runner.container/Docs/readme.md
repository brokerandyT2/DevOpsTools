# The 3SC Conductor

The Conductor is a high-fidelity, local-first pipeline runner designed to eliminate the CI feedback chasm and restore a zero-latency inner loop for developers. It runs your organization's exact, unmodified CI/CD pipeline definitions as a silent background service on your local machine, providing instantaneous, real-world feedback on every code change.

## Features

- **High-Fidelity Execution:** Parses and runs native pipeline files directly. What runs locally is what runs in production.
- **Multi-Platform Support:** Natively supports Azure DevOps, GitHub Actions, GitLab CI, and Jenkins (Declarative) pipelines.
- **Zero-Latency Feedback:** An intelligent file watcher automatically triggers a pipeline run on every source code change, giving you instant feedback.
- **Safe By Default:** A "Smart Bootstrapper" performs a safety analysis on your pipeline's first run, automatically disabling potentially destructive or long-running steps like deployments.
- **Declarative Control:** Manage the local execution flow with a simple, version-controllable `pipeline-config.json` file.
- **Interactive Debugging:** Pause execution after any step to inspect the state of your build, artifacts, and environment.

## Prerequisites

- Docker Desktop (or Docker Engine on Linux) must be installed and running.

## Getting Started

The Conductor runs as a single, "fire-and-forget" Docker container.

### 1. Running The Conductor

Open a terminal at the root of your project directory and run the following command:

```bash
docker run -d --name conductor_svc -p 35000:8080 \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v .:/src \
  x3squaredcircles.runner.container:latest
```

**Command Breakdown:**
- `-d`: Runs the container in detached (background) mode.
- `--name conductor_svc`: Gives the container a memorable name.
- `-p 35000:8080`: Exposes the Conductor's API port to your local machine.
- `-v /var/run/docker.sock...`: Allows the Conductor to run other Docker containers (the "Docker-out-of-Docker" pattern).
- `-v .:/src`: Mounts your current project directory into the container.

### 2. The First Run

On the very first run, The Conductor will:
1.  Detect your pipeline file (e.g., `.github/workflows/main.yml`).
2.  Perform a safety analysis.
3.  Generate a `pipeline-config.json` file next to your pipeline file. Steps deemed "unsafe" (like `deploy`) will be automatically set to `"skip"`.
4.  Begin watching for file changes.

### 3. Viewing Logs

To see the real-time output of your pipeline runs, open a new terminal pane and tail the container's logs:

```bash
docker logs -f conductor_svc
```

## Day-to-Day Workflow

### Automatic Runs
Simply save any source code file in your project. The Conductor will detect the change and automatically trigger a pipeline run. You will see the output in your `docker logs` terminal.

### Controlling the Pipeline (`pipeline-config.json`)
Open the generated `pipeline-config.json` file to control which steps are executed.

```json
{
  "version": "1.0",
  "steps": {
    "Build_Application": {
      "action": "run"
    },
    "Deploy_to_Dev": {
      "action": "skip" // Change to "run" to enable this step
    }
  }
}
```

- `"run"`: The step will execute normally.
- `"skip"`: The step will be skipped.
- `"pause_after"`: The pipeline will run this step and then pause execution.

### Working on the Pipeline Itself
The file watcher ignores changes to your pipeline YAML and `pipeline-config.json` to prevent restart loops. After editing these files, you must manually trigger a configuration refresh:

```bash
curl -X POST http://localhost:35000/refresh
```

The Conductor will reload the files. The next run (triggered by a source code change) will use the new configuration.

### Debugging a Step
1.  In `pipeline-config.json`, change the `action` of the step you want to debug to `"pause_after"`.
2.  Run `curl -X POST http://localhost:35000/refresh`.
3.  Trigger a pipeline run by saving a source file.
4.  The pipeline will pause after the designated step. The logs will instruct you to create a temporary file to continue:
    ```bash
    # Open a new terminal to send the 'continue' signal
    docker exec -it conductor_svc touch /tmp/conductor_continue
    ```

## API Endpoints

- `POST /refresh`: Forces the Conductor to reload the pipeline YAML and `pipeline-config.json` files from disk.
- `GET /docs`: Displays this README file.