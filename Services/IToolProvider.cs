namespace FoundryAgentApi.Services;

/// <summary>
/// Provides tools/functions that the agent can use
/// </summary>
public interface IToolProvider
{
    /// <summary>
    /// Returns the collection of tools available to the agent
    /// </summary>
    object GetTools();
}

/// <summary>
/// Default implementation of tool provider
/// Add your specific tools here
/// </summary>
public class DefaultToolProvider : IToolProvider
{
    private readonly ILogger<DefaultToolProvider> _logger;

    public DefaultToolProvider(ILogger<DefaultToolProvider> logger)
    {
        _logger = logger;
    }

    public object GetTools()
    {
        // TODO: Replace with your actual tool definitions
        // Example: return new[] { myTool1, myTool2, myTool3 };
        
        _logger.LogDebug("Providing tools to agent");
        
        // Return empty collection if no tools are defined
        return Array.Empty<object>();
    }
}
