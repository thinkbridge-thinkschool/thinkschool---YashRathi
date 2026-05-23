# Azure Application Insights Setup

## 1. Provision the resource

```bash
# Create a resource group and App Insights workspace-based resource
az group create --name rg-quotesapi-prod --location eastus

az monitor app-insights component create \
  --app quotesapi-insights \
  --location eastus \
  --resource-group rg-quotesapi-prod \
  --workspace /subscriptions/<sub>/resourceGroups/rg-quotesapi-prod/workspaces/law-quotesapi

# Retrieve the connection string — never paste this into code
az monitor app-insights component show \
  --app quotesapi-insights \
  --resource-group rg-quotesapi-prod \
  --query connectionString -o tsv
```

## 2. Store the connection string in Key Vault

```bash
# Create a Key Vault (or reuse an existing one)
az keyvault create \
  --name kv-quotesapi-prod \
  --resource-group rg-quotesapi-prod \
  --location eastus

# Store the App Insights connection string.
# '--' in the secret name maps to ':' in .NET configuration, so
# "ApplicationInsights--ConnectionString" → "ApplicationInsights:ConnectionString"
az keyvault secret set \
  --vault-name kv-quotesapi-prod \
  --name "ApplicationInsights--ConnectionString" \
  --value "InstrumentationKey=...;IngestionEndpoint=...;LiveEndpoint=..."

# Grant the app's managed identity read access
az keyvault set-policy \
  --name kv-quotesapi-prod \
  --object-id <app-managed-identity-object-id> \
  --secret-permissions get list
```

## 3. Configure the application

In `appsettings.Production.json` (or the hosting environment's app settings), set the
Key Vault URI only — the connection string itself is never in config files:

```json
{
  "AzureKeyVault": {
    "Uri": "https://kv-quotesapi-prod.vault.azure.net/"
  }
}
```

`Program.cs` calls `AddAzureKeyVault` with `DefaultAzureCredential` before Serilog or
DI configuration runs, so the Key Vault secret is available to all downstream configuration:

```csharp
var keyVaultUri = builder.Configuration["AzureKeyVault:Uri"];
if (!string.IsNullOrEmpty(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}
```

`InfrastructureExtensions.cs` then reads `ApplicationInsights:ConnectionString` (which
was injected by Key Vault) and attaches the Azure Monitor exporter alongside the
existing OTLP/Jaeger exporter:

```csharp
var appInsightsConnectionString = configuration["ApplicationInsights:ConnectionString"];

var otelBuilder = services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(Telemetry.ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(Telemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    otelBuilder.UseAzureMonitor(options =>
        options.ConnectionString = appInsightsConnectionString);
}
```

`DefaultAzureCredential` resolution order:
- App Service / AKS / VM → **Managed Identity** (no secrets at all)
- Local dev → `az login` token, then environment variables, then VS/VS Code credentials

---

## 4. KQL Queries

### Slowest 10 requests in the last hour (exercise answer)

```kusto
requests
| where timestamp > ago(1h)
| top 10 by duration desc
| project timestamp, name, url, duration, resultCode, success, operation_Id
```

### Find all traces for a specific user in the last 15 minutes

```kusto
traces
| where timestamp > ago(15m)
| where customDimensions.UserId == "abc"
| order by timestamp asc
| project timestamp, message, severityLevel, customDimensions
```

### POST /api/quotes — average response time per 5-minute bucket

```kusto
requests
| where timestamp > ago(1h)
| where name == "POST /api/quotes"
| summarize avg_duration_ms = avg(duration) by bin(timestamp, 5m)
| order by timestamp asc
| render timechart
```

### Failed requests with their exceptions

```kusto
requests
| where success == false
| join kind=leftouter exceptions on operation_Id
| project timestamp, name, resultCode, outerMessage, operation_Id
| order by timestamp desc
```

### Dependency (EF Core / SQL) slowdown detection

```kusto
dependencies
| where timestamp > ago(1h)
| where type == "SQL"
| top 10 by duration desc
| project timestamp, name, data, duration, success, operation_Id
```

---

## 5. Alert: POST /api/quotes average response time > 500 ms over 5 minutes

Create in Azure Portal → Application Insights → Alerts → New alert rule:

| Field | Value |
|---|---|
| Signal | Custom log search (KQL) |
| Query | `requests \| where name == "POST /api/quotes" \| summarize avg(duration) by bin(timestamp, 5m)` |
| Threshold | Average > 500 (ms) |
| Aggregation period | 5 minutes |
| Evaluation frequency | 1 minute |
| Action group | Email: team@example.com |

Or via CLI:

```bash
az monitor scheduled-query create \
  --name "QuotesApi-SlowPost" \
  --resource-group rg-quotesapi-prod \
  --scopes /subscriptions/<sub>/resourceGroups/rg-quotesapi-prod/providers/microsoft.insights/components/quotesapi-insights \
  --condition "avg(requests | where name == 'POST /api/quotes' | summarize avg(duration)) > 500" \
  --condition-query "requests | where name == 'POST /api/quotes' | summarize avg_duration = avg(duration) by bin(timestamp, 5m)" \
  --evaluation-frequency PT1M \
  --window-size PT5M \
  --severity 2 \
  --action-groups /subscriptions/<sub>/resourceGroups/rg-quotesapi-prod/providers/microsoft.insights/actionGroups/email-team
```

**Alert philosophy**: this alert fires only when the problem is actionable (latency sustained
over 5 minutes, not a single spike). Single-spike alerts create noise; sustained degradation
alerts create signal.
