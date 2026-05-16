using System.Text.Json;
using HeronWin.Ava;
using HeronWin.Brain;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaEvidenceTests
{
    [Fact]
    public void EvidenceBundleWriter_WritesManifestAndRawOutputForStepId()
    {
        var outputDirectory = CreateTemporaryDirectory();
        var writer = new AvaEvidenceBundleWriter();

        var reference = writer.WriteStepEvidence(new AvaEvidenceBundleWriteRequest(
            "run-001",
            outputDirectory,
            "step-001",
            1,
            "Inventory",
            "0x00010001",
            [
                new AvaEvidenceRecord(
                    "describe_window",
                    AvaEvidenceStatus.Captured,
                    42,
                    """{"Window":{"Title":"Calculator"}}""",
                    "Calculator window captured.",
                    null)
            ]));

        Assert.Equal("step-001", reference.StepId);
        Assert.Equal("captured", reference.Status);
        Assert.Equal("evidence/step-001/manifest.json", reference.ManifestPath);

        var manifestPath = Path.Combine(outputDirectory, "evidence", "step-001", "manifest.json");
        Assert.True(File.Exists(manifestPath));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var entry = manifest.RootElement.GetProperty("entries")[0];
        Assert.Equal("describe_window", entry.GetProperty("toolName").GetString());
        Assert.Equal(42, entry.GetProperty("mcpCallId").GetInt64());
        Assert.Equal("001-describe_window.raw.txt", entry.GetProperty("rawOutputPath").GetString());

        var rawPath = Path.Combine(outputDirectory, "evidence", "step-001", "001-describe_window.raw.txt");
        Assert.Equal("""{"Window":{"Title":"Calculator"}}""", File.ReadAllText(rawPath));
    }

    [Fact]
    public async Task NoOpRunner_WritesMissingEvidenceManifest_WhenWindowHandleIsMissing()
    {
        var outputDirectory = CreateTemporaryDirectory();
        var suite = BrainScenarioLoader.Parse(
            """
            name: Active window smoke
            commands:
              - Describe active window.
            """,
            "scenario.yml");
        var config = AvaValidationConfigLoader.Parse(
            """
            name: Federal Windows UIA minimum
            """,
            "validation.yml");

        var report = await AvaNoOpValidationRunner.RunAsync(
            new AvaValidationRunRequest(
                suite,
                config,
                "scenario.yml",
                "validation.yml",
                "run-001",
                outputDirectory),
            evidenceCollector: new ThrowingEvidenceCollector(),
            CancellationToken.None);

        var step = Assert.Single(report.Steps);
        Assert.Equal("missing", step.Evidence.Status);
        Assert.Equal("evidence/step-001/manifest.json", step.Evidence.ManifestPath);

        var manifestPath = Path.Combine(outputDirectory, "evidence", "step-001", "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var entry = manifest.RootElement.GetProperty("entries")[0];
        Assert.Equal("missing", entry.GetProperty("status").GetString());
        Assert.Contains(
            "No deterministic windowHandle",
            entry.GetProperty("summary").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpEvidenceCollector_PreservesMcpCallIdAndRawOutputCorrelation()
    {
        var outputDirectory = CreateTemporaryDirectory();
        var calls = new List<(string ToolName, IReadOnlyDictionary<string, object?> Args)>();
        await using var manager = new McpClientManager(
            _ => Task.FromResult<IReadOnlyList<ToolDefinition>>([]),
            toolCallTimeoutOverride: null,
            callToolOverride: (toolName, args, _) =>
            {
                calls.Add((toolName, new Dictionary<string, object?>(args, StringComparer.Ordinal)));
                var mcpCallId = toolName switch
                {
                    "describe_window" => 101,
                    "describe_window_focus" => 102,
                    "capture_window_screenshot" => 103,
                    _ => 0
                };

                return Task.FromResult(new ToolCallOutcome(
                    $"raw output for {toolName}",
                    [],
                    IsError: false,
                    McpCallId: mcpCallId));
            });
        var collector = new AvaMcpEvidenceCollector(manager);
        var suite = BrainScenarioLoader.Parse(
            """
            name: Active window smoke
            commands:
              - Describe active window.
            """,
            "scenario.yml");
        var config = AvaValidationConfigLoader.Parse(
            """
            name: Federal Windows UIA minimum
            windowHandle: 0x00010001
            """,
            "validation.yml");

        var report = await AvaNoOpValidationRunner.RunAsync(
            new AvaValidationRunRequest(
                suite,
                config,
                "scenario.yml",
                "validation.yml",
                "run-001",
                outputDirectory),
            collector,
            CancellationToken.None);

        var step = Assert.Single(report.Steps);
        Assert.Equal("captured", step.Evidence.Status);
        Assert.Equal(
            new[] { "describe_window", "describe_window_focus", "capture_window_screenshot" },
            calls.Select(static call => call.ToolName).ToArray());
        Assert.All(calls, call => Assert.Equal("0x00010001", call.Args["windowHandle"]));

        var manifestPath = Path.Combine(outputDirectory, "evidence", "step-001", "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var entries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Equal(101, entries[0].GetProperty("mcpCallId").GetInt64());
        Assert.Equal("001-describe_window.raw.txt", entries[0].GetProperty("rawOutputPath").GetString());
        Assert.Equal(
            "raw output for describe_window",
            File.ReadAllText(Path.Combine(outputDirectory, "evidence", "step-001", "001-describe_window.raw.txt")));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ava-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ThrowingEvidenceCollector : IAvaEvidenceCollector
    {
        public Task<IReadOnlyList<AvaEvidenceRecord>> CollectAsync(
            AvaEvidenceCollectionRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("No collector call expected.");
    }
}
