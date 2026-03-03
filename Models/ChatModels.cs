namespace FoundryAgentApi.Models;

public class ChatRequest
{
    public string? ConversationId { get; set; }
    public required string Message { get; set; }
    public bool StreamResponse { get; set; } = true;
}

public class ChatResponse
{
    public required string ConversationId { get; set; }
    public required string Response { get; set; }
    public ChatMetrics? Metrics { get; set; }
}

public class ChatMetrics
{
    public long GetAgentMs { get; set; }
    public long CreateConversationMs { get; set; }
    public long CreateSessionMs { get; set; }
    public long FirstStreamEventMs { get; set; }
    public long FirstTextChunkMs { get; set; }
    public long TotalMs { get; set; }
    public int ToolCalls { get; set; }
}
