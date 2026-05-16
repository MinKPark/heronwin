using HeronWin.Ava;
using HeronWin.Brain;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaValidationRunnerTests
{
    [Fact]
    public async Task ValidationRunner_DrivesCommandBeforeCollectingEvidence()
    {
        var events = new List<string>();
        var driver = new RecordingCommandDriver(request =>
        {
            events.Add($"driver:{request.Command}");
            Assert.Equal("run-001", request.RunId);
            Assert.Equal("Fixture scenario", request.ScenarioName);
            Assert.Equal(1, request.StepNumber);
            Assert.Equal("step-001", request.StepId);
            Assert.Equal("Inspect active window.", request.Command);

            return AvaCommandExecutionResult.Passed(
                "Command completed through AVA driver.",
                windowHandle: "0x00010001",
                toolCallCount: 2);
        });
        var collector = new RecordingEvidenceCollector(request =>
        {
            events.Add($"evidence:{request.WindowHandle}");
            return ValidEvidence;
        });

        var report = await AvaValidationRunner.RunAsync(
            CreateRequest(),
            driver,
            collector,
            CancellationToken.None);

        Assert.Equal(["driver:Inspect active window.", "evidence:0x00010001"], events);
        var step = Assert.Single(report.Steps);
        Assert.NotNull(step.Execution);
        Assert.Equal(AvaCommandExecutionStatus.Passed, step.Execution!.Status);
        Assert.Equal(2, step.Execution.ToolCallCount);
        Assert.Equal("captured", step.Evidence.Status);
        Assert.Empty(step.Findings);
        Assert.Equal(AvaFindingStatus.Pass, Assert.Single(step.Checkpoints).Status);
    }

    [Fact]
    public async Task ValidationRunner_UsesDriverWindowHandleForEvidence_WhenConfigHasNoWindowHandle()
    {
        AvaEvidenceCollectionRequest? evidenceRequest = null;
        var driver = new RecordingCommandDriver(_ => AvaCommandExecutionResult.Passed(
            "Command selected a concrete target window.",
            windowHandle: "0x00020002"));
        var collector = new RecordingEvidenceCollector(request =>
        {
            evidenceRequest = request;
            return ValidEvidence;
        });

        await AvaValidationRunner.RunAsync(
            CreateRequest(),
            driver,
            collector,
            CancellationToken.None);

        Assert.NotNull(evidenceRequest);
        Assert.Equal("0x00020002", evidenceRequest!.WindowHandle);
    }

    [Fact]
    public async Task ValidationRunner_FunctionalCommandFailureBecomesExecutionFinding()
    {
        var driver = new RecordingCommandDriver(_ => AvaCommandExecutionResult.Failed(
            "AVA command failed functional checks: target was not reachable.",
            ["Target was not reachable through the exposed accessibility surface."],
            windowHandle: "0x00010001",
            toolErrorCount: 1));
        var collector = new RecordingEvidenceCollector(_ => ValidEvidence);

        var report = await AvaValidationRunner.RunAsync(
            CreateRequest(),
            driver,
            collector,
            CancellationToken.None);

        var step = Assert.Single(report.Steps);
        Assert.NotNull(step.Execution);
        Assert.Equal(AvaCommandExecutionStatus.Failed, step.Execution!.Status);
        var finding = Assert.Single(step.Findings);
        Assert.Equal("AVA-EXECUTION-FAILED-001", finding.Id);
        Assert.Equal(AvaFindingStatus.NeedsReview, finding.Status);
        Assert.Equal("UIA-EXECUTION-ACCESSIBILITY", finding.RuleId);
        Assert.Equal("ava.command-driver", finding.ToolName);
        Assert.Equal(AvaFindingStatus.NeedsReview, Assert.Single(step.Checkpoints).Status);
    }

    private static AvaValidationRunRequest CreateRequest()
    {
        var suite = BrainScenarioLoader.Parse(
            """
            name: Fixture scenario
            commands:
              - Inspect active window.
            """,
            "scenario.yml");
        var config = AvaValidationConfigLoader.Parse(
            """
            name: Fixture validation
            """,
            "validation.yml");

        return new AvaValidationRunRequest(
            suite,
            config,
            "scenario.yml",
            "validation.yml",
            "run-001",
            OutputDirectory: null);
    }

    private static IReadOnlyList<AvaEvidenceRecord> ValidEvidence =>
    [
        Captured("describe_window", ValidButtonTree),
        Captured("describe_window_focus", ValidButtonTree)
    ];

    private static AvaEvidenceRecord Captured(string toolName, string rawJson)
        => new(
            toolName,
            AvaEvidenceStatus.Captured,
            null,
            rawJson,
            "fixture",
            null);

    private const string ValidButtonTree =
        """
        {
          "compactTree": {
            "name": "Calculate",
            "role": "button",
            "patterns": ["Invoke"]
          }
        }
        """;

    private sealed class RecordingCommandDriver(Func<AvaCommandExecutionRequest, AvaCommandExecutionResult> execute)
        : IAvaCommandDriver
    {
        public Task<AvaCommandExecutionResult> ExecuteAsync(
            AvaCommandExecutionRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(execute(request));
    }

    private sealed class RecordingEvidenceCollector(Func<AvaEvidenceCollectionRequest, IReadOnlyList<AvaEvidenceRecord>> collect)
        : IAvaEvidenceCollector
    {
        public Task<IReadOnlyList<AvaEvidenceRecord>> CollectAsync(
            AvaEvidenceCollectionRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(collect(request));
    }
}
