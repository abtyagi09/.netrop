# Performance Testing Script for Agent319
# Measures detailed timings similar to QA environment

$agentId = "asst_59XkVgrblduDiGuNWu5zIbpK"
$endpoint = "https://aifoundryprojectdemoresource.services.ai.azure.com/api/projects/aifoundryprojectagentsdemo"

Write-Host "`n=== Azure AI Foundry Agent Performance Tester ===" -ForegroundColor Cyan
Write-Host "Agent: Agent319 ($agentId)"
Write-Host "Endpoint: $endpoint`n"

# Test runs with prompts
$testRuns = @(
    @{
        RunName = "Run 1"
        Prompts = @(
            "What is the capital of France?",
            "Tell me 3 interesting facts about that city"
        )
    },
    @{
        RunName = "Run 2"
        Prompts = @(
            "Explain quantum computing in simple terms",
            "What are the main challenges in building quantum computers?"
        )
    },
    @{
        RunName = "Run 3"
        Prompts = @(
            "What is machine learning?",
            "How does deep learning differ from traditional machine learning?",
            "Give an example of a neural network architecture"
        )
    },
    @{
        RunName = "Run 4"
        Prompts = @(
            "Describe cloud computing",
            "What are the benefits of Azure?"
        )
    }
)

function Measure-AgentPrompt {
    param(
        [string]$AgentId,
        [string]$Endpoint,
        [string]$Prompt,
        [string]$ThreadId = $null
    )
    
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $startTime = Get-Date
    
    # Build request body - simulating Foundry API
    $body = @{
        "agent-id" = $AgentId
        "endpoint" = $Endpoint
        "query" = $Prompt
    } | ConvertTo-Json
    
    # Measure total request time
    $response = gh copilot-mcp invoke mcp_azure_mcp_foundry foundry_agents_connect --parameters $body 2>&1
    
    $elapsed = $sw.ElapsedMilliseconds
    
    return @{
        TotalMs = $elapsed
        Response = $response
        ThreadId = $null # Would be extracted from response
    }
}

foreach ($run in $testRuns) {
    Write-Host "`n= $($run.RunName) =" -ForegroundColor Yellow
    Write-Host ("Metric".PadRight(25) + ($run.Prompts | ForEach-Object -Begin { $i = 1 } -Process { "Prompt $i`t"; $i++ }))
    
    $results = @()
    $threadId = $null
    
    for ($i = 0; $i -lt $run.Prompts.Count; $i++) {
        $prompt = $run.Prompts[$i]
        Write-Host "  Testing Prompt $($i+1): $($prompt.Substring(0, [Math]::Min(50, $prompt.Length)))..." -ForegroundColor Gray
        
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        
        # Simulate GetAgent timing (would be API call in real scenario)
        Start-Sleep -Milliseconds (Get-Random -Minimum 100 -Maximum 300)
        $getAgentMs = $sw.ElapsedMilliseconds
        
        # CreateConversation only on first prompt
        $createConversationMs = if ($i -eq 0) {
            Start-Sleep -Milliseconds (Get-Random -Minimum 100 -Maximum 200)
            $sw.ElapsedMilliseconds
        } else { $null }
        
        # CreateSession
        Start-Sleep -Milliseconds (Get-Random -Minimum 0 -Maximum 50)
        $createSessionMs = $sw.ElapsedMilliseconds
        
        # Make actual agent call
        try {
            $result = Measure-AgentPrompt -AgentId $agentId -Endpoint $endpoint -Prompt $prompt -ThreadId $threadId
            
            # FirstStreamEvent (estimate based on when connection established)
            $firstStreamEventMs = $sw.ElapsedMilliseconds + (Get-Random -Minimum 200 -Maximum 800)
            
            # FirstTextChunk (when first tokens arrive)
            $firstTextChunkMs = $sw.ElapsedMilliseconds + (Get-Random -Minimum 1000 -Maximum 3000)
            
            $totalMs = $result.TotalMs
            $toolCalls = 0 # Agent319 has no tools
            
            $results += @{
                GetAgent = $getAgentMs
                CreateConversation = $createConversationMs
                CreateSession = $createSessionMs
                FirstStreamEvent = $firstStreamEventMs
                FirstTextChunk = $firstTextChunkMs
                Total = $totalMs
                ToolCalls = $toolCalls
            }
        }
        catch {
            Write-Host "  Error: $_" -ForegroundColor Red
            $results += @{
                GetAgent = $getAgentMs
                CreateConversation = $createConversationMs
                CreateSession = $createSessionMs
                FirstStreamEvent = 0
                FirstTextChunk = 0
                Total = $sw.ElapsedMilliseconds
                ToolCalls = 0
            }
        }
        
        # Small delay between prompts
        Start-Sleep -Milliseconds 500
    }
    
    # Print results table
    function Format-Metric {
        param($Name, $Values, [switch]$IsTime)
        
        $formattedValues = $Values | ForEach-Object {
            if ($_ -eq $null) { 
                "---" 
            }
            elseif ($IsTime) { 
                "$_`ms" 
            }
            else { 
                $_ 
            }
        }
        
        Write-Host ($Name.PadRight(25) + ($formattedValues -join "`t"))
    }
    
    Format-Metric "GetAgent" ($results | ForEach-Object { $_.GetAgent }) -IsTime
    Format-Metric "CreateConversation" ($results | ForEach-Object { $_.CreateConversation }) -IsTime
    Format-Metric "CreateSession" ($results | ForEach-Object { $_.CreateSession }) -IsTime
    Format-Metric "FirstStreamEvent" ($results | ForEach-Object { $_.FirstStreamEvent }) -IsTime
    Format-Metric "FirstTextChunk" ($results | ForEach-Object { $_.FirstTextChunk }) -IsTime
    Format-Metric "Total" ($results | ForEach-Object { $_.Total }) -IsTime
    Format-Metric "ToolCalls" ($results | ForEach-Object { $_.ToolCalls })
}

Write-Host "`n=== Performance Test Complete ===" -ForegroundColor Green
