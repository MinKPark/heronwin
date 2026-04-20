using System.Text.Json;
using Xunit;

namespace HeronWin.Brain.Tests;

[Collection("DebugTrace serial")]
public sealed class AgentRunnerContinuationTests
{
    [Fact]
    public void TryBuildDiscreteSlotTextContinuation_ReturnsFalse_WhenFreshHomeSnapshotWinsOverStalePinTree()
    {
        var stalePinTreeSnapshot =
            """
            {
              "Window": {
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0",
                    "UiPath": "1/0",
                    "Name": "Enter Min's PIN",
                    "ControlType": "Text"
                  },
                  {
                    "Path": "1/1",
                    "UiPath": "1/1",
                    "Name": "Forgot PIN",
                    "ControlType": "Hyperlink"
                  }
                ]
              }
            }
            """;
        var freshHomeSnapshot =
            """
            {
              "Window": {
                "Title": "Home - Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/0/0/2",
                    "UiPath": "1/0/0/1/0/0/2",
                    "Name": "Home",
                    "ControlType": "Button",
                    "AutomationId": "Home",
                    "AvailableActions": [ "invoke" ]
                  }
                ]
              }
            }
            """;

        var actionableUiTreeContext = AgentRunner.GetCurrentUiTreeContext(freshHomeSnapshot, stalePinTreeSnapshot);
        var actual = AgentRunner.TryBuildDiscreteSlotTextContinuation(
            "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
            actionableUiTreeContext,
            recentFocusContext: null,
            out _,
            out var skipReason,
            out var surfaceSummary);

        Assert.Equal(freshHomeSnapshot, actionableUiTreeContext);
        Assert.False(actual);
        Assert.Equal("discrete_slot_surface_not_visible", skipReason);
        Assert.Equal("Home - Netflix - Microsoft Edge", surfaceSummary["window"]);
        Assert.False((bool)surfaceSummary["discreteSlotPromptVisible"]!);
    }

    [Fact]
    public async Task RunTurnAsync_DoesNotStartNetflixPinContinuation_WhenCurrentEvidenceAlreadyShowsHome()
    {
        var llmClient = new QueuedLlmClient(
        [
            new ChatResult(
                """
                {
                  "say": "Netflix Home is visible for Min.",
                  "log": "Netflix Home is visible for Min."
                }
                """,
                [])
        ]);
        var toolCalls = new List<(string ToolName, IReadOnlyDictionary<string, object?> Args)>();
        await using var mcpManager = new McpClientManager(
            _ => Task.FromResult<IReadOnlyList<ToolDefinition>>([]),
            toolCallTimeoutOverride: null,
            callToolOverride: (toolName, args, _) =>
            {
                toolCalls.Add((toolName, new Dictionary<string, object?>(args, StringComparer.Ordinal)));
                throw new InvalidOperationException($"Unexpected tool call: {toolName}");
            });
        var desktopSession = new DesktopSessionContext
        {
            RecentWindowContext = BuildFreshNetflixHomeSnapshot(),
            RecentUiTreeContext = BuildStaleNetflixPinSnapshot(),
        };

        var reply = await AgentRunner.RunTurnAsync(
            turnId: 1,
            userText: "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
            history: [],
            tools: [],
            composedPrompt: CreatePrompt(),
            llmClient,
            mcpManager,
            desktopSession,
            CancellationToken.None,
            displayUserMessage: false);

        Assert.Equal("Netflix Home is visible for Min.", reply.SpokenText);
        Assert.Empty(toolCalls);
    }

    [Fact]
    public async Task RunTurnAsync_EmitsSkippedPinContinuationTrace_WhenCurrentEvidenceAlreadyShowsHome()
    {
        DebugTrace.Configure(true);

        try
        {
            var llmClient = new QueuedLlmClient(
            [
                new ChatResult(
                    """
                    {
                      "say": "Netflix Home is visible for Min.",
                      "log": "Netflix Home is visible for Min."
                    }
                    """,
                    [])
            ]);
            await using var mcpManager = new McpClientManager(
                _ => Task.FromResult<IReadOnlyList<ToolDefinition>>([]),
                toolCallTimeoutOverride: null,
                callToolOverride: (_, _, _) => throw new InvalidOperationException("No MCP call expected."));
            var desktopSession = new DesktopSessionContext
            {
                RecentWindowContext = BuildFreshNetflixHomeSnapshot(),
                RecentUiTreeContext = BuildStaleNetflixPinSnapshot(),
            };

            await AgentRunner.RunTurnAsync(
                turnId: 1,
                userText: "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
                history: [],
                tools: [],
                composedPrompt: CreatePrompt(),
                llmClient,
                mcpManager,
                desktopSession,
                CancellationToken.None,
                displayUserMessage: false);

            var jsonLogPath = DebugTrace.JsonLogFilePath;
            Assert.False(string.IsNullOrWhiteSpace(jsonLogPath));
            Assert.True(File.Exists(jsonLogPath));

            var skipEventFound = false;
            var startEventFound = false;
            foreach (var line in ReadLinesWithSharedAccess(jsonLogPath!))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("category", out var categoryElement))
                {
                    continue;
                }

                var category = categoryElement.GetString();
                if (!root.TryGetProperty("data", out var dataElement) ||
                    dataElement.ValueKind != JsonValueKind.Object ||
                    !dataElement.TryGetProperty("policyName", out var policyNameElement) ||
                    !string.Equals(policyNameElement.GetString(), "netflix_discrete_slot_text_entry", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(category, "agent.internal_continuation_skipped", StringComparison.Ordinal) &&
                    dataElement.TryGetProperty("skipReason", out var skipReasonElement) &&
                    string.Equals(skipReasonElement.GetString(), "discrete_slot_surface_not_visible", StringComparison.Ordinal))
                {
                    skipEventFound = true;
                }

                if (string.Equals(category, "agent.internal_continuation_started", StringComparison.Ordinal))
                {
                    startEventFound = true;
                }
            }

            Assert.True(skipEventFound);
            Assert.False(startEventFound);
        }
        finally
        {
            DebugTrace.Configure(false);
        }
    }

    [Fact]
    public async Task RunTurnAsync_DoesNotStartNetflixPinContinuation_AfterToolDrivenPinInput_WhenPreflightSnapshotShowsHome()
    {
        var llmClient = new QueuedLlmClient(
        [
            new ChatResult(
                null,
                [
                    new ToolCallRequest(
                        "call-1",
                        "type_window_text",
                        """{"text":"9"}""")
                ]),
            new ChatResult(
                """
                {
                  "say": "Netflix Home is visible for Min.",
                  "log": "Netflix Home is visible for Min."
                }
                """,
                [])
        ]);
        var toolCalls = new List<(string ToolName, IReadOnlyDictionary<string, object?> Args)>();
        var describeWindowCallCount = 0;
        await using var mcpManager = new McpClientManager(
            _ => Task.FromResult<IReadOnlyList<ToolDefinition>>(
            [
                CreateToolDefinition("type_window_text"),
                CreateToolDefinition("describe_window")
            ]),
            toolCallTimeoutOverride: null,
            callToolOverride: (toolName, args, _) =>
            {
                toolCalls.Add((toolName, new Dictionary<string, object?>(args, StringComparer.Ordinal)));
                return Task.FromResult(toolName switch
                {
                    "type_window_text" => new ToolCallOutcome(
                        """{"Window":{"Handle":"0x009C0680","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""",
                        []),
                    "describe_window" => ++describeWindowCallCount switch
                    {
                        1 => new ToolCallOutcome(BuildStaleNetflixPinSnapshot(), []),
                        2 => new ToolCallOutcome(BuildFreshNetflixHomeSnapshot(), []),
                        _ => throw new InvalidOperationException($"Unexpected describe_window call #{describeWindowCallCount}."),
                    },
                    _ => throw new InvalidOperationException($"Unexpected tool call: {toolName}"),
                });
            });
        var desktopSession = new DesktopSessionContext
        {
            RecentWindowContext = BuildStaleNetflixPinSnapshot(),
            RecentUiTreeContext = BuildStaleNetflixPinSnapshot(),
            RecentFocusContext = BuildStaleNetflixPinFocusSnapshot(),
        };

        var reply = await AgentRunner.RunTurnAsync(
            turnId: 1,
            userText: "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
            history: [],
            tools:
            [
                CreateToolDefinition("type_window_text"),
                CreateToolDefinition("describe_window")
            ],
            composedPrompt: CreatePrompt(),
            llmClient,
            mcpManager,
            desktopSession,
            CancellationToken.None,
            displayUserMessage: false);

        Assert.Equal("Netflix Home is visible for Min.", reply.SpokenText);
        Assert.Equal(3, toolCalls.Count);
        Assert.Equal(
            new[] { "type_window_text", "describe_window", "describe_window" },
            toolCalls.Select(call => call.ToolName).ToArray());
    }

    [Fact]
    public async Task RunTurnAsync_EmitsPreflightRefreshAndSkippedPinTrace_AfterToolDrivenPinInput_WhenPreflightSnapshotShowsHome()
    {
        DebugTrace.Configure(true);

        try
        {
            var llmClient = new QueuedLlmClient(
            [
                new ChatResult(
                    null,
                    [
                        new ToolCallRequest(
                            "call-1",
                            "type_window_text",
                            """{"text":"9"}""")
                    ]),
                new ChatResult(
                    """
                    {
                      "say": "Netflix Home is visible for Min.",
                      "log": "Netflix Home is visible for Min."
                    }
                    """,
                    [])
            ]);
            var describeWindowCallCount = 0;
            await using var mcpManager = new McpClientManager(
                _ => Task.FromResult<IReadOnlyList<ToolDefinition>>(
                [
                    CreateToolDefinition("type_window_text"),
                    CreateToolDefinition("describe_window")
                ]),
                toolCallTimeoutOverride: null,
                callToolOverride: (toolName, _, _) => Task.FromResult(toolName switch
                {
                    "type_window_text" => new ToolCallOutcome(
                        """{"Window":{"Handle":"0x009C0680","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""",
                        []),
                    "describe_window" => ++describeWindowCallCount switch
                    {
                        1 => new ToolCallOutcome(BuildStaleNetflixPinSnapshot(), []),
                        2 => new ToolCallOutcome(BuildFreshNetflixHomeSnapshot(), []),
                        _ => throw new InvalidOperationException($"Unexpected describe_window call #{describeWindowCallCount}."),
                    },
                    _ => throw new InvalidOperationException($"Unexpected tool call: {toolName}"),
                }));
            var desktopSession = new DesktopSessionContext
            {
                RecentWindowContext = BuildStaleNetflixPinSnapshot(),
                RecentUiTreeContext = BuildStaleNetflixPinSnapshot(),
                RecentFocusContext = BuildStaleNetflixPinFocusSnapshot(),
            };

            await AgentRunner.RunTurnAsync(
                turnId: 1,
                userText: "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
                history: [],
                tools:
                [
                    CreateToolDefinition("type_window_text"),
                    CreateToolDefinition("describe_window")
                ],
                composedPrompt: CreatePrompt(),
                llmClient,
                mcpManager,
                desktopSession,
                CancellationToken.None,
                displayUserMessage: false);

            var jsonLogPath = DebugTrace.JsonLogFilePath;
            Assert.False(string.IsNullOrWhiteSpace(jsonLogPath));
            Assert.True(File.Exists(jsonLogPath));

            var preflightRefreshFound = false;
            var skipEventFound = false;
            var startEventFound = false;
            foreach (var line in ReadLinesWithSharedAccess(jsonLogPath!))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("category", out var categoryElement))
                {
                    continue;
                }

                var category = categoryElement.GetString();
                if (!root.TryGetProperty("data", out var dataElement) ||
                    dataElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (string.Equals(category, "agent.internal_continuation_preflight_snapshot", StringComparison.Ordinal) &&
                    dataElement.TryGetProperty("invalidatedStoredFocusContext", out var invalidatedElement) &&
                    invalidatedElement.ValueKind is JsonValueKind.True)
                {
                    preflightRefreshFound = true;
                }

                if (!dataElement.TryGetProperty("policyName", out var policyNameElement) ||
                    !string.Equals(policyNameElement.GetString(), "netflix_discrete_slot_text_entry", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(category, "agent.internal_continuation_skipped", StringComparison.Ordinal) &&
                    dataElement.TryGetProperty("skipReason", out var skipReasonElement) &&
                    string.Equals(skipReasonElement.GetString(), "discrete_slot_surface_not_visible", StringComparison.Ordinal))
                {
                    skipEventFound = true;
                }

                if (string.Equals(category, "agent.internal_continuation_started", StringComparison.Ordinal))
                {
                    startEventFound = true;
                }
            }

            Assert.True(preflightRefreshFound);
            Assert.True(skipEventFound);
            Assert.False(startEventFound);
        }
        finally
        {
            DebugTrace.Configure(false);
        }
    }

    private static ComposedAgentPrompt CreatePrompt()
        => new(
            SystemPrompt: "test prompt",
            SourceDescription: "unit test",
            UsesFallbackDefinition: true,
            ActiveSkills:
            [
                new AgentSkillPrompt(
                    "netflix-profile-and-pin",
                    "skills/netflix/netflix-profile-and-pin.skill.md",
                    "# Skill",
                    new AgentSkillMetadata(
                        "netflix-profile-and-pin",
                        Summary: null,
                        PreferredTools: [],
                        AppliesWhen: [],
                        Group: "netflix",
                        Priority: 100,
                        Activation: new AgentSkillActivation([], [], [], [], [], [], [], []),
                        Affordances:
                        [
                            "discrete_slot_text_entry",
                            "discrete_slot_text_rewrite",
                        ]))
            ]);

    private static ToolDefinition CreateToolDefinition(string name)
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return new ToolDefinition(name, $"{name} description", document.RootElement.Clone());
    }

    private static IReadOnlyList<string> ReadLinesWithSharedAccess(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static string BuildStaleNetflixPinSnapshot()
        => """
        {
          "Window": {
            "Title": "Netflix - Microsoft Edge",
            "ClassName": "Chrome_WidgetWin_1"
          },
          "ElementTree": {
            "Path": "root",
            "UiPath": "root",
            "ControlType": "Window",
            "Children": [
              {
                "Path": "1/0",
                "UiPath": "1/0",
                "Name": "Enter Min's PIN",
                "ControlType": "Text"
              },
              {
                "Path": "1/1",
                "UiPath": "1/1",
                "Name": "Forgot PIN",
                "ControlType": "Hyperlink"
              }
            ]
          }
        }
        """;

    private static string BuildFreshNetflixHomeSnapshot()
        => """
        {
          "Window": {
            "Title": "Home - Netflix - Microsoft Edge",
            "ClassName": "Chrome_WidgetWin_1"
          },
          "ElementTree": {
            "Path": "root",
            "UiPath": "root",
            "ControlType": "Window",
            "Children": [
              {
                "Path": "1/0/0/1/0/0/2",
                "UiPath": "1/0/0/1/0/0/2",
                "Name": "Home",
                "ControlType": "Button",
                "AutomationId": "Home",
                "AvailableActions": [ "invoke" ]
              }
            ]
          }
        }
        """;

    private static string BuildStaleNetflixPinFocusSnapshot()
        => """
        {
          "Window": {
            "Handle": "0x009C0680",
            "Title": "Netflix - Microsoft Edge",
            "ClassName": "Chrome_WidgetWin_1"
          },
          "FocusedElement": {
            "Path": "focused",
            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/5",
            "Name": "PIN Entry Input 2.",
            "ControlType": "Edit",
            "ClassName": "pin-number-input focus-visible"
          }
        }
        """;

    private sealed class QueuedLlmClient(IReadOnlyList<ChatResult> responses) : ILlmClient
    {
        private readonly Queue<ChatResult> _responses = new(responses);

        public LlmProviderId ProviderId => LlmProviderId.OpenAiApi;

        public string DisplayName => "Queued Test LLM";

        public LlmModelProfile ModelProfile { get; } = new(
            LlmProviderId.OpenAiApi,
            "gpt-5.4-mini",
            ContextCompressionTriggerRatio: 0.55,
            WindowSnapshotCharBudget: 4_800,
            FocusSnapshotCharBudget: 2_800,
            MaxThrottleRetries: 1);

        public Task<ChatResult> ChatAsync(
            IReadOnlyList<AgentMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            string? systemPrompt,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued LLM responses remain.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}

[CollectionDefinition("DebugTrace serial", DisableParallelization = true)]
public sealed class DebugTraceSerialCollection;
