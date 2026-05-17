using HeronWin.Ava;
using HeronWin.Brain;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaReportTests
{
    [Fact]
    public async Task NoOpRunner_GeneratesNotTestedFindings()
    {
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

        var outputDirectory = CreateTemporaryDirectory();
        var report = await AvaNoOpValidationRunner.RunAsync(new AvaValidationRunRequest(
            suite,
            config,
            "scenario.yml",
            "validation.yml",
            "run-001",
            outputDirectory),
            evidenceCollector: null,
            CancellationToken.None);

        Assert.True(report.HasNotTestedFindings);
        var step = Assert.Single(report.Steps);
        Assert.Equal("Describe active window.", step.Command);
        Assert.Equal("step-001", step.StepId);
        Assert.Equal("missing", step.Evidence.Status);
        Assert.Equal("evidence/step-001/manifest.json", step.Evidence.ManifestPath);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "evidence", "step-001", "manifest.json")));
        var finding = Assert.Single(step.Findings);
        Assert.Equal("not-tested", finding.Status);
        Assert.Equal(AvaTriageCategory.EvidenceGap, finding.TriageCategory);
        Assert.StartsWith("ava-", finding.ExportId, StringComparison.Ordinal);
        Assert.Equal("manifest: evidence/step-001/manifest.json; tool: ava.evidence", finding.EvidenceSummary);
        Assert.Equal(AvaProfileIds.FederalWindowsUiaMin, finding.ProfileId);
        Assert.Equal("UIA-EVIDENCE-MISSING", finding.RuleId);
    }

    [Fact]
    public void ReportWriter_SerializesMarkdownAndJsonDeterministically()
    {
        var report = new AvaValidationReport(
            "run-001",
            "Active window smoke",
            "Federal Windows UIA minimum",
            "federal-windows-uia-min",
            "continue-and-report",
            ["after"],
            "scenario.yml",
            "validation.yml",
            [
                new AvaStepResult(
                    1,
                    "step-001",
                    "Step 1",
                    "Describe active window.",
                    "continue-and-report",
                    new AvaStepEvidenceReference(
                        "step-001",
                        "evidence/step-001/manifest.json",
                        "missing",
                        1),
                    [
                        new AvaCheckpointResult(
                            "after",
                            "not-tested",
                            "No live UI accessibility checks were executed in the report-only runner.")
                    ],
                    [
                        new AvaAccessibilityFinding(
                            "AVA-NOT-TESTED-001",
                            "not-tested",
                            "after",
                            "Accessibility validation is not yet connected to live UIA/MCP execution.",
                            AvaProfileIds.FederalWebMin,
                            "WEB-UIA-EVIDENCE-MISSING",
                            "UI Automation evidence for federal-web-min")
                    ])
            ]);

        var json = AvaReportWriter.ToJson(report);
        var markdown = AvaReportWriter.ToMarkdown(report);

        Assert.Contains("\"runId\": \"run-001\"", json, StringComparison.Ordinal);
        Assert.Contains("\"profileId\": \"federal-web-min\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ruleId\": \"WEB-UIA-EVIDENCE-MISSING\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"not-tested\"", json, StringComparison.Ordinal);
        Assert.Contains("\"exportId\": \"ava-114dab35d9fdad7f96fdafa9\"", json, StringComparison.Ordinal);
        Assert.Contains("\"triageCategory\": \"not-tested\"", json, StringComparison.Ordinal);
        Assert.Contains("# AVA Validation Report: Active window smoke", markdown, StringComparison.Ordinal);
        Assert.Contains("### Steps", markdown, StringComparison.Ordinal);
        Assert.Contains("| Total | Pass | Fail | Needs Review | Not Tested |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 1 | 0 | 0 | 0 | 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("### Findings", markdown, StringComparison.Ordinal);
        Assert.Contains("| Step | Total | Fail | Needs Review | Not Tested |", markdown, StringComparison.Ordinal);
        Assert.Contains("| `step-001` | 1 | 0 | 0 | 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| **Total** | 1 | 0 | 0 | 1 |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkpoint status counts", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Finding status counts", markdown, StringComparison.Ordinal);
        Assert.Contains("##### Automated Failures (0)", markdown, StringComparison.Ordinal);
        Assert.Contains("##### Human Review Needed (0)", markdown, StringComparison.Ordinal);
        Assert.Contains("##### Not Tested (1)", markdown, StringComparison.Ordinal);
        Assert.Contains("| Finding | Source | Checkpoint | Summary | Rule | Evidence | Trace | Export ID |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Node |", markdown, StringComparison.Ordinal);
        Assert.Contains("`AVA-NOT-TESTED` |  | `after`", markdown, StringComparison.Ordinal);
        Assert.Contains("`federal-web-min`<br>`WEB-UIA-EVIDENCE-MISSING`", markdown, StringComparison.Ordinal);
        Assert.Contains("`ava-114dab35d9fdad7f96fdafa9`", markdown, StringComparison.Ordinal);
        Assert.Contains("Evidence: `evidence/step-001/manifest.json` (`missing`, 1 entries)", markdown, StringComparison.Ordinal);
        Assert.Equal(json, AvaReportWriter.ToJson(report));
        Assert.Equal(markdown, AvaReportWriter.ToMarkdown(report));
    }

    [Fact]
    public void FindingExportMetadata_IsStableAndDerivedFromAuditableFields()
    {
        var finding = new AvaAccessibilityFinding(
            "AVA-ACTION-MISSING-001-DESCRIBE-WINDOW-001",
            AvaFindingStatus.Fail,
            "after",
            "Actionable UI node has no exposed control patterns or explicit actions.",
            AvaProfileIds.FederalWindowsUiaMin,
            "UIA-CONTROL-PATTERN",
            "UI Automation control patterns",
            "evidence/step-001/manifest.json",
            "step-001",
            "describe_window",
            "actionable-001");
        var sameFinding = new AvaAccessibilityFinding(
            "AVA-ACTION-MISSING-001-DESCRIBE-WINDOW-001",
            AvaFindingStatus.Fail,
            "after",
            "Different wording does not affect export identity.",
            AvaProfileIds.FederalWindowsUiaMin,
            "UIA-CONTROL-PATTERN",
            "UI Automation control patterns",
            "evidence/step-001/manifest.json",
            "step-001",
            "describe_window",
            "actionable-001");
        var differentNodeFinding = sameFinding with { NodeReference = "actionable-002" };

        Assert.Equal("ava-dbf9a13dbbdd190a0bba90df", finding.ExportId);
        Assert.Equal(finding.ExportId, sameFinding.ExportId);
        Assert.NotEqual(finding.ExportId, differentNodeFinding.ExportId);
        Assert.Equal(AvaTriageCategory.AutomatedFailure, finding.TriageCategory);
        Assert.Equal(
            "manifest: evidence/step-001/manifest.json; tool: describe_window; node: actionable-001",
            finding.EvidenceSummary);
    }

    [Fact]
    public void ReportWriter_RendersNodeTraceInMarkdownAndJson()
    {
        var report = new AvaValidationReport(
            "run-001",
            "Active window smoke",
            "Federal Windows UIA minimum",
            AvaProfileIds.FederalWindowsUiaMin,
            "continue-and-report",
            ["after"],
            "scenario.yml",
            "validation.yml",
            [
                new AvaStepResult(
                    1,
                    "step-001",
                    "Step 1",
                    "Describe active window.",
                    "continue-and-report",
                    new AvaStepEvidenceReference(
                        "step-001",
                        "evidence/step-001/manifest.json",
                        "captured",
                        1),
                    [
                        new AvaCheckpointResult(
                            "after",
                            "fail",
                            "Fixture failure.")
                    ],
                    [
                        new AvaAccessibilityFinding(
                            "AVA-ACTION-MISSING-001-DESCRIBE-WINDOW-001",
                            AvaFindingStatus.Fail,
                            "after",
                            "Actionable UI node has no exposed control patterns or explicit actions.",
                            AvaProfileIds.FederalWindowsUiaMin,
                            "UIA-CONTROL-PATTERN",
                            "UI Automation control patterns",
                            "evidence/step-001/manifest.json",
                            "step-001",
                            "describe_window",
                            "actionable-001",
                            "Window \"Calculator\" [uiPath=root] / Button \"Submit\" [uiPath=0]")
                    ])
            ]);

        var json = AvaReportWriter.ToJson(report);
        var markdown = AvaReportWriter.ToMarkdown(report);

        Assert.Contains("\"nodeTrace\":", json, StringComparison.Ordinal);
        Assert.Contains("Window \\u0022Calculator\\u0022 [uiPath=root] / Button \\u0022Submit\\u0022 [uiPath=0]", json, StringComparison.Ordinal);
        Assert.Contains("##### Automated Failures (1)", markdown, StringComparison.Ordinal);
        Assert.Contains("##### Human Review Needed (0)", markdown, StringComparison.Ordinal);
        Assert.Contains("`AVA-ACTION-MISSING` | `DESCRIBE-WINDOW` | `after`", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json)", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json) | `Window \"Calculator\" [uiPath=root] / Button \"Submit\" [uiPath=0]`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportWriter_RendersStepScreenshotsBeforeFindings()
    {
        var outputDirectory = CreateTemporaryDirectory();
        var sourceScreenshotPath = Path.Combine(outputDirectory, "window source.png");
        File.WriteAllText(sourceScreenshotPath, "fake png");
        var rawScreenshotOutput = System.Text.Json.JsonSerializer.Serialize(new
        {
            ImagePath = sourceScreenshotPath,
            ImageFormat = "png"
        });
        var evidenceReference = new AvaEvidenceBundleWriter().WriteStepEvidence(new AvaEvidenceBundleWriteRequest(
            "run-001",
            outputDirectory,
            "step-001",
            1,
            "Step 1",
            "0x00010001",
            [
                new AvaEvidenceRecord(
                    "capture_window_screenshot",
                    AvaEvidenceStatus.Captured,
                    101,
                    rawScreenshotOutput,
                    "Screenshot captured.",
                    null)
            ]));
        var report = new AvaValidationReport(
            "run-001",
            "Active window smoke",
            "Federal Windows UIA minimum",
            AvaProfileIds.FederalWindowsUiaMin,
            "continue-and-report",
            ["after"],
            "scenario.yml",
            "validation.yml",
            [
                new AvaStepResult(
                    1,
                    "step-001",
                    "Step 1",
                    "Describe active window.",
                    "continue-and-report",
                    evidenceReference,
                    [
                        new AvaCheckpointResult(
                            "after",
                            AvaFindingStatus.Pass,
                            "Fixture pass.")
                    ],
                    [])
            ]);

        var writeResult = AvaReportWriter.Write(report, outputDirectory);
        var markdown = File.ReadAllText(writeResult.MarkdownPath);
        var screenshotsIndex = markdown.IndexOf("#### Screenshots", StringComparison.Ordinal);
        var findingsIndex = markdown.IndexOf("#### Findings", StringComparison.Ordinal);

        Assert.True(screenshotsIndex >= 0);
        Assert.True(findingsIndex > screenshotsIndex);
        Assert.Contains("**After checkpoint - Capture Window Screenshot**", markdown, StringComparison.Ordinal);
        Assert.Contains(
            "![step-001 After checkpoint Capture Window Screenshot](evidence/step-001/screenshots/001-capture_window_screenshot.png)",
            markdown,
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            outputDirectory,
            "evidence",
            "step-001",
            "screenshots",
            "001-capture_window_screenshot.png")));
    }

    [Fact]
    public void ReportRegenerator_RerunsValidatorsFromSavedEvidence()
    {
        var outputDirectory = CreateTemporaryDirectory();
        var evidenceReference = new AvaEvidenceBundleWriter().WriteStepEvidence(new AvaEvidenceBundleWriteRequest(
            "run-001",
            outputDirectory,
            "step-001",
            1,
            "Step 1",
            "0x00010001",
            [
                new AvaEvidenceRecord(
                    "describe_window",
                    AvaEvidenceStatus.Captured,
                    101,
                    """
                    {
                      "compactTree": {
                        "name": "Calculator",
                        "controlType": "Window",
                        "uiPath": "root",
                        "children": [
                          {
                            "name": "Submit",
                            "controlType": "Button",
                            "uiPath": "0"
                          }
                        ]
                      }
                    }
                    """,
                    "fixture",
                    null),
                new AvaEvidenceRecord(
                    "describe_window_focus",
                    AvaEvidenceStatus.Captured,
                    102,
                    """
                    {
                      "compactTree": {
                        "name": "Submit",
                        "controlType": "Button",
                        "uiPath": "0"
                      }
                    }
                    """,
                    "fixture",
                    null)
            ]));
        var sourceReport = new AvaValidationReport(
            "run-001",
            "Active window smoke",
            "Federal Windows UIA minimum",
            AvaProfileIds.FederalWindowsUiaMin,
            "continue-and-report",
            ["after"],
            "scenario.yml",
            "validation.yml",
            [
                new AvaStepResult(
                    1,
                    "step-001",
                    "Step 1",
                    "Press submit.",
                    "continue-and-report",
                    evidenceReference,
                    [
                        new AvaCheckpointResult(
                            "after",
                            AvaFindingStatus.Pass,
                            "Fixture source report predates regeneration.")
                    ],
                    [])
            ]);
        AvaReportWriter.Write(sourceReport, outputDirectory);

        var writeResult = AvaReportRegenerator.Regenerate(outputDirectory, outputDirectory);
        var regeneratedReport = AvaReportWriter.ReadJson(writeResult.JsonPath);
        var markdown = File.ReadAllText(writeResult.MarkdownPath);

        var actionFinding = Assert.Single(
            regeneratedReport.Steps.Single().Findings,
            finding => finding.Id.StartsWith("AVA-ACTION-MISSING", StringComparison.Ordinal) &&
                finding.ToolName == "describe_window");
        Assert.Equal("Window \"Calculator\" [uiPath=root] / Button \"Submit\" [uiPath=0]", actionFinding.NodeTrace);
        Assert.Contains("| Finding | Source | Checkpoint | Summary | Rule | Evidence | Trace | Export ID |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Node |", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json)", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json) | `Window \"Calculator\" [uiPath=root] / Button \"Submit\" [uiPath=0]`", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AvaFindingStatus.Fail, "AVA-NAME-MISSING-001-DESCRIBE-WINDOW-001", AvaTriageCategory.AutomatedFailure)]
    [InlineData(AvaFindingStatus.NeedsReview, "AVA-FOCUS-MISSING-001", AvaTriageCategory.NeedsHumanReview)]
    [InlineData(AvaFindingStatus.NotTested, "AVA-EVIDENCE-MISSING-001", AvaTriageCategory.EvidenceGap)]
    [InlineData(AvaFindingStatus.NotTested, "AVA-NOT-TESTED-001", AvaTriageCategory.NotTested)]
    public void FindingTriageCategory_UsesStableNames(string status, string findingId, string expectedCategory)
    {
        var finding = new AvaAccessibilityFinding(
            findingId,
            status,
            "after",
            "Fixture summary.");

        Assert.Equal(expectedCategory, finding.TriageCategory);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ava-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
