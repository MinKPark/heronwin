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

try
{
    var validationInputs = ResolveValidationInputs(consoleOptions);
    var scenarioSuite = BrainScenarioLoader.LoadFromFile(validationInputs.UxScenarioPath);
    var validationConfig = AvaValidationConfigLoader.LoadFromFile(validationInputs.ValidationConfigPath);
    var runId = CreateRunId();
    var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ava", runId);
    McpClientManager? mcpClientManager = null;

    try
    {
        var evidenceCollector = await CreateEvidenceCollectorAsync(validationConfig);
        if (evidenceCollector.Manager is not null)
        {
            mcpClientManager = evidenceCollector.Manager;
        }

        var report = await AvaNoOpValidationRunner.RunAsync(
            new AvaValidationRunRequest(
                scenarioSuite,
                validationConfig,
                validationInputs.UxScenarioPath,
                validationInputs.ValidationConfigPath,
                runId,
                outputDirectory),
            evidenceCollector.Collector,
            CancellationToken.None);

        var writeResult = AvaReportWriter.Write(report, outputDirectory);

        Console.WriteLine($"AVA report: {writeResult.MarkdownPath}");
        Console.WriteLine($"AVA report JSON: {writeResult.JsonPath}");

        Environment.ExitCode = report.HasBlockingFindings ? 1 : 0;
    }
    finally
    {
        if (mcpClientManager is not null)
        {
            await mcpClientManager.DisposeAsync();
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

static async Task<AvaEvidenceCollectorSetup> CreateEvidenceCollectorAsync(AvaValidationConfig validationConfig)
{
    if (!AvaNoOpValidationRunner.RequiresDeterministicEvidenceCollection(validationConfig))
    {
        return new AvaEvidenceCollectorSetup(null, null);
    }

    var appConfig = AppConfig.Load("ava");
    if (appConfig.McpServers.Count == 0)
    {
        return new AvaEvidenceCollectorSetup(null, null);
    }

    var manager = new McpClientManager();
    try
    {
        await manager.ConnectAsync(appConfig.McpServers, CancellationToken.None);
        await manager.ListAllToolsAsync(CancellationToken.None);
        return new AvaEvidenceCollectorSetup(new AvaMcpEvidenceCollector(manager), manager);
    }
    catch
    {
        await manager.DisposeAsync();
        throw;
    }
}

internal sealed record AvaValidationInputPaths(
    string UxScenarioPath,
    string ValidationConfigPath);

internal sealed record AvaEvidenceCollectorSetup(
    IAvaEvidenceCollector? Collector,
    McpClientManager? Manager);
