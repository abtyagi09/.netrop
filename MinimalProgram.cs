// Minimal working example based on Microsoft Agent Framework
// Shows caching pattern and multi-turn conversation

// Prerequisites:
// dotnet add package Azure.AI.Projects --prerelease
// dotnet add package Azure.Identity --prerelease
// dotnet add package Microsoft.Agents.AI.AzureAI --prerelease

using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

const string ProjectEndpoint = "https://aifoundryprojectdemoresource.services.ai.azure.com/api/projects/aifoundryprojectagentsdemo";
const string AgentName = "Agent319";

var builder = WebApplication.CreateBuilder(args);

// ===== SINGLETON AIPROJECTCLIENT (Connection pool) =====
builder.Services.AddSingleton(_ => new AIProjectClient(
    new Uri(ProjectEndpoint),
    new DefaultAzureCredential()
));

// ===== SINGLETON AIAGENT (Cached - PERFORMANCE OPTIMIZATION) =====
// This avoids the 300-2000ms GetAIAgent call on every request!
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var client = sp.GetRequiredService<AIProjectClient>();
    var agent = client.GetAIAgent(name: AgentName);
    Console.WriteLine($"✅ Agent '{agent.Name}' cached (ID: {agent.Id})");
    return agent;
});

// ===== THREAD CACHE (For multi-turn conversations) =====
builder.Services.AddSingleton(new Dictionary<string, AgentThread>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ===== SINGLE-TURN CHAT (Non-streaming) =====
app.MapPost("/chat", async (ChatRequest request, AIAgent agent) =>
{
    var response = await agent.RunAsync(request.Message);
    return Results.Ok(new { request.Message, Response = response });
});

// ===== SINGLE-TURN CHAT (Streaming) =====
app.MapPost("/chat/stream", async (ChatRequest request, AIAgent agent, HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/event-stream";
    await ctx.Response.StartAsync();

    await foreach (var update in agent.RunStreamingAsync(request.Message))
    {
        if (!string.IsNullOrEmpty(update.Text))
        {
            await ctx.Response.WriteAsync($"data: {update.Text}\n\n");
            await ctx.Response.Body.FlushAsync();
        }
    }

    await ctx.Response.WriteAsync("data: [DONE]\n\n");
    await ctx.Response.CompleteAsync();
});

// ===== MULTI-TURN CONVERSATION =====
app.MapPost("/chat/thread/{threadId}", async (string threadId, ChatRequest request, AIAgent agent, Dictionary<string, AgentThread> threads) =>
{
    // Get or create thread (also cached for performance)
    if (!threads.TryGetValue(threadId, out var thread))
    {
        thread = agent.GetNewThread();
        threads[threadId] = thread;
        Console.WriteLine($"✅ New thread created: {threadId}");
    }

    var response = await agent.RunAsync(request.Message, thread);
    return Results.Ok(new { threadId, request.Message, Response = response });
});

// ===== AGENT INFO =====
app.MapGet("/agent", (AIAgent agent) => Results.Ok(new
{
    Id = agent.Id,
    Name = agent.Name,
    Model = agent.Model,
    Instructions = agent.Instructions,
    PerformanceNote = "✅ This agent is cached - no 300-2000ms delay!"
}));

Console.WriteLine("🚀 Starting agent API with CACHED agent for optimal performance...");
Console.WriteLine($"📍 Endpoint: {ProjectEndpoint}");
Console.WriteLine($"🤖 Agent: {AgentName}");
Console.WriteLine($"👤 Identity: Azure CLI (DefaultAzureCredential)");

app.Run();

record ChatRequest(string Message);
