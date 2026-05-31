using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using HeronWin.Brain;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaCompactTreeEvaluationTests
{
    [Fact]
    public async Task RunAsync_WritesCompactEvaluationArtifactsWithoutVisionVerdict()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = Path.Combine(tempRoot, "source");
            var outputDirectory = Path.Combine(tempRoot, "output");
            Directory.CreateDirectory(sourceDirectory);
            var compactImagePath = CreatePng(Path.Combine(sourceDirectory, "compact.png"), Color.SteelBlue);
            var focusImagePath = CreatePng(Path.Combine(sourceDirectory, "focus.png"), Color.SeaGreen);
            var screenshotPath = CreatePng(Path.Combine(sourceDirectory, "screenshot.png"), Color.DarkSlateGray);
            var calls = new List<(string ToolName, IReadOnlyDictionary<string, object?> Args)>();
            await using var manager = new McpClientManager(
                _ => Task.FromResult<IReadOnlyList<ToolDefinition>>([]),
                toolCallTimeoutOverride: null,
                callToolOverride: (toolName, args, _) =>
                {
                    calls.Add((toolName, new Dictionary<string, object?>(args, StringComparer.Ordinal)));
                    return Task.FromResult(new ToolCallOutcome(
                        BuildToolOutput(toolName, compactImagePath, focusImagePath, screenshotPath),
                        [],
                        IsError: false,
                        McpCallId: calls.Count));
                });

            var result = await AvaCompactTreeEvaluationRunner.RunAsync(
                new AvaCompactTreeEvaluationRequest(
                    "run-001",
                    "0x00010001",
                    outputDirectory,
                    RunVisionVerdict: false,
                    DebugMode: true),
                manager,
                evaluatorClient: null,
                CancellationToken.None);

            Assert.False(result.HasErrors);
            Assert.True(File.Exists(result.ReportPath));
            Assert.True(File.Exists(result.VerdictPath));
            Assert.Equal(AvaCompactTreeVisionVerdictStatus.NotRequested, result.Report.Verdict.Status);
            Assert.Equal(
                new[] { "describe_window", "describe_window_focus", "capture_window_screenshot" },
                calls.Select(static call => call.ToolName).ToArray());
            Assert.All(calls, call => Assert.Equal("0x00010001", call.Args["windowHandle"]));
            Assert.True((bool)calls[0].Args["includeImage"]!);
            Assert.True((bool)calls[0].Args["debugMode"]!);
            Assert.True((bool)calls[1].Args["includeImage"]!);
            Assert.True((bool)calls[1].Args["debugMode"]!);
            Assert.False(calls[2].Args.ContainsKey("includeImage"));

            Assert.True(File.Exists(Path.Combine(outputDirectory, "001-describe_window.json")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "002-describe_window_focus.json")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "003-capture_window_screenshot.json")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "artifacts", "001-describe_window-compact-window-render.png")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "artifacts", "002-describe_window_focus-compact-focus-render.png")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "artifacts", "003-capture_window_screenshot-real-screenshot.png")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_WritesVisionVerdict_WhenEvaluatorIsProvided()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = Path.Combine(tempRoot, "source");
            var outputDirectory = Path.Combine(tempRoot, "output");
            Directory.CreateDirectory(sourceDirectory);
            var compactImagePath = CreatePng(Path.Combine(sourceDirectory, "compact.png"), Color.SteelBlue);
            var focusImagePath = CreatePng(Path.Combine(sourceDirectory, "focus.png"), Color.SeaGreen);
            var screenshotPath = CreatePng(Path.Combine(sourceDirectory, "screenshot.png"), Color.DarkSlateGray);
            await using var manager = new McpClientManager(
                _ => Task.FromResult<IReadOnlyList<ToolDefinition>>([]),
                toolCallTimeoutOverride: null,
                callToolOverride: (toolName, _, _) => Task.FromResult(new ToolCallOutcome(
                    BuildToolOutput(toolName, compactImagePath, focusImagePath, screenshotPath),
                    [],
                    IsError: false,
                    McpCallId: 100)));
            var evaluator = new FakeVisionEvaluator();

            var result = await AvaCompactTreeEvaluationRunner.RunAsync(
                new AvaCompactTreeEvaluationRequest(
                    "run-001",
                    "0x00010001",
                    outputDirectory,
                    RunVisionVerdict: true,
                    DebugMode: false),
                manager,
                evaluator,
                CancellationToken.None);

            Assert.False(result.HasErrors);
            Assert.Equal(1, evaluator.CallCount);
            Assert.Equal(AvaCompactTreeVisionVerdictStatus.Captured, result.Report.Verdict.Status);
            Assert.True(result.Report.Verdict.SamePrimaryScreen);
            Assert.True(result.Report.Verdict.OverallMatch);
            Assert.Equal("high", result.Report.Verdict.Confidence);

            using var verdictDocument = JsonDocument.Parse(File.ReadAllText(result.VerdictPath));
            Assert.Equal("captured", verdictDocument.RootElement.GetProperty("status").GetString());
            Assert.True(verdictDocument.RootElement.GetProperty("overallMatch").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string BuildToolOutput(
        string toolName,
        string compactImagePath,
        string focusImagePath,
        string screenshotPath)
        => toolName switch
        {
            "describe_window" => JsonSerializer.Serialize(new
            {
                window = new { handle = "0x00010001", title = "Calculator" },
                sourceStats = new
                {
                    sourceNodeCount = 10,
                    keptNodeCount = 5,
                    omittedNodeCount = 5,
                    algorithmVersion = "compact-tree-v1"
                },
                compactTree = new { path = "root", uiPath = "root", controlType = "Window", name = "Calculator" },
                llmTree = new { uiPath = "root", controlType = "Window", name = "Calculator" },
                renderedImage = new
                {
                    imagePath = compactImagePath,
                    imageFormat = "png",
                    imageSize = new { width = 32, height = 24 }
                }
            }),
            "describe_window_focus" => JsonSerializer.Serialize(new
            {
                window = new { handle = "0x00010001", title = "Calculator" },
                sourceStats = new
                {
                    sourceNodeCount = 3,
                    keptNodeCount = 2,
                    omittedNodeCount = 1,
                    algorithmVersion = "compact-tree-v1"
                },
                compactTree = new { path = "focused", uiPath = "root/0", controlType = "Button", name = "Seven" },
                llmTree = new { uiPath = "root/0", controlType = "Button", name = "Seven" },
                renderedImage = new
                {
                    imagePath = focusImagePath,
                    imageFormat = "png",
                    imageSize = new { width = 32, height = 24 }
                }
            }),
            "capture_window_screenshot" => JsonSerializer.Serialize(new
            {
                window = new { handle = "0x00010001", title = "Calculator" },
                imagePath = screenshotPath,
                imageFormat = "png",
                imageSize = new { width = 32, height = 24 }
            }),
            _ => throw new InvalidOperationException($"Unexpected tool: {toolName}")
        };

    private static string CreatePng(string path, Color color)
    {
        using var bitmap = new Bitmap(32, 24);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ava-compact-tree-eval-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeVisionEvaluator : ILlmClient
    {
        public int CallCount { get; private set; }

        public LlmProviderId ProviderId => LlmProviderId.OpenAiApi;

        public string DisplayName => "fake vision evaluator";

        public LlmModelProfile ModelProfile { get; } = LlmModelProfiles.Create(LlmProviderId.OpenAiApi, "gpt-5.4-mini");

        public Task<ChatResult> ChatAsync(
            IReadOnlyList<AgentMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            string? systemPrompt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var visual = Assert.IsType<AgentMessage.VisualContext>(Assert.Single(messages));
            Assert.Equal(2, visual.Images.Count);
            Assert.Empty(tools);
            Assert.Contains("Return strict JSON", visual.Content, StringComparison.Ordinal);
            return Task.FromResult(new ChatResult(
                """
                {
                  "samePrimaryScreen": true,
                  "sameRecognizableTaskOrState": true,
                  "sameKeyText": true,
                  "sameKeyActionableControls": true,
                  "missingCriticalElements": [],
                  "hallucinatedElements": [],
                  "overallMatch": true,
                  "confidence": "high",
                  "notes": "The compact render preserves the key calculator surface."
                }
                """,
                []));
        }
    }
}
