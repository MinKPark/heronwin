using System.Text.Json;
using Xunit;

namespace HeronWin.HerFace.Tests;

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
                CreateToolDefinition("describe_selected_window"),
                CreateToolDefinition("click_selected_window_element")
            ]);
        });

        var first = await manager.ListAllToolsAsync(CancellationToken.None);
        var second = await manager.ListAllToolsAsync(CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.Same(first, second);
        Assert.Collection(
            second,
            tool => Assert.Equal("describe_selected_window", tool.Name),
            tool => Assert.Equal("click_selected_window_element", tool.Name));
    }

    [Fact]
    public async Task ListAllToolsAsync_CachesEmptyResult_AfterFirstLoad()
    {
        var callCount = 0;
        var manager = new McpClientManager(_ =>
        {
            callCount++;
            return Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        });

        var first = await manager.ListAllToolsAsync(CancellationToken.None);
        var second = await manager.ListAllToolsAsync(CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.Same(first, second);
        Assert.Empty(second);
    }

    private static ToolDefinition CreateToolDefinition(string name)
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return new ToolDefinition(name, $"{name} description", document.RootElement.Clone());
    }
}
