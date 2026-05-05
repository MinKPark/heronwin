using System.ComponentModel;
using HeronWin.Tools.DesktopAutomation;
using ModelContextProtocol.Server;

namespace HeronWin.Tools.Cognition;

[McpServerToolType]
public static class CognitionTools
{
    [McpServerTool, Description("List visible top-level windows on the current Windows desktop.")]
    public static async Task<string> ListWindows(
        UiAutomationExecutor executor,
        [Description("Optional current window handle to mark as selected in the returned list, such as 0x00123456.")]
        string? windowHandle = null,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.ListWindows(CreateSelectionState(windowHandle)),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("List visible items on the main Windows taskbar strip, such as Start, Search, and pinned or running app buttons.")]
    public static async Task<string> ListTaskbarItems(
        UiAutomationExecutor executor,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            WindowAutomation.ListTaskbarElements,
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Describe a window as a compact retained UI tree for model-facing use. The response includes source stats, a rich compactTree, a slim llmTree projection, optional rendered image metadata, and optional debug evidence.")]
    public static async Task<string> DescribeWindow(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("When true, render the compact tree to a local PNG and include image metadata in the response.")]
        bool includeImage = false,
        [Description("When true, also include debug-only evidence such as the full retained UI tree and a real screenshot artifact path.")]
        bool debugMode = false,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.DescribeSelectedWindow(CreateSelectionState(windowHandle), includeImage, debugMode),
            cancellationToken);

        return CompactUiSnapshotJson.Serialize(result);
    }

    [McpServerTool, Description("Capture a PNG screenshot of a window.")]
    public static async Task<string> CaptureWindowScreenshot(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.CaptureSelectedWindowScreenshot(CreateSelectionState(windowHandle)),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Describe the currently focused UI element inside a window as a compact retained UI tree for model-facing use. The response includes source stats, a rich compactTree, a slim llmTree projection, optional rendered image metadata, and optional debug evidence.")]
    public static async Task<string> DescribeWindowFocus(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("When true, render the compact tree to a local PNG and include image metadata in the response.")]
        bool includeImage = false,
        [Description("When true, also include debug-only evidence such as the full focused subtree and a real screenshot artifact path.")]
        bool debugMode = false,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.DescribeSelectedWindowFocus(CreateSelectionState(windowHandle), includeImage, debugMode),
            cancellationToken);

        return CompactUiSnapshotJson.Serialize(result);
    }

    [McpServerTool, Description("List a window's traditional main-menu sections and their immediate visible menu items.")]
    public static async Task<string> ListWindowMainMenuItems(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.ListMainMenuItems(CreateSelectionState(windowHandle), windowHandle),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Open the context menu for the currently focused element in a window and list the immediate visible menu items.")]
    public static async Task<string> ListWindowContextMenuItems(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.ListContextMenuItems(CreateSelectionState(windowHandle)),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    private static WindowSelectionState CreateSelectionState(string? windowHandle)
    {
        var selectionState = new WindowSelectionState();
        if (!string.IsNullOrWhiteSpace(windowHandle))
        {
            selectionState.SetSelectedHandle(ParseWindowHandle(windowHandle));
        }

        return selectionState;
    }

    private static nint ParseWindowHandle(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexValue))
            {
                return (nint)hexValue;
            }
        }
        else if (long.TryParse(trimmed, out var decimalValue))
        {
            return (nint)decimalValue;
        }

        throw new InvalidOperationException($"'{value}' is not a valid window handle.");
    }
}
