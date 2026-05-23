# Azure Setup — Day 5 / Piece 4

## Live URL

https://ca-api-nb3bgcnwnlpwe.lemoncliff-d4727121.southeastasia.azurecontainerapps.io/healthy

---

# Overview

This project demonstrates deploying an ASP.NET API to Azure Container Apps using `azd up`.

The deployment provisions:
- Azure Resource Group
- Azure Container Registry (ACR)
- Log Analytics Workspace
- Azure Container Apps Environment
- Azure Container App

The application is containerized automatically and deployed through Azure Developer CLI (`azd`).

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

# Resources Created

| Resource | Name |
|---|---|
| Resource Group | `rg-quotesapi-dev` |
| Container Registry | `acrnb3bgcnwnlpwe` |
| Log Analytics Workspace | `log-nb3bgcnwnlpwe` |
| Container Apps Environment | `cae-nb3bgcnwnlpwe` |
| Container App | `ca-api-nb3bgcnwnlpwe` |

---

# Notes

- Region used: `southeastasia`
- Allowed regions in student subscription:
  - koreacentral
  - southeastasia
  - eastasia
  - austriaeast
  - malaysiawest

- Student subscription allows only **1 Container Apps Environment globally**
- A fresh Container Apps Environment was created after deleting the previous one
- `Jwt__SigningKey` is stored securely as a Container Apps secret
- `KeyVault__Uri` is overridden with an empty value to skip Key Vault during startup
- SQLite database is ephemeral and recreated on each container restart with seed data

---

# What I Learned

- How Azure Container Apps works with ASP.NET applications
- How `azd up` automates provisioning and deployment
- Basics of Container Apps Environment, revisions, and container deployment
- How Azure automatically builds and deploys Docker images

---

# What Would Break This?

- Using unsupported Azure regions
- Missing Azure login (`az login`)
- Container Apps Environment quota limits in student subscription
- Incorrect environment variables or missing secrets
- Invalid Docker build or failed container startup