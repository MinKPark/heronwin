using HeronWin.Brain;

namespace HeronWin.Ava;

internal sealed record AvaValidationRunRequest(
    BrainScenarioSuite ScenarioSuite,
    AvaValidationConfig ValidationConfig,
    string UxScenarioPath,
    string ValidationConfigPath,
    string RunId,
    string? OutputDirectory = null);

internal static class AvaNoOpValidationRunner
{
    public static AvaValidationReport Run(AvaValidationRunRequest request)
        => RunAsync(request, evidenceCollector: null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    public static async Task<AvaValidationReport> RunAsync(
        AvaValidationRunRequest request,
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
                scenario.Commands[index],
                index,
                request,
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
        string command,
        int zeroBasedIndex,
        AvaValidationRunRequest request,
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

        var evidenceRecords = await CollectEvidenceAsync(
            request.RunId,
            stepId,
            stepNumber,
            stepName,
            command,
            effectiveConfig,
            evidenceCollector,
            cancellationToken);

        var evidenceReference = WriteEvidenceReference(
            request,
            stepId,
            stepNumber,
            stepName,
            effectiveConfig,
            evidenceRecords,
            evidenceWriter);

        var validationCheckpoint = effectiveConfig.Checkpoints.LastOrDefault() ?? AvaCheckpointTiming.After;
        var findings = AvaDeterministicValidators.Validate(new AvaDeterministicValidationContext(
            stepNumber,
            stepId,
            request.ValidationConfig.Profile,
            validationCheckpoint,
            evidenceReference,
            evidenceRecords));
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
            command,
            effectiveConfig.ContinuationPolicy,
            evidenceReference,
            checkpoints,
            findings);
    }

    private static async Task<IReadOnlyList<AvaEvidenceRecord>> CollectEvidenceAsync(
        string runId,
        string stepId,
        int stepNumber,
        string stepName,
        string command,
        AvaEffectiveStepValidationConfig effectiveConfig,
        IAvaEvidenceCollector? evidenceCollector,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(effectiveConfig.WindowHandle))
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
                    effectiveConfig.WindowHandle),
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
            effectiveConfig.WindowHandle,
            evidenceRecords));
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
