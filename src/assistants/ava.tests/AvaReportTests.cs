using System.Drawing;
using System.Drawing.Imaging;
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
        Assert.Contains("| Finding | Source | Checkpoint | Summary | Rule | Evidence | Element Path | Automation ID | ARIA |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Node |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Export ID |", markdown, StringComparison.Ordinal);
        Assert.Contains("`AVA-NOT-TESTED` |  | `after`", markdown, StringComparison.Ordinal);
        Assert.Contains("[federal-web-min-web-uia-evidence-missing](docs/ava/rules/federal-web-min-web-uia-evidence-missing.md)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("UI Automation evidence for federal-web-min", markdown, StringComparison.Ordinal);
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
                            "Window \"Calculator\" / Button \"Submit\"",
                            "submitButton",
                            "aria-expanded: false")
                    ])
            ]);

        var json = AvaReportWriter.ToJson(report);
        var markdown = AvaReportWriter.ToMarkdown(report);

        Assert.Contains("\"nodeTrace\":", json, StringComparison.Ordinal);
        Assert.Contains("Window \\u0022Calculator\\u0022 / Button \\u0022Submit\\u0022", json, StringComparison.Ordinal);
        Assert.Contains("\"automationId\": \"submitButton\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ariaProperties\": \"aria-expanded: false\"", json, StringComparison.Ordinal);
        Assert.Contains("##### Automated Failures (1)", markdown, StringComparison.Ordinal);
        Assert.Contains("##### Human Review Needed (0)", markdown, StringComparison.Ordinal);
        Assert.Contains("`AVA-ACTION-MISSING` | `DESCRIBE-WINDOW` | `after`", markdown, StringComparison.Ordinal);
        Assert.Contains("[federal-windows-uia-min-uia-control-pattern](docs/ava/rules/federal-windows-uia-min-uia-control-pattern.md)", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json)", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json) | `Window \"Calculator\" / Button \"Submit\"` | `submitButton` | `aria-expanded: false`", markdown, StringComparison.Ordinal);
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
                            AvaFindingStatus.Fail,
                            "Fixture finding.")
                    ],
                    [
                        new AvaAccessibilityFinding(
                            "AVA-NAME-MISSING-001-WEB-DOM-SNAPSHOT-001",
                            AvaFindingStatus.Fail,
                            "after",
                            "Interactive web node is missing an accessible name.",
                            AvaProfileIds.FederalWebMin,
                            "WEB-WCAG-4.1.2-NAME",
                            "WCAG 2.0 SC 4.1.2 Name, Role, Value",
                            evidenceReference.ManifestPath,
                            "step-001",
                            "web_dom_snapshot",
                            "7",
                            "Web button (AX node 7)")
                    ])
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
    public void ReportWriter_SuppressesRepeatedFindingsAfterFirstOccurrence()
    {
        var firstFinding = new AvaAccessibilityFinding(
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
            "Window \"Calculator\" / Button \"Submit\"",
            "submitButton");
        var repeatedFinding = new AvaAccessibilityFinding(
            "AVA-ACTION-MISSING-002-DESCRIBE-WINDOW-001",
            AvaFindingStatus.Fail,
            "after",
            "Actionable UI node has no exposed control patterns or explicit actions.",
            AvaProfileIds.FederalWindowsUiaMin,
            "UIA-CONTROL-PATTERN",
            "UI Automation control patterns",
            "evidence/step-002/manifest.json",
            "step-002",
            "describe_window_focus",
            "actionable-001",
            "Window \"Calculator\" / Button \"Submit\"",
            "submitButton");
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
                CreateStep(1, "step-001", firstFinding),
                CreateStep(2, "step-002", repeatedFinding)
            ]);

        var json = AvaReportWriter.ToJson(report);
        var markdown = AvaReportWriter.ToMarkdown(report);

        Assert.Contains("| `step-001` | 1 | 1 | 0 | 0 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| `step-002` | 0 | 0 | 0 | 0 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| **Total** | 1 | 1 | 0 | 0 |", markdown, StringComparison.Ordinal);
        Assert.Contains("### 2. Step 2", markdown, StringComparison.Ordinal);
        Assert.Contains("_No findings._", markdown, StringComparison.Ordinal);
        Assert.Single(AvaReportWriter.ReadJson(WriteJsonForRead(json)).Steps.SelectMany(static step => step.Findings));
    }

    [Fact]
    public void ReportWriter_SuppressesFocusTreeCopyOfSameFinding()
    {
        var windowFinding = new AvaAccessibilityFinding(
            "AVA-NAME-MISSING-001-DESCRIBE-WINDOW-001",
            AvaFindingStatus.Fail,
            "after",
            "Actionable UI node is missing an accessible name.",
            AvaProfileIds.FederalWindowsUiaMin,
            "UIA-NAME-PROPERTY",
            "UI Automation name property",
            "evidence/step-001/manifest.json",
            "step-001",
            "describe_window",
            "actionable-001",
            "Window \"Calculator\" / Pane / Document \"Calculator\" / Group",
            elementBounds: new AvaElementBounds(10, 20, 300, 200));
        var focusFinding = new AvaAccessibilityFinding(
            "AVA-NAME-MISSING-001-DESCRIBE-WINDOW-FOCUS-001",
            AvaFindingStatus.Fail,
            "after",
            "Actionable UI node is missing an accessible name.",
            AvaProfileIds.FederalWindowsUiaMin,
            "UIA-NAME-PROPERTY",
            "UI Automation name property",
            "evidence/step-001/manifest.json",
            "step-001",
            "describe_window_focus",
            "actionable-001",
            "Document \"Calculator\" / Group",
            elementBounds: new AvaElementBounds(10, 20, 300, 200));
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
                            AvaFindingStatus.Fail,
                            "Fixture finding.")
                    ],
                    [windowFinding, focusFinding])
            ]);

        var markdown = AvaReportWriter.ToMarkdown(report);
        var json = AvaReportWriter.ToJson(report);

        Assert.Contains("| `step-001` | 1 | 1 | 0 | 0 |", markdown, StringComparison.Ordinal);
        Assert.Single(AvaReportWriter.ReadJson(WriteJsonForRead(json)).Steps.Single().Findings);
        Assert.DoesNotContain("DESCRIBE-WINDOW-FOCUS", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportWriter_AddsHighlightedScreenshotLinkForFindingWithBounds()
    {
        var outputDirectory = CreateTemporaryDirectory();
        var sourceScreenshotPath = Path.Combine(outputDirectory, "window-source.png");
        using (var bitmap = new Bitmap(100, 100))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            bitmap.Save(sourceScreenshotPath, ImageFormat.Png);
        }

        var rawScreenshotOutput = System.Text.Json.JsonSerializer.Serialize(new
        {
            imagePath = sourceScreenshotPath,
            imageSize = new { width = 100, height = 100 },
            window = new
            {
                bounds = new { left = 0, top = 0, width = 100, height = 100 }
            }
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
        var finding = new AvaAccessibilityFinding(
            "AVA-ACTION-MISSING-001-DESCRIBE-WINDOW-001",
            AvaFindingStatus.Fail,
            "after",
            "Actionable UI node has no exposed control patterns or explicit actions.",
            AvaProfileIds.FederalWindowsUiaMin,
            "UIA-CONTROL-PATTERN",
            "UI Automation control patterns",
            evidenceReference.ManifestPath,
            "step-001",
            "describe_window",
            "actionable-001",
            "Window \"Calculator\" / Button \"Submit\"",
            automationId: null,
            ariaProperties: null,
            elementBounds: new AvaElementBounds(20, 30, 40, 20));
        var report = new AvaValidationReport(
            "run-001",
            "Active window smoke",
            "Federal Windows UIA minimum",
            AvaProfileIds.FederalWindowsUiaMin,
            "continue-and-report",
            ["after"],
            "scenario.yml",
            "validation.yml",
            [CreateStep(1, "step-001", finding, evidenceReference)]);

        var writeResult = AvaReportWriter.Write(report, outputDirectory);
        var markdown = File.ReadAllText(writeResult.MarkdownPath);
        var highlightPath = Path.Combine(outputDirectory, "evidence", "step-001", "highlights", $"{finding.ExportId}.png");

        Assert.Contains("[manifest.json](evidence/step-001/manifest.json)<br>[picture](evidence/step-001/highlights/", markdown, StringComparison.Ordinal);
        Assert.True(File.Exists(highlightPath));
        using var highlighted = new Bitmap(highlightPath);
        Assert.NotEqual(Color.White.ToArgb(), highlighted.GetPixel(20, 30).ToArgb());
    }

    [Fact]
    public void ReportWriter_RendersWebHtmlEvidenceLink()
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
                    "web_dom_snapshot",
                    AvaEvidenceStatus.Captured,
                    null,
                    """{"target":{"title":"Example"}}""",
                    "Captured CDP DOM, accessibility tree, and HTML for Example.",
                    null)
                {
                    Artifacts =
                    [
                        new AvaEvidenceArtifact(
                            "html",
                            "text/html",
                            "<!doctype html><html><body><button>Submit</button></body></html>",
                            "html")
                    ],
                }
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
                            AvaFindingStatus.Fail,
                            "Fixture finding.")
                    ],
                    [
                        new AvaAccessibilityFinding(
                            "AVA-NAME-MISSING-001-WEB-DOM-SNAPSHOT-001",
                            AvaFindingStatus.Fail,
                            "after",
                            "Interactive web node is missing an accessible name.",
                            AvaProfileIds.FederalWebMin,
                            "WEB-WCAG-4.1.2-NAME",
                            "WCAG 2.0 SC 4.1.2 Name, Role, Value",
                            evidenceReference.ManifestPath,
                            "step-001",
                            "web_dom_snapshot",
                            "7",
                            "Web button (AX node 7)")
                    ])
            ]);

        var writeResult = AvaReportWriter.Write(report, outputDirectory);
        var markdown = File.ReadAllText(writeResult.MarkdownPath);
        var htmlPath = Path.Combine(outputDirectory, "evidence", "step-001", "web", "001-page.html");
        var manifestJson = File.ReadAllText(Path.Combine(outputDirectory, "evidence", "step-001", "manifest.json"));

        Assert.True(File.Exists(htmlPath));
        Assert.Contains("<button>Submit</button>", File.ReadAllText(htmlPath), StringComparison.Ordinal);
        Assert.Contains("\"artifacts\"", manifestJson, StringComparison.Ordinal);
        Assert.Contains("\"path\": \"web/001-page.html\"", manifestJson, StringComparison.Ordinal);
        Assert.Contains("#### Web Evidence", markdown, StringComparison.Ordinal);
        Assert.Contains("[html](evidence/step-001/web/001-page.html)", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json)<br>", markdown, StringComparison.Ordinal);
        Assert.Contains("<br>[html](evidence/step-001/web/001-page.html)", markdown, StringComparison.Ordinal);
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
                            "automationId": "submitButton",
                            "ariaExpanded": false,
                            "isKeyboardFocusable": true,
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
        Assert.Equal("Window \"Calculator\" / Button \"Submit\"", actionFinding.NodeTrace);
        Assert.Equal("submitButton", actionFinding.AutomationId);
        Assert.Equal("aria-expanded: false", actionFinding.AriaProperties);
        Assert.Contains("| Finding | Source | Checkpoint | Summary | Rule | Evidence | Element Path | Automation ID | ARIA |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Node |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Export ID |", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json)", markdown, StringComparison.Ordinal);
        Assert.Contains("[manifest.json](evidence/step-001/manifest.json) | `Window \"Calculator\" / Button \"Submit\"` | `submitButton` | `aria-expanded: false`", markdown, StringComparison.Ordinal);
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

    private static AvaStepResult CreateStep(
        int index,
        string stepId,
        AvaAccessibilityFinding finding,
        AvaStepEvidenceReference? evidenceReference = null)
        => new(
            index,
            stepId,
            $"Step {index}",
            "Describe active window.",
            "continue-and-report",
            evidenceReference ?? new AvaStepEvidenceReference(
                stepId,
                $"evidence/{stepId}/manifest.json",
                "captured",
                1),
            [
                new AvaCheckpointResult(
                    "after",
                    finding.Status,
                    "Fixture finding.")
            ],
            [finding]);

    private static string WriteJsonForRead(string json)
    {
        var path = Path.Combine(CreateTemporaryDirectory(), "report.json");
        File.WriteAllText(path, json);
        return path;
    }
}
