using System.Text.Json;
using Xunit;

namespace HeronWin.Brain.Tests;

[Collection("DebugTrace serial")]
public sealed class AgentRunnerContinuationTests
{
    [Fact]
    public void TryBuildNetflixPinContinuation_ReturnsFalse_WhenFreshHomeSnapshotWinsOverStalePinTree()
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
        var actual = AgentRunner.TryBuildNetflixPinContinuation(
            "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
            actionableUiTreeContext,
            recentFocusContext: null,
            out _,
            out var skipReason,
            out var surfaceSummary);

        Assert.Equal(freshHomeSnapshot, actionableUiTreeContext);
        Assert.False(actual);
        Assert.Equal("pin_prompt_not_visible", skipReason);
        Assert.Equal("Home - Netflix - Microsoft Edge", surfaceSummary["window"]);
        Assert.False((bool)surfaceSummary["pinPromptVisible"]!);
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
                    !string.Equals(policyNameElement.GetString(), "netflix_pin_entry", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(category, "agent.internal_continuation_skipped", StringComparison.Ordinal) &&
                    dataElement.TryGetProperty("skipReason", out var skipReasonElement) &&
                    string.Equals(skipReasonElement.GetString(), "pin_prompt_not_visible", StringComparison.Ordinal))
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

    private static ComposedAgentPrompt CreatePrompt()
        => new(
            SystemPrompt: "test prompt",
            SourceDescription: "unit test",
            UsesFallbackDefinition: true,
            ActiveSkills: []);

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
