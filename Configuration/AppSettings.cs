namespace FoundryAgentApi.Configuration;

public class AppSettings
{
    public AzureSettings? Azure { get; set; }
    public AgentCachingSettings? AgentCaching { get; set; }
}

public class AzureSettings
{
    public AISettings? AI { get; set; }
}

public class AISettings
{
    public string? FoundryProjectEndpoint { get; set; }
    public string? AgentName { get; set; }
    public string? Model { get; set; }
}

public class AgentCachingSettings
{
    public bool Enabled { get; set; } = true;
    public int RefreshIntervalMinutes { get; set; } = 30;
}
