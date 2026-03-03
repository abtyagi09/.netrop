using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.AzureAI;
using FoundryAgentApi.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ===== CONFIGURATION =====
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);
builder.Services.Configure<AppSettings>(builder.Configuration);

// ===== AUTHENTICATION =====
// Use DefaultAzureCredential - this will use your Azure CLI identity
var credential = new DefaultAzureCredential();

// ===== AZURE AI PROJECT CLIENT =====
var projectEndpoint = appSettings.Azure?.AI?.FoundryProjectEndpoint 
    ?? throw new InvalidOperationException("FoundryProjectEndpoint not configured");
var agentName = appSettings.Azure?.AI?.AgentName ?? "Agent319";

builder.Services.AddSingleton(_ => new AIProjectClient(
    new Uri(projectEndpoint),
    credential
));

// ===== ASP.NET CORE SERVICES =====
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ===== CORS =====
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ===== HEALTH CHECKS =====
builder.Services.AddHealthChecks();

var app = builder.Build();

// ===== MIDDLEWARE PIPELINE =====
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// ===== GET AGENT =====
app.MapGet("/agent", async (AIProjectClient client) =>
{
    try
    {
        var agent = client.GetAIAgent(name: agentName);
        
        return Results.Ok(new 
        { 
            Status = "Connected to Azure AI Foundry",
            AgentName = agent.Name,
            Model = agent.Model,
            Instructions = agent.Instructions,
            UsingIdentity = "Azure CLI (DefaultAzureCredential)"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get agent: {ex.Message}");
    }
});

// ===== CHAT WITH AGENT (Single Turn) =====
app.MapPost("/chat", async (ChatRequest request, AIProjectClient client) =>
{
    try
    {
        var agent = client.GetAIAgent(name: agentName);
        var response = await agent.RunAsync(request.Message);
        
        return Results.Ok(new 
        { 
            AgentName = agent.Name,
            UserMessage = request.Message,
            AgentResponse = response
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Chat failed: {ex.Message}");
    }
});

// ===== CHAT WITH STREAMING =====
app.MapPost("/chat/stream", async (ChatRequest request, AIProjectClient client, HttpContext httpContext) =>
{
    try
    {
        var agent = client.GetAIAgent(name: agentName);
        
        httpContext.Response.ContentType = "text/event-stream";
        await httpContext.Response.StartAsync();
        
        await foreach (var update in agent.RunStreamingAsync(request.Message))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                await httpContext.Response.WriteAsync($"data: {update.Text}\n\n");
                await httpContext.Response.Body.FlushAsync();
            }
        }
        
        await httpContext.Response.WriteAsync("data: [DONE]\n\n");
        await httpContext.Response.CompleteAsync();
    }
    catch (Exception ex)
    {
        await httpContext.Response.WriteAsync($"data: Error: {ex.Message}\n\n");
        await httpContext.Response.CompleteAsync();
    }
});

// ===== CHAT WITH THREAD (Multi-turn) =====
var threads = new Dictionary<string, AgentThread>();

app.MapPost("/chat/thread", async (ChatThreadRequest request, AIProjectClient client) =>
{
    try
    {
        var agent = client.GetAIAgent(name: agentName);
        
        // Get or create thread
        if (!threads.TryGetValue(request.ThreadId, out var thread))
        {
            thread = agent.GetNewThread();
            threads[request.ThreadId] = thread;
        }
        
        var response = await agent.RunAsync(request.Message, thread);
        
        return Results.Ok(new 
        { 
            ThreadId = request.ThreadId,
            AgentName = agent.Name,
            UserMessage = request.Message,
            AgentResponse = response
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Chat failed: {ex.Message}");
    }
});

// ===== STARTUP MESSAGE =====
app.Logger.LogInformation("🚀 Foundry Agent API starting...");
app.Logger.LogInformation("📍 Foundry Endpoint: {Endpoint}", projectEndpoint);
app.Logger.LogInformation("🤖 Agent Name: {AgentName}", agentName);
app.Logger.LogInformation("👤 Using your Azure identity via DefaultAzureCredential");

app.Run();

// Request models
record ChatRequest(string Message);
record ChatThreadRequest(string ThreadId, string Message);
