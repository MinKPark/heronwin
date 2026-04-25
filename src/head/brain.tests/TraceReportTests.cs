using System.Text.Json;
using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class TraceReportTests
{
    [Fact]
    public void Parse_ReturnsTraceReportMode_WhenTraceReportFlagIsPresent()
    {
        var tracePath = Path.Combine(Path.GetTempPath(), "brain.debug.jsonl");

        var actual = BrainConsoleMode.Parse(["--trace-report", tracePath]);

        Assert.True(actual.IsTraceReport);
        Assert.False(actual.IsScripted);
        Assert.Equal(Path.GetFullPath(tracePath), actual.TraceReportPath);
    }

    [Fact]
    public void Parse_Throws_WhenTraceReportAndScenarioAreMixed()
    {
        var tracePath = Path.Combine(Path.GetTempPath(), "brain.debug.jsonl");
        var scenarioPath = Path.Combine(Path.GetTempPath(), "scenario.yml");

        var ex = Assert.Throws<InvalidOperationException>(
            () => BrainConsoleMode.Parse(["--trace-report", tracePath, "--scenario", scenarioPath]));

        Assert.Contains("trace-report", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_ComputesLlmTimingAndAttemptBreakdown()
    {
        var tracePath = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(
                tracePath,
                [
                    CreateTraceLine(
                        "2026-04-22T21:00:00.0000000-07:00",
                        1,
                        "session.start",
                        """{"llmProvider":"OpenAiCodex","openAiModel":"gpt-5.4-mini"}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:01.0000000-07:00",
                        2,
                        "agent.turn.scripted_begin",
                        """{"turn":1,"scenario":"Smoke","command":"open netflix"}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:02.0000000-07:00",
                        3,
                        "llm.request",
                        """{"turn":1,"attempt":1}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:12.0000000-07:00",
                        4,
                        "llm.response",
                        """{"turn":1,"attempt":1,"textPreview":"tool call"}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:12.2000000-07:00",
                        5,
                        "agent.tool_call_completed",
                        """{"turn":1,"executedTool":"list_windows","elapsedMs":150,"isError":false}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:12.5000000-07:00",
                        6,
                        "agent.desktop_followup_snapshot",
                        """{"turn":1,"elapsedMs":200,"tool":"list_windows"}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:13.0000000-07:00",
                        7,
                        "llm.request",
                        """{"turn":1,"attempt":2}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:33.0000000-07:00",
                        8,
                        "llm.response",
                        """{"turn":1,"attempt":2,"textPreview":"done"}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:34.0000000-07:00",
                        9,
                        "assistant.reply",
                        """{"turn":1,"elapsedMs":32000,"attempts":2,"sayText":"Netflix is open.","logText":"Netflix is open."}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:35.0000000-07:00",
                        10,
                        "display.info",
                        """{"message":"Scenario passed: Smoke"}""")
                ]);

            var report = BrainTraceReporter.Generate(tracePath);

            Assert.Equal("Smoke", report.ScenarioName);
            Assert.Equal("OpenAiCodex", report.Provider);
            Assert.Equal("gpt-5.4-mini", report.Model);
            Assert.Equal(35000d, report.ScenarioElapsedMs, precision: 3);
            Assert.Equal(2, report.TotalLlmAttemptCount);
            Assert.Equal(30000d, report.TotalLlmTimeMs, precision: 3);
            Assert.Equal(15000d, report.AverageLlmAttemptMs, precision: 3);
            Assert.Single(report.Turns);
            Assert.Equal(2, report.Turns[0].AttemptCount);
            Assert.Equal(30000d, report.Turns[0].LlmTimeMs, precision: 3);
            Assert.Equal(["list_windows"], report.Turns[0].Attempts[0].ExecutedTools);
            Assert.Empty(report.Turns[0].Attempts[1].ExecutedTools);

            var markdown = report.ToMarkdown();

            Assert.Contains("# Brain Trace Report", markdown, StringComparison.Ordinal);
            Assert.Contains("Scenario elapsed: `35.000 s`", markdown, StringComparison.Ordinal);
            Assert.Contains("| 1 | 32.000 | 2 | 30.000 | 15.000 | 1 | 0.150 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| LLM time | 2 | 30.000 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| 1 | 10.000 | list_windows |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tracePath);
        }
    }

    [Fact]
    public void Generate_IncludesTurnStartHelperBucket()
    {
        var tracePath = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(
                tracePath,
                [
                    CreateTraceLine(
                        "2026-04-22T21:00:00.0000000-07:00",
                        1,
                        "session.start",
                        """{"llmProvider":"OpenAiCodex","openAiModel":"gpt-5.4-mini"}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:01.0000000-07:00",
                        2,
                        "agent.turn.scripted_begin",
                        """{"turn":1,"scenario":"Smoke","command":"open netflix"}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:01.0500000-07:00",
                        3,
                        "agent.turn.ready_state_used",
                        """{"turn":1,"elapsedMs":45,"sourceTurn":0}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:02.0000000-07:00",
                        4,
                        "llm.request",
                        """{"turn":1,"attempt":1,"promptTokenEstimate":1234}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:04.0000000-07:00",
                        5,
                        "llm.response",
                        """{"turn":1,"attempt":1,"textPreview":"done"}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:05.0000000-07:00",
                        6,
                        "assistant.reply",
                        """{"turn":1,"elapsedMs":4000,"attempts":1,"sayText":"Netflix is open.","logText":"Netflix is open."}"""),
                    CreateTraceLine(
                        "2026-04-22T21:00:06.0000000-07:00",
                        7,
                        "display.info",
                        """{"message":"Scenario passed: Smoke"}""")
                ]);

            var report = BrainTraceReporter.Generate(tracePath);

            Assert.Contains(
                report.Buckets,
                bucket => bucket.Name == "Turn-start helper time"
                          && bucket.Count == 1
                          && bucket.ElapsedMs == 45d);
        }
        finally
        {
            File.Delete(tracePath);
        }
    }

    private static string CreateTraceLine(string timestamp, long sequence, string category, string dataJson)
    {
        using var data = JsonDocument.Parse(dataJson);
        var envelope = new Dictionary<string, object?>
        {
            ["timestamp"] = timestamp,
            ["sequence"] = sequence,
            ["category"] = category,
            ["data"] = JsonSerializer.Deserialize<object>(data.RootElement.GetRawText()),
        };

        return JsonSerializer.Serialize(envelope);
    }
}
