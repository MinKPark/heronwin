using System.Text.Json;
using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class ScriptedModeTests
{
    [Fact]
    public void Parse_ReturnsShowHelp_WhenHelpFlagIsPresent()
    {
        var actual = HerfaceConsoleMode.Parse(["--help"]);

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
                # comment

                open netflix
                play the trailer
                """);

            var actual = HerfaceConsoleMode.Parse(["--commands-file", commandsFilePath]);

            Assert.True(actual.IsScripted);
            Assert.Equal(["open netflix", "play the trailer"], actual.Commands);
        }
        finally
        {
            File.Delete(commandsFilePath);
        }
    }

    [Fact]
    public void Parse_Throws_WhenScenarioAndInlineCommandsAreMixed()
    {
        var scenarioPath = Path.Combine(Path.GetTempPath(), "sample-scenario.json");

        var ex = Assert.Throws<InvalidOperationException>(
            () => HerfaceConsoleMode.Parse(["--scenario", scenarioPath, "--command", "open netflix"]));

        Assert.Contains("either scripted commands", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScenarioLoader_ParsesScenarioSuite_WithAssertions()
    {
        const string json = """
        {
          "name": "Desktop smoke suite",
          "scenarios": [
            {
              "name": "Open Netflix",
              "commands": ["open netflix"],
              "assertions": {
                "requiredCategories": ["assistant.reply"],
                "forbiddenCategories": ["agent.reply_contradiction_detected"],
                "requiredFinalText": ["Netflix"],
                "forbiddenFinalText": ["not complete"]
              }
            }
          ]
        }
        """;

        var actual = HerfaceScenarioLoader.Parse(json, "suite.json");

        Assert.Equal("Desktop smoke suite", actual.Name);
        Assert.Single(actual.Scenarios);
        Assert.Equal("Open Netflix", actual.Scenarios[0].Name);
        Assert.Equal(["open netflix"], actual.Scenarios[0].Commands);
        Assert.Equal(["assistant.reply"], actual.Scenarios[0].Assertions.RequiredCategories);
        Assert.Equal(["Netflix"], actual.Scenarios[0].Assertions.RequiredFinalText);
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

        var actual = HerfaceScenarioEvaluator.AssessTurn(records, 7);

        Assert.False(actual.Passed);
        Assert.True(actual.HasReplyContradiction);
        Assert.True(actual.HasExplicitlyUnresolvedOutcome);
        Assert.Contains(actual.Failures, failure => failure.Contains("contradiction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actual.Failures, failure => failure.Contains("not complete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateScenario_Passes_WhenRequiredCategoryAndFinalTextArePresent()
    {
        var scenario = new HerfaceScenarioDefinition(
            "Open Netflix",
            ["open netflix"],
            new HerfaceScenarioAssertions(
                RequiredCategories: ["assistant.reply"],
                ForbiddenCategories: ["agent.reply_contradiction_detected"],
                RequiredFinalText: ["Netflix"],
                ForbiddenFinalText: ["not complete"],
                AllowToolErrors: false,
                AllowReplyContradictions: false,
                AllowExplicitlyUnresolvedOutcome: false));
        var assessment = new HerfaceTurnAssessment(
            Passed: true,
            ToolCallCount: 2,
            ToolErrorCount: 0,
            HasAssistantReply: true,
            HasReplyContradiction: false,
            HasExplicitlyUnresolvedOutcome: false,
            FinalSayText: "Netflix is open now.",
            FinalLogText: "Netflix home screen is visible.",
            Failures: []);
        var turn = new HerfaceScriptedTurnResult(
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

        var actual = HerfaceScenarioEvaluator.EvaluateScenario(records, scenario, [turn]);

        Assert.True(actual.Passed);
        Assert.Empty(actual.Failures);
    }

    private static HerfaceTraceRecord CreateTraceRecord(long sequence, string category, string dataJson)
    {
        using var document = JsonDocument.Parse(dataJson);
        return new HerfaceTraceRecord(
            sequence,
            category,
            document.RootElement.Clone(),
            dataJson);
    }
}
