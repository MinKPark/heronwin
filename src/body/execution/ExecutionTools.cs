using System.ComponentModel;
using HeronWin.Body.DesktopAutomation;
using ModelContextProtocol.Server;

namespace HeronWin.Body.Execution;

[McpServerToolType]
public static class ExecutionTools
{
    [McpServerTool, Description("Activate a window and bring it to the foreground. Prefer windowHandle values returned by list_windows.")]
    public static async Task<string> ActivateWindow(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456. Preferred when available.")]
        string? windowHandle = null,
        [Description("Case-insensitive substring of the target window title. Used only when windowHandle is omitted.")]
        string? titleContains = null,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.SelectWindow(new WindowSelectionState(), windowHandle, titleContains),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Activate or start an app from the main Windows taskbar by activating one visible app button. Prefer elementPath values returned by list_taskbar_items.")]
    public static async Task<string> ActivateTaskbarApp(
        UiAutomationExecutor executor,
        [Description("Full element path returned by list_taskbar_items, such as 2/0/5. Preferred when available.")]
        string? elementPath = null,
        [Description("Case-insensitive substring match against the visible taskbar app button label. Used only when elementPath is omitted.")]
        string? titleContains = null,
        [Description("Case-insensitive substring match against the visible taskbar app button automation id. Useful for stable AppUserModelIds.")]
        string? automationIdContains = null,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.SelectTaskbarApp(new WindowSelectionState(), elementPath, titleContains, automationIdContains),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Open the Windows taskbar Search UI, type an app name into the search box, and press Enter to start the top result.")]
    public static async Task<string> LaunchApplication(
        UiAutomationExecutor executor,
        [Description("App name or search query to type into the taskbar search box.")]
        string appName,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.LaunchAppViaTaskbarSearch(new WindowSelectionState(), appName),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Focus a specific child element in a window using the slash-delimited path returned by describe_window, such as 0, 1/3, or 2/0/1.")]
    public static async Task<string> FocusWindowElement(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("Slash-delimited child path from describe_window. Use root to focus the window element itself.")]
        string elementPath,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.FocusSelectedWindowElement(CreateSelectionState(windowHandle), elementPath),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Click a specific child element in a window by its UI Automation path.")]
    public static async Task<string> ClickWindowElement(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("Slash-delimited child path from describe_window. When using describe_window_focus output, prefer the element's uiPath because path values there are relative to the focused subtree root. Use root to click the window itself.")]
        string elementPath,
        [Description("Mouse button to use. Supported values: left, right, primary, secondary.")]
        string mouseButton = "left",
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.ClickSelectedWindowElement(CreateSelectionState(windowHandle), elementPath, mouseButton),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Activate a specific child element in a window by its UI Automation path. The tool focuses the target directly when possible, otherwise it falls back to Tab and arrow-key navigation until the target element receives focus, then presses Enter.")]
    public static async Task<string> InvokeWindowElement(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("Slash-delimited child path from describe_window. When using describe_window_focus output, prefer the element's uiPath because path values there are relative to the focused subtree root. Use root to invoke the window itself.")]
        string elementPath,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.InvokeSelectedWindowElement(CreateSelectionState(windowHandle), elementPath),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Set or replace the text value for a specific editable child element in a window by its UI Automation path.")]
    public static async Task<string> SetWindowElementText(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("Slash-delimited child path from describe_window. When using describe_window_focus output, prefer the element's uiPath because path values there are relative to the focused subtree root.")]
        string elementPath,
        [Description("Text to set on the target editable element.")]
        string text,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.SetSelectedWindowElementValue(CreateSelectionState(windowHandle), elementPath, text),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Send a named key or shortcut to a window.")]
    public static async Task<string> PressWindowKey(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("Named key such as Enter, Escape, Tab, Up, Down, F5, A, or 1.")]
        string key,
        [Description("Optional modifier keys such as Control, Shift, Alt, or Win.")]
        string[]? modifiers = null,
        [Description("How many times to repeat the key press. Must be at least 1.")]
        int repeatCount = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.SendInputToWindow(CreateSelectionState(windowHandle), key, modifiers, text: null, repeatCount),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Type Unicode text into the currently focused control in a window.")]
    public static async Task<string> TypeWindowText(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("Unicode text to type into the currently focused control.")]
        string text,
        [Description("How many times to repeat the text input. Must be at least 1.")]
        int repeatCount = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.SendInputToWindow(CreateSelectionState(windowHandle), key: null, modifiers: null, text, repeatCount),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Invoke an item from a window's main menu by following a path like File > Open.")]
    public static async Task<string> InvokeWindowMainMenuItem(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("Menu path using > separators, for example File > Open or Help > About.")]
        string menuPath,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.InvokeMainMenuItem(CreateSelectionState(windowHandle), menuPath, windowHandle),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    [McpServerTool, Description("Open the context menu for the currently focused element in a window and invoke a menu path like Rename or Open with > Choose another app.")]
    public static async Task<string> InvokeWindowContextMenuItem(
        UiAutomationExecutor executor,
        [Description("Window handle from list_windows, such as 0x00123456.")]
        string windowHandle,
        [Description("Menu path using > separators, for example Rename or Open with > Choose another app.")]
        string menuPath,
        CancellationToken cancellationToken = default)
    {
        var result = await executor.RunAsync(
            () => WindowAutomation.InvokeContextMenuItem(CreateSelectionState(windowHandle), menuPath),
            cancellationToken);

        return WindowAutomation.Serialize(result);
    }

    private static WindowSelectionState CreateSelectionState(string windowHandle)
    {
        var selectionState = new WindowSelectionState();
        selectionState.SetSelectedHandle(ParseWindowHandle(windowHandle));
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
