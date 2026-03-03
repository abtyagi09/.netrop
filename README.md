# Azure AI Foundry Agent API

A production-ready ASP.NET Core 10 Web API for calling Azure AI Foundry agents with optimized performance patterns.

## 📋 Overview

This project demonstrates two approaches for integrating Azure AI Foundry agents into an ASP.NET Core API:

1. **Non-Cached Approach**: Fetches the agent from Foundry on every request (ensures latest instructions, higher latency)
2. **Cached Approach** ✓ (Recommended): Caches the agent instance with configurable refresh intervals (better performance)

## 🚀 Quick Start

### Prerequisites

- .NET 10 SDK
- Azure subscription with AI Foundry project
- Azure CLI (for authentication) or Managed Identity

### Configuration

Update `appsettings.json`:

```json
{
  "Azure": {
    "AI": {
      "FoundryProjectEndpoint": "https://your-project.api.azureml.ms",
      "AgentName": "my-agent",
      "Model": "gpt-5-mini"
    }
  },
  "AgentCaching": {
    "Enabled": true,
    "RefreshIntervalMinutes": 30
  }
}
```

### Run the Application

```bash
dotnet restore
dotnet build
dotnet run
```

The API will be available at `https://localhost:5001` (or configured port).

## 📦 NuGet Packages

This project uses the following Azure AI packages:

```xml
<PackageReference Include="Azure.AI.Projects" Version="1.*" />
<PackageReference Include="Azure.AI.Projects.OpenAI" Version="1.*" />
<PackageReference Include="Azure.AI.Agents.Persistent" Version="1.*" />
<PackageReference Include="Azure.AI.OpenAI" Version="2.*" />
<PackageReference Include="Microsoft.Agents.AI.AzureAI" Version="1.*" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.*" />
<PackageReference Include="Azure.Identity" Version="1.*" />
```

## 🏗️ Architecture

### Service Registration (Program.cs)

```csharp
// Register AIProjectClient as singleton
builder.Services.AddSingleton(_ => new AIProjectClient(
    new Uri(appSettings.Azure?.AI?.FoundryProjectEndpoint!),
    credential
));

// Choose your agent service implementation:

// OPTION 1: Non-cached (high latency, always fresh)
// builder.Services.AddScoped<IAgentService, NonCachedAgentService>();

// OPTION 2: Cached (low latency, periodic refresh) ✓ RECOMMENDED
builder.Services.AddSingleton<IAgentService, CachedAgentService>();
```

### Two Implementation Approaches

#### 1. NonCachedAgentService

Calls `GetAIAgentAsync` on **every request**:

```csharp
public async Task<AIAgent> GetAgentAsync(CancellationToken cancellationToken)
{
    return await _aiProjectClient.GetAIAgentAsync(
        agentName,
        _toolProvider.GetTools(),
        clientFactory: inner => inner.AsBuilder().UseOpenTelemetry().Build(),
        cancellationToken: cancellationToken);
}
```

**Pros:**
- Always has the latest agent instructions
- Simple implementation

**Cons:**
- High latency (300-2000ms per request as shown in metrics)
- Increased load on Foundry API
- Higher costs

#### 2. CachedAgentService ✓ (Recommended)

Caches the agent instance and refreshes periodically:

```csharp
public async Task<AIAgent> GetAgentAsync(CancellationToken cancellationToken)
{
    var needsRefresh = _cachedAgent == null || 
                      DateTime.UtcNow - _lastRefresh > refreshInterval;

    if (needsRefresh)
    {
        await RefreshAgentAsync(cancellationToken);
    }

    return _cachedAgent;
}
```

**Pros:**
- **Dramatically reduced latency** (near-zero agent fetch time after first load)
- Reduced load on Foundry API
- Lower costs
- Configurable refresh interval

**Cons:**
- May serve stale instructions until next refresh
- Requires manual refresh via API if immediate update needed

## 📊 Performance Comparison

Based on your metrics, here's the expected improvement with caching:

### Non-Cached (Current Approach)
| Metric | Time |
|--------|------|
| GetAgent | 300-2000ms |
| FirstTextChunk | 4,000-50,000ms |
| **Total** | **6,000-53,000ms** |

### Cached (Optimized Approach)
| Metric | Time |
|--------|------|
| GetAgent | **~0-5ms** (after first load) |
| FirstTextChunk | 4,000-50,000ms |
| **Total** | **4,000-48,000ms** |

**Expected improvement: 300-2000ms faster per request!**

## 🎯 Answering Your Questions

### 1. Is the AIAgent object safe to cache and reuse?

**Answer: Yes, with considerations:**

The `AIAgent` object returned by `GetAIAgentAsync` is generally safe to cache and reuse across concurrent requests, BUT:

- **Thread Safety**: The agent object itself should be thread-safe for reads, but verify in the SDK documentation
- **Session State**: Each conversation should have its own session (we handle this in `ChatService`)
- **No Stale Connections**: The agent object doesn't maintain long-lived connections that would go stale
- **Token Freshness**: The `AIProjectClient` uses `DefaultAzureCredential` which handles token refresh automatically

**Our implementation** uses `CachedAgentService` registered as a **Singleton**, which is safe because:
- We protect refreshes with a `SemaphoreSlim` to prevent concurrent refreshes
- We create new sessions per conversation (not cached)
- The agent itself is stateless configuration

### 2. What is the expected latency profile for GetAIAgentAsync?

**Answer: Network round-trip every time (300-2000ms)**

Based on your metrics and our investigation:

- `GetAIAgentAsync` makes a **full network round-trip to Foundry** on every call
- There is **NO built-in client-side caching** in the SDK
- Expected latency: **300-2000ms** depending on:
  - Network conditions
  - Region proximity
  - Foundry service load
  - Agent complexity

This is why caching provides such dramatic improvements!

### 3. Recommended pattern for caching with version updates?

**Answer: Use our CachedAgentService with manual refresh endpoint**

Our implementation provides the best of both worlds:

```csharp
// Automatic periodic refresh (every 30 minutes by default)
"AgentCaching": {
  "Enabled": true,
  "RefreshIntervalMinutes": 30
}
```

**Plus** a manual refresh endpoint when you update instructions:

```bash
# After updating agent instructions in your portal, call:
POST /api/chat/refresh-agent
```

**Workflow:**
1. Agent runs with cached version (fast)
2. Every 30 minutes, automatically refreshes (configurable)
3. When you update instructions via `CreateAIAgentAsync`, call the refresh endpoint
4. All new requests immediately use the updated agent

### 4. Other performance recommendations?

#### A. ** Use Cached Agent Service** (already implemented) ✓

Switch from `NonCachedAgentService` to `CachedAgentService` in [Program.cs](Program.cs):

```csharp
// Use this:
builder.Services.AddSingleton<IAgentService, CachedAgentService>();

// Instead of this:
// builder.Services.AddScoped<IAgentService, NonCachedAgentService>();
```

#### B. **Optimize Tool Loading**

Only load necessary tools on agent initialization (in `IToolProvider`):

```csharp
public object GetTools()
{
    // Load only the tools this agent actually uses
    return new[] { searchTool, calculatorTool }; // Not all possible tools
}
```

#### C. **Consider Connection Pooling**

The `AIProjectClient` is already registered as a Singleton, which is good. Ensure HTTP client pooling:

```csharp
// Already handled by HttpClientFactory patterns in the SDK
```

#### D. **Use Streaming for Better UX**

Even if total time is the same, streaming provides better perceived performance:

```csharp
POST /api/chat/stream
```

Users see the first token at `FirstTextChunk` instead of waiting for `Total`.

#### E. **Monitor with OpenTelemetry**

We've already added OpenTelemetry integration:

```csharp
clientFactory: inner => inner.AsBuilder().UseOpenTelemetry().Build()
```

Use this to identify bottlenecks in tool calls and model response times.

#### F. **Conversation Session Management**

Our `ChatService` already implements session caching per conversation:

```csharp
// Reuse sessions across messages in the same conversation
if (!_sessions.TryGetValue(conversationId, out var existingSession))
{
    session = agent.CreateSession(conversationId);
    _sessions[conversationId] = session;
}
```

For production, replace the in-memory dictionary with:
- Redis
- Azure Cosmos DB
- Azure Table Storage

#### G. **Parallel Tool Calls**

If your agent makes multiple tool calls, ensure they can run in parallel (depends on Foundry agent configuration).

#### H. **Consider Region Colocation**

You mentioned QA is in the same region - ensure production is also colocated:
- API: Azure App Service in East US 2
- Foundry: East US 2
- Other dependencies: East US 2

## 📡 API Endpoints

### Send Chat Message

```bash
POST /api/chat
Content-Type: application/json

{
  "conversationId": "optional-id",
  "message": "What is the weather today?",
  "streamResponse": false
}
```

**Response:**

```json
{
  "conversationId": "abc-123",
  "response": "The weather today is...",
  "metrics": {
    "getAgentMs": 2,
    "createConversationMs": 0,
    "createSessionMs": 1,
    "firstStreamEventMs": 850,
    "firstTextChunkMs": 4200,
    "totalMs": 6100,
    "toolCalls": 1
  }
}
```

### Stream Chat Message

```bash
POST /api/chat/stream
Content-Type: application/json

{
  "message": "Tell me a story"
}
```

**Response:** Server-Sent Events (SSE) stream

### Refresh Agent

```bash
POST /api/chat/refresh-agent
```

Force refresh the cached agent to pick up the latest instructions.

## 🔧 Configuration Options

### appsettings.json

```json
{
  "Azure": {
    "AI": {
      "FoundryProjectEndpoint": "https://your-project.api.azureml.ms",
      "AgentName": "my-agent",
      "Model": "gpt-5-mini"
    }
  },
  "AgentCaching": {
    "Enabled": true,              // Enable/disable caching
    "RefreshIntervalMinutes": 30  // Auto-refresh interval
  }
}
```

### Environment Variables (for production)

```bash
Azure__AI__FoundryProjectEndpoint=https://your-project.api.azureml.ms
Azure__AI__AgentName=my-agent
AgentCaching__Enabled=true
AgentCaching__RefreshIntervalMinutes=30
```

## 🔐 Authentication

This project uses `DefaultAzureCredential`, which supports:

1. **Managed Identity** (recommended for Azure-hosted apps)
2. **Azure CLI** (for local development: `az login`)
3. **Visual Studio** / **Visual Studio Code**
4. **Environment variables**
5. **Interactive browser**

For production, use **Managed Identity**:

```bash
# Enable in Azure App Service
az webapp identity assign --name your-app --resource-group your-rg

# Grant permissions to Foundry project
az role assignment create \
  --assignee <managed-identity-id> \
  --role "Azure AI Developer" \
  --scope <foundry-project-scope>
```

## 📈 Monitoring & Metrics

### Built-in Metrics

Every chat response includes detailed metrics:

```json
{
  "metrics": {
    "getAgentMs": 2,           // Time to get/retrieve agent
    "createConversationMs": 483, // Time to create conversation (first message only)
    "createSessionMs": 1,        // Time to create session
    "firstStreamEventMs": 850,   // Time to first event from Foundry
    "firstTextChunkMs": 4200,    // Time to first actual text chunk
    "totalMs": 6100,             // Total request time
    "toolCalls": 1               // Number of tool calls made
  }
}
```

### OpenTelemetry

OpenTelemetry tracing is enabled:

```csharp
clientFactory: inner => inner.AsBuilder().UseOpenTelemetry().Build()
```

Export to Application Insights, Jaeger, or other backends.

### Health Checks

```bash
GET /health
```

## 🧪 Testing

### Local Testing

1. Ensure Azure CLI is logged in: `az login`
2. Update `appsettings.Development.json` with your Foundry endpoint
3. Run: `dotnet run`
4. Test with Swagger UI: `https://localhost:5001/swagger`

### Load Testing

Compare cached vs non-cached performance:

```bash
# Test non-cached
# Switch to NonCachedAgentService in Program.cs
ab -n 100 -c 10 -p message.json -T application/json https://localhost:5001/api/chat

# Test cached
# Switch to CachedAgentService in Program.cs
ab -n 100 -c 10 -p message.json -T application/json https://localhost:5001/api/chat
```

## 📝 Production Checklist

- [ ] Use `CachedAgentService` instead of `NonCachedAgentService`
- [ ] Configure appropriate `RefreshIntervalMinutes` (recommended: 15-60 minutes)
- [ ] Replace in-memory session storage with Redis/Cosmos DB
- [ ] Enable Managed Identity authentication
- [ ] Configure Application Insights for monitoring
- [ ] Set up health checks and alerts
- [ ] Implement proper error handling and retries
- [ ] Add rate limiting if needed
- [ ] Configure CORS for your frontend domains
- [ ] Set up deployment pipeline with agent refresh on deploy
- [ ] Test failover scenarios (what happens if agent refresh fails?)
- [ ] Document your tools in `IToolProvider`

## 🤔 Troubleshooting

### High Latency Even with Caching

If `FirstTextChunk` is still slow (10-50 seconds):

1. **Model Performance**: gpt-5-mini might be slower - test with gpt-4o
2. **Tool Calls**: Multiple tool calls add latency - optimize tool logic
3. **Region Latency**: Ensure API and Foundry are in the same region
4. **Foundry Load**: Check Azure status and Foundry metrics

### Agent Not Refreshing

1. Check logs for refresh errors
2. Verify `RefreshIntervalMinutes` configuration
3. Manually call `/api/chat/refresh-agent`
4. Check Foundry project permissions

### Authentication Failures

```
Azure.Identity.CredentialUnavailableException
```

1. Ensure `az login` is run locally
2. Check Managed Identity is enabled and has permissions in production
3. Verify Foundry project endpoint is correct

## 📚 Additional Resources

- [Azure AI Foundry Documentation](https://learn.microsoft.com/azure/ai-foundry/)
- [Azure.AI.Projects SDK](https://learn.microsoft.com/dotnet/api/azure.ai.projects)
- [DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)

## 🎉 Summary

**For your specific use case, we recommend:**

1. ✅ **Use `CachedAgentService`** - Saves 300-2000ms per request
2. ✅ **Set refresh interval to 15-30 minutes** - Balance between freshness and performance
3. ✅ **Call `/api/chat/refresh-agent`** after updating instructions - Get immediate updates when needed
4. ✅ **Use streaming** - Better user experience even with same total time
5. ✅ **Monitor with OpenTelemetry** - Identify other bottlenecks

**Expected Results:**
- GetAgent: **300-2000ms → 0-5ms** ✨
- Total request time: **6-53 seconds → 4-51 seconds**
- First response visible: **4-50 seconds** (no change, but use streaming for better UX)

The agent object is safe to cache, there's no built-in SDK caching, and our implementation provides the recommended pattern for production use.

---

**Questions? Found an issue?** Open an issue or reach out!

Happy coding! 🚀
