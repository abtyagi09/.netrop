# Azure Deployment Guide

This guide covers deploying the Foundry Agent API to Azure App Service with optimal configuration.

## Prerequisites

- Azure subscription
- Azure CLI installed (`az --version`)
- .NET 10 SDK
- Azure AI Foundry project created

## Quick Deploy (5 minutes)

### Option 1: Azure CLI

```bash
# Login to Azure
az login

# Set variables
RESOURCE_GROUP="rg-foundry-agent-api"
LOCATION="eastus2"  # Match your Foundry project region!
APP_NAME="foundry-agent-api-$(openssl rand -hex 4)"
PLAN_NAME="plan-foundry-agent-api"
FOUNDRY_ENDPOINT="https://your-project.api.azureml.ms"
AGENT_NAME="my-agent"

# Create resource group
az group create \
  --name $RESOURCE_GROUP \
  --location $LOCATION

# Create App Service Plan (Linux, .NET 10)
az appservice plan create \
  --name $PLAN_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --is-linux \
  --sku B1

# Create Web App
az webapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --plan $PLAN_NAME \
  --runtime "DOTNET:10"

# Enable managed identity
az webapp identity assign \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP

# Configure app settings
az webapp config appsettings set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --settings \
    Azure__AI__FoundryProjectEndpoint=$FOUNDRY_ENDPOINT \
    Azure__AI__AgentName=$AGENT_NAME \
    Azure__AI__Model="gpt-5-mini" \
    AgentCaching__Enabled=true \
    AgentCaching__RefreshIntervalMinutes=30 \
    ASPNETCORE_ENVIRONMENT=Production

# Deploy the app
az webapp deployment source config-zip \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --src ./publish.zip

echo "App URL: https://$APP_NAME.azurewebsites.net"
```

### Option 2: VS Code Extension

1. Install "Azure App Service" extension
2. Right-click on project → "Deploy to Web App"
3. Follow prompts
4. Configure app settings in Azure Portal

### Option 3: GitHub Actions (Recommended for CI/CD)

See [CI/CD Setup](#cicd-setup) below.

## Detailed Setup

### Step 1: Create Azure Resources

```bash
# Variables
RESOURCE_GROUP="rg-foundry-agent-api"
LOCATION="eastus2"  # ⚠️ MATCH YOUR FOUNDRY REGION
APP_NAME="foundry-agent-api-prod"
PLAN_NAME="plan-foundry-agent-api"
APP_INSIGHTS_NAME="appi-foundry-agent-api"

# Resource Group
az group create \
  --name $RESOURCE_GROUP \
  --location $LOCATION

# App Service Plan (adjust SKU based on needs)
# B1 = Basic ($13/month)
# P1V2 = Premium ($73/month) - Recommended for production
az appservice plan create \
  --name $PLAN_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --is-linux \
  --sku P1V2

# Web App
az webapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --plan $PLAN_NAME \
  --runtime "DOTNET:10"

# Application Insights
az monitor app-insights component create \
  --app $APP_INSIGHTS_NAME \
  --location $LOCATION \
  --resource-group $RESOURCE_GROUP \
  --application-type web

# Get App Insights connection string
AI_CONNECTION_STRING=$(az monitor app-insights component show \
  --app $APP_INSIGHTS_NAME \
  --resource-group $RESOURCE_GROUP \
  --query connectionString -o tsv)
```

### Step 2: Configure Managed Identity

```bash
# Enable system-assigned managed identity
IDENTITY_ID=$(az webapp identity assign \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query principalId -o tsv)

echo "Managed Identity: $IDENTITY_ID"

# Grant permissions to Foundry project
# Replace with your Foundry project resource ID
FOUNDRY_PROJECT_ID="/subscriptions/YOUR-SUB/resourceGroups/YOUR-RG/providers/Microsoft.MachineLearningServices/workspaces/YOUR-PROJECT"

az role assignment create \
  --assignee $IDENTITY_ID \
  --role "Azure AI Developer" \
  --scope $FOUNDRY_PROJECT_ID

# Wait 30 seconds for permissions to propagate
echo "Waiting for permissions to propagate..."
sleep 30
```

### Step 3: Configure Application Settings

```bash
# Get your Foundry endpoint from Azure Portal or:
# Go to your Foundry project → Overview → Project endpoint

FOUNDRY_ENDPOINT="https://your-project.api.azureml.ms"
AGENT_NAME="my-agent"

az webapp config appsettings set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --settings \
    Azure__AI__FoundryProjectEndpoint=$FOUNDRY_ENDPOINT \
    Azure__AI__AgentName=$AGENT_NAME \
    Azure__AI__Model="gpt-5-mini" \
    AgentCaching__Enabled=true \
    AgentCaching__RefreshIntervalMinutes=30 \
    APPLICATIONINSIGHTS_CONNECTION_STRING=$AI_CONNECTION_STRING \
    ASPNETCORE_ENVIRONMENT=Production \
    WEBSITE_TIME_ZONE="UTC"
```

### Step 4: Build and Deploy

```bash
# Build and publish locally
dotnet publish -c Release -o ./publish

# Create zip file
cd publish
zip -r ../publish.zip .
cd ..

# Deploy to Azure
az webapp deployment source config-zip \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --src ./publish.zip

# Wait for deployment
echo "Waiting for deployment to complete..."
sleep 30

# Test the deployment
APP_URL="https://$APP_NAME.azurewebsites.net"
echo "Testing: $APP_URL/health"
curl $APP_URL/health
```

## CI/CD Setup

### GitHub Actions

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy to Azure App Service

on:
  push:
    branches: [ main ]
  workflow_dispatch:

env:
  APP_NAME: 'foundry-agent-api-prod'
  DOTNET_VERSION: '10.0.x'

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    
    steps:
    - name: 'Checkout code'
      uses: actions/checkout@v4
    
    - name: 'Setup .NET'
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
    
    - name: 'Restore dependencies'
      run: dotnet restore
    
    - name: 'Build'
      run: dotnet build --configuration Release --no-restore
    
    - name: 'Publish'
      run: dotnet publish -c Release -o ${{env.DOTNET_ROOT}}/publish
    
    - name: 'Deploy to Azure'
      uses: azure/webapps-deploy@v2
      with:
        app-name: ${{ env.APP_NAME }}
        publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
        package: ${{env.DOTNET_ROOT}}/publish
    
    - name: 'Refresh Agent Cache'
      run: |
        sleep 15  # Wait for app to be ready
        curl -X POST https://${{ env.APP_NAME }}.azurewebsites.net/api/chat/refresh-agent
```

**Setup:**

1. Get publish profile:
```bash
az webapp deployment list-publishing-profiles \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --xml
```

2. Add to GitHub Secrets as `AZURE_WEBAPP_PUBLISH_PROFILE`

### Azure DevOps Pipeline

Create `azure-pipelines.yml`:

```yaml
trigger:
  branches:
    include:
    - main

variables:
  buildConfiguration: 'Release'
  appName: 'foundry-agent-api-prod'
  azureSubscription: 'YOUR-SERVICE-CONNECTION'

stages:
- stage: Build
  jobs:
  - job: Build
    pool:
      vmImage: 'ubuntu-latest'
    
    steps:
    - task: UseDotNet@2
      inputs:
        packageType: 'sdk'
        version: '10.0.x'
    
    - task: DotNetCoreCLI@2
      displayName: 'Restore'
      inputs:
        command: 'restore'
    
    - task: DotNetCoreCLI@2
      displayName: 'Build'
      inputs:
        command: 'build'
        arguments: '--configuration $(buildConfiguration) --no-restore'
    
    - task: DotNetCoreCLI@2
      displayName: 'Publish'
      inputs:
        command: 'publish'
        publishWebProjects: true
        arguments: '--configuration $(buildConfiguration) --output $(Build.ArtifactStagingDirectory)'
    
    - task: PublishBuildArtifacts@1
      inputs:
        pathToPublish: '$(Build.ArtifactStagingDirectory)'
        artifactName: 'drop'

- stage: Deploy
  dependsOn: Build
  jobs:
  - deployment: Deploy
    environment: 'production'
    pool:
      vmImage: 'ubuntu-latest'
    
    strategy:
      runOnce:
        deploy:
          steps:
          - task: AzureWebApp@1
            displayName: 'Deploy to Azure App Service'
            inputs:
              azureSubscription: '$(azureSubscription)'
              appName: '$(appName)'
              package: '$(Pipeline.Workspace)/drop/**/*.zip'
          
          - task: PowerShell@2
            displayName: 'Refresh Agent Cache'
            inputs:
              targetType: 'inline'
              script: |
                Start-Sleep -Seconds 15
                Invoke-RestMethod -Method Post -Uri "https://$(appName).azurewebsites.net/api/chat/refresh-agent"
```

## Production Configuration

### Scaling

```bash
# Enable autoscale
az monitor autoscale create \
  --resource-group $RESOURCE_GROUP \
  --resource $APP_NAME \
  --resource-type Microsoft.Web/sites \
  --name autoscale-foundry-api \
  --min-count 2 \
  --max-count 10 \
  --count 2

# Add scale rule: CPU > 70%
az monitor autoscale rule create \
  --resource-group $RESOURCE_GROUP \
  --autoscale-name autoscale-foundry-api \
  --condition "CpuPercentage > 70 avg 5m" \
  --scale out 1

# Add scale rule: CPU < 30%
az monitor autoscale rule create \
  --resource-group $RESOURCE_GROUP \
  --autoscale-name autoscale-foundry-api \
  --condition "CpuPercentage < 30 avg 5m" \
  --scale in 1
```

### Health Checks

```bash
# Configure health check endpoint
az webapp config set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --generic-configurations '{"healthCheckPath": "/health"}'
```

### Logging

```bash
# Enable application logging
az webapp log config \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --application-logging filesystem \
  --level information

# Stream logs
az webapp log tail \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP
```

### HTTPS and Domains

```bash
# Enforce HTTPS
az webapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --https-only true

# Add custom domain (if you have one)
az webapp config hostname add \
  --webapp-name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --hostname api.yourdomain.com

# Enable managed certificate
az webapp config ssl bind \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --certificate-thumbprint auto \
  --ssl-type SNI
```

### CORS Configuration

```bash
# Allow specific origins
az webapp cors add \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --allowed-origins https://yourdomain.com https://app.yourdomain.com

# Or allow all (not recommended for production)
az webapp cors add \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --allowed-origins '*'
```

## Monitoring Setup

### Application Insights Queries

```kusto
// Average GetAgent time
requests
| where timestamp > ago(1h)
| where name == "POST Chat/SendMessage"
| extend getAgentMs = toint(customDimensions["GetAgentMs"])
| summarize avg(getAgentMs), max(getAgentMs), min(getAgentMs) by bin(timestamp, 5m)
| render timechart

// Cache hit rate (custom tracking)
traces
| where timestamp > ago(1h)
| where message contains "Agent" and message contains "cached"
| summarize CacheHits = countif(message contains "cached in 0ms"),
            TotalRequests = count()
| project CacheHitRate = (CacheHits * 100.0) / TotalRequests

// Error rate
requests
| where timestamp > ago(1h)
| summarize SuccessCount = countif(success == true),
            FailureCount = countif(success == false)
| project ErrorRate = (FailureCount * 100.0) / (SuccessCount + FailureCount)
```

### Alerts

```bash
# Create action group for notifications
az monitor action-group create \
  --name ag-foundry-alerts \
  --resource-group $RESOURCE_GROUP \
  --short-name FoundryAPI \
  --email-receiver \
    name="DevTeam" \
    email="devteam@company.com"

# Alert: High error rate
az monitor metrics alert create \
  --name "High Error Rate" \
  --resource-group $RESOURCE_GROUP \
  --scopes "/subscriptions/YOUR-SUB/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$APP_NAME" \
  --condition "avg percentage Http5xx > 5" \
  --window-size 5m \
  --evaluation-frequency 1m \
  --action ag-foundry-alerts

# Alert: High response time
az monitor metrics alert create \
  --name "High Response Time" \
  --resource-group $RESOURCE_GROUP \
  --scopes "/subscriptions/YOUR-SUB/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$APP_NAME" \
  --condition "avg AverageResponseTime > 5000" \
  --window-size 5m \
  --evaluation-frequency 1m \
  --action ag-foundry-alerts
```

## Cost Optimization

### Recommended SKUs by Load

| Load | SKU | Monthly Cost* | Notes |
|------|-----|--------------|-------|
| Dev/Test | B1 | ~$13 | Single instance, manual scaling |
| Low (< 10 req/s) | P1V2 | ~$73 | Production features, autoscale |
| Medium (< 100 req/s) | P2V2 | ~$146 | More CPU/memory |
| High (100+ req/s) | P3V2 | ~$292 | Maximum performance |

*Prices approximate, check Azure Pricing Calculator

### Cost Saving Tips

1. **Use cached agent service** - Reduces API calls to Foundry
2. **Right-size your SKU** - Don't over-provision
3. **Use reserved instances** - 1-year commitment = 40% savings
4. **Enable autoscale** - Scale down during low traffic
5. **Monitor Foundry usage** - Track token consumption

## Disaster Recovery

### Backup Strategy

```bash
# Create backup
az webapp config backup create \
  --resource-group $RESOURCE_GROUP \
  --webapp-name $APP_NAME \
  --backup-name backup-$(date +%Y%m%d) \
  --container-url "<your-storage-sas-url>"

# Schedule automatic backups
az webapp config backup update \
  --resource-group $RESOURCE_GROUP \
  --webapp-name $APP_NAME \
  --container-url "<your-storage-sas-url>" \
  --frequency 1d \
  --retention 30
```

### Multi-Region Deployment

For high availability, deploy to multiple regions:

```bash
# Primary: East US 2
APP_NAME_PRIMARY="foundry-api-eastus2"

# Secondary: West US 2
APP_NAME_SECONDARY="foundry-api-westus2"

# Use Azure Front Door or Traffic Manager for routing
az network traffic-manager profile create \
  --name tm-foundry-api \
  --resource-group $RESOURCE_GROUP \
  --routing-method Performance \
  --unique-dns-name foundry-api-tm
```

## Troubleshooting

### Issue: 500 Internal Server Error after deployment

**Fix:**
```bash
# Check logs
az webapp log tail --name $APP_NAME --resource-group $RESOURCE_GROUP

# Common cause: Missing app settings
az webapp config appsettings list \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP

# Verify managed identity permissions
az role assignment list --assignee $IDENTITY_ID
```

### Issue: Authentication failures

**Fix:**
```bash
# Test managed identity
az webapp ssh --name $APP_NAME --resource-group $RESOURCE_GROUP
# Inside SSH shell:
curl "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https://management.azure.com/" -H Metadata:true

# If it fails, reassign identity
az webapp identity assign --name $APP_NAME --resource-group $RESOURCE_GROUP
```

### Issue: Slow startup / cold start

**Fix:**
```bash
# Enable Always On (requires Basic or higher tier)
az webapp config set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --always-on true

# Increase instance count
az appservice plan update \
  --name $PLAN_NAME \
  --resource-group $RESOURCE_GROUP \
  --number-of-workers 2
```

## Post-Deployment Checklist

- [ ] App is accessible via HTTPS
- [ ] Health endpoint returns 200: `/health`
- [ ] Chat endpoint works: `POST /api/chat`
- [ ] Managed identity has Foundry permissions
- [ ] Application Insights is receiving telemetry
- [ ] Logs are visible in Azure Portal
- [ ] Agent cache is working (check logs for "cached in 0ms")
- [ ] Manual refresh endpoint works: `POST /api/chat/refresh-agent`
- [ ] Autoscaling is configured (if needed)
- [ ] Alerts are configured
- [ ] Backup is scheduled
- [ ] Custom domain is configured (if applicable)
- [ ] CORS is configured for your frontend

## Next Steps

1. **Monitor for 1 week** - Watch metrics and logs
2. **Tune refresh interval** - Adjust based on usage patterns
3. **Optimize costs** - Right-size SKU after observing load
4. **Add more regions** - If needed for global users
5. **Implement caching** - Add Redis for session storage

---

Need help? Check [Azure App Service documentation](https://docs.microsoft.com/azure/app-service/) or open an issue!
