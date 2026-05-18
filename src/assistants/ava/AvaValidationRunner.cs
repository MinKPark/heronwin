using System.Text.Json;
using HeronWin.Brain;

namespace HeronWin.Ava;

internal sealed record AvaValidationRunRequest(
    BrainScenarioSuite ScenarioSuite,
    AvaValidationConfig ValidationConfig,
    string UxScenarioPath,
    string ValidationConfigPath,
    string RunId,
    string? OutputDirectory = null);

internal static class AvaCommandExecutionStatus
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string NotRun = "not-run";
}

internal sealed record AvaCommandExecutionRequest(
    string RunId,
    string ScenarioName,
    BrainScenarioAssertions Assertions,
    int StepNumber,
    string StepId,
    string StepName,
    string Command,
    AvaEffectiveStepValidationConfig ValidationConfig);

internal sealed record AvaExecutionAccessibilityObservation(
    string Category,
    string Summary,
    string? ToolName = null);

internal sealed record AvaCommandExecutionResult(
    string Status,
    string Summary,
    string? AssistantLogText,
    string? AssistantSpokenText,
    string? WindowHandle,
    int ToolCallCount,
    int ToolErrorCount,
    IReadOnlyList<string> Failures,
    IReadOnlyList<AvaExecutionAccessibilityObservation> Observations)
{
    public static AvaCommandExecutionResult Passed(
        string summary,
        string? assistantLogText = null,
        string? assistantSpokenText = null,
        string? windowHandle = null,
        int toolCallCount = 0,
        int toolErrorCount = 0,
        IReadOnlyList<AvaExecutionAccessibilityObservation>? observations = null)
        => new(
            AvaCommandExecutionStatus.Passed,
            summary,
            assistantLogText,
            assistantSpokenText,
            windowHandle,
            toolCallCount,
            toolErrorCount,
            [],
            observations ?? []);

    public static AvaCommandExecutionResult Failed(
        string summary,
        IReadOnlyList<string> failures,
        string? assistantLogText = null,
        string? assistantSpokenText = null,
        string? windowHandle = null,
        int toolCallCount = 0,
        int toolErrorCount = 0,
        IReadOnlyList<AvaExecutionAccessibilityObservation>? observations = null)
        => new(
            AvaCommandExecutionStatus.Failed,
            summary,
            assistantLogText,
            assistantSpokenText,
            windowHandle,
            toolCallCount,
            toolErrorCount,
            failures,
            observations ?? []);

    public static AvaCommandExecutionResult NotRun(string summary)
        => new(
            AvaCommandExecutionStatus.NotRun,
            summary,
            null,
            null,
            null,
            0,
            0,
            [],
            []);

    public bool FailedFunctionally =>
        string.Equals(Status, AvaCommandExecutionStatus.Failed, StringComparison.Ordinal);
}

internal interface IAvaCommandDriver
{
    Task<AvaCommandExecutionResult> ExecuteAsync(
        AvaCommandExecutionRequest request,
        CancellationToken cancellationToken);
}

internal sealed class AvaReportOnlyCommandDriver : IAvaCommandDriver
{
    public static AvaReportOnlyCommandDriver Instance { get; } = new();

    private AvaReportOnlyCommandDriver()
    {
    }

    public Task<AvaCommandExecutionResult> ExecuteAsync(
        AvaCommandExecutionRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(AvaCommandExecutionResult.NotRun(
            "No AVA command driver was configured; the report contains validation evidence only."));
}

internal sealed class AvaBrainCommandDriver(
    AppConfig config,
    ILlmClient llmClient,
    McpClientManager mcpManager,
    string? jsonLogPath) : IAvaCommandDriver
{
    private readonly List<AgentMessage> history = [];
    private readonly DesktopSessionContext desktopSession = new();
    private long nextTurnId;
    private AppConfig currentConfig = config;

    public async Task<AvaCommandExecutionResult> ExecuteAsync(
        AvaCommandExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var turnId = Interlocked.Increment(ref nextTurnId);
        DebugTrace.WriteStructuredEvent(
            "ava.command.begin",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["runId"] = request.RunId,
                ["scenario"] = request.ScenarioName,
                ["stepId"] = request.StepId,
                ["stepNumber"] = request.StepNumber,
                ["command"] = request.Command,
            });

        try
        {
            var processedTurn = await BrainTurnProcessor.ProcessAsync(
                turnId,
                request.Command,
                history,
                desktopSession,
                currentConfig,
                llmClient,
                mcpManager,
                cancellationToken,
                turnSource: "ava");
            if (processedTurn.UpdatedConfig is not null)
            {
                currentConfig = processedTurn.UpdatedConfig;
            }

            var assessment = AssessFunctionalResult(turnId, request.Assertions, processedTurn.Reply);
            var windowHandle = await ResolveCurrentWindowHandleAsync(cancellationToken);
            var result = assessment.Passed
                ? AvaCommandExecutionResult.Passed(
                    "AVA command completed functional checks.",
                    processedTurn.Reply.LogText,
                    processedTurn.Reply.SpokenText,
                    windowHandle,
                    toolCallCount: assessment.ToolCallCount,
                    toolErrorCount: assessment.ToolErrorCount,
                    observations: BuildExecutionObservations(assessment))
                : AvaCommandExecutionResult.Failed(
                    $"AVA command failed functional checks: {string.Join("; ", assessment.Failures)}",
                    assessment.Failures,
                    processedTurn.Reply.LogText,
                    processedTurn.Reply.SpokenText,
                    windowHandle,
                    toolCallCount: assessment.ToolCallCount,
                    toolErrorCount: assessment.ToolErrorCount,
                    observations: BuildExecutionObservations(assessment));

            DebugTrace.WriteStructuredEvent(
                "ava.command.complete",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["runId"] = request.RunId,
                    ["stepId"] = request.StepId,
                    ["status"] = result.Status,
                    ["toolCallCount"] = result.ToolCallCount,
                    ["toolErrorCount"] = result.ToolErrorCount,
                    ["failureCount"] = result.Failures.Count,
                    ["windowHandle"] = result.WindowHandle,
                });

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugTrace.WriteStructuredEvent(
                "ava.command.exception",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["runId"] = request.RunId,
                    ["stepId"] = request.StepId,
                    ["error"] = DebugTrace.Preview(ex.ToString(), 800),
                });

            return AvaCommandExecutionResult.Failed(
                $"AVA command execution threw {ex.GetType().Name}: {ex.Message}",
                [$"{ex.GetType().Name}: {ex.Message}"]);
        }
    }

    private BrainTurnAssessment AssessFunctionalResult(
        long turnId,
        BrainScenarioAssertions assertions,
        AgentReply reply)
    {
        if (!string.IsNullOrWhiteSpace(jsonLogPath))
        {
            var records = BrainTraceLogReader.ReadAll(jsonLogPath);
            return BrainScenarioEvaluator.AssessTurn(records, turnId, assertions);
        }

        var finalText = $"{reply.SpokenText}\n{reply.LogText}".Trim();
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(finalText))
        {
            failures.Add("No assistant reply text was produced for this AVA command.");
        }

        return new BrainTurnAssessment(
            failures.Count == 0,
            ToolCallCount: 0,
            ToolErrorCount: 0,
            HasAssistantReply: failures.Count == 0,
            HasReplyContradiction: false,
            HasExplicitlyUnresolvedOutcome: false,
            reply.SpokenText,
            reply.LogText,
            failures);
    }

    private async Task<string?> ResolveCurrentWindowHandleAsync(CancellationToken cancellationToken)
    {
        var windowHandle = FirstNonEmpty(
            desktopSession.CurrentWindowHandle,
            TryExtractWindowHandle(desktopSession.RecentWindowContext),
            TryExtractWindowHandle(desktopSession.RecentUiTreeContext),
            TryExtractWindowHandle(desktopSession.RecentFocusContext),
            TryExtractWindowHandle(desktopSession.RecentListWindowsOutput));
        if (!string.IsNullOrWhiteSpace(windowHandle))
        {
            desktopSession.CurrentWindowHandle = windowHandle;
            return windowHandle;
        }

        try
        {
            var toolResult = await mcpManager.CallToolAsync(
                "list_windows",
                new Dictionary<string, object?>(),
                cancellationToken);
            if (!toolResult.IsError &&
                !string.IsNullOrWhiteSpace(toolResult.Text) &&
                TryExtractWindowHandle(toolResult.Text) is { Length: > 0 } resolvedHandle)
            {
                desktopSession.CurrentWindowHandle = resolvedHandle;
                desktopSession.RecentListWindowsOutput = toolResult.Text;
                return resolvedHandle;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DebugTrace.WriteStructuredEvent(
                "ava.command.window_handle_unavailable",
                new Dictionary<string, object?>
                {
                    ["error"] = DebugTrace.Preview(ex.Message, 400),
                });
        }

        return null;
    }

    internal static string? TryExtractWindowHandle(string? toolOutputText)
    {
        if (string.IsNullOrWhiteSpace(toolOutputText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(toolOutputText);
            var root = document.RootElement;

            if (TryGetJsonStringProperty(root, "selectedWindowHandle") is { Length: > 0 } selectedWindowHandle)
            {
                return selectedWindowHandle;
            }

            foreach (var propertyName in new[] { "selectedWindow", "window" })
            {
                if (TryGetJsonProperty(root, propertyName, out var window) &&
                    window.ValueKind == JsonValueKind.Object &&
                    TryGetJsonStringProperty(window, "handle") is { Length: > 0 } handle)
                {
                    return handle;
                }
            }

            if (!TryGetJsonProperty(root, "windows", out var windowsElement) ||
                windowsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var handles = new List<string>();
            foreach (var window in windowsElement.EnumerateArray())
            {
                if (window.ValueKind != JsonValueKind.Object ||
                    TryGetJsonStringProperty(window, "handle") is not { Length: > 0 } handle)
                {
                    continue;
                }

                handles.Add(handle);
                if (TryGetJsonBooleanProperty(window, "isSelected") == true)
                {
                    return handle;
                }
            }

            return handles.Count == 1 ? handles[0] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static string? TryGetJsonStringProperty(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool? TryGetJsonBooleanProperty(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        property = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (!string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            property = candidate.Value.Clone();
            return true;
        }

        return false;
    }

    private static IReadOnlyList<AvaExecutionAccessibilityObservation> BuildExecutionObservations(
        BrainTurnAssessment assessment)
    {
        var observations = new List<AvaExecutionAccessibilityObservation>();
        if (assessment.ToolErrorCount > 0)
        {
            observations.Add(new AvaExecutionAccessibilityObservation(
                "execution-friction",
                $"The command produced {assessment.ToolErrorCount} tool error event(s)."));
        }

        if (assessment.Failures.Count > 0)
        {
            observations.AddRange(assessment.Failures.Select(static failure =>
                new AvaExecutionAccessibilityObservation("execution-blocker", failure)));
        }

        return observations;
    }
}

internal static class AvaValidationRunner
{
    public static AvaValidationReport Run(AvaValidationRunRequest request)
        => RunAsync(
                request,
                AvaReportOnlyCommandDriver.Instance,
                evidenceCollector: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    public static async Task<AvaValidationReport> RunAsync(
        AvaValidationRunRequest request,
        IAvaCommandDriver commandDriver,
        IAvaEvidenceCollector? evidenceCollector,
        CancellationToken cancellationToken)
    {
        var scenario = SelectScenario(request.ScenarioSuite);
        var steps = new List<AvaStepResult>();
        var evidenceWriter = request.OutputDirectory is null
            ? null
            : new AvaEvidenceBundleWriter();

        for (var index = 0; index < scenario.Commands.Count; index++)
        {
            steps.Add(await BuildStepResultAsync(
                scenario,
                scenario.Commands[index],
                index,
                request,
                commandDriver,
                evidenceCollector,
                evidenceWriter,
                cancellationToken));
        }

        return new AvaValidationReport(
            request.RunId,
            scenario.Name,
            request.ValidationConfig.Name,
            request.ValidationConfig.Profile,
            request.ValidationConfig.ContinuationPolicy,
            request.ValidationConfig.Checkpoints,
            request.UxScenarioPath,
            request.ValidationConfigPath,
            steps);
    }

    public static bool RequiresDeterministicEvidenceCollection(AvaValidationConfig validationConfig)
        => Enumerable.Range(0, Math.Max(1, validationConfig.Steps.Count))
            .Select(validationConfig.ResolveStep)
            .Any(static step => !string.IsNullOrWhiteSpace(step.WindowHandle));

    private static BrainScenarioDefinition SelectScenario(BrainScenarioSuite suite)
    {
        if (suite.Scenarios.Count == 0)
        {
            throw new InvalidOperationException("UX scenario file must contain at least one scenario.");
        }

        if (suite.Scenarios.Count > 1)
        {
            throw new InvalidOperationException("AVA Phase 2 no-op validation supports one UX scenario per run.");
        }

        return suite.Scenarios[0];
    }

    private static async Task<AvaStepResult> BuildStepResultAsync(
        BrainScenarioDefinition scenario,
        string command,
        int zeroBasedIndex,
        AvaValidationRunRequest request,
        IAvaCommandDriver commandDriver,
        IAvaEvidenceCollector? evidenceCollector,
        AvaEvidenceBundleWriter? evidenceWriter,
        CancellationToken cancellationToken)
    {
        var stepNumber = zeroBasedIndex + 1;
        var effectiveConfig = request.ValidationConfig.ResolveStep(zeroBasedIndex);
        var stepName = string.IsNullOrWhiteSpace(effectiveConfig.Name)
            ? $"Step {stepNumber}"
            : effectiveConfig.Name;
        var stepId = $"step-{stepNumber:000}";

        var executionResult = await commandDriver.ExecuteAsync(
            new AvaCommandExecutionRequest(
                request.RunId,
                scenario.Name,
                scenario.Assertions,
                stepNumber,
                stepId,
                stepName,
                command,
                effectiveConfig),
            cancellationToken);
        var reportCommand = AvaSensitiveValueRedactor.Redact(command) ?? command;
        var reportExecutionResult = RedactExecutionResult(executionResult);
        var evidenceWindowHandle = string.IsNullOrWhiteSpace(executionResult.WindowHandle)
            ? effectiveConfig.WindowHandle
            : executionResult.WindowHandle;

        var evidenceRecords = await CollectEvidenceAsync(
            request.RunId,
            stepId,
            stepNumber,
            stepName,
            reportCommand,
            evidenceWindowHandle,
            evidenceCollector,
            cancellationToken);

        var evidenceReference = WriteEvidenceReference(
            request,
            stepId,
            stepNumber,
            stepName,
            effectiveConfig,
            evidenceWindowHandle,
            evidenceRecords,
            evidenceWriter);

        var validationCheckpoint = effectiveConfig.Checkpoints.LastOrDefault() ?? AvaCheckpointTiming.After;
        var validationContext = new AvaDeterministicValidationContext(
            stepNumber,
            stepId,
            request.ValidationConfig.Profile,
            validationCheckpoint,
            evidenceReference,
            evidenceRecords);
        var findings = AvaDeterministicValidators.Validate(validationContext)
            .Concat(AvaWebDeterministicValidators.Validate(validationContext))
            .Concat(CreateExecutionFindings(
                stepNumber,
                stepId,
                request.ValidationConfig.Profile,
                validationCheckpoint,
                evidenceReference,
                reportExecutionResult))
            .ToArray();
        var checkpoints = effectiveConfig.Checkpoints
            .Select(checkpoint =>
            {
                var checkpointFindings = findings
                    .Where(finding => string.Equals(finding.Checkpoint, checkpoint, StringComparison.Ordinal))
                    .ToArray();
                if (checkpointFindings.Length == 0 &&
                    !string.Equals(checkpoint, validationCheckpoint, StringComparison.Ordinal))
                {
                    return new AvaCheckpointResult(
                        checkpoint,
                        AvaFindingStatus.NotTested,
                        "Deterministic validators run after scenario steps; this checkpoint was not evaluated.");
                }

                var status = AvaFindingStatus.Aggregate(checkpointFindings.Select(static finding => finding.Status));
                var summary = status == AvaFindingStatus.Pass
                    ? "Deterministic accessibility validators completed without findings for this checkpoint."
                    : $"Deterministic accessibility validators produced {checkpointFindings.Length} finding(s) for this checkpoint.";

                return new AvaCheckpointResult(
                    checkpoint,
                    status,
                    summary);
            })
            .ToArray();

        return new AvaStepResult(
            stepNumber,
            stepId,
            stepName,
            reportCommand,
            effectiveConfig.ContinuationPolicy,
            evidenceReference,
            checkpoints,
            findings,
            reportExecutionResult);
    }

    private static AvaCommandExecutionResult RedactExecutionResult(AvaCommandExecutionResult executionResult)
        => executionResult with
        {
            Summary = AvaSensitiveValueRedactor.Redact(executionResult.Summary) ?? executionResult.Summary,
            AssistantLogText = AvaSensitiveValueRedactor.Redact(executionResult.AssistantLogText),
            AssistantSpokenText = AvaSensitiveValueRedactor.Redact(executionResult.AssistantSpokenText),
            Failures = executionResult.Failures
                .Select(static failure => AvaSensitiveValueRedactor.Redact(failure) ?? failure)
                .ToArray(),
            Observations = executionResult.Observations
                .Select(static observation => observation with
                {
                    Summary = AvaSensitiveValueRedactor.Redact(observation.Summary) ?? observation.Summary,
                })
                .ToArray(),
        };

    private static async Task<IReadOnlyList<AvaEvidenceRecord>> CollectEvidenceAsync(
        string runId,
        string stepId,
        int stepNumber,
        string stepName,
        string command,
        string? windowHandle,
        IAvaEvidenceCollector? evidenceCollector,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(windowHandle))
        {
            return
            [
                AvaEvidenceRecord.Missing(
                    "No deterministic windowHandle was configured for this step.")
            ];
        }

        if (evidenceCollector is null)
        {
            return
            [
                AvaEvidenceRecord.Missing(
                    "No evidence collector was configured for this deterministic windowHandle.")
            ];
        }

        try
        {
            var records = await evidenceCollector.CollectAsync(
                new AvaEvidenceCollectionRequest(
                    runId,
                    stepId,
                    stepNumber,
                    stepName,
                    command,
                    windowHandle),
                cancellationToken);

            if (records.Count > 0)
            {
                return records;
            }

            return
            [
                AvaEvidenceRecord.Missing(
                    "The evidence collector returned no result for this step.")
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return
            [
                AvaEvidenceRecord.ErrorResult(
                    "ava.evidence",
                    $"{ex.GetType().Name}: {ex.Message}")
            ];
        }
    }

    private static AvaStepEvidenceReference WriteEvidenceReference(
        AvaValidationRunRequest request,
        string stepId,
        int stepNumber,
        string stepName,
        AvaEffectiveStepValidationConfig effectiveConfig,
        string? windowHandle,
        IReadOnlyList<AvaEvidenceRecord> evidenceRecords,
        AvaEvidenceBundleWriter? evidenceWriter)
    {
        if (evidenceWriter is null || string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return new AvaStepEvidenceReference(
                stepId,
                $"evidence/{stepId}/manifest.json",
                ResolveEvidenceStatus(evidenceRecords),
                evidenceRecords.Count);
        }

        return evidenceWriter.WriteStepEvidence(new AvaEvidenceBundleWriteRequest(
            request.RunId,
            request.OutputDirectory,
            stepId,
            stepNumber,
            stepName,
            windowHandle,
            evidenceRecords));
    }

    private static IReadOnlyList<AvaAccessibilityFinding> CreateExecutionFindings(
        int stepNumber,
        string stepId,
        string profileId,
        string checkpoint,
        AvaStepEvidenceReference evidenceReference,
        AvaCommandExecutionResult executionResult)
    {
        if (!executionResult.FailedFunctionally)
        {
            return [];
        }

        var rule = AvaProfileCatalog.ResolveRule(profileId, $"AVA-EXECUTION-FAILED-{stepNumber:000}");
        return
        [
            new AvaAccessibilityFinding(
                $"AVA-EXECUTION-FAILED-{stepNumber:000}",
                AvaFindingStatus.NeedsReview,
                checkpoint,
                executionResult.Summary,
                rule?.ProfileId ?? profileId,
                rule?.RuleId,
                rule?.SourceStandard,
                evidenceReference.ManifestPath,
                stepId,
                toolName: "ava.command-driver")
        ];
    }

    private static string ResolveEvidenceStatus(IReadOnlyList<AvaEvidenceRecord> records)
    {
        if (records.Any(static record => record.Status == AvaEvidenceStatus.Captured))
        {
            return AvaEvidenceStatus.Captured;
        }

        if (records.Any(static record => record.Status == AvaEvidenceStatus.Error))
        {
            return AvaEvidenceStatus.Error;
        }

        return AvaEvidenceStatus.Missing;
    }
}

internal static class AvaNoOpValidationRunner
{
    public static AvaValidationReport Run(AvaValidationRunRequest request)
        => AvaValidationRunner.Run(request);

    public static Task<AvaValidationReport> RunAsync(
        AvaValidationRunRequest request,
        IAvaEvidenceCollector? evidenceCollector,
        CancellationToken cancellationToken)
        => AvaValidationRunner.RunAsync(
            request,
            AvaReportOnlyCommandDriver.Instance,
            evidenceCollector,
            cancellationToken);

    public static bool RequiresDeterministicEvidenceCollection(AvaValidationConfig validationConfig)
        => AvaValidationRunner.RequiresDeterministicEvidenceCollection(validationConfig);
}
