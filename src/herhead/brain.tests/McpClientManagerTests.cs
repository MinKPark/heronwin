using System.Text.Json;
using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class McpClientManagerTests
{
    [Fact]
    public async Task ListAllToolsAsync_CachesResults_AfterFirstLoad()
    {
        var callCount = 0;
        var manager = new McpClientManager(_ =>
        {
            callCount++;
            return Task.FromResult<IReadOnlyList<ToolDefinition>>(
            [
                CreateToolDefinition("describe_window"),
                CreateToolDefinition("click_window_element")
            ]);
        }, null);

        var first = await manager.ListAllToolsAsync(CancellationToken.None);
        var second = await manager.ListAllToolsAsync(CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.Same(first, second);
        Assert.Collection(
            second,
            tool => Assert.Equal("describe_window", tool.Name),
            tool => Assert.Equal("click_window_element", tool.Name));
    }

    [Fact]
    public async Task ListAllToolsAsync_CachesEmptyResult_AfterFirstLoad()
    {
        var callCount = 0;
        var manager = new McpClientManager(_ =>
        {
            callCount++;
            return Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        }, null);

        var first = await manager.ListAllToolsAsync(CancellationToken.None);
        var second = await manager.ListAllToolsAsync(CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.Same(first, second);
        Assert.Empty(second);
    }

    [Fact]
    public async Task RunWithTimeoutAsync_ReturnsResult_WhenOperationCompletesInTime()
    {
        var actual = await McpClientManager.RunWithTimeoutAsync(
            async cancellationToken =>
            {
                await Task.Delay(10, cancellationToken);
                return "ok";
            },
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        Assert.Equal("ok", actual);
    }

    [Fact]
    public async Task RunWithTimeoutAsync_ThrowsTimeoutException_WhenOperationNeverCompletes()
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            McpClientManager.RunWithTimeoutAsync(
                _ => pending.Task,
                TimeSpan.FromMilliseconds(50),
                CancellationToken.None));

        Assert.Contains("50", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithTimeoutAsync_PreservesCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            McpClientManager.RunWithTimeoutAsync(
                async cancellationToken =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    return "never";
                },
                TimeSpan.FromSeconds(5),
                cancellationSource.Token));
    }

    [Fact]
    public void TryBuildUiAutomationDebugShadowRequest_ForCompactWindow_RequestsFullDepthRawTree()
    {
        var args = new Dictionary<string, object?>
        {
            ["windowHandle"] = "0x00123456",
            ["budgetHintChars"] = 7200,
            ["includeImage"] = true,
        };

        var actual = McpClientManager.TryBuildUiAutomationDebugShadowRequest(
            "describe_window_compact",
            args,
            out var shadowTool,
            out var shadowArgs);

        Assert.True(actual);
        Assert.Equal("describe_window", shadowTool);
        Assert.Equal("0x00123456", shadowArgs["windowHandle"]);
        Assert.Equal(true, shadowArgs["fullDepth"]);
        Assert.DoesNotContain("budgetHintChars", shadowArgs.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("includeImage", shadowArgs.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public void TryBuildUiAutomationDebugShadowRequest_ForCompactFocus_RequestsRawFocusTree()
    {
        var args = new Dictionary<string, object?>
        {
            ["windowHandle"] = "0x00123456",
            ["budgetHintChars"] = 3600,
            ["includeImage"] = false,
        };

        var actual = McpClientManager.TryBuildUiAutomationDebugShadowRequest(
            "describe_window_focus_compact",
            args,
            out var shadowTool,
            out var shadowArgs);

        Assert.True(actual);
        Assert.Equal("describe_window_focus", shadowTool);
        Assert.Equal("0x00123456", shadowArgs["windowHandle"]);
        Assert.Equal(4, shadowArgs["maxDepth"]);
        Assert.DoesNotContain("budgetHintChars", shadowArgs.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("includeImage", shadowArgs.Keys, StringComparer.Ordinal);
    }

    private static ToolDefinition CreateToolDefinition(string name)
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return new ToolDefinition(name, $"{name} description", document.RootElement.Clone());
    }
}
