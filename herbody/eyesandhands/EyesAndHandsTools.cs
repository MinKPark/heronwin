using System.ComponentModel;
using ModelContextProtocol.Server;

namespace HeronWin.HerBody.EyesAndHands;

[McpServerToolType]
public static class EyesAndHandsTools
{
    [McpServerTool, Description("List visible top-level windows on the current Windows desktop.")]
    public static async Task<string> ListWindows(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        CancellationToken cancellationToken)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.ListWindows(selectionState),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("List visible elements on the main Windows taskbar strip, such as Start, Search, and pinned or running app buttons.")]
    public static async Task<string> ListTaskbarElements(
        UiAutomationExecutor executor,
        CancellationToken cancellationToken)
    {
        var result = await executor.RunAsync(
            WindowAutomation.ListTaskbarElements,
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Select or start an app from the main Windows taskbar by activating one visible app button. Prefer elementPath values returned by list_taskbar_elements.")]
    public static async Task<string> ActivateTaskbarApp(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("Full element path returned by list_taskbar_elements, such as 2/0/5. Preferred when available.")]
        string? elementPath = null,
        [Description("Case-insensitive substring match against the visible taskbar app button label. Used only when elementPath is omitted.")]
        string? titleContains = null,
        [Description("Case-insensitive substring match against the visible taskbar app button automation id. Useful for stable AppUserModelIds.")]
        string? automationIdContains = null,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.ActivateTaskbarApp(selectionState, elementPath, titleContains, automationIdContains),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Open the Windows taskbar Search UI, type an app name into the search box, and press Enter to start the top result.")]
    public static async Task<string> SearchTaskbarApp(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("App name or search query to type into the taskbar search box.")]
        string appName,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.SearchTaskbarApp(selectionState, appName),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Select a window and bring it to the foreground. Prefer windowHandle values returned by list_windows.")]
    public static async Task<string> SelectWindow(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("Window handle from list_windows, such as 0x00123456. Preferred when available.")]
        string? windowHandle = null,
        [Description("Case-insensitive substring of the target window title. Used only when windowHandle is omitted.")]
        string? titleContains = null,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.SelectWindow(selectionState, windowHandle, titleContains),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Describe the selected window as a structured UI Automation tree. If a window has been selected, the server focuses it before inspection; otherwise it falls back to the current foreground window. maxDepth includes the root level and must be between 1 and 4 when fullDepth is false.")]
    public static async Task<string> DescribeActiveWindow(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("How many UI tree levels to include, counting the window root as level 1. Must be between 1 and 4 when fullDepth is false.")]
        int maxDepth = 2,
        [Description("When true, return the full available UI Automation tree without a depth cap. This can produce a large payload.")]
        bool fullDepth = false,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.DescribeActiveWindow(selectionState, maxDepth, fullDepth),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Capture a PNG screenshot of the selected window. If no window has been selected, the server falls back to the current foreground window.")]
    public static async Task<string> CaptureActiveWindowScreenshot(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.CaptureActiveWindowScreenshot(selectionState),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Focus a specific child element in the selected window using the slash-delimited path returned by describe_active_window, such as 0, 1/3, or 2/0/1. If a window has been selected, the server focuses it before attempting the element focus.")]
    public static async Task<string> FocusActiveWindowElement(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("Slash-delimited child path from describe_active_window. Use root to focus the window element itself.")]
        string elementPath,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.FocusActiveWindowElement(selectionState, elementPath),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Describe the currently focused UI element inside the selected window as a structured UI Automation tree. If a window has been selected, the server focuses it before inspection; otherwise it falls back to the current foreground window. maxDepth includes the focused element as level 1 and must be between 1 and 4.")]
    public static async Task<string> DescribeFocusedElement(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("How many UI tree levels to include, counting the focused element as level 1. Must be between 1 and 4.")]
        int maxDepth = 2,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.DescribeFocusedElement(selectionState, maxDepth),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("List the selected window's traditional main-menu sections and their immediate visible menu items. If windowHandle is omitted, the currently selected window is used.")]
    public static async Task<string> ListMainMenuItems(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("Optional window handle from list_windows. If omitted, the currently selected window is used.")]
        string? windowHandle = null,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.ListMainMenuItems(selectionState, windowHandle),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Open or invoke an item from the selected window's main menu by following a path like File > Open.")]
    public static async Task<string> InvokeMainMenuItem(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("Menu path using > separators, for example File > Open or Help > About.")]
        string menuPath,
        [Description("Optional window handle from list_windows. If omitted, the currently selected window is used.")]
        string? windowHandle = null,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.InvokeMainMenuItem(selectionState, menuPath, windowHandle),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Open the context menu for the currently focused element in the selected window and list the immediate visible menu items.")]
    public static async Task<string> ListContextMenuItems(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.ListContextMenuItems(selectionState),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Open the context menu for the currently focused element in the selected window and invoke a menu path like Rename or Open with > Choose another app.")]
    public static async Task<string> InvokeContextMenuItem(
        UiAutomationExecutor executor,
        WindowSelectionState selectionState,
        [Description("Menu path using > separators, for example Rename or Open with > Choose another app.")]
        string menuPath,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.InvokeContextMenuItem(selectionState, menuPath),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }
}
