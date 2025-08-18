# 3SC API Assembler for Python

This document provides a comprehensive guide for using the 3SC API Assembler with a Python business logic layer.

## Core Concept

The Assembler works by analyzing your Python source code (`.py` files). It discovers classes and methods decorated with the 3SC DSL decorators. It then generates a new, thin API project (e.g., a FastAPI application) that acts as a secure HTTP shim over your existing logic.

**Your business logic is never modified.** The generated API is a separate, buildable artifact. It is highly recommended to use **PEP 484 type hints** in your business logic for the most accurate and type-safe API generation.

---

## Step 1: Get the 3SC DSL File

The Assembler container serves the required Python DSL file from its built-in web server. Download this file and add it to a shared location in your Python project.

```bash
# From the root of your project, while the Assembler container is running:
curl http://localhost:8080/code/python > ./my_project/common/three_sc_dsl.py
```

## Step 2: Define Your Infrastructure in Code

Create a `deployment_definition.py` file to define your deployment groups and service contracts. This class provides the symbols for decorating your services.

```python
# my_project/deployment_definition.py
from my_project.common.three_sc_dsl import Cloud, DeploymentPattern, group, contract
from my_project.security import JwtAuthenticationService # Your implementation

class DeploymentDefinition:
    # Define the deployment target
    class Groups:
        @group(cloud=Cloud.GCP, pattern=DeploymentPattern.FUNCTIONS)
        def CustomerServices(self): pass

    # Map the IAuthenticationHooks contract to your concrete implementation
    class Contracts:
        @contract(implemented_by=JwtAuthenticationService)
        def Authentication(self): pass
```

## Step 3: Decorate Your Business Logic

Apply the DSL decorators to your existing service classes to expose them as API endpoints.

```python
# my_project/services/customer_service.py
from my_project.common.three_sc_dsl import api_endpoint, deploy_to, requires_connection, http_get, requires, SecretProvider
from my_project.deployment_definition import DeploymentDefinition

@api_endpoint("/api/customers")
@deploy_to("CustomerServices") # The string name must match the property name in DeploymentDefinition
@requires_connection(provider=SecretProvider.GCP_SECRET_MANAGER, contract="PrimaryDatabase")
class CustomerService:
    
    def __init__(self, repo: CustomerRepository):
        self.repo = repo

    @http_get("/{customer_id}")
    @requires(contract="Authentication", method="validate_token")
    def get_by_id(self, customer_id: int) -> Customer:
        return self.repo.get(customer_id)
```

## Step 4: Configure the connections.manifest.json

Create a `connections.manifest.json` file in your repository root. This file maps the logical contract names from your code (e.g., "PrimaryDatabase") to the actual secret IDs in your secret manager for each environment.

```json
{
  "connections": {
    "PrimaryDatabase": {
      "dev": "projects/my-proj/secrets/sql-dev-secret/versions/latest",
      "qa": "projects/my-proj/secrets/sql-qa-secret/versions/latest",
      "prod": "projects/my-proj/secrets/sql-prod-secret/versions/latest"
    }
  }
}
```

## Step 5: CI/CD Pipeline Configuration

The pipeline consists of three stages: generate source, build (e.g., package into a zip) the generated source, and deploy the built artifact.

```yaml
# azure-pipelines.yml

variables:
  # --- Core Config ---
  # Note: For Python, LIBS and SOURCES point to the same source directory
  ASSEMBLER_LIBS: '$(Build.SourcesDirectory)/my_project/'
  ASSEMBLER_SOURCES: '$(Build.SourcesDirectory)/my_project/'
  ASSEMBLER_ENV: 'prod'
  # ... other shared variables (LICENSE_SERVER, REPO_URL, etc.)

jobs:
# 1. GENERATE the API Shim Source Code
- job: Generate
  displayName: 'Assemble API Source Code'
  pool:
    vmImage: 'ubuntu-latest'
  steps:
  - script: |
      docker run --rm \
        -v $(System.DefaultWorkingDirectory):/src \
        -e ASSEMBLER_LIBS -e ASSEMBLER_SOURCES -e ASSEMBLER_ENV -e LICENSE_SERVER \
        my-registry.azurecr.io/3sc-api-assembler:latest \
        generate --output ./generated
    displayName: 'Run 3SC API Assembler (Generate)'
  - publish: $(System.DefaultWorkingDirectory)/generated
    artifact: GeneratedApiSource

# 2. BUILD the Generated Source Code (e.g., installing dependencies and creating a zip)
- job: Build
  displayName: 'Build Generated API'
  dependsOn: Generate
  pool:
    vmImage: 'ubuntu-latest'
  steps:
  - task: UsePythonVersion@0
    inputs:
      versionSpec: '3.11'
  - download: current
    artifact: GeneratedApiSource
  - script: |
      cd $(Pipeline.Workspace)/GeneratedApiSource/CustomerServices
      python -m venv venv
      source venv/bin/activate
      pip install -r requirements.txt
      # Zip the contents for deployment
      zip -r ../customer-api.zip .
    displayName: 'Install Dependencies and Package Artifact'
  - publish: $(Pipeline.Workspace)/GeneratedApiSource/customer-api.zip
    artifact: CompiledApi

# 3. DEPLOY the Built Artifact
- job: Deploy
  displayName: 'Deploy Compiled API'
  dependsOn: Build
  pool:
    vmImage: 'ubuntu-latest'
  steps:
  - download: current
    artifact: CompiledApi
  - script: |
      docker run --rm \
        -v $(System.DefaultWorkingDirectory):/src \
        -e ASSEMBLER_ENV -e LICENSE_SERVER \
        my-registry.azurecr.io/3sc-api-assembler:latest \
        deploy --group CustomerServices --artifact-path $(Pipeline.Workspace)/CompiledApi/customer-api.zip
    displayName: 'Run 3SC API Assembler (Deploy)'
```