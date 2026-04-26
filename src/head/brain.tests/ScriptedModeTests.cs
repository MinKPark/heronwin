using System.Text.Json;
using Xunit;

namespace HeronWin.Brain.Tests;

[Collection("DebugTrace serial")]
public sealed class ScriptedModeTests
{
    [Fact]
    public void Parse_ReturnsShowHelp_WhenHelpFlagIsPresent()
    {
        var actual = BrainConsoleMode.Parse(["--help"]);

        Assert.True(actual.ShowHelp);
        Assert.False(actual.IsScripted);
    }

    [Fact]
    public void Parse_LoadsCommandsFile_AndIgnoresCommentsAndBlankLines()
    {
        var commandsFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                commandsFilePath,
                """
                commands:
                  - "open netflix"
                  - "play the trailer"
                """);

            var actual = BrainConsoleMode.Parse(["--commands-file", commandsFilePath]);

            Assert.True(actual.IsScripted);
            Assert.Equal(["open netflix", "play the trailer"], actual.Commands);
        }
        finally
        {
            File.Delete(commandsFilePath);
        }
    }

    [Fact]
    public void CommandFileLoader_LoadsRootYamlSequence_AndStripsComments()
    {
        var commandsFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                commandsFilePath,
                """
                # comment
                - "open netflix"
                - "play the trailer" # inline comment
                """);

            var actual = BrainCommandFileLoader.LoadFromFile(commandsFilePath);

            Assert.Equal(["open netflix", "play the trailer"], actual);
        }
        finally
        {
            File.Delete(commandsFilePath);
        }
    }

    [Fact]
    public void CommandFileLoader_PreservesHashInsideQuotedString()
    {
        var commandsFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                commandsFilePath,
                """
                - "open #trending"
                """);

            var actual = BrainCommandFileLoader.LoadFromFile(commandsFilePath);

            Assert.Equal(["open #trending"], actual);
        }
        finally
        {
            File.Delete(commandsFilePath);
        }
    }

    [Fact]
    public void Parse_Throws_WhenScenarioAndInlineCommandsAreMixed()
    {
        var scenarioPath = Path.Combine(Path.GetTempPath(), "sample-scenario.yml");

        var ex = Assert.Throws<InvalidOperationException>(
            () => BrainConsoleMode.Parse(["--scenario", scenarioPath, "--command", "open netflix"]));

        Assert.Contains("either scripted commands", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScenarioLoader_ParsesYamlScenarioSuite_WithAssertions()
    {
        const string yaml = """
        name: Desktop smoke suite
        scenarios:
          - name: Open Netflix
            commands:
              - "open netflix"
            assertions:
              requiredCategories:
                - assistant.reply
              forbiddenCategories:
                - agent.reply_contradiction_detected
              requiredFinalText:
                - Netflix
              forbiddenFinalText:
                - not complete
        """;

        var actual = BrainScenarioLoader.Parse(yaml, "suite.yml");

        Assert.Equal("Desktop smoke suite", actual.Name);
        Assert.Single(actual.Scenarios);
        Assert.Equal("Open Netflix", actual.Scenarios[0].Name);
        Assert.Equal(["open netflix"], actual.Scenarios[0].Commands);
        Assert.Equal(["assistant.reply"], actual.Scenarios[0].Assertions.RequiredCategories);
        Assert.Equal(["Netflix"], actual.Scenarios[0].Assertions.RequiredFinalText);
    }

    [Fact]
    public void ScenarioLoader_ParsesSingleYamlScenario_WithBooleanAssertions()
    {
        const string yaml = """
        name: Open Netflix
        commands:
          - "open netflix"
        assertions:
          allowToolErrors: true
          allowReplyContradictions: false
          allowExplicitlyUnresolvedOutcome: true
        """;

        var actual = BrainScenarioLoader.Parse(yaml, "scenario.yml");

        Assert.Equal("scenario.yml", actual.Name);
        Assert.Single(actual.Scenarios);
        Assert.Equal("Open Netflix", actual.Scenarios[0].Name);
        Assert.True(actual.Scenarios[0].Assertions.AllowToolErrors);
        Assert.False(actual.Scenarios[0].Assertions.AllowReplyContradictions);
        Assert.True(actual.Scenarios[0].Assertions.AllowExplicitlyUnresolvedOutcome);
    }

    [Fact]
    public void ScenarioLoader_ExpandsEnvironmentPlaceholders_InCommands()
    {
        const string yaml = """
        name: Open Netflix
        commands:
          - "enter ${NETFLIX_PROFILE_PIN}"
        """;

        Environment.SetEnvironmentVariable("NETFLIX_PROFILE_PIN", "2468");
        try
        {
            var actual = BrainScenarioLoader.Parse(yaml, "scenario.yml");

            Assert.Equal(["enter 2468"], actual.Scenarios[0].Commands);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NETFLIX_PROFILE_PIN", null);
        }
    }

    [Fact]
    public void AssessTurn_Fails_WhenReplyIsContradictoryAndUnresolved()
    {
        var records = new[]
        {
            CreateTraceRecord(1, "agent.tool_call_completed", """{"turn":7,"isError":false}"""),
            CreateTraceRecord(2, "agent.reply_contradiction_detected", """{"turn":7}"""),
            CreateTraceRecord(
                3,
                "assistant.reply",
                """{"turn":7,"sayText":"Netflix is open now.","logText":"The request is not complete yet."}""")
        };

        var actual = BrainScenarioEvaluator.AssessTurn(records, 7);

        Assert.False(actual.Passed);
        Assert.True(actual.HasReplyContradiction);
        Assert.True(actual.HasExplicitlyUnresolvedOutcome);
        Assert.Contains(actual.Failures, failure => failure.Contains("contradiction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actual.Failures, failure => failure.Contains("not complete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AssessTurn_Passes_WhenToolErrorsAreRecoveredBeforeResolvedReply()
    {
        var records = new[]
        {
            CreateTraceRecord(1, "agent.tool_call_completed", """{"turn":8,"isError":true}"""),
            CreateTraceRecord(2, "agent.tool_call_completed", """{"turn":8,"isError":false}"""),
            CreateTraceRecord(
                3,
                "assistant.reply",
                """{"turn":8,"sayText":"Netflix is open now.","logText":"Netflix home screen is visible."}""")
        };

        var actual = BrainScenarioEvaluator.AssessTurn(records, 8);

        Assert.True(actual.Passed);
        Assert.Equal(1, actual.ToolErrorCount);
        Assert.Empty(actual.Failures);
    }

    [Fact]
    public void AssessTurn_Passes_WhenReplyContradictionIsRecoveredBeforeFinalReply()
    {
        var records = new[]
        {
            CreateTraceRecord(1, "agent.reply_contradiction_detected", """{"turn":10}"""),
            CreateTraceRecord(
                2,
                "assistant.reply",
                """{"turn":10,"sayText":"Netflix is open on a playback page.","logText":"Confirmed playback page is open in Netflix."}""")
        };

        var actual = BrainScenarioEvaluator.AssessTurn(records, 10);

        Assert.True(actual.Passed);
        Assert.True(actual.HasReplyContradiction);
        Assert.Empty(actual.Failures);
    }

    [Fact]
    public void AssessTurn_Fails_WhenToolErrorsRemainUnrecovered()
    {
        var records = new[]
        {
            CreateTraceRecord(1, "agent.tool_call_completed", """{"turn":9,"isError":true}"""),
            CreateTraceRecord(
                2,
                "assistant.reply",
                """{"turn":9,"sayText":"I could not open Netflix.","logText":"The request is not complete yet because opening Netflix failed."}""")
        };

        var actual = BrainScenarioEvaluator.AssessTurn(records, 9);

        Assert.False(actual.Passed);
        Assert.Equal(1, actual.ToolErrorCount);
        Assert.Contains(actual.Failures, failure => failure.Contains("unrecovered tool error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateScenario_Passes_WhenRequiredCategoryAndFinalTextArePresent()
    {
        var scenario = new BrainScenarioDefinition(
            "Open Netflix",
            ["open netflix"],
            new BrainScenarioAssertions(
                RequiredCategories: ["assistant.reply"],
                ForbiddenCategories: ["agent.reply_contradiction_detected"],
                RequiredFinalText: ["Netflix"],
                ForbiddenFinalText: ["not complete"],
                AllowToolErrors: false,
                AllowReplyContradictions: false,
                AllowExplicitlyUnresolvedOutcome: false));
        var assessment = new BrainTurnAssessment(
            Passed: true,
            ToolCallCount: 2,
            ToolErrorCount: 0,
            HasAssistantReply: true,
            HasReplyContradiction: false,
            HasExplicitlyUnresolvedOutcome: false,
            FinalSayText: "Netflix is open now.",
            FinalLogText: "Netflix home screen is visible.",
            Failures: []);
        var turn = new BrainScriptedTurnResult(
            1,
            "open netflix",
            new AgentReply("Netflix home screen is visible.", "Netflix is open now.", "{}"),
            assessment);
        var records = new[]
        {
            CreateTraceRecord(
                1,
                "assistant.reply",
                """{"turn":1,"sayText":"Netflix is open now.","logText":"Netflix home screen is visible."}""")
        };

        var actual = BrainScenarioEvaluator.EvaluateScenario(records, scenario, [turn]);

        Assert.True(actual.Passed);
        Assert.Empty(actual.Failures);
    }

    [Fact]
    public void EvaluateScenario_Fails_WhenFinalReplyContainsForbiddenWrongTitle()
    {
        var scenario = new BrainScenarioDefinition(
            "Play Boyfriend on Demand",
            ["open boyfriend on demand and play episode 1"],
            new BrainScenarioAssertions(
                RequiredCategories: ["assistant.reply"],
                ForbiddenCategories: [],
                RequiredFinalText: ["Boyfriend on Demand"],
                ForbiddenFinalText: ["Pursuit of Jade"],
                AllowToolErrors: false,
                AllowReplyContradictions: false,
                AllowExplicitlyUnresolvedOutcome: false));
        var assessment = new BrainTurnAssessment(
            Passed: true,
            ToolCallCount: 4,
            ToolErrorCount: 0,
            HasAssistantReply: true,
            HasReplyContradiction: false,
            HasExplicitlyUnresolvedOutcome: false,
            FinalSayText: "Episode 1 is playing now.",
            FinalLogText: "Confirmed playback for Pursuit of Jade E1 Episode 1.",
            Failures: []);
        var turn = new BrainScriptedTurnResult(
            5,
            "open boyfriend on demand and play episode 1",
            new AgentReply(
                "Confirmed playback for Pursuit of Jade E1 Episode 1.",
                "Episode 1 is playing now.",
                "{}"),
            assessment);
        var records = new[]
        {
            CreateTraceRecord(
                1,
                "assistant.reply",
                """{"turn":5,"sayText":"Episode 1 is playing now.","logText":"Confirmed playback for Pursuit of Jade E1 Episode 1."}""")
        };

        var actual = BrainScenarioEvaluator.EvaluateScenario(records, scenario, [turn]);

        Assert.False(actual.Passed);
        Assert.Contains(
            actual.Failures,
            failure => failure.Contains("required text", StringComparison.OrdinalIgnoreCase)
                       || failure.Contains("forbidden text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScriptedLookahead_TryParseDecision_AcceptsNoOpCompletion()
    {
        const string responseText = """
        {
          "say": "Netflix Home is visible.",
          "log": "The fresh UI snapshot shows Netflix Home.",
          "currentTurnStatus": "complete",
          "nextTurnStatus": "next_complete_noop",
          "nextSay": "No PIN entry is needed.",
          "nextLog": "The PIN prompt is not visible, so the conditional PIN step is already satisfied.",
          "nextTurnReason": "The next scripted command is conditional on a prompt that is absent."
        }
        """;

        var parsed = ScriptedLookahead.TryParseDecision(
            sourceTurnId: 1,
            targetTurnId: 2,
            responseText,
            out var decision,
            out var skipReason);

        Assert.True(parsed);
        Assert.Equal(string.Empty, skipReason);
        Assert.Equal(1, decision.SourceTurnId);
        Assert.Equal(2, decision.TargetTurnId);
        Assert.True(decision.CurrentTurnComplete);
        Assert.True(decision.NextTurnCompleteNoOp);
        Assert.Equal("No PIN entry is needed.", decision.NextSay);
    }

    [Fact]
    public void ScriptedLookahead_TryParseDecision_RejectsUnexpectedToolTargetTurn()
    {
        const string responseText = """
        {
          "say": "Netflix Home is visible.",
          "log": "The fresh UI snapshot shows Netflix Home.",
          "currentTurnStatus": "complete",
          "nextTurnStatus": "next_complete_noop",
          "toolTargetTurn": 99
        }
        """;

        var parsed = ScriptedLookahead.TryParseDecision(
            sourceTurnId: 1,
            targetTurnId: 2,
            responseText,
            out _,
            out var skipReason);

        Assert.False(parsed);
        Assert.Equal("unexpected_tool_target_turn", skipReason);
    }

    [Fact]
    public void TryAdvanceNoOpLookaheadTurn_RecordsSyntheticTurnAndUpdatesHistory()
    {
        DebugTrace.Configure(true);

        try
        {
            var jsonLogPath = DebugTrace.JsonLogFilePath;
            Assert.False(string.IsNullOrWhiteSpace(jsonLogPath));

            var scenario = new BrainScenarioDefinition(
                "Conditional PIN",
                [
                    "Open Netflix.",
                    "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
                ],
                BrainScenarioAssertions.Empty);
            var history = new List<AgentMessage>
            {
                new AgentMessage.User("Open Netflix."),
                new AgentMessage.Assistant("""{"say":"Netflix is open.","log":"Netflix is open."}"""),
            };
            var turns = new List<BrainScriptedTurnResult>();
            var decision = new ScriptedLookaheadDecision(
                SourceTurnId: 1,
                TargetTurnId: 2,
                CurrentTurnStatus: "complete",
                NextTurnStatus: "next_complete_noop",
                NextSay: "No PIN entry is needed.",
                NextLog: "The PIN prompt is not visible, so the conditional PIN step is already satisfied.",
                Reason: "The fresh UI snapshot shows Netflix Home.");

            var advanced = ScriptedConversationRunner.TryAdvanceNoOpLookaheadTurn(
                scenario,
                targetCommandIndex: 1,
                targetTurnId: 2,
                sourceTurnId: 1,
                decision,
                history,
                jsonLogPath!,
                turns,
                out var passed);

            Assert.True(advanced);
            Assert.True(passed);
            var turn = Assert.Single(turns);
            Assert.Equal(2, turn.TurnId);
            Assert.Equal(scenario.Commands[1], turn.Command);
            Assert.True(turn.Assessment.Passed);
            Assert.Equal(4, history.Count);
            var advancedUserMessage = Assert.IsType<AgentMessage.User>(history[2]);
            Assert.Equal(scenario.Commands[1], advancedUserMessage.Content);

            var records = BrainTraceLogReader.ReadAll(jsonLogPath!);
            Assert.Contains(
                records,
                record => record.Category == "agent.lookahead.advanced" &&
                          record.TryGetInt64("sourceTurn", out var sourceTurn) &&
                          sourceTurn == 1 &&
                          record.TryGetInt64("targetTurn", out var targetTurn) &&
                          targetTurn == 2);
            Assert.Contains(
                records,
                record => record.Category == "assistant.reply" &&
                          record.TryGetInt64("turn", out var turnId) &&
                          turnId == 2 &&
                          record.TryGetInt64("attempts", out var attempts) &&
                          attempts == 0);
        }
        finally
        {
            DebugTrace.Configure(false);
        }
    }

    private static BrainTraceRecord CreateTraceRecord(long sequence, string category, string dataJson)
    {
        using var document = JsonDocument.Parse(dataJson);
        return new BrainTraceRecord(
            sequence,
            category,
            document.RootElement.Clone(),
            dataJson);
    }
}
