using HeronWin.Brain;
using HeronWin.Ava;

Console.OutputEncoding = System.Text.Encoding.UTF8;

AvaConsoleOptions consoleOptions;
try
{
    consoleOptions = BrainConsoleMode.ParseAva(args);
}
catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
{
    Console.Error.WriteLine($"x  {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

if (consoleOptions.ShowHelp || !consoleOptions.IsValidationRun && !consoleOptions.IsTraceReport)
{
    BrainConsoleMode.PrintAvaHelp();
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

try
{
    var validationInputs = ResolveValidationInputs(consoleOptions);
    var scenarioSuite = BrainScenarioLoader.LoadFromFile(validationInputs.UxScenarioPath);
    var validationConfig = AvaValidationConfigLoader.LoadFromFile(validationInputs.ValidationConfigPath);
    var runId = CreateRunId();
    var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ava", runId);
    var appConfig = AppConfig.Load("ava");
    var provider = LlmProviderCatalog.Resolve(appConfig);
    if (!provider.Capabilities.SupportsScriptedMode)
    {
        Display.Error($"{provider.DisplayName} does not support AVA scenario validation mode.");
        Environment.ExitCode = 1;
        return;
    }

    provider.ValidateConfiguration(appConfig);
    ArtifactCleanup.CleanupPreviousRunArtifacts(AppContext.BaseDirectory, Environment.ProcessPath);
    DebugTrace.Configure(true);
    Display.Banner("ava", "accessibility validation assistant");

    var httpClientSetup = BrainHttpClientFactory.Create();
    using var httpClient = httpClientSetup.Client;
    await using var mcpClientManager = new McpClientManager();

    DebugTrace.WriteStructuredEvent(
        "session.start",
        new Dictionary<string, object?>
        {
            ["pid"] = Environment.ProcessId,
            ["process"] = Environment.ProcessPath ?? "(unknown)",
            ["cwd"] = Directory.GetCurrentDirectory(),
            ["baseDir"] = AppContext.BaseDirectory,
            ["sessionId"] = DebugTrace.SessionId,
            ["assistantId"] = "ava",
            ["launchMode"] = "accessibility-validation",
            ["uxScenarioPath"] = validationInputs.UxScenarioPath,
            ["validationConfigPath"] = validationInputs.ValidationConfigPath,
            ["runId"] = runId,
            ["debugTraceEnabled"] = DebugTrace.IsEnabled,
            ["llmProvider"] = appConfig.LlmProvider.ToString(),
            ["openAiModel"] = appConfig.OpenAiModel,
            ["openAiCodexModel"] = appConfig.OpenAiCodexModel,
            ["anthropicModel"] = appConfig.AnthropicModel,
            ["maxContextTokens"] = appConfig.MaxContextTokens,
            ["postActionUiSettleDelayMs"] = appConfig.PostActionUiSettleDelayMs,
            ["logsDirectory"] = DebugTrace.BuildLogsDirectory(AppContext.BaseDirectory),
            ["mcpServers"] = appConfig.McpServers.Select(server => new Dictionary<string, object?>
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

    Display.Info($"LLM: {provider.DisplayName}");
    if (httpClientSetup.BypassedBrokenLoopbackProxy)
    {
        Display.Warn(
            $"Ignoring broken loopback proxy setting for outbound API calls: {httpClientSetup.BypassedProxyValue}");
    }

    if (!string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
    {
        Display.Info($"Debug trace: {DebugTrace.LogFilePath}");
        if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
        {
            Display.Info($"Debug trace JSONL: {DebugTrace.JsonLogFilePath}");
        }
    }

    var tools = await ConnectMcpServersAsync(appConfig, mcpClientManager, cancellationSource.Token);
    var evidenceCollector = HasEvidenceTools(tools)
        ? new AvaMcpEvidenceCollector(mcpClientManager)
        : null;
    if (evidenceCollector is null)
    {
        Display.Warn("AVA evidence tools are not available; validation findings will report evidence gaps.");
    }

    var llmClient = provider.CreateClient(appConfig, httpClient);
    var commandDriver = new AvaBrainCommandDriver(
        appConfig,
        llmClient,
        mcpClientManager,
        DebugTrace.JsonLogFilePath);
    try
    {
        var report = await AvaValidationRunner.RunAsync(
            new AvaValidationRunRequest(
                scenarioSuite,
                validationConfig,
                validationInputs.UxScenarioPath,
                validationInputs.ValidationConfigPath,
                runId,
                outputDirectory),
            commandDriver,
            evidenceCollector,
            cancellationSource.Token);

        var writeResult = AvaReportWriter.Write(report, outputDirectory);

        Console.WriteLine($"AVA report: {writeResult.MarkdownPath}");
        Console.WriteLine($"AVA report JSON: {writeResult.JsonPath}");

        Environment.ExitCode = report.HasBlockingFindings ? 1 : 0;
    }
    finally
    {
        Display.Info("Shutting down...");
        DebugTrace.WriteEvent("session.shutdown", "AVA validation shutdown completed.");
        if (!string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
        {
            Display.Info($"Debug log saved to: {DebugTrace.LogFilePath}");
            if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
            {
                Display.Info($"Debug JSONL saved to: {DebugTrace.JsonLogFilePath}");
            }

            Display.Info($"Debug artifacts saved under: {DebugTrace.BuildLogsDirectory(AppContext.BaseDirectory)}");
        }
    }
}
catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
{
    Console.Error.WriteLine($"x  {ex.Message}");
    Environment.ExitCode = 1;
}

static AvaValidationInputPaths ResolveValidationInputs(AvaConsoleOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.RunBundlePath))
    {
        var bundle = AvaRunBundleLoader.LoadFromFile(options.RunBundlePath);
        return new AvaValidationInputPaths(bundle.UxScenarioPath, bundle.ValidationConfigPath);
    }

    return new AvaValidationInputPaths(options.UxScenarioPath!, options.ValidationConfigPath!);
}

static string CreateRunId()
    => $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}";

static async Task<IReadOnlyList<ToolDefinition>> ConnectMcpServersAsync(
    AppConfig appConfig,
    McpClientManager mcpClientManager,
    CancellationToken cancellationToken)
{
    if (appConfig.McpServers.Count == 0)
    {
        Display.Info("No MCP servers configured. Running without external UI tools.");
        return await mcpClientManager.ListAllToolsAsync(cancellationToken);
    }

    try
    {
        Display.Info($"Connecting to {appConfig.McpServers.Count} MCP server(s)...");
        await mcpClientManager.ConnectAsync(appConfig.McpServers, cancellationToken);
        var tools = await mcpClientManager.ListAllToolsAsync(cancellationToken);
        Display.Info($"MCP tools available: {string.Join(", ", tools.Select(tool => tool.Name).DefaultIfEmpty("(none)"))}");
        return tools;
    }
    catch (Exception ex)
    {
        Display.Warn($"MCP connection failed: {ex.Message}");
        return await mcpClientManager.ListAllToolsAsync(cancellationToken);
    }
}

static bool HasEvidenceTools(IReadOnlyList<ToolDefinition> tools)
{
    var toolNames = tools.Select(static tool => tool.Name).ToHashSet(StringComparer.Ordinal);
    return toolNames.Contains("describe_window") &&
        toolNames.Contains("describe_window_focus") &&
        toolNames.Contains("capture_window_screenshot");
}

internal sealed record AvaValidationInputPaths(
    string UxScenarioPath,
    string ValidationConfigPath);
