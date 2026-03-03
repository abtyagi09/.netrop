using System.Diagnostics;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

const string ProjectEndpoint = "https://aifoundryprojectdemoresource.services.ai.azure.com/api/projects/aifoundryprojectagentsdemo";
const string AgentName = "Agent319";

Console.WriteLine("=== Azure AI Foundry Agent Performance Tester ===\n");
Console.WriteLine($"Agent: {AgentName}");
Console.WriteLine($"Endpoint: {ProjectEndpoint}\n");

var credential = new DefaultAzureCredential();
var client = new AIProjectClient(new Uri(ProjectEndpoint), credential);

// Define test prompts
var testRuns = new[]
{
    new { RunName = "Run 1", Prompts = new[] { "What is the capital of France?", "Tell me 3 facts about Paris" } },
    new { RunName = "Run 2", Prompts = new[] { "Explain quantum computing briefly", "What are quantum computing challenges?" } },
    new { RunName = "Run 3", Prompts = new[] { "What is machine learning?", "How does deep learning differ?", "Give an example of neural networks" } },
    new { RunName = "Run 4", Prompts = new[] { "Describe cloud computing", "What is Azure?" } }
};

foreach (var run in testRuns)
{
    Console.WriteLine($"\n{'='} {run.RunName} {'='}");
    Console.WriteLine($"{"Metric",-25} {string.Join("\t", run.Prompts.Select((_, i) => $"Prompt {i + 1}"))}");
    
    var results = new List<PromptResult>();
    
    for (int i = 0; i < run.Prompts.Length; i++)
    {
        var prompt = run.Prompts[i];
        var result = await MeasurePrompt(client, AgentName, prompt, i == 0);
        results.Add(result);
        
        // Small delay between prompts
        await Task.Delay(100);
    }
    
    // Print results
    PrintMetric("GetAgent", results.Select(r => r.GetAgentMs));
    PrintMetric("CreateConversation", results.Select((r, idx) => idx == 0 ? r.CreateConversationMs : null));
    PrintMetric("CreateSession", results.Select(r => r.CreateSessionMs));
    PrintMetric("FirstStreamEvent", results.Select(r => r.FirstStreamEventMs));
    PrintMetric("FirstTextChunk", results.Select(r => r.FirstTextChunkMs));
    PrintMetric("Total", results.Select(r => r.TotalMs));
    PrintMetric("ToolCalls", results.Select(r => r.ToolCalls), isTime: false);
    
    Console.WriteLine();
}

static async Task<PromptResult> MeasurePrompt(AIProjectClient client, string agentName, string prompt, bool isFirstInConversation)
{
    var sw = Stopwatch.StartNew();
    var result = new PromptResult();
    
    // Measure GetAgent
    var agent = client.GetAIAgent(name: agentName);
    result.GetAgentMs = sw.ElapsedMilliseconds;
    
    // Measure CreateConversation (simulated - thread creation)
    AgentThread? thread = null;
    if (isFirstInConversation)
    {
        thread = agent.GetNewThread();
        result.CreateConversationMs = sw.ElapsedMilliseconds;
    }
    
    // Measure CreateSession (simulated as same as conversation or agent get)
    result.CreateSessionMs = sw.ElapsedMilliseconds;
    
    // Measure streaming
    bool firstStreamEvent = false;
    bool firstTextChunk = false;
    
    await foreach (var update in agent.RunStreamingAsync(prompt, thread))
    {
        if (!firstStreamEvent)
        {
            result.FirstStreamEventMs = sw.ElapsedMilliseconds;
            firstStreamEvent = true;
        }
        
        if (!firstTextChunk && !string.IsNullOrEmpty(update.Text))
        {
            result.FirstTextChunkMs = sw.ElapsedMilliseconds;
            firstTextChunk = true;
        }
    }
    
    result.TotalMs = sw.ElapsedMilliseconds;
    result.ToolCalls = 0; // Agent319 has no tools
    
    return result;
}

static void PrintMetric(string name, IEnumerable<long?> values, bool isTime = true)
{
    var formatted = values.Select(v =>
    {
        if (v == null) return "—";
        return isTime ? $"{v:N0}ms" : v.ToString();
    });
    
    Console.WriteLine($"{name,-25} {string.Join("\t", formatted)}");
}

static void PrintMetric(string name, IEnumerable<int> values, bool isTime = true)
{
    Console.WriteLine($"{name,-25} {string.Join("\t", values)}");
}

class PromptResult
{
    public long GetAgentMs { get; set; }
    public long? CreateConversationMs { get; set; }
    public long CreateSessionMs { get; set; }
    public long FirstStreamEventMs { get; set; }
    public long FirstTextChunkMs { get; set; }
    public long TotalMs { get; set; }
    public int ToolCalls { get; set; }
}
