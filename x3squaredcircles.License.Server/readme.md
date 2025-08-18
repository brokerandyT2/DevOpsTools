3SC Licensing Container (LC) - Installation & Configuration Guide

Version: 2.0
Audience: DevOps Engineers, Site Reliability Engineers, and System Administrators

1. Overview

The 3SC Licensing Container (LC) is a secure, self-hosted service that provides real-time license enforcement for the 3SC Automated Governance Platform. It is delivered as a standard container image and is designed to run 24/7 within your secure infrastructure.

This document provides the complete instructions for deploying and configuring the LC.

2. Core Architectural Requirements

The LC is a stateful service and has two fundamental requirements for its operating environment:

Persistent, Encrypted Storage: The LC requires a persistent volume to be mounted at the /data path inside the container. This volume is used to store the license.db SQLite database, which tracks real-time concurrency and historical usage. It is a mandatory security requirement that this volume be encrypted at rest using your infrastructure provider's standard tools (e.g., AWS EBS with KMS, Azure Disk Encryption).

Network Accessibility: The LC must be accessible via a stable network address (e.g., a Kubernetes Service DNS name) from the CI/CD runners where the 3SC toolchain will be executed.

3. Initial Setup: Environment Variables

All configuration for the LC is provided via environment variables at runtime. This is the complete list of required variables.

Critical Configuration (Required for all deployments):

Environment Variable	Description	Example Value
ACCEPT_EULA	Confirms that you accept the 3SC End User License Agreement. The container will fail to start if this is not set to Y.	Y
CICD_PLATFORM_URL	The base URL for your CI/CD provider's API. This is used by the daily service to fetch the contributor count for the annual true-up.	https://dev.azure.com/YourOrganization
CICD_PLATFORM_PAT	A Personal Access Token (PAT) with read-only access to your CI/CD provider's user/graph API. The token should have the minimum permissions required to list users and their access levels. Store this as a secret.	ghp_... or azp_...
4. Deployment Examples

You will receive a custom container image tag from the 3SC customer portal (e.g., registry.3sc.com/customer-abc/license-container:1.2.3). Use this tag in the deployment manifests below.

This is the recommended deployment method for production environments.

deployment.yaml

code
Yaml
download
content_copy
expand_less

apiVersion: apps/v1
kind: Deployment
metadata:
  name: sc-licensing-service
  labels:
    app: sc-license
spec:
  replicas: 1 # IMPORTANT: The LC must run as a single instance.
  selector:
    matchLabels:
      app: sc-license
  template:
    metadata:
      labels:
        app: sc-license
    spec:
      containers:
      - name: license-container
        image: registry.3sc.com/customer-abc/license-container:1.2.3 # <-- YOUR CUSTOM IMAGE TAG
        ports:
        - containerPort: 80
        env:
        - name: ACCEPT_EULA
          value: "Y"
        - name: CICD_PLATFORM_URL
          value: "https://dev.azure.com/YourOrganization"
        - name: CICD_PLATFORM_PAT
          valueFrom:
            secretKeyRef:
              name: sc-license-secrets # A K8s secret you create
              key: cicdPlatformPat
        volumeMounts:
        - name: license-data
          mountPath: /data
      volumes:
      - name: license-data
        persistentVolumeClaim:
          claimName: license-data-pvc
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: license-data-pvc
spec:
  accessModes:
  - ReadWriteOnce
  resources:
    requests:
      storage: 1Gi # 1 GiB is sufficient for many years of data.
  # IMPORTANT: Ensure your default StorageClass is configured for encryption.
---
apiVersion: v1
kind: Service
metadata:
  name: sc-licensing-service
spec:
  selector:
    app: sc-license
  ports:
  - protocol: TCP
    port: 80
    targetPort: 80

docker-compose.yml

code
Yaml
download
content_copy
expand_less
IGNORE_WHEN_COPYING_START
IGNORE_WHEN_COPYING_END
version: '3.8'

services:
  licensing:
    image: registry.3sc.com/customer-abc/license-container:1.2.3 # <-- YOUR CUSTOM IMAGE TAG
    container_name: sc_licensing_service
    restart: unless-stopped
    ports:
      - "8080:80"
    environment:
      - ACCEPT_EULA=Y
      - CICD_PLATFORM_URL=https://dev.azure.com/YourOrganization
      - CICD_PLATFORM_PAT=${CICD_PLATFORM_PAT} # Load from a .env file for security
    volumes:
      - license_data:/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost/health"]
      interval: 30s
      timeout: 10s
      retries: 3

volumes:
  license_data:
    driver: local
5. Post-Deployment Verification

Once the container is running, you can verify its status.

Check Logs: The container logs should show a successful startup sequence, including:

✓ Database schema is up to date.

✓ License configuration loaded and validated successfully.

-> Daily contributor count service is running in the background.

3SC Licensing Container Started Successfully

Hit the Health Endpoint: From within your network, access the service's health endpoint.

curl http://<your-service-address>/health

You should receive a 200 OK response with a status of "healthy".

6. Toolchain Configuration

The final step is to configure your CI/CD pipelines to use the LC. In each pipeline where 3SC tools are used, you must set the following environment variable:

Environment Variable	Description	Example Value
LICENSE_SERVER	The address of your running LC service.	http://sc-licensing-service.your-namespace.svc.cluster.local
7. Annual Renewal & Reporting

The embedded license key is valid for 380 days.

Warning Period: 45 days before expiration, the 3SC tools will begin logging a non-blocking warning in your pipelines.

Enforcement Period: 7 days after expiration, the 3SC tools will pause your pipelines and require manual approval to proceed.

To renew:

Access the LC's secure admin endpoint at http://<your-service-address>/license/report. This will generate and download an encrypted annual_usage_report.bin file.

Upload this file to your secure portal at portal.3squaredcircles.com.

Upon payment of the renewal invoice (including any user true-up), your portal will provide you with the new container image tag for the next year.

Update your deployment manifest with the new image tag and redeploy the container. No other changes are needed.