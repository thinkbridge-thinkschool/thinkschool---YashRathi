az commands
1. Create the resource group
az group create -n thinkschool-rg -l Southeast Asia

2. Create the Container Apps environment
az containerapp env create -n thinkschool-env -g thinkschool-rg -l Southeast Asia

3. Show the environment (JSON)
az containerapp env show -n thinkschool-env -g thinkschool-rg

Output:

PS C:\Users\LENOVO\OneDrive\Desktop\Thinkschool> az containerapp env show -n cae-nb3bgcnwnlpwe -g rg-quotesapi-dev -o json
>> 
The behavior of this command has been altered by the following extension: containerapp
{
  "id": "/subscriptions/f2ab3e93-bb60-46ed-bb28-c8c15a1af0f7/resourceGroups/rg-quotesapi-dev/providers/Microsoft.App/managedEnvironments/cae-nb3bgcnwnlpwe",
  "location": "Southeast Asia",
  "name": "cae-nb3bgcnwnlpwe",
  "properties": {
    "appInsightsConfiguration": null,
    "appLogsConfiguration": {
      "destination": "log-analytics",
      "logAnalyticsConfiguration": {
        "customerId": "86389132-40b4-41e7-994f-7832eac4479b",
        "dynamicJsonColumns": false,
        "sharedKey": null
      }
    },
    "availabilityZones": null,
    "customDomainConfiguration": {
      "certificateKeyVaultProperties": null,
      "certificatePassword": null,
      "certificateValue": null,
      "customDomainVerificationId": "D6690236CDCE4676D905FEB283A69A50641DA7764EB5ABAECB9F39B33CB6923F",
      "dnsSuffix": null,
      "expirationDate": null,
      "subjectName": null,
      "thumbprint": null
    },
    "daprAIConnectionString": null,
    "daprAIInstrumentationKey": null,
    "daprConfiguration": {
      "version": "1.16.4-msft.6"
    },
    "defaultDomain": "lemoncliff-d4727121.southeastasia.azurecontainerapps.io",
    "diskEncryptionConfiguration": null,
    "environmentMode": "ConsumptionOnly",
    "eventStreamEndpoint": "https://southeastasia.azurecontainerapps.dev/subscriptions/f2ab3e93-bb60-46ed-bb28-c8c15a1af0f7/resourceGroups/rg-quotesapi-dev/managedEnvironments/cae-nb3bgcnwnlpwe/eventstream",
    "infrastructureResourceGroup": null,
    "ingressConfiguration": null,
    "kedaConfiguration": {
      "version": "2.18.1"
    },
    "openTelemetryConfiguration": null,
    "peerAuthentication": {
      "mtls": {
        "enabled": false
      }
    },
    "peerTrafficConfiguration": {
      "encryption": {
        "enabled": false
      }
    },
    "provisioningState": "Succeeded",
    "publicNetworkAccess": "Enabled",
    "staticIp": "20.198.185.192",
    "vnetConfiguration": null,
    "workloadProfiles": null,
    "zoneRedundant": false
  },
  "resourceGroup": "rg-quotesapi-dev",
  "systemData": {
    "createdAt": "2026-05-23T09:55:39.5226734",
    "createdBy": "202101040075@msteams.mitaoe.ac.in",
    "createdByType": "User",
    "lastModifiedAt": "2026-05-23T09:55:39.5226734",
    "lastModifiedBy": "202101040075@msteams.mitaoe.ac.in",
    "lastModifiedByType": "User"
  },
  "tags": {
    "azd-env-name": "quotesapi-dev"
  },
  "type": "Microsoft.App/managedEnvironments"
}
