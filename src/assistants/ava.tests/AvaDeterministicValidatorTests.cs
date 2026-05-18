using HeronWin.Ava;
using HeronWin.Brain;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaDeterministicValidatorTests
{
    [Fact]
    public async Task Runner_ValidNamedRoleEvidence_PassesWithoutFindings()
    {
        var report = await RunWithEvidenceAsync([
            Captured("describe_window", ValidButtonTree),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var step = Assert.Single(report.Steps);
        Assert.Empty(step.Findings);
        var checkpoint = Assert.Single(step.Checkpoints);
        Assert.Equal(AvaFindingStatus.Pass, checkpoint.Status);
    }

    [Fact]
    public async Task Runner_UsesOriginalDebugTreeInsteadOfLlmProjection()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "debugEvidence": {
                    "fullTree": {
                      "elementTree": {
                        "name": "Submit",
                        "controlType": "Button",
                        "isKeyboardFocusable": true,
                        "availableActions": ["focus", "invoke"]
                      }
                    }
                  },
                  "compactTree": {
                    "controlType": "Button",
                    "isKeyboardFocusable": true
                  },
                  "llmTree": {
                    "controlType": "Button",
                    "isKeyboardFocusable": true
                  }
                }
                """),
            Captured(
                "describe_window_focus",
                """
                {
                  "debugEvidence": {
                    "focusTree": {
                      "focusedElement": {
                        "name": "Submit",
                        "controlType": "Button",
                        "isKeyboardFocusable": true,
                        "availableActions": ["focus", "invoke"]
                      }
                    }
                  },
                  "compactTree": {
                    "controlType": "Button",
                    "isKeyboardFocusable": true
                  },
                  "llmTree": {
                    "controlType": "Button",
                    "isKeyboardFocusable": true
                  }
                }
                """)
        ]);

        var step = Assert.Single(report.Steps);
        Assert.Empty(step.Findings);
        Assert.Equal(AvaFindingStatus.Pass, Assert.Single(step.Checkpoints).Status);
    }

    [Theory]
    [InlineData("describe_window", """{"compactTree": """, "AVA-TREE-PARSE")]
    [InlineData("describe_window_focus", """{"compactTree": """, "AVA-TREE-PARSE")]
    [InlineData("describe_window", """{"windowTitle":"Calculator"}""", "AVA-TREE-MISSING")]
    [InlineData("describe_window_focus", """{"windowTitle":"Calculator"}""", "AVA-TREE-MISSING")]
    public async Task Runner_MalformedOrMissingTreeEvidence_ProducesNeedsReview(
        string toolName,
        string rawJson,
        string idPrefix)
    {
        var windowTree = toolName == "describe_window" ? rawJson : ValidButtonTree;
        var focusTree = toolName == "describe_window_focus" ? rawJson : ValidButtonTree;
        var report = await RunWithEvidenceAsync([
            Captured("describe_window", windowTree),
            Captured("describe_window_focus", focusTree)
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.Id.StartsWith(idPrefix, StringComparison.Ordinal));
        Assert.Equal(AvaFindingStatus.NeedsReview, finding.Status);
        Assert.Equal(AvaTriageCategory.NeedsHumanReview, finding.TriageCategory);
        Assert.Equal("evidence/step-001/manifest.json", finding.EvidenceReference);
        Assert.Equal(AvaProfileIds.FederalWindowsUiaMin, finding.ProfileId);
        Assert.StartsWith("UIA-", finding.RuleId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_ActionableNodesMissingNameOrRole_ProduceFailFindings()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "children": [
                      { "isKeyboardFocusable": true, "actions": ["invoke"] }
                    ]
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var findings = report.Steps.Single().Findings;
        Assert.Contains(findings, finding => finding.Id.StartsWith("AVA-NAME-MISSING", StringComparison.Ordinal) &&
            finding.Status == AvaFindingStatus.Fail &&
            finding.TriageCategory == AvaTriageCategory.AutomatedFailure);
        Assert.Contains(findings, finding => finding.Id.StartsWith("AVA-ROLE-MISSING", StringComparison.Ordinal) &&
            finding.Status == AvaFindingStatus.Fail &&
            finding.TriageCategory == AvaTriageCategory.AutomatedFailure);
        Assert.Equal(AvaFindingStatus.Fail, report.Steps.Single().Checkpoints.Single().Status);
    }

    [Fact]
    public async Task Runner_ActionableNodesWithoutPatternsOrActions_UsesConservativeSeverity()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "children": [
                      {
                        "name": "Submit",
                        "role": "button",
                        "bounds": { "left": 10, "top": 10, "width": 80, "height": 30 }
                      },
                      { "name": "Canvas region", "role": "custom", "isKeyboardFocusable": true }
                    ]
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var actionFindings = report.Steps.Single().Findings
            .Where(static finding => finding.Id.StartsWith("AVA-ACTION-MISSING", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(actionFindings, finding => finding.Status == AvaFindingStatus.Fail);
        Assert.Contains(actionFindings, finding => finding.Status == AvaFindingStatus.NeedsReview);
    }

    [Fact]
    public async Task Runner_StructuralAvailableActionsDoNotMakeContainersActionable()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "name": "App",
                    "controlType": "Window",
                    "children": [
                      {
                        "controlType": "Pane",
                        "availableActions": ["scroll_into_view"]
                      },
                      {
                        "controlType": "ListItem",
                        "availableActions": ["scroll_into_view"]
                      }
                    ]
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var step = Assert.Single(report.Steps);
        Assert.Empty(step.Findings);
        Assert.Equal(AvaFindingStatus.Pass, Assert.Single(step.Checkpoints).Status);
    }

    [Fact]
    public async Task Runner_NodeFindingsIncludeReadableElementTrace()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "name": "Calculator",
                    "controlType": "Window",
                    "uiPath": "root",
                    "children": [
                      {
                        "name": "Main pane",
                        "controlType": "Pane",
                        "uiPath": "0",
                        "children": [
                          {
                            "name": "Submit",
                            "controlType": "Button",
                            "automationId": "submitButton",
                            "ariaProperties": "expanded=false; current=page",
                            "isKeyboardFocusable": true,
                            "uiPath": "0/2"
                          }
                        ]
                      }
                    ]
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.Id.StartsWith("AVA-ACTION-MISSING", StringComparison.Ordinal));

        Assert.Equal("actionable-001", finding.NodeReference);
        Assert.Equal(
            "Window \"Calculator\" / Pane \"Main pane\" / Button \"Submit\"",
            finding.NodeTrace);
        Assert.Equal("submitButton", finding.AutomationId);
        Assert.Equal("aria-current: page; aria-expanded: false", finding.AriaProperties);
        Assert.Contains("trace: Window \"Calculator\" / Pane \"Main pane\" / Button \"Submit\"",
            finding.EvidenceSummary,
            StringComparison.Ordinal);
        Assert.Contains("automationId: submitButton", finding.EvidenceSummary, StringComparison.Ordinal);
        Assert.Contains("aria: aria-current: page; aria-expanded: false", finding.EvidenceSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_UsesDescendantBoundsWhenActionableNodeOmitsOwnBounds()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "controlType": "Window",
                    "children": [
                      {
                        "controlType": "Group",
                        "isKeyboardFocusable": true,
                        "children": [
                          {
                            "name": "Visible child",
                            "controlType": "Text",
                            "bounds": { "left": 10, "top": 20, "width": 30, "height": 40 }
                          }
                        ]
                      }
                    ]
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.Id.StartsWith("AVA-NAME-MISSING", StringComparison.Ordinal));

        Assert.NotNull(finding.ElementBounds);
        Assert.Equal(10, finding.ElementBounds.Left);
        Assert.Equal(20, finding.ElementBounds.Top);
        Assert.Equal(30, finding.ElementBounds.Width);
        Assert.Equal(40, finding.ElementBounds.Height);
    }

    [Fact]
    public async Task Runner_IgnoresNonFocusableActionNodesWithoutVisibleBounds()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "controlType": "Window",
                    "children": [
                      {
                        "controlType": "Edit",
                        "availableActions": ["set_value"]
                      }
                    ]
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var step = Assert.Single(report.Steps);
        Assert.Empty(step.Findings);
        Assert.Equal(AvaFindingStatus.Pass, Assert.Single(step.Checkpoints).Status);
    }

    [Fact]
    public async Task Runner_IgnoresGenericNonFocusableContainerGroupsWithOnlyInvoke()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "controlType": "Window",
                    "children": [
                      {
                        "controlType": "Group",
                        "availableActions": ["invoke", "scroll_into_view"],
                        "bounds": { "left": 0, "top": 0, "width": 800, "height": 600 },
                        "children": [
                          {
                            "name": "Visible child",
                            "controlType": "Text",
                            "bounds": { "left": 20, "top": 30, "width": 120, "height": 40 }
                          }
                        ]
                      }
                    ]
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var step = Assert.Single(report.Steps);
        Assert.Empty(step.Findings);
        Assert.Equal(AvaFindingStatus.Pass, Assert.Single(step.Checkpoints).Status);
    }

    [Fact]
    public async Task Runner_ReportsFocusableContainerGroupsWithInvoke()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "controlType": "Window",
                    "children": [
                      {
                        "controlType": "Group",
                        "isKeyboardFocusable": true,
                        "availableActions": ["invoke", "scroll_into_view"],
                        "bounds": { "left": 0, "top": 0, "width": 200, "height": 80 },
                        "children": [
                          {
                            "name": "Visible child",
                            "controlType": "Text",
                            "bounds": { "left": 20, "top": 20, "width": 120, "height": 40 }
                          }
                        ]
                      }
                    ]
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.Id.StartsWith("AVA-NAME-MISSING", StringComparison.Ordinal));
        Assert.Equal(AvaFindingStatus.Fail, finding.Status);
        Assert.Equal("Window / Group", finding.NodeTrace);
    }

    [Fact]
    public async Task Runner_IgnoresUnnamedFocusableWebContainerGroups()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "controlType": "Window",
                    "children": [
                      {
                        "name": "Netflix",
                        "controlType": "Document",
                        "automationId": "RootWebArea",
                        "children": [
                          {
                            "controlType": "Group",
                            "className": "passive default-ltr-iqcdef-cache-fntwn3",
                            "isKeyboardFocusable": true,
                            "availableActions": ["focus", "invoke", "scroll_into_view"],
                            "bounds": { "left": 0, "top": 0, "width": 800, "height": 600 }
                          }
                        ]
                      }
                    ]
                  }
                }
                """),
            Captured(
                "describe_window_focus",
                """
                {
                  "compactTree": {
                    "controlType": "Group",
                    "className": "passive default-ltr-iqcdef-cache-fntwn3",
                    "isKeyboardFocusable": true,
                    "availableActions": ["focus", "invoke", "scroll_into_view"],
                    "bounds": { "left": 0, "top": 0, "width": 800, "height": 600 },
                    "children": [
                      {
                        "controlType": "Group",
                        "isKeyboardFocusable": true,
                        "availableActions": ["focus", "scroll_into_view"],
                        "bounds": { "left": 0, "top": 0, "width": 800, "height": 600 }
                      }
                    ]
                  }
                }
                """)
        ]);

        var step = Assert.Single(report.Steps);
        Assert.Empty(step.Findings);
        Assert.Equal(AvaFindingStatus.Pass, Assert.Single(step.Checkpoints).Status);
    }

    [Fact]
    public async Task Runner_IgnoresUnsupportedAriaPropertyPlaceholders()
    {
        var report = await RunWithEvidenceAsync([
            Captured(
                "describe_window",
                """
                {
                  "compactTree": {
                    "name": "Submit",
                    "controlType": "Button",
                    "automationId": "submitButton",
                    "ariaRole": "System.ArgumentException: Unsupported Property.",
                    "ariaProperties": "System.ArgumentException: Unsupported Property.",
                    "isKeyboardFocusable": true
                  }
                }
                """),
            Captured("describe_window_focus", ValidButtonTree)
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.Id.StartsWith("AVA-ACTION-MISSING", StringComparison.Ordinal));

        Assert.Equal("submitButton", finding.AutomationId);
        Assert.Null(finding.AriaProperties);
    }

    [Fact]
    public async Task Runner_MissingFocusEvidenceWithWindowEvidence_ProducesNeedsReview()
    {
        var report = await RunWithEvidenceAsync([
            Captured("describe_window", ValidButtonTree)
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.Id.StartsWith("AVA-FOCUS-MISSING", StringComparison.Ordinal));
        Assert.Equal(AvaFindingStatus.NeedsReview, finding.Status);
    }

    [Fact]
    public async Task Runner_CapturedEvidenceWithoutUiTree_ProducesNotTested()
    {
        var report = await RunWithEvidenceAsync([
            Captured("capture_window_screenshot", """{"screenshotPath":"window.png"}""")
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.Id.StartsWith("AVA-TREE-EVIDENCE-MISSING", StringComparison.Ordinal));
        Assert.Equal(AvaFindingStatus.NotTested, finding.Status);
        Assert.Equal(AvaTriageCategory.EvidenceGap, finding.TriageCategory);
        Assert.Equal("capture_window_screenshot", finding.ToolName);
    }

    [Fact]
    public async Task Runner_ErrorEvidence_ProducesNeedsReview()
    {
        var report = await RunWithEvidenceAsync([
            AvaEvidenceRecord.ErrorResult("describe_window", "collector failed")
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.Id.StartsWith("AVA-EVIDENCE-ERROR", StringComparison.Ordinal));
        Assert.Equal(AvaFindingStatus.NeedsReview, finding.Status);
        Assert.Equal("describe_window", finding.ToolName);
    }

    [Fact]
    public async Task Runner_BrowserUiaEvidenceWithWebProfile_PassesWhenNodesExposeNameRoleAndActions()
    {
        var report = await RunWithEvidenceAsync(
            [
                Captured("describe_window", ValidBrowserDocumentTree),
                Captured("describe_window_focus", ValidBrowserFocusTree)
            ],
            AvaProfileIds.FederalWebMin);

        var step = Assert.Single(report.Steps);
        Assert.Empty(step.Findings);
        Assert.Equal(AvaFindingStatus.Pass, Assert.Single(step.Checkpoints).Status);
        Assert.Equal(AvaProfileIds.FederalWebMin, report.Profile);
    }

    [Fact]
    public async Task Runner_BrowserUiaEvidenceWithWebProfile_MapsFindingsToWebRules()
    {
        var report = await RunWithEvidenceAsync(
            [
                Captured(
                    "describe_window",
                    """
                    {
                      "windowTitle": "Example page - Microsoft Edge",
                      "compactTree": {
                        "name": "Example page - Microsoft Edge",
                        "role": "window",
                        "children": [
                          {
                            "name": "Example Domain",
                            "role": "document",
                            "children": [
                              { "role": "button", "isKeyboardFocusable": true, "patterns": ["Invoke"] },
                              { "name": "Read more", "role": "hyperlink", "isKeyboardFocusable": true }
                            ]
                          }
                        ]
                      }
                    }
                    """),
                Captured("describe_window_focus", ValidBrowserFocusTree)
            ],
            AvaProfileIds.FederalWebMin);

        var findings = report.Steps.Single().Findings;
        var nameFinding = Assert.Single(findings, finding =>
            finding.Id.StartsWith("AVA-NAME-MISSING", StringComparison.Ordinal));
        var actionFinding = Assert.Single(findings, finding =>
            finding.Id.StartsWith("AVA-ACTION-MISSING", StringComparison.Ordinal));

        Assert.Equal(AvaProfileIds.FederalWebMin, nameFinding.ProfileId);
        Assert.Equal("WEB-WCAG-4.1.2-NAME", nameFinding.RuleId);
        Assert.Equal("WCAG 2.0 SC 4.1.2 Name, Role, Value", nameFinding.SourceStandard);
        Assert.Equal(AvaProfileIds.FederalWebMin, actionFinding.ProfileId);
        Assert.Equal("WEB-WCAG-2.1.1-ACTIONABILITY", actionFinding.RuleId);
        Assert.Equal("WCAG 2.0 SC 2.1.1 Keyboard", actionFinding.SourceStandard);
    }

    [Fact]
    public async Task Runner_CdpWebEvidence_ProducesFederalWebFinding()
    {
        var report = await RunWithEvidenceAsync([
            Captured("describe_window", ValidButtonTree),
            Captured("describe_window_focus", ValidButtonTree),
            Captured(
                "web_dom_snapshot",
                """
                {
                  "accessibilityTree": {
                    "result": {
                      "nodes": [
                        {
                          "nodeId": "7",
                          "ignored": false,
                          "role": { "value": "button" },
                          "name": { "value": "" },
                          "properties": [
                            { "name": "focusable", "value": { "value": true } }
                          ]
                        }
                      ]
                    }
                  }
                }
                """)
        ]);

        var finding = Assert.Single(report.Steps.Single().Findings, finding =>
            finding.ToolName == "web_dom_snapshot");

        Assert.Equal(AvaFindingStatus.Fail, finding.Status);
        Assert.Equal(AvaProfileIds.FederalWebMin, finding.ProfileId);
        Assert.Equal("WEB-WCAG-4.1.2-NAME", finding.RuleId);
        Assert.Equal("Web button (AX node 7)", finding.NodeTrace);
        Assert.Equal("computed-role: button; focusable: true", finding.AriaProperties);
    }

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

    private const string ValidBrowserDocumentTree =
        """
        {
          "windowTitle": "Example page - Microsoft Edge",
          "compactTree": {
            "name": "Example page - Microsoft Edge",
            "role": "window",
            "children": [
              {
                "name": "Example Domain",
                "role": "document",
                "children": [
                  {
                    "name": "Learn more",
                    "role": "hyperlink",
                    "isKeyboardFocusable": true,
                    "patterns": ["Invoke"]
                  },
                  {
                    "name": "Search",
                    "role": "edit",
                    "isKeyboardFocusable": true,
                    "patterns": ["Value"]
                  }
                ]
              }
            ]
          }
        }
        """;

    private const string ValidBrowserFocusTree =
        """
        {
          "compactTree": {
            "name": "Learn more",
            "role": "hyperlink",
            "isKeyboardFocusable": true,
            "patterns": ["Invoke"]
          }
        }
        """;

    private static AvaEvidenceRecord Captured(string toolName, string rawJson)
        => new(
            toolName,
            AvaEvidenceStatus.Captured,
            null,
            rawJson,
            "fixture",
            null);

    private static Task<AvaValidationReport> RunWithEvidenceAsync(
        IReadOnlyList<AvaEvidenceRecord> records,
        string profile = AvaProfileIds.FederalWindowsUiaMin)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ava-tests", Guid.NewGuid().ToString("N"));
        var suite = BrainScenarioLoader.Parse(
            """
            name: Fixture scenario
            commands:
              - Inspect fixture window.
            """,
            "scenario.yml");
        var config = AvaValidationConfigLoader.Parse(
            $$"""
            name: Fixture config
            profile: {{profile}}
            windowHandle: 0x00010001
            """,
            "validation.yml");

        return AvaNoOpValidationRunner.RunAsync(
            new AvaValidationRunRequest(
                suite,
                config,
                "scenario.yml",
                "validation.yml",
                "run-001",
                outputDirectory),
            new FixtureEvidenceCollector(records),
            CancellationToken.None);
    }

    private sealed class FixtureEvidenceCollector(IReadOnlyList<AvaEvidenceRecord> records) : IAvaEvidenceCollector
    {
        public Task<IReadOnlyList<AvaEvidenceRecord>> CollectAsync(
            AvaEvidenceCollectionRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(records);
    }
}
