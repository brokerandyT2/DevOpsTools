# 3SC API Assembler Documentation

## Step 1: Get the 3SC DSL File

The Assembler container serves the required JavaScript DSL file from its built-in web server. This file contains constants that are recommended for use in your JSDoc blocks to prevent typos.

```bash
# From the root of your project, while the Assembler container is running:
curl http://localhost:8080/code/javascript > ./src/common/three_sc_dsl.js
```

## Step 2: Define Your Infrastructure in Code

Create a `deployment.definition.js` file to define your deployment groups and service contracts. This file provides the symbols for decorating your services.

```javascript
// src/deployment.definition.js
const { JwtAuthenticationService } = require('../security/JwtAuthenticationService'); // Your implementation

/**
 * @namespace DeploymentDefinition
 */
module.exports = {
    /**
     * Deployment Groups
     * @memberof DeploymentDefinition
     */
    Groups: {
        /**
         * The primary customer services group for AWS Lambda.
         * @group {cloud: "AWS", pattern: "ApiGateway"}
         */
        CustomerServices: "CustomerServices"
    },

    /**
     * Service Contracts
     * @memberof DeploymentDefinition
     */
    Contracts: {
        /**
         * The main authentication contract.
         * @contract {implementedBy: "JwtAuthenticationService"}
         */
        Authentication: "IAuthenticationHooks"
    }
};
```

## Step 3: Decorate Your Business Logic with JSDoc

Apply the DSL JSDoc tags to your existing service classes to expose them as API endpoints.

```javascript
// src/services/CustomerService.js
const DeploymentDefinition = require('../deployment.definition');

/**
 * Service for handling customer business logic.
 * @apiEndpoint /api/customers
 * @deployTo CustomerServices
 * @requiresConnection {provider: "AWSSecretsManager", contract: "PrimaryDatabase"}
 */
module.exports = class CustomerService {
    
    constructor(repo) {
        this.repo = repo;
    }

    /**
     * Retrieves a customer by their unique ID.
     * @httpGet /:id
     * @requires {contract: "Authentication", method: "validateToken"}
     * @param {number} id The customer ID.
     * @returns {Promise<Customer>}
     */
    async getById(id) {
        return this.repo.get(id);
    }
}
```

## Step 4: Configure the connections.manifest.json

Create a `connections.manifest.json` file in your repository root. This file maps the logical contract names from your code to the actual secret names/ARNs in your secrets manager for each environment.

```json
{
  "connections": {
    "PrimaryDatabase": {
      "dev": "arn:aws:secretsmanager:us-east-1:123:secret:rds-dev-secret-abc",
      "qa": "arn:aws:secretsmanager:us-east-1:123:secret:rds-qa-secret-def",
      "prod": "arn:aws:secretsmanager:us-east-1:123:secret:rds-prod-secret-ghi"
    }
  }
}
```

## Step 5: CI/CD Pipeline Configuration

The pipeline consists of three stages: generate source, build (e.g., npm install and package) the generated source, and deploy the built artifact.

```yaml
# azure-pipelines.yml

variables:
  # --- Core Config ---
  # Note: For JavaScript, LIBS and SOURCES point to the same source directory
  ASSEMBLER_LIBS: '$(Build.SourcesDirectory)/src/'
  ASSEMBLER_SOURCES: '$(Build.SourcesDirectory)/src/'
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

# 2. BUILD the Generated Source Code (using npm)
- job: Build
  displayName: 'Build Generated API'
  dependsOn: Generate
  pool:
    vmImage: 'ubuntu-latest'
  steps:
  - task: NodeTool@0
    inputs:
      versionSpec: '18.x'
  - download: current
    artifact: GeneratedApiSource
  - script: |
      # The generated project will have its own package.json
      cd $(Pipeline.Workspace)/GeneratedApiSource/CustomerServices 
      npm install
      # Create a deployment package (e.g., zip)
      # This command would be defined in the generated package.json
      npm run package 
    workingDirectory: '$(Pipeline.Workspace)/GeneratedApiSource/CustomerServices'
    displayName: 'Install Dependencies and Package Artifact'
  - publish: $(Pipeline.Workspace)/GeneratedApiSource/CustomerServices/dist.zip
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
        deploy --group CustomerServices --artifact-path $(Pipeline.Workspace)/CompiledApi/dist.zip
    displayName: 'Run 3SC API Assembler (Deploy)'
```