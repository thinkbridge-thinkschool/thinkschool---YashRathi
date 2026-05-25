# Azure Setup — Day 5 / Piece 32

## Live API URL

[https://ca-api-nb3bgcnwnlpwe.lemoncliff-d4727121.southeastasia.azurecontainerapps.io/health
](https://piece33-verifyappinsights.happyhill-feb8a1b3.southeastasia.azurecontainerapps.io/)
---

# Overview

This project demonstrates deploying an ASP.NET Core API to Azure Container Apps using Azure Developer CLI (`azd up`).

The deployment automatically provisions and configures Azure resources, builds the Docker container, pushes the image to Azure Container Registry (ACR), and deploys the application to Azure Container Apps.

---

# Tech Stack

- ASP.NET Core Web API
- Azure Container Apps
- Azure Developer CLI (`azd`)
- Docker
- Azure Container Registry (ACR)
- Log Analytics Workspace
- SQLite

---

# Project Structure

```text
QuotesApi/
│
├── Program.cs
├── appsettings.json
├── Dockerfile
└── azure.yaml
```

---

# azure.yaml

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/Azure/azure-dev/main/schemas/v1.0/azure.yaml.json

name: quotes-api

services:
  api:
    project: ./QuotesApi
    language: dotnet
    host: containerapp
```

---

# Deployment Command

```bash
azd up
```

---

# azd up Output

```text
Initialize bicep provider

Provisioning and deploying (azd up)
Packaging overlaps with provisioning for faster execution.

  api: Packaging
Initialize bicep provider
  api: Packaging (Building Docker image)
Creating a deployment plan
Comparing deployment state
Validating deployment
  api: Packaging (Tagging container image)
Creating/Updating resources

  (✓) Done: Resource group: rg-quotesapi-dev
  (✓) Done: Container Registry: acrnb3bgcnwnlpwe
  (✓) Done: Log Analytics workspace: log-nb3bgcnwnlpwe
  (✓) Done: Container Apps Environment: cae-nb3bgcnwnlpwe
  (✓) Done: Container App: ca-api-nb3bgcnwnlpwe

  api: Publishing
  api: Publishing (Tagging container image)
  api: Publishing (Logging into container registry)
  api: Publishing (Pushing container image)

  api: Deploying
  api: Deploying (Updating container app revision)
  api: Deploying (Waiting for container revision)
  api: Deploying (Fetching endpoints for service)

  api: Done

SUCCESS: Your application was provisioned and deployed to Azure.
```

---

# Azure Resources Created

| Resource Type | Resource Name |
|---|---|
| Resource Group | `rg-quotesapi-dev` |
| Azure Container Registry | `acrnb3bgcnwnlpwe` |
| Log Analytics Workspace | `log-nb3bgcnwnlpwe` |
| Container Apps Environment | `cae-nb3bgcnwnlpwe` |
| Container App | `ca-api-nb3bgcnwnlpwe` |

---

# Features

- Fully automated Azure deployment using `azd up`
- Automatic Docker image build and push
- ASP.NET Core API hosted on Azure Container Apps
- Secure secret handling using Container Apps secrets
- Health endpoint for deployment verification
- Cloud-native deployment workflow

---

# Notes

- Region used: `southeastasia`

### Allowed regions in Azure Student Subscription

- koreacentral
- southeastasia
- eastasia
- austriaeast
- malaysiawest

### Important Limitations

- Azure Student Subscription allows only **1 Container Apps Environment globally**
- Previous Container Apps Environment must be deleted before creating a new one

### Configuration Notes

- `Jwt__SigningKey` stored securely as Container Apps secret
- `KeyVault__Uri` overridden with empty value to skip Key Vault during startup
- SQLite database is ephemeral and recreated on container restart with seed data

---

# What I Learned

- How Azure Container Apps works with ASP.NET Core
- How `azd up` automates provisioning and deployment
- Basics of Azure Container Apps revisions and environments
- How Docker images are built and deployed automatically
- Managing secrets and environment variables in Azure

---

# What Would Break This?

- Using unsupported Azure regions
- Missing Azure login (`az login`)
- Container Apps Environment quota limits
- Invalid environment variables or missing secrets
- Docker build failures
- Application startup crashes
- Incorrect container port configuration

---

# Useful Commands

## Login to Azure

```bash
az login
```

## Initialize AZD

```bash
azd init
```

## Deploy Application

```bash
azd up
```

## Delete Resources

```bash
azd down
```

---

# API Health Check

```bash
curl -i https://ca-api-nb3bgcnwnlpwe.lemoncliff-d4727121.southeastasia.azurecontainerapps.io/health
```

Response:

```text
HTTP/1.1 200 OK
Content-Type: text/plain
Date: Sat, 23 May 2026 ...
Transfer-Encoding: chunked

Healthy
```

---

# Author

**Yash Rathi**  
B.Tech Computer Engineering Student  
Learning Cloud Computing & Azure 🚀
