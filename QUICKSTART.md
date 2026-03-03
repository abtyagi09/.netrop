# Quick Start Guide

Get your Foundry Agent API running in 5 minutes!

## ⚡ Fast Track

### 1. Prerequisites

Ensure you have:
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) (logged in: `az login`)
- Azure AI Foundry project with an agent created

### 2. Clone/Download Project

```bash
# If from Git
git clone <your-repo-url>
cd FoundryAgentApi

# Or navigate to your project folder
cd c:\.netrop
```

### 3. Configure Settings

Edit `appsettings.Development.json`:

```json
{
  "Azure": {
    "AI": {
      "FoundryProjectEndpoint": "https://YOUR-PROJECT.api.azureml.ms",
      "AgentName": "YOUR-AGENT-NAME",
      "Model": "gpt-5-mini"
    }
  },
  "AgentCaching": {
    "Enabled": true,
    "RefreshIntervalMinutes": 30
  }
}
```

**Where to find these values:**
1. Go to [Azure AI Foundry](https://ml.azure.com)
2. Open your project
3. **FoundryProjectEndpoint**: Overview → Project endpoint
4. **AgentName**: Agents → Your agent name

### 4. Run Locally

```bash
# Restore packages
dotnet restore

# Run the API
dotnet run
```

You should see:
```
🚀 Foundry Agent API starting...
📍 Foundry Endpoint: https://your-project.api.azureml.ms
🤖 Agent Name: my-agent
💾 Agent Caching: ENABLED
⏱️  Cache Refresh Interval: 30 minutes

Now listening on: https://localhost:5001
```

### 5. Test It!

Open your browser to [https://localhost:5001/swagger](https://localhost:5001/swagger)

Or use curl:

```bash
# Test health
curl https://localhost:5001/health

# Send a chat message
curl -X POST https://localhost:5001/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello, how can you help me?"}'
```

**Expected response:**
```json
{
  "conversationId": "abc-123-def",
  "response": "Hello! I can help you with...",
  "metrics": {
    "getAgentMs": 5,
    "createConversationMs": 450,
    "createSessionMs": 2,
    "firstStreamEventMs": 850,
    "firstTextChunkMs": 4200,
    "totalMs": 6100,
    "toolCalls": 0
  }
}
```

## 🎯 What Just Happened?

1. **Agent Loaded**: The API connected to your Foundry project and loaded your agent
2. **Agent Cached**: The agent is now cached for 30 minutes (configurable)
3. **Conversation Created**: A new conversation was started
4. **Message Sent**: Your message was sent to the agent
5. **Response Received**: The agent's response was returned with timing metrics

## 📊 Understanding the Metrics

```
getAgentMs: 5ms          ← Time to get agent from cache (was 300-2000ms without cache!)
createConversationMs: 450ms  ← Time to create conversation (first message only)
createSessionMs: 2ms         ← Time to create/get session
firstStreamEventMs: 850ms    ← Time until first response from Foundry
firstTextChunkMs: 4200ms     ← Time until first text appears
totalMs: 6100ms              ← Total request time
toolCalls: 0                 ← Number of tools the agent called
```

## 🚀 Next Steps

### Improve Performance

See [PERFORMANCE_ANALYSIS.md](PERFORMANCE_ANALYSIS.md) for detailed optimization strategies.

**Key takeaway**: Caching reduces `getAgentMs` from **300-2000ms to ~5ms** (99% faster!)

### Deploy to Azure

Follow [DEPLOYMENT.md](DEPLOYMENT.md) to deploy to Azure App Service.

Quick deploy:
```bash
# Set variables
RESOURCE_GROUP="rg-foundry-agent-api"
APP_NAME="foundry-agent-api-prod"
LOCATION="eastus2"  # Match your Foundry region!

# Create and deploy (takes ~3 minutes)
az group create --name $RESOURCE_GROUP --location $LOCATION

az webapp up \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --runtime "DOTNET:10" \
  --sku P1V2 \
  --location $LOCATION

# Configure settings
az webapp config appsettings set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --settings \
    Azure__AI__FoundryProjectEndpoint="YOUR-ENDPOINT" \
    Azure__AI__AgentName="YOUR-AGENT" \
    AgentCaching__Enabled=true

# Enable managed identity and grant permissions
az webapp identity assign --name $APP_NAME --resource-group $RESOURCE_GROUP
```

### Customize Your Agent

1. **Add Tools**: Edit [Services/IToolProvider.cs](Services/IToolProvider.cs)
2. **Change Caching**: Edit `appsettings.json` → `AgentCaching.RefreshIntervalMinutes`
3. **Disable Caching**: Set `AgentCaching.Enabled = false` (not recommended)
4. **Add Logging**: Already configured with OpenTelemetry

## 🔧 Configuration Options

### Caching Strategies

```json
{
  "AgentCaching": {
    // RECOMMENDED for most use cases
    "Enabled": true,
    "RefreshIntervalMinutes": 30
  }
}
```

| Interval | Use Case | Trade-off |
|----------|----------|-----------|
| 5-15 min | Frequently updated agents | More API calls |
| 30 min ✅ | Balanced (recommended) | Good performance + freshness |
| 60+ min | Stable agents | Best performance |

### Switching Between Cached/Non-Cached

In [Program.cs](Program.cs):

```csharp
// OPTION 1: Cached (recommended)
builder.Services.AddSingleton<IAgentService, CachedAgentService>();

// OPTION 2: Non-cached (always fresh, higher latency)
// builder.Services.AddScoped<IAgentService, NonCachedAgentService>();
```

## 💡 Pro Tips

### 1. Use Streaming for Better UX

```bash
curl -X POST https://localhost:5001/api/chat/stream \
  -H "Content-Type: application/json" \
  -d '{"message": "Tell me a story"}'
```

Users see text as it's generated instead of waiting for the full response.

### 2. Refresh Agent After Updates

When you update your agent's instructions:

```bash
# Manually refresh the cache
curl -X POST https://localhost:5001/api/chat/refresh-agent
```

### 3. Continue Conversations

```bash
# First message
RESPONSE=$(curl -s -X POST https://localhost:5001/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello"}')

# Extract conversation ID
CONV_ID=$(echo $RESPONSE | jq -r '.conversationId')

# Continue conversation
curl -X POST https://localhost:5001/api/chat \
  -H "Content-Type: application/json" \
  -d "{\"conversationId\": \"$CONV_ID\", \"message\": \"Tell me more\"}"
```

### 4. Monitor Performance

Watch the logs:
```bash
# Local
dotnet run | grep "Agent"

# Azure
az webapp log tail --name $APP_NAME --resource-group $RESOURCE_GROUP
```

Look for:
- `"Agent 'my-agent' fetched in XXXms"` - Should be ~5ms after first load
- `"Agent 'my-agent' refreshed and cached"` - Every RefreshIntervalMinutes

## 🐛 Troubleshooting

### Error: "FoundryProjectEndpoint not configured"

**Fix**: Update `appsettings.json` with your Foundry endpoint:
```json
{
  "Azure": {
    "AI": {
      "FoundryProjectEndpoint": "https://YOUR-PROJECT.api.azureml.ms"
    }
  }
}
```

### Error: "The user, group or application does not have access"

**Fix**: Ensure you're logged into Azure CLI:
```bash
az login
az account show  # Verify correct subscription
```

### Error: High latency even with caching

**Check**:
1. Is `CachedAgentService` being used? (Check Program.cs)
2. Is this the first request? (First load always takes time)
3. Check logs for "cached in 0ms" - if not appearing, caching isn't working

**Debug**:
```bash
# Check which service is registered
dotnet run | grep "AddSingleton<IAgentService"

# Or check logs
dotnet run | grep -i "cache"
```

### Slow responses overall

This is likely **NOT** the agent caching issue. Check:

1. **FirstTextChunk time** - This is model processing, not agent loading
2. **Tool calls** - Multiple tools add latency
3. **Foundry region** - Ensure close to your location
4. **Model** - Try different models (gpt-4o vs gpt-5-mini)

See [PERFORMANCE_ANALYSIS.md](PERFORMANCE_ANALYSIS.md) for detailed analysis.

## 📚 Documentation

- **[README.md](README.md)** - Complete overview and Q&A
- **[PERFORMANCE_ANALYSIS.md](PERFORMANCE_ANALYSIS.md)** - Detailed performance comparison
- **[DEPLOYMENT.md](DEPLOYMENT.md)** - Azure deployment guide
- **[requests.http](requests.http)** - Example API requests

## 🎉 Success Checklist

- [ ] API runs locally
- [ ] Health endpoint returns "Healthy"
- [ ] Chat endpoint accepts messages
- [ ] Metrics show low `getAgentMs` (< 10ms after first request)
- [ ] Agent responses are correct
- [ ] Refresh endpoint works

**All checked?** You're ready for production! 🚀

## 🆘 Need Help?

### Common Questions

**Q: How do I know caching is working?**
A: Check logs for "Agent 'my-agent' fetched in 5ms" (or similar low number). First request will be high (300-2000ms), subsequent should be ~5ms.

**Q: When should I use non-cached?**
A: Only if you need absolute latest instructions every time and can tolerate 300-2000ms extra latency.

**Q: How do I add my own tools?**
A: Edit [Services/IToolProvider.cs](Services/IToolProvider.cs) and implement your tool logic.

**Q: Can I use different agents for different requests?**
A: Yes, but you'll need to modify the service to accept agent name as a parameter.

### Get Support

1. Check [README.md](README.md) - Answers to Ryan's original questions
2. Check [PERFORMANCE_ANALYSIS.md](PERFORMANCE_ANALYSIS.md) - Detailed metrics
3. Review logs for error messages
4. Open an issue with:
   - Error message
   - Logs
   - Configuration (with secrets removed)

---

**Happy coding!** 🎉 You now have a high-performance Foundry Agent API!
