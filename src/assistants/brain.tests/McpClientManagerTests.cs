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
    public async Task ListAllToolsAsync_IncludesBuiltInProcessTools_WhenNoOverrideIsUsed()
    {
        await using var manager = new McpClientManager();

        var tools = await manager.ListAllToolsAsync(CancellationToken.None);

        Assert.Contains(tools, tool => tool.Name == "list_processes");
        Assert.Contains(tools, tool => tool.Name == "start_process");
        Assert.Contains(tools, tool => tool.Name == "stop_process");
    }

    [Fact]
    public async Task CallToolAsync_ReturnsToolError_ForInvalidBuiltInStartProcessArguments()
    {
        await using var manager = new McpClientManager();

        var result = await manager.CallToolAsync(
            "start_process",
            new Dictionary<string, object?>(),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Failed to start process", result.Text, StringComparison.Ordinal);
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

    private static ToolDefinition CreateToolDefinition(string name)
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return new ToolDefinition(name, $"{name} description", document.RootElement.Clone());
    }
}
