using HeronWin.Brain;

Console.OutputEncoding = System.Text.Encoding.UTF8;

BrainConsoleOptions consoleOptions;
try
{
    consoleOptions = BrainConsoleMode.ParseTars(args);
}
catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
{
    Console.Error.WriteLine($"x  {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

if (consoleOptions.ShowHelp || !consoleOptions.IsTraceReport && string.IsNullOrWhiteSpace(consoleOptions.ScenarioFilePath))
{
    BrainConsoleMode.PrintTarsHelp();
    Environment.ExitCode = consoleOptions.ShowHelp ? 0 : 1;
    return;
}

if (consoleOptions.IsTraceReport)
{
    try
    {
        Console.WriteLine(BrainTraceReporter.GenerateMarkdown(consoleOptions.TraceReportPath!));
    }
    catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
    {
        Console.Error.WriteLine($"x  {ex.Message}");
        Environment.ExitCode = 1;
    }

    return;
}

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var config = AppConfig.Load("tars");
var provider = LlmProviderCatalog.Resolve(config);

ArtifactCleanup.CleanupPreviousRunArtifacts(AppContext.BaseDirectory, Environment.ProcessPath);
DebugTrace.Configure(config.EnableDebugTrace || consoleOptions.RequiresDebugTrace);
Display.Banner("tars", "scenario assistant");
var httpClientSetup = BrainHttpClientFactory.Create();
using var httpClient = httpClientSetup.Client;
await using var mcpManager = new McpClientManager();

DebugTrace.WriteStructuredEvent(
    "session.start",
    new Dictionary<string, object?>
    {
        ["pid"] = Environment.ProcessId,
        ["process"] = Environment.ProcessPath ?? "(unknown)",
        ["cwd"] = Directory.GetCurrentDirectory(),
        ["baseDir"] = AppContext.BaseDirectory,
        ["sessionId"] = DebugTrace.SessionId,
        ["assistantId"] = "tars",
        ["launchMode"] = "scenario",
        ["scriptedScenarioPath"] = consoleOptions.ScenarioFilePath,
        ["debugTraceEnabled"] = DebugTrace.IsEnabled,
        ["llmProvider"] = config.LlmProvider.ToString(),
        ["openAiModel"] = config.OpenAiModel,
        ["openAiCodexModel"] = config.OpenAiCodexModel,
        ["anthropicModel"] = config.AnthropicModel,
        ["maxContextTokens"] = config.MaxContextTokens,
        ["postActionUiSettleDelayMs"] = config.PostActionUiSettleDelayMs,
        ["logsDirectory"] = DebugTrace.BuildLogsDirectory(AppContext.BaseDirectory),
        ["providerCapabilities"] = new Dictionary<string, object?>
        {
            ["supportsScriptedMode"] = provider.Capabilities.SupportsScriptedMode,
            ["supportsVisionInputs"] = provider.Capabilities.SupportsVisionInputs,
            ["supportsToolCalls"] = provider.Capabilities.SupportsToolCalls,
        },
        ["mcpServers"] = config.McpServers.Select(server => new Dictionary<string, object?>
        {
            ["name"] = server.Name,
            ["command"] = server.Command,
            ["args"] = server.Args ?? [],
            ["envKeys"] = server.Env?.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
        }).ToArray(),
        ["displayTopology"] = DisplayTopology.Capture(),
        ["textLogPath"] = DebugTrace.LogFilePath,
        ["jsonLogPath"] = DebugTrace.JsonLogFilePath,
    });

DebugTrace.WriteEvent(
    "config.loaded",
    $"assistant=tars, mode=scenario, llmProvider={config.LlmProvider}, openAiModel={config.OpenAiModel}, openAiCodexModel={config.OpenAiCodexModel}, anthropicModel={config.AnthropicModel}, postActionUiSettleDelayMs={config.PostActionUiSettleDelayMs}, agentDefinitionPath={config.AgentDefinitionPath}, agentCoreDefinitionPath={config.AgentPrompts.CoreDefinitionPath ?? "(none)"}, agentSkills={config.AgentPrompts.Skills.Count}, mcpServers={config.McpServers.Count}, debugTrace={DebugTrace.IsEnabled}");

var exitCode = await RunScenarioModeAsync(cancellationSource.Token);
await ShutdownAsync();
Environment.ExitCode = exitCode;
return;

async Task<int> RunScenarioModeAsync(CancellationToken cancellationToken)
{
    if (!provider.Capabilities.SupportsScriptedMode)
    {
        Display.Error($"{provider.DisplayName} does not support scenario mode.");
        return 1;
    }

    ILlmClient scriptedLlmClient;
    try
    {
        provider.ValidateConfiguration(config);
        scriptedLlmClient = provider.CreateClient(config, httpClient);
    }
    catch (Exception ex)
    {
        Display.Error(ex.Message);
        return 1;
    }

    Display.Info($"LLM: {scriptedLlmClient.DisplayName}");
    if (httpClientSetup.BypassedBrokenLoopbackProxy)
    {
        Display.Warn(
            $"Ignoring broken loopback proxy setting for outbound API calls: {httpClientSetup.BypassedProxyValue}");
    }
    if (DebugTrace.IsEnabled && !string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
    {
        Display.Info($"Debug trace: {DebugTrace.LogFilePath}");
        if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
        {
            Display.Info($"Debug trace JSONL: {DebugTrace.JsonLogFilePath}");
        }
    }

    await ConnectMcpServersAsync(cancellationToken);
    return await ScriptedConversationRunner.RunAsync(
        consoleOptions,
        config,
        scriptedLlmClient,
        mcpManager,
        cancellationToken);
}

async Task ConnectMcpServersAsync(CancellationToken cancellationToken)
{
    if (config.McpServers.Count > 0)
    {
        Display.Info($"Connecting to {config.McpServers.Count} MCP server(s)...");
        try
        {
            await mcpManager.ConnectAsync(config.McpServers, cancellationToken);
            var tools = await mcpManager.ListAllToolsAsync(cancellationToken);
            Display.Info($"MCP tools available: {string.Join(", ", tools.Select(tool => tool.Name).DefaultIfEmpty("(none)"))}");
        }
        catch (Exception ex)
        {
            Display.Warn($"MCP connection failed: {ex.Message}");
        }
    }
    else
    {
        Display.Info("No MCP servers configured. Running without tool support.");
    }
}

async Task ShutdownAsync()
{
    Display.Info("Shutting down...");
    DebugTrace.WriteEvent("session.shutdown", "Application shutdown completed.");
    if (!DebugTrace.IsEnabled)
    {
        ArtifactCleanup.CleanupCurrentRunArtifacts(DebugTrace.LogFilePath, DebugTrace.JsonLogFilePath, AppContext.BaseDirectory);
    }

    if (DebugTrace.IsEnabled && !string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
    {
        Display.Info($"Debug log saved to: {DebugTrace.LogFilePath}");
        if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
        {
            Display.Info($"Debug JSONL saved to: {DebugTrace.JsonLogFilePath}");
        }

        Display.Info($"Debug artifacts saved under: {DebugTrace.BuildLogsDirectory(AppContext.BaseDirectory)}");
    }

    await Task.CompletedTask;
}
