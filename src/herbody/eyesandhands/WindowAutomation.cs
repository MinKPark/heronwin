using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Imaging;

namespace HeronWin.HerBody.EyesAndHands;

internal static class WindowAutomation
{
    private const int MaxBoundedUiDepth = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new UiElementSnapshotJsonConverter() },
    };

    private static readonly Condition MenuBarCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar);

    private static readonly Condition MenuCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu);

    private static readonly Condition MenuItemCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem);

    private static readonly HashSet<string> TransparentBoundedTreeClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BrowserRootView",
        "BrowserView",
        "BrowserFrameViewWin",
        "MultiContentsView",
        "NonClientView",
        "RootView",
        "SidebarContentsSplitView",
        "TopContainerOverlayView",
        "TopContainerView",
        "View",
        "WebAppFrameToolbarView",
        "WebAppToolbarButtonContainer",
    };

    private static readonly HashSet<string> TransparentBoundedTreeAutomationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "appMountPoint",
    };

    private static readonly IReadOnlyDictionary<string, ushort> ModifierVirtualKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = NativeMethods.VkControl,
            ["control"] = NativeMethods.VkControl,
            ["shift"] = NativeMethods.VkShift,
            ["alt"] = NativeMethods.VkAlt,
            ["win"] = NativeMethods.VkLWin,
            ["windows"] = NativeMethods.VkLWin,
            ["meta"] = NativeMethods.VkLWin,
        };

    private static readonly string[] TaskbarHostAutomationIds = ["TaskbarFrame"];
    private static readonly string[] TaskbarHostClassNames =
    [
        "Taskbar.TaskbarFrameAutomationPeer",
        "MSTaskSwWClass",
        "MSTaskListWClass",
    ];
    private const string SearchButtonAutomationId = "SearchButton";

    private static readonly TimeSpan MenuSearchTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MenuSearchInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan LaunchSelectionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LaunchSelectionInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TaskbarSearchTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TaskbarSearchInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan UiSettleInitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan UiSettlePollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan UiSettleTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan UiSettleWaitSlice = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan KeyboardNavigationStepDelay = TimeSpan.FromMilliseconds(120);
    private const string DebugArtifactDirectoryEnvironmentVariable = "HERFACE_DEBUG_ARTIFACT_DIR";
    private static readonly string DefaultScreenshotDirectory = Path.Combine(
        Path.GetTempPath(),
        "heronwin",
        "eyesandhands");
    private const int MaxKeyboardNavigationSteps = 48;

    internal static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    internal static WindowListResult ListWindows(WindowSelectionState selectionState)
    {
        var selectedHandle = selectionState.GetSelectedHandle();
        var windows = new List<WindowSummary>();

        _ = NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = NativeMethods.GetWindowText(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
            if (!NativeMethods.GetWindowRect(hWnd, out var rect))
            {
                return true;
            }

            windows.Add(new WindowSummary(
                FormatHandle(hWnd),
                title,
                NativeMethods.GetClassName(hWnd),
                unchecked((int)processId),
                new WindowBounds(
                    rect.Left,
                    rect.Top,
                    rect.Right - rect.Left,
                    rect.Bottom - rect.Top),
                selectedHandle.HasValue && selectedHandle.Value == hWnd));

            return true;
        }, nint.Zero);

        windows.Sort((left, right) =>
        {
            var selectedCompare = right.IsSelected.CompareTo(left.IsSelected);
            if (selectedCompare != 0)
            {
                return selectedCompare;
            }

            var titleCompare = string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
            if (titleCompare != 0)
            {
                return titleCompare;
            }

            return string.Compare(left.Handle, right.Handle, StringComparison.OrdinalIgnoreCase);
        });

        return new WindowListResult(
            selectedHandle is null ? null : FormatHandle(selectedHandle.Value),
            windows);
    }

    internal static WindowSelectionResult SelectWindow(
        WindowSelectionState selectionState,
        string? windowHandle,
        string? titleContains)
    {
        var stopwatch = Stopwatch.StartNew();
        var previousSelectedHandle = selectionState.GetSelectedHandle();
        var foregroundBefore = NativeMethods.GetForegroundWindow();
        var handle = ResolveWindowHandle(windowHandle, titleContains);
        EnsureWindowExists(handle);
        using var settleObserver = UiSettleObserver.TryCreate(handle);
        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);
        var uiSettle = WaitForUiToSettle(handle, settleObserver);
        var result = BuildSelectionResult(handle, wasFocused: true, uiSettle);
        LogTrace(
            "select_window.complete",
            ("requestedHandle", windowHandle),
            ("requestedTitleContains", titleContains),
            ("previousSelectedHandle", FormatHandleOrNone(previousSelectedHandle)),
            ("foregroundBefore", FormatHandleOrNone(foregroundBefore)),
            ("foregroundAfter", FormatHandleOrNone(NativeMethods.GetForegroundWindow())),
            ("selectedWindow", DescribeSelectionResult(result)),
            ("uiSettleStatus", uiSettle.Status),
            ("uiSettleElapsedMs", uiSettle.ElapsedMilliseconds),
            ("elapsedMs", RoundElapsedMilliseconds(stopwatch.Elapsed)));

        return result;
    }

    internal static TaskbarElementListResult ListTaskbarElements()
    {
        var (taskbarHandle, taskbarRoot) = GetTaskbarRootElement();
        var (hostElement, hostPath) = FindMainTaskbarHost(taskbarRoot);
        var visibleElements = EnumerateVisibleTaskbarChildren(hostElement, hostPath);

        return new TaskbarElementListResult(
            BuildWindowDescriptor(taskbarHandle),
            BuildElementSnapshot(hostElement, hostPath, [], includeChildren: false),
            visibleElements);
    }

    internal static TaskbarAppActivationResult SelectTaskbarApp(
        WindowSelectionState selectionState,
        string? elementPath,
        string? titleContains,
        string? automationIdContains)
    {
        if (string.IsNullOrWhiteSpace(elementPath) &&
            string.IsNullOrWhiteSpace(titleContains) &&
            string.IsNullOrWhiteSpace(automationIdContains))
        {
            throw new InvalidOperationException(
                "Provide elementPath, titleContains, or automationIdContains to target a taskbar app.");
        }

        var stopwatch = Stopwatch.StartNew();
        var (taskbarHandle, taskbarRoot) = GetTaskbarRootElement();
        var (hostElement, hostPath) = FindMainTaskbarHost(taskbarRoot);
        var visibleApps = EnumerateVisibleTaskbarChildren(hostElement, hostPath)
            .Where(static element => element.IsAppButton)
            .ToArray();

        if (visibleApps.Length == 0)
        {
            throw new InvalidOperationException("No visible taskbar app buttons are currently available.");
        }

        var target = ResolveTaskbarAppTarget(visibleApps, elementPath, titleContains, automationIdContains);
        var foregroundBefore = NativeMethods.GetForegroundWindow();
        FocusWindow(taskbarHandle);

        var targetElement = ResolveElementPath(taskbarRoot, target.Path);
        var actionTaken = ActivateTaskbarElement(targetElement);
        var selectedWindow = TrySelectForegroundWindowAfterLaunch(selectionState, [taskbarHandle]);
        var uiSettleHandle = selectedWindow is null ? taskbarHandle : ParseHandle(selectedWindow.Handle);
        var uiSettle = WaitForUiToSettle(uiSettleHandle);
        var result = new TaskbarAppActivationResult(
            BuildWindowDescriptor(taskbarHandle),
            BuildElementSnapshot(hostElement, hostPath, [], includeChildren: false),
            BuildTaskbarElementSummary(targetElement, target.Path),
            actionTaken,
            selectedWindow,
            uiSettle);

        LogTrace(
            "select_taskbar_app.complete",
            ("visibleApps", visibleApps.Length),
            ("target", DescribeTaskbarElement(target)),
            ("foregroundBefore", FormatHandleOrNone(foregroundBefore)),
            ("foregroundAfter", FormatHandleOrNone(NativeMethods.GetForegroundWindow())),
            ("actionTaken", actionTaken),
            ("selectedWindow", DescribeSelectionResult(selectedWindow)),
            ("uiSettleStatus", uiSettle.Status),
            ("uiSettleElapsedMs", uiSettle.ElapsedMilliseconds),
            ("elapsedMs", RoundElapsedMilliseconds(stopwatch.Elapsed)));

        return result;
    }

    internal static TaskbarAppSearchResult LaunchAppViaTaskbarSearch(
        WindowSelectionState selectionState,
        string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new InvalidOperationException("appName is required.");
        }

        var stopwatch = Stopwatch.StartNew();
        var normalizedQuery = appName.Trim();
        var (taskbarHandle, taskbarRoot) = GetTaskbarRootElement();
        var (hostElement, hostPath) = FindMainTaskbarHost(taskbarRoot);
        var visibleElements = EnumerateVisibleTaskbarChildren(hostElement, hostPath);
        var searchTarget = ResolveTaskbarSearchTarget(visibleElements);

        var foregroundBefore = NativeMethods.GetForegroundWindow();
        FocusWindow(taskbarHandle);

        var searchElement = ResolveElementPath(taskbarRoot, searchTarget.Path);
        var searchActionTaken = OpenTaskbarSearch(searchElement);
        var inputElement = WaitForSearchInputElement();
        if (inputElement is null)
        {
            PressWindowsSearchShortcut();
            searchActionTaken = $"{searchActionTaken}_then_pressed_win_s";
            inputElement = WaitForSearchInputElement()
                ?? throw new InvalidOperationException(
                    "Opened Windows Search but could not find a search input element to type into.");
        }

        var textEntryActionTaken = EnterTextIntoSearchInput(inputElement, normalizedQuery);
        Thread.Sleep(150);
        var searchWindowHandle = NativeMethods.GetForegroundWindow();
        PressEnterKey();
        Thread.Sleep(150);
        var selectedWindow = TrySelectForegroundWindowAfterLaunch(selectionState, [taskbarHandle, searchWindowHandle]);
        var uiSettleHandle = selectedWindow is null ? taskbarHandle : ParseHandle(selectedWindow.Handle);
        var uiSettle = WaitForUiToSettle(uiSettleHandle);
        var result = new TaskbarAppSearchResult(
            BuildWindowDescriptor(taskbarHandle),
            BuildElementSnapshot(hostElement, hostPath, [], includeChildren: false),
            BuildTaskbarElementSummary(searchElement, searchTarget.Path),
            BuildElementSnapshot(inputElement, "focused", [], includeChildren: false),
            normalizedQuery,
            searchActionTaken,
            textEntryActionTaken,
            "pressed_enter",
            selectedWindow,
            uiSettle);

        LogTrace(
            "launch_app_via_taskbar_search.complete",
            ("query", normalizedQuery),
            ("visibleTaskbarElements", visibleElements.Count),
            ("searchTarget", DescribeTaskbarElement(searchTarget)),
            ("foregroundBefore", FormatHandleOrNone(foregroundBefore)),
            ("searchWindowHandle", FormatHandleOrNone(searchWindowHandle)),
            ("foregroundAfter", FormatHandleOrNone(NativeMethods.GetForegroundWindow())),
            ("searchActionTaken", searchActionTaken),
            ("textEntryActionTaken", textEntryActionTaken),
            ("launchActionTaken", "pressed_enter"),
            ("selectedWindow", DescribeSelectionResult(selectedWindow)),
            ("uiSettleStatus", uiSettle.Status),
            ("uiSettleElapsedMs", uiSettle.ElapsedMilliseconds),
            ("elapsedMs", RoundElapsedMilliseconds(stopwatch.Elapsed)));

        return result;
    }

    internal static WindowTreeResult DescribeSelectedWindow(
        WindowSelectionState selectionState,
        int maxDepth,
        bool fullDepth)
    {
        int? normalizedDepth = fullDepth ? null : NormalizeDepth(maxDepth);
        var handle = ResolveInteractionWindowHandle(selectionState);

        var windowElement = AutomationElement.FromHandle(handle);
        return new WindowTreeResult(
            BuildWindowDescriptor(handle),
            normalizedDepth,
            fullDepth,
            CaptureElementTree(windowElement, normalizedDepth, "root", "root"));
    }

    internal static WindowScreenshotResult CaptureSelectedWindowScreenshot(WindowSelectionState selectionState)
    {
        var handle = ResolveInteractionWindowHandle(selectionState);
        EnsureWindowExists(handle);
        FocusWindow(handle);

        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            throw new InvalidOperationException("Could not determine the bounds of the active window.");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The active window does not have visible bounds to capture.");
        }

        var screenshotDirectory = GetScreenshotDirectory();
        Directory.CreateDirectory(screenshotDirectory);
        var filePath = Path.Combine(
            screenshotDirectory,
            BuildScreenshotFileName(handle, NativeMethods.GetWindowText(handle)));

        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
        }

        bitmap.Save(filePath, ImageFormat.Png);

        return new WindowScreenshotResult(
            BuildWindowDescriptor(handle),
            filePath,
            "png",
            new ImageDimensions(width, height));
    }

    internal static FocusedElementResult FocusSelectedWindowElement(
        WindowSelectionState selectionState,
        string elementPath)
    {
        var handle = ResolveInteractionWindowHandle(selectionState);
        using var settleObserver = UiSettleObserver.TryCreate(handle);

        var normalizedPath = NormalizeElementPath(elementPath);
        var windowElement = AutomationElement.FromHandle(handle);
        var targetElement = ResolveElementPath(windowElement, normalizedPath);
        var focusedTarget = FocusAutomationElementOrDescendant(targetElement, normalizedPath);
        var uiSettle = WaitForUiToSettle(handle, settleObserver);

        return new FocusedElementResult(
            BuildWindowDescriptor(handle),
            BuildElementSnapshot(focusedTarget.Element, focusedTarget.Path, [], includeChildren: false),
            focusedTarget.ActionTaken,
            uiSettle);
    }

    internal static ElementClickResult ClickSelectedWindowElement(
        WindowSelectionState selectionState,
        string elementPath,
        string mouseButton)
    {
        var handle = ResolveInteractionWindowHandle(selectionState);
        using var settleObserver = UiSettleObserver.TryCreate(handle);
        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);

        var normalizedPath = NormalizeElementPath(elementPath);
        var normalizedButton = NormalizeMouseButton(mouseButton);
        var windowElement = AutomationElement.FromHandle(handle);
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            throw new InvalidOperationException("Could not determine the bounds of the selected window.");
        }

        var windowBounds = new WindowBounds(
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top);
        var targetElement = ResolveElementPath(windowElement, normalizedPath);
        var clickableTarget = ResolveClickableElementOrDescendant(targetElement, normalizedPath, windowBounds);

        ClickAtScreenPoint(clickableTarget.ClickPoint, normalizedButton);
        var uiSettle = WaitForUiToSettle(handle, settleObserver);

        return new ElementClickResult(
            BuildWindowDescriptor(handle),
            BuildElementSnapshot(clickableTarget.Element, clickableTarget.Path, [], includeChildren: false),
            normalizedButton,
            clickableTarget.ClickPoint,
            clickableTarget.PreparationActionTaken,
            $"{normalizedButton}_clicked",
            uiSettle);
    }

    internal static ElementInvocationResult InvokeSelectedWindowElement(
        WindowSelectionState selectionState,
        string elementPath)
    {
        var handle = ResolveInteractionWindowHandle(selectionState);
        using var settleObserver = UiSettleObserver.TryCreate(handle);
        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);

        var normalizedPath = NormalizeElementPath(elementPath);
        var windowElement = AutomationElement.FromHandle(handle);
        var targetElement = ResolveElementPath(windowElement, normalizedPath);
        var directWindowClose = TryCloseAutomationWindowDirectly(targetElement, normalizedPath);
        if (directWindowClose is not null)
        {
            var directWindowCloseUiSettle = WaitForUiToSettle(handle, settleObserver);
            return new ElementInvocationResult(
                BuildWindowDescriptor(handle),
                BuildElementSnapshot(directWindowClose.Value.Element, directWindowClose.Value.Path, [], includeChildren: false),
                "window_close",
                [],
                0,
                directWindowClose.Value.ActionTaken,
                directWindowCloseUiSettle);
        }

        var directInvocation = TryInvokeAutomationElementOrDescendant(targetElement, normalizedPath);
        if (directInvocation is not null)
        {
            var directUiSettle = WaitForUiToSettle(handle, settleObserver);
            if (!ShouldRetryInvocationWithKeyboard(windowElement, targetElement, directUiSettle))
            {
                return new ElementInvocationResult(
                    BuildWindowDescriptor(handle),
                    BuildElementSnapshot(directInvocation.Value.Element, directInvocation.Value.Path, [], includeChildren: false),
                    "direct_activation",
                    [],
                    0,
                    directInvocation.Value.ActionTaken,
                    directUiSettle);
            }
        }

        if (NativeMethods.GetWindowRect(handle, out var rect))
        {
            var windowBounds = new WindowBounds(
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top);
            var mouseClickInvocation = TryInvokeAutomationElementByMouseClick(targetElement, normalizedPath, windowBounds);
            if (mouseClickInvocation is not null)
            {
                var mouseClickUiSettle = WaitForUiToSettle(handle, settleObserver);
                if (!ShouldRetryInvocationWithKeyboard(windowElement, targetElement, mouseClickUiSettle))
                {
                    return new ElementInvocationResult(
                        BuildWindowDescriptor(handle),
                        BuildElementSnapshot(mouseClickInvocation.Value.Element, mouseClickInvocation.Value.Path, [], includeChildren: false),
                        "mouse_click",
                        [],
                        0,
                        mouseClickInvocation.Value.ActionTaken,
                        mouseClickUiSettle);
                }
            }
        }

        var invocationTarget = InvokeElementViaKeyboardNavigation(handle, windowElement, targetElement, normalizedPath);
        var uiSettle = WaitForUiToSettle(handle, settleObserver);

        return new ElementInvocationResult(
            BuildWindowDescriptor(handle),
            BuildElementSnapshot(invocationTarget.Element, invocationTarget.Path, [], includeChildren: false),
            "keyboard_navigation",
            invocationTarget.NavigationKeys,
            invocationTarget.NavigationKeys.Count,
            invocationTarget.ActionTaken,
            uiSettle);
    }

    internal static ElementValueSetResult SetSelectedWindowElementValue(
        WindowSelectionState selectionState,
        string elementPath,
        string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var handle = ResolveInteractionWindowHandle(selectionState);
        using var settleObserver = UiSettleObserver.TryCreate(handle);
        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);

        var normalizedPath = NormalizeElementPath(elementPath);
        var windowElement = AutomationElement.FromHandle(handle);
        var targetElement = ResolveElementPath(windowElement, normalizedPath);
        var textEntryTarget = ResolveTextEntryElementOrDescendant(targetElement, normalizedPath);
        var actionTaken = EnterTextIntoSearchInput(textEntryTarget.Element, value);
        var uiSettle = WaitForUiToSettle(handle, settleObserver);

        return new ElementValueSetResult(
            BuildWindowDescriptor(handle),
            BuildElementSnapshot(textEntryTarget.Element, textEntryTarget.Path, [], includeChildren: false),
            value.Length,
            actionTaken,
            uiSettle);
    }

    internal static KeyboardInputResult SendInputToWindow(
        WindowSelectionState selectionState,
        string? key,
        IReadOnlyList<string>? modifiers,
        string? text,
        int repeatCount)
    {
        if (repeatCount < 1)
        {
            throw new InvalidOperationException("repeatCount must be at least 1.");
        }

        var hasKey = key is not null;
        var hasText = text is not null;
        if (hasKey == hasText)
        {
            throw new InvalidOperationException("Provide exactly one of key or text.");
        }

        if (hasKey && string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("key must not be empty.");
        }

        if (hasText && text!.Length == 0)
        {
            throw new InvalidOperationException("text must not be empty.");
        }

        var handle = ResolveKeyboardInputWindowHandle(selectionState);
        using var settleObserver = UiSettleObserver.TryCreate(handle);
        ActivateWindow(handle);
        Thread.Sleep(150);
        selectionState.SetSelectedHandle(handle);

        if (hasText)
        {
            var normalizedText = text!;
            for (var i = 0; i < repeatCount; i++)
            {
                SendUnicodeText(normalizedText, "Failed to send Unicode text to the focused control.");
            }

            var uiSettle = WaitForUiToSettle(handle, settleObserver);

            return new KeyboardInputResult(
                BuildWindowDescriptor(handle),
                "text",
                null,
                [],
                repeatCount,
                normalizedText.Length,
                "typed_text",
                uiSettle);
        }

        var normalizedModifiers = NormalizeModifierNames(modifiers);
        var modifierVirtualKeys = ResolveModifierVirtualKeys(normalizedModifiers);
        var normalizedKey = key!.Trim();
        var keyVirtualKey = ResolveVirtualKey(normalizedKey);

        for (var i = 0; i < repeatCount; i++)
        {
            PressKeyWithModifiers(
                keyVirtualKey,
                modifierVirtualKeys,
                $"Failed to send key '{normalizedKey}' to the selected window.");
        }

        var settledUi = WaitForUiToSettle(handle, settleObserver);

        return new KeyboardInputResult(
            BuildWindowDescriptor(handle),
            "key",
            normalizedKey,
            normalizedModifiers,
            repeatCount,
            null,
            modifierVirtualKeys.Count > 0 ? "pressed_modified_key" : "pressed_key",
            settledUi);
    }

    internal static FocusedElementTreeResult DescribeSelectedWindowFocus(
        WindowSelectionState selectionState,
        int maxDepth)
    {
        var normalizedDepth = NormalizeDepth(maxDepth);
        var handle = ResolveInteractionWindowHandle(selectionState);

        var windowElement = AutomationElement.FromHandle(handle);
        var focusedElement = AutomationElement.FocusedElement
            ?? throw new InvalidOperationException("No focused UI element is currently available.");

        if (!IsSameOrDescendantOf(focusedElement, windowElement))
        {
            throw new InvalidOperationException(
                "The currently focused UI element does not belong to the selected window.");
        }

        var focusedElementUiPath = TryFindElementPath(windowElement, focusedElement, "root")
            ?? throw new InvalidOperationException(
                "Could not resolve the focused UI element's path within the selected window.");

        return new FocusedElementTreeResult(
            BuildWindowDescriptor(handle),
            normalizedDepth,
            CaptureElementTree(focusedElement, normalizedDepth, "focused", focusedElementUiPath));
    }

    internal static MainMenuListResult ListMainMenuItems(
        WindowSelectionState selectionState,
        string? windowHandle)
    {
        var handle = ResolveWindowHandle(windowHandle, null, selectionState);
        EnsureWindowExists(handle);
        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);

        var windowElement = AutomationElement.FromHandle(handle);
        var menuBar = FindMainMenuBar(windowElement);
        var processId = GetProcessId(handle);
        if (FindVisiblePopupMenu(processId) is not null)
        {
            DismissOpenMenus();
            FocusWindow(handle);
        }

        var topLevelLabels = GetTopLevelMenuItems(menuBar)
            .Select(GetElementName)
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .ToArray();
        var menus = new List<MainMenuSection>(topLevelLabels.Length);

        foreach (var label in topLevelLabels)
        {
            var currentTopLevelItem = FindBestMatch(GetTopLevelMenuItems(menuBar), label)
                ?? throw new InvalidOperationException($"Could not find the top-level menu item '{label}'.");

            ExpandMenu(currentTopLevelItem);
            var items = GetVisiblePopupMenuItems(processId, label);
            menus.Add(new MainMenuSection(
                label,
                label,
                items));
            DismissOpenMenus();
            FocusWindow(handle);
        }

        DismissOpenMenus();

        return new MainMenuListResult(
            BuildWindowDescriptor(handle),
            BuildElementSnapshot(menuBar, "menu_bar", [], includeChildren: false),
            menus);
    }

    internal static MenuInvocationResult InvokeMainMenuItem(
        WindowSelectionState selectionState,
        string menuPath,
        string? windowHandle)
    {
        if (string.IsNullOrWhiteSpace(menuPath))
        {
            throw new InvalidOperationException("menuPath is required.");
        }

        var handle = ResolveWindowHandle(windowHandle, null, selectionState);
        EnsureWindowExists(handle);
        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);
        using var settleObserver = UiSettleObserver.TryCreate(handle);

        var windowElement = AutomationElement.FromHandle(handle);
        var menuBar = FindMainMenuBar(windowElement);
        var segments = menuPath.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("menuPath must contain at least one menu item.");
        }

        var topLevelItem =
            FindBestMatch(GetTopLevelMenuItems(menuBar), segments[0]);

        if (topLevelItem is null)
        {
            throw new InvalidOperationException($"Could not find the top-level menu item '{segments[0]}'.");
        }

        if (segments.Length == 1)
        {
            var topLevelActionTaken = InvokeAction(topLevelItem, allowExpandOnly: true);
            var uiSettle = WaitForUiToSettle(handle, settleObserver);
            return BuildMenuInvocationResult(handle, menuPath, topLevelActionTaken, uiSettle);
        }

        ExpandMenu(topLevelItem);
        var actionTaken = InvokeVisibleMenuPath(handle, string.Join(" > ", segments.Skip(1)));
        var settledUi = WaitForUiToSettle(handle, settleObserver);

        return BuildMenuInvocationResult(handle, menuPath, actionTaken, settledUi);
    }

    internal static ContextMenuListResult ListContextMenuItems(WindowSelectionState selectionState)
    {
        var (handle, _, focusedElement) = ResolveFocusedElementForContextMenu(selectionState);
        var openActionTaken = OpenContextMenu(focusedElement, GetProcessId(handle));
        var items = GetVisiblePopupMenuItems(GetProcessId(handle), null);
        DismissOpenMenus();

        return new ContextMenuListResult(
            BuildWindowDescriptor(handle),
            BuildElementSnapshot(focusedElement, "focused", [], includeChildren: false),
            openActionTaken,
            items);
    }

    internal static ContextMenuInvocationResult InvokeContextMenuItem(
        WindowSelectionState selectionState,
        string menuPath)
    {
        if (string.IsNullOrWhiteSpace(menuPath))
        {
            throw new InvalidOperationException("menuPath is required.");
        }

        var (handle, _, focusedElement) = ResolveFocusedElementForContextMenu(selectionState);
        using var settleObserver = UiSettleObserver.TryCreate(handle);
        var openActionTaken = OpenContextMenu(focusedElement, GetProcessId(handle));
        var actionTaken = InvokeVisibleMenuPath(handle, menuPath);
        var uiSettle = WaitForUiToSettle(handle, settleObserver);

        return new ContextMenuInvocationResult(
            BuildSelectionResult(handle, wasFocused: true).Handle,
            NativeMethods.GetWindowText(handle),
            GetProcessId(handle),
            menuPath,
            openActionTaken,
            actionTaken,
            uiSettle);
    }

    private static WindowSelectionResult BuildSelectionResult(
        nint handle,
        bool wasFocused,
        UiSettleResult? uiSettle = null)
    {
        _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);

        return new WindowSelectionResult(
            FormatHandle(handle),
            NativeMethods.GetWindowText(handle),
            NativeMethods.GetClassName(handle),
            unchecked((int)processId),
            wasFocused,
            uiSettle);
    }

    private static WindowDescriptor BuildWindowDescriptor(nint handle)
    {
        _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        _ = NativeMethods.GetWindowRect(handle, out var rect);

        return new WindowDescriptor(
            FormatHandle(handle),
            NativeMethods.GetWindowText(handle),
            NativeMethods.GetClassName(handle),
            unchecked((int)processId),
            new WindowBounds(
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top));
    }

    private static void LogTrace(string category, params (string Key, object? Value)[] fields)
    {
        if (!DebugTrace.IsEnabled)
        {
            return;
        }

        var parts = fields
            .Where(field => field.Value is not null)
            .Select(field => $"{field.Key}={FormatTraceValue(field.Value!)}");

        DebugTrace.WriteLine($"{category} {string.Join(" ", parts)}".TrimEnd());
    }

    private static string FormatTraceValue(object value)
    {
        return value switch
        {
            string text when text.Length == 0 => "\"\"",
            string text => $"\"{text.Replace("\"", "'", StringComparison.Ordinal)}\"",
            bool boolValue => boolValue ? "true" : "false",
            nint handle => FormatHandle(handle),
            IEnumerable<string> values => string.Join(",", values),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "\"\"",
        };
    }

    private static string FormatHandleOrNone(nint? handle)
        => handle.HasValue && handle.Value != nint.Zero ? FormatHandle(handle.Value) : "none";

    private static string DescribeSelectionResult(WindowSelectionResult? result)
        => result is null ? "none" : $"{result.Handle} ({result.Title})";

    private static string DescribeTaskbarElement(TaskbarElementSummary element)
        => $"{element.Path} ({element.Name}; automationId={element.AutomationId}; className={element.ClassName})";

    private static int RoundElapsedMilliseconds(TimeSpan elapsed)
        => (int)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero);

    private static UiElementSnapshot CaptureElementTree(
        AutomationElement element,
        int? remainingLevels,
        string path,
        string uiPath)
    {
        var children = CaptureChildSnapshots(element, remainingLevels, path, uiPath);

        return BuildElementSnapshot(element, path, children, includeChildren: true, uiPath);
    }

    private static List<UiElementSnapshot> CaptureChildSnapshots(
        AutomationElement element,
        int? remainingLevels,
        string path,
        string uiPath)
    {
        var children = new List<UiElementSnapshot>();
        if (remainingLevels.HasValue && remainingLevels.Value <= 1)
        {
            return children;
        }

        var childElements = element.FindAll(TreeScope.Children, Condition.TrueCondition);
        int? nextRemainingLevels = remainingLevels.HasValue ? remainingLevels.Value - 1 : null;
        for (var i = 0; i < childElements.Count; i++)
        {
            var childPath = BuildChildPath(path, i);
            var childUiPath = BuildChildPath(uiPath, i);
            if (nextRemainingLevels.HasValue)
            {
                AppendBoundedChildSnapshot(children, childElements[i], childPath, childUiPath, nextRemainingLevels.Value);
                continue;
            }

            children.Add(CaptureElementTree(childElements[i], remainingLevels: null, childPath, childUiPath));
        }

        return children;
    }

    private static void AppendBoundedChildSnapshot(
        List<UiElementSnapshot> snapshots,
        AutomationElement element,
        string path,
        string uiPath,
        int remainingLevels)
    {
        if (ShouldPromoteChildrenInBoundedTree(element))
        {
            var childElements = element.FindAll(TreeScope.Children, Condition.TrueCondition);
            if (childElements.Count > 0)
            {
                for (var i = 0; i < childElements.Count; i++)
                {
                    AppendBoundedChildSnapshot(
                        snapshots,
                        childElements[i],
                        BuildChildPath(path, i),
                        BuildChildPath(uiPath, i),
                        remainingLevels);
                }

                return;
            }
        }

        var snapshot = CaptureElementTree(element, remainingLevels, path, uiPath);
        if (ShouldOmitElementInBoundedTree(snapshot))
        {
            return;
        }

        snapshots.Add(snapshot);
    }

    private static string BuildChildPath(string path, int childIndex) =>
        path is "root" or "focused"
            ? $"{childIndex}"
            : $"{path}/{childIndex}";

    private static UiElementSnapshot BuildElementSnapshot(
        AutomationElement element,
        string path,
        IReadOnlyList<UiElementSnapshot> children,
        bool includeChildren,
        string? uiPath = null)
    {
        return new UiElementSnapshot(
            path,
            uiPath ?? path,
            GetElementName(element),
            GetControlTypeName(element),
            GetAutomationId(element),
            GetAutomationClassName(element),
            GetIsEnabled(element),
            GetIsOffscreen(element),
            GetHasKeyboardFocus(element),
            GetIsKeyboardFocusable(element),
            GetAvailableActions(element),
            GetBoundingRectangle(element),
            includeChildren ? children : []);
    }

    private static IReadOnlyList<string> GetAvailableActions(AutomationElement element)
    {
        var actions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        var hasSelectionPattern = TryGetPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out _);
        if (GetIsKeyboardFocusable(element) || hasSelectionPattern)
        {
            actions.Add("focus");
        }

        if (TryGetPattern<InvokePattern>(element, InvokePattern.Pattern, out _))
        {
            actions.Add("invoke");
        }

        if (hasSelectionPattern)
        {
            actions.Add("select");
        }

        if (TryGetPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out _))
        {
            actions.Add("expand");
            actions.Add("collapse");
        }

        if (TryGetPattern<TogglePattern>(element, TogglePattern.Pattern, out _))
        {
            actions.Add("toggle");
        }

        if (TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out _))
        {
            actions.Add("set_value");
        }

        if (TryGetPattern<RangeValuePattern>(element, RangeValuePattern.Pattern, out _))
        {
            actions.Add("set_range_value");
        }

        if (TryGetPattern<ScrollItemPattern>(element, ScrollItemPattern.Pattern, out _))
        {
            actions.Add("scroll_into_view");
        }

        if (TryGetPattern<ScrollPattern>(element, ScrollPattern.Pattern, out _))
        {
            actions.Add("scroll");
        }

        if (TryGetPattern<WindowPattern>(element, WindowPattern.Pattern, out _))
        {
            actions.Add("close");
            actions.Add("maximize");
            actions.Add("minimize");
            actions.Add("restore");
        }

        if (TryGetPattern<DockPattern>(element, DockPattern.Pattern, out _))
        {
            actions.Add("dock");
        }

        if (TryGetPattern<TransformPattern>(element, TransformPattern.Pattern, out var transformPattern))
        {
            if (transformPattern.Current.CanMove)
            {
                actions.Add("move");
            }

            if (transformPattern.Current.CanResize)
            {
                actions.Add("resize");
            }

            if (transformPattern.Current.CanRotate)
            {
                actions.Add("rotate");
            }
        }

        return actions.ToArray();
    }

    private static bool ShouldPromoteChildrenInBoundedTree(AutomationElement element)
    {
        return ShouldPromoteChildrenInBoundedTree(
            GetControlTypeName(element),
            GetAutomationClassName(element),
            GetElementName(element),
            GetAutomationId(element),
            GetAvailableActions(element),
            GetIsKeyboardFocusable(element),
            GetHasKeyboardFocus(element));
    }

    internal static bool ShouldPromoteChildrenInBoundedTree(
        string controlType,
        string className,
        string name,
        string automationId,
        IReadOnlyList<string> availableActions,
        bool isKeyboardFocusable,
        bool hasKeyboardFocus)
    {
        _ = automationId;

        if (hasKeyboardFocus || isKeyboardFocusable)
        {
            return false;
        }

        if (!IsStructuralContainer(controlType))
        {
            return false;
        }

        if (TransparentBoundedTreeClassNames.Contains(className))
        {
            return true;
        }

        if (!HasOnlyIgnorableContainerActions(availableActions))
        {
            return false;
        }

        return !HasUsefulBoundedTreeIdentity(name, automationId);
    }

    private static bool ShouldOmitElementInBoundedTree(UiElementSnapshot snapshot)
    {
        return ShouldOmitElementInBoundedTree(
            snapshot.ControlType,
            snapshot.Name,
            snapshot.AutomationId,
            snapshot.AvailableActions,
            snapshot.IsKeyboardFocusable,
            snapshot.HasKeyboardFocus,
            snapshot.Children.Count);
    }

    internal static bool ShouldOmitElementInBoundedTree(
        string controlType,
        string name,
        string automationId,
        IReadOnlyList<string> availableActions,
        bool isKeyboardFocusable,
        bool hasKeyboardFocus,
        int childCount)
    {
        if (childCount > 0 || hasKeyboardFocus || isKeyboardFocusable)
        {
            return false;
        }

        if (!IsStructuralContainer(controlType) || !HasOnlyIgnorableContainerActions(availableActions))
        {
            return false;
        }

        return !HasUsefulBoundedTreeIdentity(name, automationId);
    }

    private static bool IsStructuralContainer(string controlType)
    {
        return controlType.Equals("Pane", StringComparison.OrdinalIgnoreCase) ||
               controlType.Equals("Group", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsefulBoundedTreeIdentity(string name, string automationId)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(automationId) &&
               !TransparentBoundedTreeAutomationIds.Contains(automationId);
    }

    internal static bool HasOnlyIgnorableContainerActions(IReadOnlyList<string> availableActions)
    {
        foreach (var action in availableActions)
        {
            if (!action.Equals("invoke", StringComparison.OrdinalIgnoreCase) &&
                !action.Equals("scroll", StringComparison.OrdinalIgnoreCase) &&
                !action.Equals("scroll_into_view", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool HasOnlyStructuralActions(IReadOnlyList<string> availableActions)
    {
        foreach (var action in availableActions)
        {
            if (!action.Equals("scroll", StringComparison.OrdinalIgnoreCase) &&
                !action.Equals("scroll_into_view", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static MenuInvocationResult BuildMenuInvocationResult(
        nint handle,
        string menuPath,
        string actionTaken,
        UiSettleResult uiSettle)
    {
        var selection = BuildSelectionResult(handle, wasFocused: true);

        return new MenuInvocationResult(
            selection.Handle,
            selection.Title,
            selection.ProcessId,
            menuPath,
            actionTaken,
            uiSettle);
    }

    internal static bool IsUiChangeSettled(
        bool windowAvailable,
        WindowInteractionState? interactionState,
        DateTime utcNow,
        DateTime? lastObservedChangeUtc,
        TimeSpan quietPeriod)
    {
        if (!windowAvailable)
        {
            return true;
        }

        if (IsSettledInteractionState(interactionState))
        {
            return true;
        }

        if (interactionState is not null)
        {
            return false;
        }

        return lastObservedChangeUtc is null ||
               utcNow - lastObservedChangeUtc.Value >= quietPeriod;
    }

    private static AutomationElement FindMainMenuBar(AutomationElement windowElement)
    {
        return windowElement.FindFirst(TreeScope.Descendants, MenuBarCondition)
            ?? throw new InvalidOperationException(
                "The selected window does not expose a main menu through UI Automation.");
    }

    private static IReadOnlyList<AutomationElement> GetTopLevelMenuItems(AutomationElement menuBar)
    {
        var directChildren = Enumerate(menuBar.FindAll(TreeScope.Children, MenuItemCondition))
            .Where(element => !string.IsNullOrWhiteSpace(GetElementName(element)))
            .ToArray();

        if (directChildren.Length > 0)
        {
            return directChildren;
        }

        return Enumerate(menuBar.FindAll(TreeScope.Descendants, MenuItemCondition))
            .Where(element => !string.IsNullOrWhiteSpace(GetElementName(element)))
            .ToArray();
    }

    private static (nint Handle, AutomationElement WindowElement, AutomationElement FocusedElement) ResolveFocusedElementForContextMenu(
        WindowSelectionState selectionState)
    {
        var handle = ResolveInteractionWindowHandle(selectionState);
        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);

        var windowElement = AutomationElement.FromHandle(handle);
        var focusedElement = AutomationElement.FocusedElement
            ?? throw new InvalidOperationException(
                "No focused UI element is currently available. Focus an element before opening its context menu.");

        if (!IsSameOrDescendantOf(focusedElement, windowElement))
        {
            throw new InvalidOperationException(
                "The currently focused UI element does not belong to the selected window.");
        }

        return (handle, windowElement, focusedElement);
    }

    private static string OpenContextMenu(AutomationElement focusedElement, int processId)
    {
        if (FindVisiblePopupMenu(processId) is not null)
        {
            DismissOpenMenus();
            Thread.Sleep(100);
        }

        var focusAction = TryFocusElement(focusedElement);
        PressShiftF10();
        Thread.Sleep(150);

        if (FindVisiblePopupMenu(processId) is not null)
        {
            return focusAction is null ? "pressed_shift_f10" : $"{focusAction}_then_pressed_shift_f10";
        }

        PressAppsKey();
        Thread.Sleep(150);
        if (FindVisiblePopupMenu(processId) is not null)
        {
            return focusAction is null ? "pressed_apps" : $"{focusAction}_then_pressed_apps";
        }

        throw new InvalidOperationException(
            "Tried to open the context menu for the focused element, but no context menu became visible.");
    }

    private static string InvokeVisibleMenuPath(nint handle, string menuPath)
    {
        var segments = menuPath.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("menuPath must contain at least one menu item.");
        }

        var processId = GetProcessId(handle);
        var firstItem = WaitForMenuItem(processId, segments[0]);
        if (firstItem is null)
        {
            throw new InvalidOperationException($"Could not find menu item '{segments[0]}'.");
        }

        string actionTaken;
        if (segments.Length == 1)
        {
            return InvokeAction(firstItem, allowExpandOnly: false);
        }

        actionTaken = ExpandMenu(firstItem);
        for (var i = 1; i < segments.Length; i++)
        {
            var nextItem = WaitForMenuItem(processId, segments[i]);
            if (nextItem is null)
            {
                var traversedPath = string.Join(" > ", segments.Take(i));
                throw new InvalidOperationException(
                    $"Could not find menu item '{segments[i]}' after opening '{traversedPath}'.");
            }

            var isLast = i == segments.Length - 1;
            actionTaken = isLast
                ? InvokeAction(nextItem, allowExpandOnly: false)
                : ExpandMenu(nextItem);
        }

        return actionTaken;
    }

    private static IReadOnlyList<MenuItemSummary> GetVisiblePopupMenuItems(int processId, string? parentMenuPath)
    {
        var popupMenu = WaitForVisiblePopupMenu(processId)
            ?? throw new InvalidOperationException("Opened a menu, but no visible menu items were exposed through UI Automation.");

        var children = Enumerate(popupMenu.FindAll(TreeScope.Children, MenuItemCondition))
            .ToArray();

        return children
            .Select(item => BuildMenuItemSummary(item, parentMenuPath))
            .ToArray();
    }

    private static AutomationElement? WaitForVisiblePopupMenu(int processId)
    {
        var deadline = DateTime.UtcNow + MenuSearchTimeout;
        while (DateTime.UtcNow <= deadline)
        {
            var match = FindVisiblePopupMenu(processId);
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(MenuSearchInterval);
        }

        return null;
    }

    private static AutomationElement? FindVisiblePopupMenu(int processId)
    {
        var visibleMenuCondition = new AndCondition(
            MenuCondition,
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false));

        return Enumerate(AutomationElement.RootElement.FindAll(TreeScope.Descendants, visibleMenuCondition))
            .Select(menu => new
            {
                Element = menu,
                ChildItems = Enumerate(menu.FindAll(TreeScope.Children, MenuItemCondition)).ToArray(),
            })
            .Where(candidate => candidate.ChildItems.Length > 0)
            .OrderByDescending(candidate => candidate.ChildItems.Length)
            .ThenByDescending(candidate =>
            {
                var bounds = GetBoundingRectangle(candidate.Element);
                return bounds is null ? 0d : bounds.Width * bounds.Height;
            })
            .Select(candidate => candidate.Element)
            .FirstOrDefault();
    }

    private static MenuItemSummary BuildMenuItemSummary(AutomationElement element, string? parentMenuPath)
    {
        var label = GetElementName(element);
        var isSeparator = string.IsNullOrWhiteSpace(label);
        var displayLabel = isSeparator ? "(separator)" : label;
        var menuPath = string.IsNullOrWhiteSpace(parentMenuPath)
            ? displayLabel
            : $"{parentMenuPath} > {displayLabel}";

        return new MenuItemSummary(
            displayLabel,
            menuPath,
            GetControlTypeName(element),
            GetIsEnabled(element),
            HasSubmenu(element),
            isSeparator,
            GetAvailableActions(element));
    }

    private static bool HasSubmenu(AutomationElement element)
    {
        if (!TryGetPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out var expandCollapsePattern))
        {
            return false;
        }

        return expandCollapsePattern.Current.ExpandCollapseState is
            ExpandCollapseState.Collapsed or
            ExpandCollapseState.Expanded or
            ExpandCollapseState.PartiallyExpanded;
    }

    private static nint ResolveWindowHandle(
        string? windowHandle,
        string? titleContains,
        WindowSelectionState? selectionState = null)
    {
        if (!string.IsNullOrWhiteSpace(windowHandle))
        {
            return ParseHandle(windowHandle);
        }

        if (!string.IsNullOrWhiteSpace(titleContains))
        {
            return ResolveWindowByTitle(titleContains);
        }

        var selectedHandle = selectionState?.GetSelectedHandle();
        if (selectedHandle.HasValue)
        {
            return selectedHandle.Value;
        }

        throw new InvalidOperationException(
            "No window was supplied and no window is currently selected. Use list_windows followed by select_window first.");
    }

    private static nint ResolveWindowByTitle(string titleContains)
    {
        var matches = new List<(nint Handle, string Title)>();

        _ = NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = NativeMethods.GetWindowText(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((hWnd, title));
            }

            return true;
        }, nint.Zero);

        LogTrace(
            "select_window.resolve_by_title",
            ("titleContains", titleContains),
            ("matchCount", matches.Count),
            ("matches", string.Join(" | ", matches.Select(match => $"{FormatHandle(match.Handle)} ({match.Title})"))));

        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"No visible window title contains '{titleContains}'."),
            1 => matches[0].Handle,
            _ => throw new InvalidOperationException(
                $"Multiple visible windows matched '{titleContains}': {string.Join(", ", matches.Select(match => $"{FormatHandle(match.Handle)} ({match.Title})"))}. Use a specific windowHandle instead."),
        };
    }

    private static AutomationElement ResolveElementPath(AutomationElement root, string elementPath)
    {
        if (elementPath == "root")
        {
            return root;
        }

        var current = root;
        foreach (var segment in elementPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(segment, out var index) || index < 0)
            {
                throw new InvalidOperationException($"Element path segment '{segment}' is not a valid child index.");
            }

            var children = current.FindAll(TreeScope.Children, Condition.TrueCondition);
            if (index >= children.Count)
            {
                throw new InvalidOperationException(
                    $"Element path '{elementPath}' is out of range at segment '{segment}'.");
            }

            current = children[index];
        }

        return current;
    }

    private static string NormalizeElementPath(string elementPath)
    {
        if (string.IsNullOrWhiteSpace(elementPath))
        {
            throw new InvalidOperationException("elementPath is required.");
        }

        return elementPath.Trim().Replace('\\', '/');
    }

    private static string? GetParentPath(string elementPath)
    {
        if (string.Equals(elementPath, "root", StringComparison.Ordinal))
        {
            return null;
        }

        var separatorIndex = elementPath.LastIndexOf('/');
        return separatorIndex < 0 ? "root" : elementPath[..separatorIndex];
    }

    private static int GetLastPathIndex(string elementPath)
    {
        var lastSegment = elementPath.Split('/').Last();
        return int.TryParse(lastSegment, out var index)
            ? index
            : throw new InvalidOperationException($"Element path segment '{lastSegment}' is not a valid child index.");
    }

    private static bool IsPathPrefix(string prefixPath, string fullPath)
    {
        if (string.Equals(prefixPath, fullPath, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(prefixPath, "root", StringComparison.Ordinal))
        {
            return true;
        }

        return fullPath.StartsWith($"{prefixPath}/", StringComparison.Ordinal);
    }

    private static string? TryFindElementPath(
        AutomationElement root,
        AutomationElement target,
        string currentPath)
    {
        if (AreSameAutomationElement(root, target))
        {
            return currentPath;
        }

        var children = root.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < children.Count; i++)
        {
            var childPath = BuildChildPath(currentPath, i);
            var resolvedPath = TryFindElementPath(children[i], target, childPath);
            if (resolvedPath is not null)
            {
                return resolvedPath;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> BuildKeyboardInvocationKeyPreference(string? currentPath, string targetPath)
    {
        var normalizedTargetPath = NormalizeElementPath(targetPath);
        var normalizedCurrentPath = string.IsNullOrWhiteSpace(currentPath)
            ? null
            : NormalizeElementPath(currentPath);

        if (normalizedCurrentPath is null || normalizedCurrentPath == "root")
        {
            return ["Tab", "Right", "Down", "Left", "Up", "Shift+Tab"];
        }

        if (IsPathPrefix(normalizedCurrentPath, normalizedTargetPath))
        {
            return ["Right", "Down", "Tab", "Left", "Up", "Shift+Tab"];
        }

        if (IsPathPrefix(normalizedTargetPath, normalizedCurrentPath))
        {
            return ["Left", "Up", "Shift+Tab", "Right", "Down", "Tab"];
        }

        var currentParentPath = GetParentPath(normalizedCurrentPath);
        var targetParentPath = GetParentPath(normalizedTargetPath);
        if (currentParentPath is not null &&
            string.Equals(currentParentPath, targetParentPath, StringComparison.Ordinal))
        {
            var currentIndex = GetLastPathIndex(normalizedCurrentPath);
            var targetIndex = GetLastPathIndex(normalizedTargetPath);
            if (currentIndex < targetIndex)
            {
                return ["Right", "Down", "Tab", "Left", "Up", "Shift+Tab"];
            }

            if (currentIndex > targetIndex)
            {
                return ["Left", "Up", "Shift+Tab", "Right", "Down", "Tab"];
            }
        }

        return ["Tab", "Right", "Down", "Left", "Up", "Shift+Tab"];
    }

    internal static string NormalizeMouseButton(string mouseButton)
    {
        if (string.IsNullOrWhiteSpace(mouseButton))
        {
            throw new InvalidOperationException("mouseButton is required.");
        }

        return mouseButton.Trim().ToLowerInvariant() switch
        {
            "left" or "primary" => "left",
            "right" or "secondary" => "right",
            _ => throw new InvalidOperationException(
                $"Unsupported mouseButton '{mouseButton}'. Use left or right."),
        };
    }

    private static (nint Handle, AutomationElement RootElement) GetTaskbarRootElement()
    {
        var handle = NativeMethods.FindWindowW("Shell_TrayWnd", null);
        if (handle == nint.Zero || !NativeMethods.IsWindow(handle))
        {
            throw new InvalidOperationException("Could not locate the main Windows taskbar window.");
        }

        return (handle, AutomationElement.FromHandle(handle));
    }

    private static (AutomationElement Element, string Path) FindMainTaskbarHost(AutomationElement taskbarRoot)
    {
        foreach (var automationId in TaskbarHostAutomationIds)
        {
            var match = FindElementWithPath(
                taskbarRoot,
                "root",
                element => string.Equals(GetAutomationId(element), automationId, StringComparison.Ordinal));
            if (match is not null)
            {
                return match.Value;
            }
        }

        foreach (var className in TaskbarHostClassNames)
        {
            var match = FindElementWithPath(
                taskbarRoot,
                "root",
                element => string.Equals(GetAutomationClassName(element), className, StringComparison.Ordinal));
            if (match is not null)
            {
                return match.Value;
            }
        }

        var runningApplicationsMatch = FindElementWithPath(
            taskbarRoot,
            "root",
            element => string.Equals(GetElementName(element), "Running applications", StringComparison.OrdinalIgnoreCase));
        if (runningApplicationsMatch is not null)
        {
            return runningApplicationsMatch.Value;
        }

        throw new InvalidOperationException("Could not find the main taskbar host element through UI Automation.");
    }

    private static (AutomationElement Element, string Path)? FindElementWithPath(
        AutomationElement root,
        string path,
        Func<AutomationElement, bool> predicate)
    {
        if (predicate(root))
        {
            return (root, path);
        }

        var children = root.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < children.Count; i++)
        {
            var childPath = path == "root" ? $"{i}" : $"{path}/{i}";
            var match = FindElementWithPath(children[i], childPath, predicate);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static IReadOnlyList<TaskbarElementSummary> EnumerateVisibleTaskbarChildren(
        AutomationElement hostElement,
        string hostPath)
    {
        var result = new List<TaskbarElementSummary>();
        var children = hostElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (GetIsOffscreen(child))
            {
                continue;
            }

            var childPath = hostPath == "root" ? $"{i}" : $"{hostPath}/{i}";
            result.Add(BuildTaskbarElementSummary(child, childPath));
        }

        return result;
    }

    private static TaskbarElementSummary BuildTaskbarElementSummary(AutomationElement element, string path)
    {
        return new TaskbarElementSummary(
            path,
            GetElementName(element),
            GetControlTypeName(element),
            GetAutomationId(element),
            GetAutomationClassName(element),
            GetIsEnabled(element),
            GetIsOffscreen(element),
            GetHasKeyboardFocus(element),
            GetIsKeyboardFocusable(element),
            GetAvailableActions(element),
            GetBoundingRectangle(element),
            IsTaskbarAppButton(element));
    }

    private static bool IsTaskbarAppButton(AutomationElement element)
    {
        var className = GetAutomationClassName(element);
        var automationId = GetAutomationId(element);

        return string.Equals(className, "Taskbar.TaskListButtonAutomationPeer", StringComparison.Ordinal) ||
               automationId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase);
    }

    internal static TaskbarElementSummary ResolveTaskbarSearchTarget(
        IReadOnlyList<TaskbarElementSummary> visibleElements)
    {
        var exactMatch = visibleElements.FirstOrDefault(element =>
            string.Equals(element.AutomationId, SearchButtonAutomationId, StringComparison.Ordinal));
        if (exactMatch is not null)
        {
            LogTrace(
                "taskbar_search_target.resolved",
                ("strategy", "automation_id_exact"),
                ("visibleElements", visibleElements.Count),
                ("target", DescribeTaskbarElement(exactMatch)));
            return exactMatch;
        }

        var fallbackMatch = visibleElements.FirstOrDefault(element =>
            !element.IsAppButton &&
            NormalizeLabel(element.Name).Contains("search", StringComparison.Ordinal));
        if (fallbackMatch is not null)
        {
            LogTrace(
                "taskbar_search_target.resolved",
                ("strategy", "label_contains_search"),
                ("visibleElements", visibleElements.Count),
                ("target", DescribeTaskbarElement(fallbackMatch)));
            return fallbackMatch;
        }

        LogTrace(
            "taskbar_search_target.missing",
            ("visibleElements", visibleElements.Count),
            ("visibleElementSummaries", string.Join(" | ", visibleElements.Select(DescribeTaskbarElement))));
        throw new InvalidOperationException(
            "Could not find a visible Windows Search control on the main taskbar.");
    }

    internal static bool IsInteractiveSelectionTarget(
        string title,
        string className,
        bool isVisible,
        bool isExcludedHandle)
        => GetInteractiveSelectionTargetRejectionReason(title, className, isVisible, isExcludedHandle) is null;

    internal static string? GetInteractiveSelectionTargetRejectionReason(
        string title,
        string className,
        bool isVisible,
        bool isExcludedHandle)
    {
        if (isExcludedHandle)
        {
            return "excluded_handle";
        }

        if (!isVisible)
        {
            return "window_not_visible";
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return "window_has_blank_title";
        }

        if (string.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal))
        {
            return "shell_taskbar_window";
        }

        if (string.Equals(title, "Program Manager", StringComparison.OrdinalIgnoreCase))
        {
            return "desktop_program_manager";
        }

        if (string.Equals(title, "Windows Input Experience", StringComparison.OrdinalIgnoreCase))
        {
            return "windows_input_experience";
        }

        return null;
    }

    internal static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "window";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());

        sanitized = Regex.Replace(sanitized, "\\s+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "window" : sanitized;
    }

    internal static string GetScreenshotDirectory(string? debugArtifactDirectory = null)
    {
        var configuredDirectory = string.IsNullOrWhiteSpace(debugArtifactDirectory)
            ? Environment.GetEnvironmentVariable(DebugArtifactDirectoryEnvironmentVariable)
            : debugArtifactDirectory;

        return string.IsNullOrWhiteSpace(configuredDirectory)
            ? DefaultScreenshotDirectory
            : Path.GetFullPath(configuredDirectory);
    }

    internal static ushort ResolveVirtualKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("key is required.");
        }

        var normalizedKey = key.Trim();
        if (normalizedKey.Length == 1)
        {
            var character = normalizedKey[0];
            if (character is >= 'a' and <= 'z')
            {
                return (ushort)char.ToUpperInvariant(character);
            }

            if ((character is >= 'A' and <= 'Z') || (character is >= '0' and <= '9'))
            {
                return character;
            }
        }

        if (normalizedKey.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalizedKey[1..], out var functionKeyNumber) &&
            functionKeyNumber is >= 1 and <= 24)
        {
            return (ushort)(0x70 + functionKeyNumber - 1);
        }

        return normalizedKey.ToLowerInvariant() switch
        {
            "enter" or "return" => NativeMethods.VkReturn,
            "escape" or "esc" => NativeMethods.VkEscape,
            "tab" => 0x09,
            "space" or "spacebar" => 0x20,
            "backspace" or "bs" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "apps" or "menu" => NativeMethods.VkApps,
            "win" or "windows" or "meta" => NativeMethods.VkLWin,
            _ => throw new InvalidOperationException(
                $"Unsupported key '{key}'. Use a named key like Enter, Tab, Escape, Up, F5, A, or 1, or use text for direct typing."),
        };
    }

    internal static IReadOnlyList<string> NormalizeModifierNames(IReadOnlyList<string>? modifiers)
    {
        if (modifiers is null || modifiers.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>(modifiers.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var modifier in modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifier))
            {
                continue;
            }

            var canonicalName = NormalizeModifierName(modifier);
            if (seen.Add(canonicalName))
            {
                normalized.Add(canonicalName);
            }
        }

        return normalized;
    }

    internal static IReadOnlyList<ushort> ResolveModifierVirtualKeys(IReadOnlyList<string> normalizedModifiers)
    {
        return normalizedModifiers
            .Select(modifier => ModifierVirtualKeys[modifier])
            .ToArray();
    }

    private static string NormalizeModifierName(string modifier)
    {
        var normalizedModifier = modifier.Trim().ToLowerInvariant();
        if (!ModifierVirtualKeys.ContainsKey(normalizedModifier))
        {
            throw new InvalidOperationException(
                $"Unsupported modifier '{modifier}'. Supported modifiers are Control, Shift, Alt, and Win.");
        }

        return normalizedModifier switch
        {
            "ctrl" => "control",
            "windows" or "meta" => "win",
            _ => normalizedModifier,
        };
    }

    private static TaskbarElementSummary ResolveTaskbarAppTarget(
        IReadOnlyList<TaskbarElementSummary> visibleApps,
        string? elementPath,
        string? titleContains,
        string? automationIdContains)
    {
        if (!string.IsNullOrWhiteSpace(elementPath))
        {
            var normalizedPath = NormalizeElementPath(elementPath);
            var elementPathMatch = visibleApps.FirstOrDefault(app => string.Equals(app.Path, normalizedPath, StringComparison.Ordinal));
            if (elementPathMatch is not null)
            {
                LogTrace(
                    "taskbar_app_target.resolved",
                    ("strategy", "element_path"),
                    ("visibleApps", visibleApps.Count),
                    ("target", DescribeTaskbarElement(elementPathMatch)));
                return elementPathMatch;
            }

            LogTrace(
                "taskbar_app_target.missing",
                ("strategy", "element_path"),
                ("visibleApps", visibleApps.Count),
                ("requestedPath", normalizedPath));
            throw new InvalidOperationException(
                $"No visible taskbar app button matched elementPath '{elementPath}'.");
        }

        if (!string.IsNullOrWhiteSpace(automationIdContains))
        {
            return ResolveTaskbarAppBySubstring(
                visibleApps,
                automationIdContains,
                static app => app.AutomationId,
                "automationIdContains");
        }

        return ResolveTaskbarAppByTitle(visibleApps, titleContains!);
    }

    private static TaskbarElementSummary ResolveTaskbarAppByTitle(
        IReadOnlyList<TaskbarElementSummary> visibleApps,
        string expectedLabel)
    {
        var exactMatch = visibleApps.FirstOrDefault(app =>
            string.Equals(NormalizeLabel(app.Name), NormalizeLabel(expectedLabel), StringComparison.Ordinal));
        if (exactMatch is not null)
        {
            LogTrace(
                "taskbar_app_target.resolved",
                ("strategy", "title_exact"),
                ("visibleApps", visibleApps.Count),
                ("expectedLabel", expectedLabel),
                ("target", DescribeTaskbarElement(exactMatch)));
            return exactMatch;
        }

        var matches = visibleApps
            .Where(app =>
            {
                var normalizedActual = NormalizeLabel(app.Name);
                var normalizedExpected = NormalizeLabel(expectedLabel);
                return normalizedActual.StartsWith(normalizedExpected, StringComparison.Ordinal) ||
                       normalizedActual.Contains(normalizedExpected, StringComparison.Ordinal);
            })
            .ToArray();

        LogTrace(
            "taskbar_app_target.candidates",
            ("strategy", "title_contains"),
            ("visibleApps", visibleApps.Count),
            ("expectedLabel", expectedLabel),
            ("matchCount", matches.Length),
            ("matches", string.Join(" | ", matches.Select(DescribeTaskbarElement))));

        return matches.Length switch
        {
            0 => throw new InvalidOperationException(
                $"No visible taskbar app button label contains '{expectedLabel}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Multiple visible taskbar app buttons matched '{expectedLabel}': {string.Join(", ", matches.Select(match => $"{match.Path} ({match.Name})"))}. Use elementPath instead."),
        };
    }

    private static TaskbarElementSummary ResolveTaskbarAppBySubstring(
        IReadOnlyList<TaskbarElementSummary> visibleApps,
        string expectedSubstring,
        Func<TaskbarElementSummary, string> selector,
        string argumentName)
    {
        var matches = visibleApps
            .Where(app => selector(app).Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        LogTrace(
            "taskbar_app_target.candidates",
            ("strategy", argumentName),
            ("visibleApps", visibleApps.Count),
            ("expectedSubstring", expectedSubstring),
            ("matchCount", matches.Length),
            ("matches", string.Join(" | ", matches.Select(DescribeTaskbarElement))));

        return matches.Length switch
        {
            0 => throw new InvalidOperationException(
                $"No visible taskbar app button matched {argumentName} '{expectedSubstring}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Multiple visible taskbar app buttons matched '{expectedSubstring}': {string.Join(", ", matches.Select(match => $"{match.Path} ({match.Name})"))}. Use elementPath instead."),
        };
    }

    internal static TaskbarActivationMode ResolveTaskbarActivationMode(
        bool canInvoke,
        bool canSelect,
        bool canToggle,
        bool isKeyboardFocusable)
    {
        if (canInvoke)
        {
            return TaskbarActivationMode.Invoke;
        }

        if (canSelect)
        {
            return TaskbarActivationMode.Select;
        }

        if (canToggle)
        {
            return TaskbarActivationMode.Toggle;
        }

        return isKeyboardFocusable
            ? TaskbarActivationMode.FocusAndPressEnter
            : TaskbarActivationMode.FocusOnly;
    }

    private static string ActivateTaskbarElement(AutomationElement element)
    {
        var canInvoke = TryGetPattern<InvokePattern>(element, InvokePattern.Pattern, out var invokePattern);
        var canSelect = TryGetPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selectionItemPattern);
        var canToggle = TryGetPattern<TogglePattern>(element, TogglePattern.Pattern, out var togglePattern);
        var activationMode = ResolveTaskbarActivationMode(
            canInvoke,
            canSelect,
            canToggle,
            GetIsKeyboardFocusable(element));

        switch (activationMode)
        {
            case TaskbarActivationMode.Invoke:
                invokePattern.Invoke();
                Thread.Sleep(150);
                return "invoked";

            case TaskbarActivationMode.Select:
                selectionItemPattern.Select();
                Thread.Sleep(150);
                return "selected";

            case TaskbarActivationMode.Toggle:
                togglePattern.Toggle();
                Thread.Sleep(150);
                return "toggled";

            case TaskbarActivationMode.FocusAndPressEnter:
                element.SetFocus();
                Thread.Sleep(150);
                PressEnterKey();
                Thread.Sleep(150);
                return "focused_and_pressed_enter";

            default:
                element.SetFocus();
                Thread.Sleep(150);
                return "focused";
        }
    }

    private static string OpenTaskbarSearch(AutomationElement element)
    {
        if (TryGetPattern<TogglePattern>(element, TogglePattern.Pattern, out var togglePattern))
        {
            var state = togglePattern.Current.ToggleState;
            if (state == ToggleState.Off)
            {
                togglePattern.Toggle();
                Thread.Sleep(250);
                return "toggled_search_on";
            }

            var focusAction = TryFocusElement(element);
            Thread.Sleep(150);
            return focusAction is null ? "search_already_open" : $"search_already_open_{focusAction}";
        }

        return ActivateTaskbarElement(element);
    }

    private static WindowSelectionResult? TrySelectForegroundWindowAfterLaunch(
        WindowSelectionState selectionState,
        IReadOnlyCollection<nint> excludedHandles)
    {
        var stopwatch = Stopwatch.StartNew();
        var attemptCount = 0;
        var deadline = DateTime.UtcNow + LaunchSelectionTimeout;
        while (DateTime.UtcNow <= deadline)
        {
            attemptCount += 1;
            if (TryPromoteForegroundWindow(selectionState, excludedHandles, out var selectedWindow))
            {
                LogTrace(
                    "foreground_launch_selection.complete",
                    ("status", "selected"),
                    ("attempts", attemptCount),
                    ("excludedHandles", string.Join(", ", excludedHandles.Select(FormatHandle))),
                    ("selectedWindow", DescribeSelectionResult(selectedWindow)),
                    ("elapsedMs", RoundElapsedMilliseconds(stopwatch.Elapsed)));
                return selectedWindow;
            }

            Thread.Sleep(LaunchSelectionInterval);
        }

        attemptCount += 1;
        if (TryPromoteForegroundWindow(selectionState, excludedHandles, out var fallbackWindow))
        {
            LogTrace(
                "foreground_launch_selection.complete",
                ("status", "selected_after_timeout_boundary"),
                ("attempts", attemptCount),
                ("excludedHandles", string.Join(", ", excludedHandles.Select(FormatHandle))),
                ("selectedWindow", DescribeSelectionResult(fallbackWindow)),
                ("elapsedMs", RoundElapsedMilliseconds(stopwatch.Elapsed)));
            return fallbackWindow;
        }

        selectionState.Clear();
        LogTrace(
            "foreground_launch_selection.complete",
            ("status", "no_selection"),
            ("attempts", attemptCount),
            ("excludedHandles", string.Join(", ", excludedHandles.Select(FormatHandle))),
            ("elapsedMs", RoundElapsedMilliseconds(stopwatch.Elapsed)));
        return null;
    }

    private static bool TryPromoteForegroundWindow(
        WindowSelectionState selectionState,
        IReadOnlyCollection<nint> excludedHandles,
        out WindowSelectionResult selectedWindow)
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == nint.Zero || !NativeMethods.IsWindow(handle))
        {
            selectedWindow = null!;
            return false;
        }

        var title = NativeMethods.GetWindowText(handle);
        var className = NativeMethods.GetClassName(handle);
        var isVisible = NativeMethods.IsWindowVisible(handle);
        var isExcludedHandle = excludedHandles.Contains(handle);
        var rejectionReason = GetInteractiveSelectionTargetRejectionReason(
            title,
            className,
            isVisible,
            isExcludedHandle);

        if (rejectionReason is not null)
        {
            LogTrace(
                "foreground_promotion.rejected",
                ("handle", FormatHandle(handle)),
                ("title", title),
                ("className", className),
                ("isVisible", isVisible),
                ("isExcludedHandle", isExcludedHandle),
                ("reason", rejectionReason));
            selectedWindow = null!;
            return false;
        }

        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);
        selectedWindow = BuildSelectionResult(handle, wasFocused: true);
        LogTrace(
            "foreground_promotion.accepted",
            ("handle", FormatHandle(handle)),
            ("title", title),
            ("className", className),
            ("selectedWindow", DescribeSelectionResult(selectedWindow)));
        return true;
    }

    private static AutomationElement? WaitForSearchInputElement()
    {
        var deadline = DateTime.UtcNow + TaskbarSearchTimeout;
        while (DateTime.UtcNow <= deadline)
        {
            var candidate = TryFindSearchInputElement();
            if (candidate is not null)
            {
                return candidate;
            }

            Thread.Sleep(TaskbarSearchInterval);
        }

        return null;
    }

    private static AutomationElement? TryFindSearchInputElement()
    {
        var focusedElement = AutomationElement.FocusedElement;
        if (focusedElement is not null && IsSearchInputElement(focusedElement))
        {
            return focusedElement;
        }

        var activeWindowHandle = NativeMethods.GetForegroundWindow();
        if (activeWindowHandle == nint.Zero || !NativeMethods.IsWindow(activeWindowHandle))
        {
            return null;
        }

        var activeWindow = AutomationElement.FromHandle(activeWindowHandle);
        if (IsSearchInputElement(activeWindow))
        {
            return activeWindow;
        }

        var descendants = activeWindow.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        AutomationElement? bestCandidate = null;
        var bestScore = 0;

        for (var i = 0; i < descendants.Count; i++)
        {
            var candidate = descendants[i];
            var score = ScoreSearchInputCandidate(candidate);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestCandidate = candidate;
        }

        return bestCandidate;
    }

    private static bool IsSearchInputElement(AutomationElement element) => ScoreSearchInputCandidate(element) > 0;

    private static int ScoreSearchInputCandidate(AutomationElement element)
    {
        if (GetIsOffscreen(element) || !GetIsEnabled(element))
        {
            return 0;
        }

        var score = 0;

        try
        {
            if (element.Current.ControlType == ControlType.Edit)
            {
                score += 8;
            }
        }
        catch
        {
            return 0;
        }

        if (TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out _))
        {
            score += 6;
        }

        if (GetIsKeyboardFocusable(element))
        {
            score += 4;
        }

        if (GetHasKeyboardFocus(element))
        {
            score += 3;
        }

        var normalizedName = NormalizeLabel(GetElementName(element));
        var normalizedAutomationId = NormalizeLabel(GetAutomationId(element));
        if (normalizedName.Contains("search", StringComparison.Ordinal) ||
            normalizedAutomationId.Contains("search", StringComparison.Ordinal))
        {
            score += 2;
        }

        return score;
    }

    private static string EnterTextIntoSearchInput(AutomationElement inputElement, string text)
    {
        var focusAction = TryFocusElement(inputElement);

        if (TrySetValue(inputElement, text))
        {
            return focusAction is null ? "set_value" : $"{focusAction}_and_set_value";
        }

        PressSelectAllShortcut();
        Thread.Sleep(100);
        SendUnicodeText(text);
        Thread.Sleep(100);

        return focusAction is null ? "typed_text" : $"{focusAction}_and_typed_text";
    }

    private static bool TrySetValue(AutomationElement element, string value)
    {
        if (!TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
        {
            return false;
        }

        if (valuePattern.Current.IsReadOnly)
        {
            return false;
        }

        valuePattern.SetValue(value);
        Thread.Sleep(100);
        return true;
    }

    private static void PressWindowsSearchShortcut()
    {
        PressModifiedKey(NativeMethods.VkLWin, NativeMethods.VkS);
        Thread.Sleep(250);
    }

    private static void PressShiftF10()
    {
        PressModifiedKey(NativeMethods.VkShift, NativeMethods.VkF10);
    }

    private static void PressAppsKey()
    {
        PressKey(NativeMethods.VkApps, "Failed to send the Apps key to the focused element.");
    }

    private static void PressEscapeKey()
    {
        PressKey(NativeMethods.VkEscape, "Failed to send Escape to dismiss the active menu.");
    }

    private static void DismissOpenMenus()
    {
        PressEscapeKey();
        Thread.Sleep(100);
    }

    private static void PressSelectAllShortcut()
    {
        PressModifiedKey(NativeMethods.VkControl, NativeMethods.VkA);
    }

    private static void PressModifiedKey(ushort modifierVirtualKey, ushort keyVirtualKey)
    {
        PressKeyWithModifiers(
            keyVirtualKey,
            [modifierVirtualKey],
            $"Failed to send modified key chord 0x{modifierVirtualKey:X2}+0x{keyVirtualKey:X2}.");
    }

    private static void PressEnterKey()
    {
        PressKey(NativeMethods.VkReturn, "Failed to send Enter to the focused taskbar item.");
    }

    private static void PressKeyboardNavigationKey(string key)
    {
        switch (key)
        {
            case "Shift+Tab":
                PressKeyWithModifiers(
                    ResolveVirtualKey("Tab"),
                    [NativeMethods.VkShift],
                    "Failed to send Shift+Tab during keyboard navigation.");
                break;
            case "Tab":
            case "Left":
            case "Right":
            case "Up":
            case "Down":
                PressKey(
                    ResolveVirtualKey(key),
                    $"Failed to send {key} during keyboard navigation.");
                break;
            default:
                throw new InvalidOperationException($"Unsupported keyboard navigation key '{key}'.");
        }
    }

    private static void PressKey(ushort virtualKey, string errorMessage)
    {
        SendKeyboardInputs(
            errorMessage,
            CreateVirtualKeyInput(virtualKey),
            CreateVirtualKeyInput(virtualKey, keyUp: true));
    }

    private static void SendUnicodeText(string text)
    {
        SendUnicodeText(text, "Failed to send Unicode text to the focused search input.");
    }

    private static void SendUnicodeText(string text, string errorMessage)
    {
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character));
            inputs.Add(CreateUnicodeInput(character, keyUp: true));
        }

        if (inputs.Count == 0)
        {
            return;
        }

        SendKeyboardInputs(errorMessage, [.. inputs]);
    }

    private static void PressKeyWithModifiers(
        ushort keyVirtualKey,
        IReadOnlyList<ushort> modifierVirtualKeys,
        string errorMessage)
    {
        var inputs = new List<NativeMethods.INPUT>((modifierVirtualKeys.Count * 2) + 2);
        foreach (var modifierVirtualKey in modifierVirtualKeys)
        {
            inputs.Add(CreateVirtualKeyInput(modifierVirtualKey));
        }

        inputs.Add(CreateVirtualKeyInput(keyVirtualKey));
        inputs.Add(CreateVirtualKeyInput(keyVirtualKey, keyUp: true));

        for (var i = modifierVirtualKeys.Count - 1; i >= 0; i--)
        {
            inputs.Add(CreateVirtualKeyInput(modifierVirtualKeys[i], keyUp: true));
        }

        SendKeyboardInputs(errorMessage, [.. inputs]);
    }

    private static NativeMethods.INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp = false)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.InputKeyboard,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? NativeMethods.KeyEventFKeyUp : 0,
                },
            },
        };
    }

    private static NativeMethods.INPUT CreateUnicodeInput(char character, bool keyUp = false)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.InputKeyboard,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wScan = character,
                    dwFlags = NativeMethods.KeyEventFUnicode | (keyUp ? NativeMethods.KeyEventFKeyUp : 0),
                },
            },
        };
    }

    private static void SendKeyboardInputs(string errorMessage, params NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != (uint)inputs.Length)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static void ClickAtScreenPoint(ScreenPoint clickPoint, string mouseButton)
    {
        if (!NativeMethods.SetCursorPos(clickPoint.X, clickPoint.Y))
        {
            throw new InvalidOperationException(
                $"Failed to move the mouse cursor to screen point ({clickPoint.X}, {clickPoint.Y}).");
        }

        Thread.Sleep(50);

        NativeMethods.INPUT[] inputs = mouseButton switch
        {
            "left" =>
            [
                CreateMouseInput(NativeMethods.MouseEventFLeftDown),
                CreateMouseInput(NativeMethods.MouseEventFLeftUp),
            ],
            "right" =>
            [
                CreateMouseInput(NativeMethods.MouseEventFRightDown),
                CreateMouseInput(NativeMethods.MouseEventFRightUp),
            ],
            _ => throw new InvalidOperationException(
                $"Unsupported normalized mouseButton '{mouseButton}'."),
        };

        SendMouseInputs(
            $"Failed to send a {mouseButton} mouse click to screen point ({clickPoint.X}, {clickPoint.Y}).",
            inputs);
    }

    private static NativeMethods.INPUT CreateMouseInput(uint mouseFlags)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.InputMouse,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dwFlags = mouseFlags,
                },
            },
        };
    }

    private static void SendMouseInputs(string errorMessage, params NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != (uint)inputs.Length)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static (AutomationElement Element, string Path, string ActionTaken) FocusAutomationElementOrDescendant(
        AutomationElement element,
        string path)
    {
        var actionTaken = TryFocusElement(element);
        if (actionTaken is not null)
        {
            var focusedElement = AutomationElement.FocusedElement ?? element;
            return (focusedElement, path, actionTaken);
        }

        var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < children.Count; i++)
        {
            var childPath = path == "root" ? $"{i}" : $"{path}/{i}";
            try
            {
                return FocusAutomationElementOrDescendant(children[i], childPath);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException("Target element cannot receive focus.");
    }

    private static (AutomationElement Element, string Path, string? PreparationActionTaken, ScreenPoint ClickPoint) ResolveClickableElementOrDescendant(
        AutomationElement element,
        string path,
        WindowBounds windowBounds)
    {
        var preparationActionTaken = TryPrepareElementForMouseClick(element);
        if (TryResolveClickableScreenPoint(element, windowBounds, out var clickPoint))
        {
            return (element, path, preparationActionTaken, clickPoint);
        }

        var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < children.Count; i++)
        {
            var childPath = path == "root" ? $"{i}" : $"{path}/{i}";
            try
            {
                return ResolveClickableElementOrDescendant(children[i], childPath, windowBounds);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException(
            "Target element does not expose clickable screen bounds through UI Automation.");
    }

    private static string? TryPrepareElementForMouseClick(AutomationElement element)
    {
        var selectionApplied = false;
        var scrollApplied = false;
        var focusApplied = false;

        if (TryGetPattern<ScrollItemPattern>(element, ScrollItemPattern.Pattern, out var scrollItemPattern))
        {
            scrollItemPattern.ScrollIntoView();
            scrollApplied = true;
            Thread.Sleep(100);
        }

        if (TryGetPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selectionItemPattern))
        {
            selectionItemPattern.Select();
            selectionApplied = true;
            Thread.Sleep(100);
        }

        try
        {
            element.SetFocus();
            focusApplied = true;
            Thread.Sleep(100);
        }
        catch (InvalidOperationException)
        {
        }

        if (selectionApplied && focusApplied)
        {
            return "selected_and_focused";
        }

        if (selectionApplied)
        {
            return "selected";
        }

        if (focusApplied)
        {
            return scrollApplied ? "scrolled_and_focused" : "focused";
        }

        if (scrollApplied)
        {
            return "scrolled_into_view";
        }

        return null;
    }

    private static (AutomationElement Element, string Path, string ActionTaken)? TryInvokeAutomationElementOrDescendant(
        AutomationElement element,
        string path)
    {
        var actionTaken = TryInvokeAutomationElementDirectly(element);
        if (actionTaken is not null)
        {
            return (AutomationElement.FocusedElement ?? element, path, actionTaken);
        }

        var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < children.Count; i++)
        {
            var childPath = path == "root" ? $"{i}" : $"{path}/{i}";
            var invokedChild = TryInvokeAutomationElementOrDescendant(children[i], childPath);
            if (invokedChild is not null)
            {
                return invokedChild;
            }
        }

        return null;
    }

    private static (AutomationElement Element, string Path, string ActionTaken)? TryCloseAutomationWindowDirectly(
        AutomationElement element,
        string path)
    {
        var focusAction = TryFocusElement(element);

        try
        {
            if (TryGetPattern<WindowPattern>(element, WindowPattern.Pattern, out var windowPattern))
            {
                windowPattern.Close();
                Thread.Sleep(150);
                return (AutomationElement.FocusedElement ?? element, path, focusAction is null ? "closed_window" : $"{focusAction}_then_closed_window");
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (COMException)
        {
        }

        return null;
    }

    private static (AutomationElement Element, string Path, string ActionTaken)? TryInvokeAutomationElementByMouseClick(
        AutomationElement element,
        string path,
        WindowBounds windowBounds)
    {
        try
        {
            var clickableTarget = ResolveClickableElementOrDescendant(element, path, windowBounds);
            ClickAtScreenPoint(clickableTarget.ClickPoint, "left");
            var actionTaken = clickableTarget.PreparationActionTaken is null
                ? "left_clicked"
                : $"{clickableTarget.PreparationActionTaken}_then_left_clicked";
            return (clickableTarget.Element, clickableTarget.Path, actionTaken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static (AutomationElement Element, string Path) ResolveTextEntryElementOrDescendant(
        AutomationElement element,
        string path)
    {
        if (CanAcceptTextEntry(element))
        {
            return (element, path);
        }

        var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < children.Count; i++)
        {
            var childPath = path == "root" ? $"{i}" : $"{path}/{i}";
            try
            {
                return ResolveTextEntryElementOrDescendant(children[i], childPath);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException(
            $"Target element path '{path}' does not expose an editable text-entry element through UI Automation.");
    }

    private static bool CanAcceptTextEntry(AutomationElement element)
    {
        if (!GetIsEnabled(element))
        {
            return false;
        }

        ControlType controlType;
        try
        {
            controlType = element.Current.ControlType;
        }
        catch
        {
            return false;
        }

        if (controlType == ControlType.Edit)
        {
            return true;
        }

        if (!TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern) ||
            valuePattern.Current.IsReadOnly)
        {
            return false;
        }

        var normalizedName = NormalizeLabel(GetElementName(element));
        var normalizedAutomationId = NormalizeLabel(GetAutomationId(element));
        var normalizedClassName = NormalizeLabel(GetAutomationClassName(element));

        return GetHasKeyboardFocus(element) ||
               normalizedName.Contains("search", StringComparison.Ordinal) ||
               normalizedName.Contains("text", StringComparison.Ordinal) ||
               normalizedName.Contains("input", StringComparison.Ordinal) ||
               normalizedAutomationId.Contains("search", StringComparison.Ordinal) ||
               normalizedAutomationId.Contains("text", StringComparison.Ordinal) ||
               normalizedAutomationId.Contains("input", StringComparison.Ordinal) ||
               normalizedAutomationId.Contains("edit", StringComparison.Ordinal) ||
               normalizedClassName.Contains("textfield", StringComparison.Ordinal) ||
               normalizedClassName.Contains("edit", StringComparison.Ordinal);
    }

    private static string? TryInvokeAutomationElementDirectly(AutomationElement element)
    {
        var focusAction = TryFocusElement(element);

        try
        {
            if (TryGetPattern<InvokePattern>(element, InvokePattern.Pattern, out var invokePattern))
            {
                invokePattern.Invoke();
                Thread.Sleep(150);
                return focusAction is null ? "invoked" : $"{focusAction}_then_invoked";
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (COMException)
        {
        }

        return null;
    }

    private static bool TryResolveClickableScreenPoint(
        AutomationElement element,
        WindowBounds windowBounds,
        out ScreenPoint clickPoint)
    {
        clickPoint = default;

        var bounds = GetBoundingRectangle(element);
        if (bounds is null || bounds.Width <= 1 || bounds.Height <= 1 || GetIsOffscreen(element))
        {
            return false;
        }

        if (!TryResolveVisibleClickArea(bounds, windowBounds, out var visibleBounds))
        {
            return false;
        }

        if (IsLikelyImpreciseContainerClickTarget(
            GetControlTypeName(element),
            GetElementName(element),
            GetAutomationId(element),
            GetIsKeyboardFocusable(element),
            bounds,
            visibleBounds,
            windowBounds))
        {
            return false;
        }

        clickPoint = new ScreenPoint(
            (int)Math.Round(visibleBounds.Left + (visibleBounds.Width / 2d), MidpointRounding.AwayFromZero),
            (int)Math.Round(visibleBounds.Top + (visibleBounds.Height / 2d), MidpointRounding.AwayFromZero));
        return true;
    }

    internal static bool TryResolveVisibleClickArea(
        ElementBounds elementBounds,
        WindowBounds windowBounds,
        out ElementBounds visibleBounds)
    {
        var left = Math.Max(elementBounds.Left, windowBounds.Left);
        var top = Math.Max(elementBounds.Top, windowBounds.Top);
        var right = Math.Min(elementBounds.Left + elementBounds.Width, windowBounds.Left + windowBounds.Width);
        var bottom = Math.Min(elementBounds.Top + elementBounds.Height, windowBounds.Top + windowBounds.Height);

        if (right - left <= 1 || bottom - top <= 1)
        {
            visibleBounds = null!;
            return false;
        }

        visibleBounds = new ElementBounds(left, top, right - left, bottom - top);
        return true;
    }

    internal static bool IsLikelyImpreciseContainerClickTarget(
        string controlType,
        string name,
        string automationId,
        bool isKeyboardFocusable,
        ElementBounds elementBounds,
        ElementBounds visibleBounds,
        WindowBounds windowBounds)
    {
        if (isKeyboardFocusable)
        {
            return false;
        }

        var normalizedAutomationId = automationId.Trim();
        var shouldTreatAutomationIdAsGenericContainer = string.Equals(
            normalizedAutomationId,
            "appMountPoint",
            StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(name) ||
            (!string.IsNullOrWhiteSpace(normalizedAutomationId) && !shouldTreatAutomationIdAsGenericContainer))
        {
            return false;
        }

        if (controlType is not ("Group" or "Pane" or "Document" or "Window"))
        {
            return false;
        }

        var windowArea = Math.Max(1d, windowBounds.Width * windowBounds.Height);
        var visibleArea = Math.Max(0d, visibleBounds.Width) * Math.Max(0d, visibleBounds.Height);
        var widthRatio = visibleBounds.Width / Math.Max(1d, windowBounds.Width);
        var heightRatio = visibleBounds.Height / Math.Max(1d, windowBounds.Height);
        var areaRatio = visibleArea / windowArea;
        var isVeryTallContainer = elementBounds.Height >= windowBounds.Height * 2d;

        return areaRatio >= 0.35d ||
               (widthRatio >= 0.8d && heightRatio >= 0.35d) ||
               isVeryTallContainer;
    }

    private static string? TryFocusElement(AutomationElement element)
    {
        var selectionApplied = false;
        var scrollApplied = false;

        if (TryGetPattern<ScrollItemPattern>(element, ScrollItemPattern.Pattern, out var scrollItemPattern))
        {
            scrollItemPattern.ScrollIntoView();
            scrollApplied = true;
        }

        if (TryGetPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selectionItemPattern))
        {
            selectionItemPattern.Select();
            selectionApplied = true;
        }

        try
        {
            element.SetFocus();
            Thread.Sleep(150);
            return selectionApplied ? "selected_and_focused" : scrollApplied ? "scrolled_and_focused" : "focused";
        }
        catch (InvalidOperationException) when (selectionApplied)
        {
            Thread.Sleep(150);
            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement is not null && IsSameOrDescendantOf(focusedElement, element))
            {
                return "selected_and_focused";
            }
        }

        return null;
    }

    private static bool ShouldRetryInvocationWithKeyboard(
        AutomationElement windowElement,
        AutomationElement targetElement,
        UiSettleResult uiSettle)
    {
        if (uiSettle.WindowInteractionStateChangeCount > 0 ||
            uiSettle.StructureChangedEventCount > 0 ||
            uiSettle.AsyncContentLoadedEventCount > 0)
        {
            return false;
        }

        var focusedElement = AutomationElement.FocusedElement;
        if (focusedElement is null)
        {
            return false;
        }

        if (!IsSameOrDescendantOf(focusedElement, windowElement))
        {
            return false;
        }

        return IsSameOrDescendantOf(focusedElement, targetElement);
    }

    private static (AutomationElement Element, string Path, IReadOnlyList<string> NavigationKeys, string ActionTaken) InvokeElementViaKeyboardNavigation(
        nint handle,
        AutomationElement windowElement,
        AutomationElement targetElement,
        string targetPath)
    {
        var currentFocusedElement = EnsureFocusedElementWithinWindow(handle, windowElement);
        if (IsSameOrDescendantOf(currentFocusedElement, targetElement))
        {
            var currentPath = FindPathToElementOrAncestor(windowElement, "root", currentFocusedElement) ?? targetPath;
            PressEnterKey();
            return (currentFocusedElement, currentPath, [], "pressed_enter_on_focused_element");
        }

        var navigationKeys = new List<string>();
        for (var step = 0; step < MaxKeyboardNavigationSteps; step++)
        {
            currentFocusedElement = EnsureFocusedElementWithinWindow(handle, windowElement);
            var currentPath = FindPathToElementOrAncestor(windowElement, "root", currentFocusedElement);
            var candidateKeys = BuildKeyboardInvocationKeyPreference(currentPath, targetPath);
            var advanced = false;

            foreach (var candidateKey in candidateKeys)
            {
                var previousFocusedElement = currentFocusedElement;
                PressKeyboardNavigationKey(candidateKey);
                navigationKeys.Add(candidateKey);
                WaitWithMessagePump(KeyboardNavigationStepDelay);

                currentFocusedElement = EnsureFocusedElementWithinWindow(handle, windowElement);
                if (IsSameOrDescendantOf(currentFocusedElement, targetElement))
                {
                    var focusedPath = FindPathToElementOrAncestor(windowElement, "root", currentFocusedElement) ?? targetPath;
                    PressEnterKey();
                    return (currentFocusedElement, focusedPath, navigationKeys, "focused_via_keyboard_then_pressed_enter");
                }

                if (!AreSameAutomationElement(previousFocusedElement, currentFocusedElement))
                {
                    advanced = true;
                    break;
                }
            }

            if (!advanced)
            {
                break;
            }
        }

        throw new InvalidOperationException(
            $"Could not reach element path '{targetPath}' via keyboard navigation after {navigationKeys.Count} key presses.");
    }

    private static AutomationElement EnsureFocusedElementWithinWindow(nint handle, AutomationElement windowElement)
    {
        var focusedElement = AutomationElement.FocusedElement;
        if (focusedElement is not null && IsSameOrDescendantOf(focusedElement, windowElement))
        {
            return focusedElement;
        }

        FocusWindow(handle);
        focusedElement = AutomationElement.FocusedElement;
        if (focusedElement is not null && IsSameOrDescendantOf(focusedElement, windowElement))
        {
            return focusedElement;
        }

        return windowElement;
    }

    private static string? FindPathToElementOrAncestor(
        AutomationElement root,
        string path,
        AutomationElement targetElement)
    {
        if (!IsSameOrDescendantOf(targetElement, root))
        {
            return null;
        }

        var children = root.FindAll(TreeScope.Children, Condition.TrueCondition);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (!IsSameOrDescendantOf(targetElement, child))
            {
                continue;
            }

            var childPath = path == "root" ? $"{i}" : $"{path}/{i}";
            return FindPathToElementOrAncestor(child, childPath, targetElement) ?? childPath;
        }

        return path;
    }

    private static bool IsSameOrDescendantOf(AutomationElement element, AutomationElement ancestor)
    {
        var targetRuntimeId = GetRuntimeIdSignature(ancestor);
        if (targetRuntimeId is null)
        {
            return false;
        }

        var walker = TreeWalker.RawViewWalker;
        var current = element;
        while (current is not null)
        {
            if (string.Equals(GetRuntimeIdSignature(current), targetRuntimeId, StringComparison.Ordinal))
            {
                return true;
            }

            current = walker.GetParent(current);
        }

        return false;
    }

    private static bool AreSameAutomationElement(AutomationElement left, AutomationElement right)
    {
        var leftRuntimeId = GetRuntimeIdSignature(left);
        var rightRuntimeId = GetRuntimeIdSignature(right);
        return leftRuntimeId is not null &&
               string.Equals(leftRuntimeId, rightRuntimeId, StringComparison.Ordinal);
    }

    private static string? GetRuntimeIdSignature(AutomationElement element)
    {
        try
        {
            return string.Join(",", element.GetRuntimeId());
        }
        catch
        {
            return null;
        }
    }

    private static int NormalizeDepth(int maxDepth)
    {
        if (maxDepth < 1 || maxDepth > MaxBoundedUiDepth)
        {
            throw new InvalidOperationException($"maxDepth must be between 1 and {MaxBoundedUiDepth}.");
        }

        return maxDepth;
    }

    private static nint ResolveInteractionWindowHandle(WindowSelectionState selectionState)
    {
        var handle = InteractionWindowResolver.ResolveWindowForInteraction(
            selectionState,
            NativeMethods.IsWindow,
            FocusWindow,
            GetActiveWindowHandle);
        EnsureWindowExists(handle);
        return handle;
    }

    private static nint ResolveKeyboardInputWindowHandle(WindowSelectionState selectionState)
    {
        var handle = InteractionWindowResolver.ResolveWindowForInteraction(
            selectionState,
            NativeMethods.IsWindow,
            ActivateWindow,
            GetActiveWindowHandle);
        EnsureWindowExists(handle);
        return handle;
    }

    private static nint GetActiveWindowHandle()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException("No active foreground window is currently available.");
        }

        return handle;
    }

    private static void EnsureWindowExists(nint handle)
    {
        if (handle == nint.Zero || !NativeMethods.IsWindow(handle))
        {
            throw new InvalidOperationException($"Window handle '{FormatHandle(handle)}' is not valid.");
        }
    }

    private static void ActivateWindow(nint handle)
    {
        if (NativeMethods.IsIconic(handle))
        {
            _ = NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
        }

        _ = NativeMethods.BringWindowToTop(handle);
        _ = NativeMethods.SetForegroundWindow(handle);
    }

    private static void FocusWindow(nint handle)
    {
        ActivateWindow(handle);

        try
        {
            AutomationElement.FromHandle(handle).SetFocus();
        }
        catch (InvalidOperationException)
        {
            // Shell windows like the taskbar may reject direct UIA focus even after they are foregrounded.
        }

        Thread.Sleep(150);
    }

    private static UiSettleResult WaitForUiToSettle(
        nint handle,
        UiSettleObserver? observer = null,
        TimeSpan? initialDelay = null)
    {
        var effectiveInitialDelay = initialDelay ?? UiSettleInitialDelay;
        var startUtc = DateTime.UtcNow;
        var createdObserver = observer is null ? UiSettleObserver.TryCreate(handle) : null;
        var activeObserver = observer ?? createdObserver;
        var traceLines = DebugTrace.IsEnabled ? new List<string>() : null;
        AppendUiSettleTrace(
            traceLines,
            $"ui-settle begin handle={FormatHandle(handle)} observerAttached={activeObserver is not null} initialDelayMs={(int)effectiveInitialDelay.TotalMilliseconds} pollMs={(int)UiSettlePollInterval.TotalMilliseconds} timeoutMs={(int)UiSettleTimeout.TotalMilliseconds}");

        try
        {
            WaitWithMessagePump(effectiveInitialDelay);

            var deadlineUtc = DateTime.UtcNow + UiSettleTimeout;
            while (true)
            {
                var nowUtc = DateTime.UtcNow;
                var snapshot = CaptureUiSettleSnapshot(handle, activeObserver);
                if (IsUiChangeSettled(
                    snapshot.WindowAvailable,
                    snapshot.WindowInteractionState,
                    nowUtc,
                    snapshot.LastObservedChangeUtc,
                    UiSettlePollInterval))
                {
                    AppendUiSettleTrace(
                        traceLines,
                        DescribeUiSettleCheck(handle, nowUtc, snapshot, settled: true, timedOut: false, startUtc));
                    return BuildUiSettleResult(snapshot, nowUtc - startUtc, effectiveInitialDelay, timedOut: false, traceLines);
                }

                if (nowUtc >= deadlineUtc)
                {
                    AppendUiSettleTrace(
                        traceLines,
                        DescribeUiSettleCheck(handle, nowUtc, snapshot, settled: false, timedOut: true, startUtc));
                    return BuildUiSettleResult(snapshot, nowUtc - startUtc, effectiveInitialDelay, timedOut: true, traceLines);
                }

                AppendUiSettleTrace(
                    traceLines,
                    DescribeUiSettleCheck(handle, nowUtc, snapshot, settled: false, timedOut: false, startUtc));
                WaitWithMessagePump(UiSettlePollInterval);
            }
        }
        finally
        {
            createdObserver?.Dispose();
        }
    }

    private static void AppendUiSettleTrace(List<string>? traceLines, string message)
    {
        DebugTrace.WriteLine(message);
        if (traceLines is null)
        {
            return;
        }

        traceLines.Add(DebugTrace.FormatTimestampedLine(message, DateTimeOffset.Now));
    }

    private static UiSettleSnapshot CaptureUiSettleSnapshot(nint handle, UiSettleObserver? observer)
    {
        var windowAvailable = handle != nint.Zero && NativeMethods.IsWindow(handle);
        var interactionState = windowAvailable ? TryGetWindowInteractionState(handle) : null;
        var observerSnapshot = observer?.GetSnapshot() ?? UiSettleObserverSnapshot.Empty;

        return new UiSettleSnapshot(
            windowAvailable,
            interactionState,
            observerSnapshot.WindowInteractionStateChangeCount,
            observerSnapshot.StructureChangedEventCount,
            observerSnapshot.AsyncContentLoadedEventCount,
            observerSnapshot.LastObservedChangeUtc);
    }

    private static string DescribeUiSettleCheck(
        nint handle,
        DateTime nowUtc,
        UiSettleSnapshot snapshot,
        bool settled,
        bool timedOut,
        DateTime startUtc)
    {
        var elapsedMilliseconds = (int)Math.Round((nowUtc - startUtc).TotalMilliseconds, MidpointRounding.AwayFromZero);

        return
            $"ui-settle check handle={FormatHandle(handle)} windowAvailable={snapshot.WindowAvailable} interactionState={snapshot.WindowInteractionState?.ToString() ?? "null"} interactionChanges={snapshot.WindowInteractionStateChangeCount} structureChanges={snapshot.StructureChangedEventCount} asyncChanges={snapshot.AsyncContentLoadedEventCount} elapsedMs={elapsedMilliseconds} settled={settled} timedOut={timedOut}";
    }

    private static UiSettleResult BuildUiSettleResult(
        UiSettleSnapshot snapshot,
        TimeSpan elapsed,
        TimeSpan initialDelay,
        bool timedOut,
        List<string>? traceLines)
    {
        var status = !snapshot.WindowAvailable
            ? "window_unavailable"
            : timedOut
                ? "timed_out"
                : "settled";

        return new UiSettleResult(
            status,
            !timedOut || !snapshot.WindowAvailable,
            snapshot.WindowInteractionState?.ToString(),
            snapshot.WindowInteractionStateChangeCount,
            snapshot.StructureChangedEventCount,
            snapshot.AsyncContentLoadedEventCount,
            (int)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
            (int)Math.Round(initialDelay.TotalMilliseconds, MidpointRounding.AwayFromZero),
            traceLines ?? []);
    }

    private static bool IsSettledInteractionState(WindowInteractionState? interactionState)
    {
        return interactionState is
            WindowInteractionState.ReadyForUserInteraction or
            WindowInteractionState.BlockedByModalWindow;
    }

    private static WindowInteractionState? TryGetWindowInteractionState(nint handle)
    {
        try
        {
            var windowElement = AutomationElement.FromHandle(handle);
            return TryGetPattern<WindowPattern>(windowElement, WindowPattern.Pattern, out var windowPattern)
                ? windowPattern.Current.WindowInteractionState
                : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void WaitWithMessagePump(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var deadlineUtc = DateTime.UtcNow + duration;
        while (true)
        {
            var remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            Thread.Sleep(remaining < UiSettleWaitSlice ? remaining : UiSettleWaitSlice);
            Application.DoEvents();
        }
    }

    private static int GetProcessId(nint handle)
    {
        _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        return unchecked((int)processId);
    }

    private static string ExpandMenu(AutomationElement element)
    {
        if (TryGetPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out var expandCollapsePattern))
        {
            var state = expandCollapsePattern.Current.ExpandCollapseState;
            if (state is ExpandCollapseState.Collapsed or ExpandCollapseState.PartiallyExpanded)
            {
                expandCollapsePattern.Expand();
            }

            Thread.Sleep(150);
            return "expanded";
        }

        if (TryGetPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selectionItemPattern))
        {
            selectionItemPattern.Select();
            Thread.Sleep(150);
            return "selected";
        }

        if (TryGetPattern<InvokePattern>(element, InvokePattern.Pattern, out var invokePattern))
        {
            invokePattern.Invoke();
            Thread.Sleep(150);
            return "invoked";
        }

        throw new InvalidOperationException($"Menu item '{GetElementName(element)}' could not be expanded.");
    }

    private static string InvokeAction(AutomationElement element, bool allowExpandOnly)
    {
        if (TryGetPattern<InvokePattern>(element, InvokePattern.Pattern, out var invokePattern))
        {
            invokePattern.Invoke();
            Thread.Sleep(150);
            return "invoked";
        }

        if (TryGetPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selectionItemPattern))
        {
            selectionItemPattern.Select();
            Thread.Sleep(150);
            return "selected";
        }

        if (allowExpandOnly && TryGetPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out var expandCollapsePattern))
        {
            expandCollapsePattern.Expand();
            Thread.Sleep(150);
            return "expanded";
        }

        throw new InvalidOperationException($"Menu item '{GetElementName(element)}' could not be invoked.");
    }

    private static AutomationElement? WaitForMenuItem(int processId, string label)
    {
        var deadline = DateTime.UtcNow + MenuSearchTimeout;
        var visibleMenuItemCondition = new AndCondition(
            MenuItemCondition,
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false));

        while (DateTime.UtcNow <= deadline)
        {
            var allMenuItems = AutomationElement.RootElement.FindAll(TreeScope.Descendants, visibleMenuItemCondition);
            var match = FindBestMatch(Enumerate(allMenuItems), label);
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(MenuSearchInterval);
        }

        return null;
    }

    private static AutomationElement? FindBestMatch(IEnumerable<AutomationElement> candidates, string expectedLabel)
    {
        var normalizedExpected = NormalizeLabel(expectedLabel);
        if (string.IsNullOrWhiteSpace(normalizedExpected))
        {
            return null;
        }

        var exactMatch = candidates.FirstOrDefault(candidate =>
            string.Equals(NormalizeLabel(GetElementName(candidate)), normalizedExpected, StringComparison.Ordinal));

        if (exactMatch is not null)
        {
            return exactMatch;
        }

        return candidates.FirstOrDefault(candidate =>
        {
            var normalizedActual = NormalizeLabel(GetElementName(candidate));
            return normalizedActual.StartsWith(normalizedExpected, StringComparison.Ordinal) ||
                   normalizedActual.Contains(normalizedExpected, StringComparison.Ordinal);
        });
    }

    private static IEnumerable<AutomationElement> Enumerate(AutomationElementCollection collection)
    {
        for (var i = 0; i < collection.Count; i++)
        {
            yield return collection[i];
        }
    }

    private static string GetElementName(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static string GetControlTypeName(AutomationElement element)
    {
        try
        {
            var programmaticName = element.Current.ControlType.ProgrammaticName ?? string.Empty;
            var parts = programmaticName.Split('.');
            return parts[^1];
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string GetAutomationId(AutomationElement element)
    {
        try
        {
            return element.Current.AutomationId ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetAutomationClassName(AutomationElement element)
    {
        try
        {
            return element.Current.ClassName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool GetIsEnabled(AutomationElement element)
    {
        try
        {
            return element.Current.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static bool GetIsOffscreen(AutomationElement element)
    {
        try
        {
            return element.Current.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    private static bool GetHasKeyboardFocus(AutomationElement element)
    {
        try
        {
            return element.Current.HasKeyboardFocus;
        }
        catch
        {
            return false;
        }
    }

    private static bool GetIsKeyboardFocusable(AutomationElement element)
    {
        try
        {
            return element.Current.IsKeyboardFocusable;
        }
        catch
        {
            return false;
        }
    }

    private static ElementBounds? GetBoundingRectangle(AutomationElement element)
    {
        try
        {
            var rectangle = element.Current.BoundingRectangle;
            if (rectangle.IsEmpty)
            {
                return null;
            }

            return new ElementBounds(
                rectangle.Left,
                rectangle.Top,
                rectangle.Width,
                rectangle.Height);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeLabel(string value)
    {
        var withoutAccessKeys = value
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("...", string.Empty, StringComparison.Ordinal)
            .Replace("…", string.Empty, StringComparison.Ordinal);

        return Regex.Replace(withoutAccessKeys, "\\s+", " ").Trim().ToLowerInvariant();
    }

    private static string BuildScreenshotFileName(nint handle, string title)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var titleSegment = SanitizeFileNameSegment(title);
        return $"{timestamp}-{titleSegment}-{FormatHandle(handle)}.png";
    }

    private static bool TryGetPattern<TPattern>(
        AutomationElement element,
        AutomationPattern pattern,
        out TPattern resolvedPattern)
        where TPattern : class
    {
        if (element.TryGetCurrentPattern(pattern, out var patternObject) && patternObject is TPattern typedPattern)
        {
            resolvedPattern = typedPattern;
            return true;
        }

        resolvedPattern = null!;
        return false;
    }

    private static nint ParseHandle(string value)
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

    private static string FormatHandle(nint handle) => $"0x{handle.ToInt64():X8}";
}

internal sealed record WindowListResult(
    string? SelectedWindowHandle,
    IReadOnlyList<WindowSummary> Windows);

internal sealed record WindowSummary(
    string Handle,
    string Title,
    string ClassName,
    int ProcessId,
    WindowBounds Bounds,
    bool IsSelected);

internal sealed record WindowBounds(
    int Left,
    int Top,
    int Width,
    int Height);

internal sealed record ElementBounds(
    double Left,
    double Top,
    double Width,
    double Height);

internal sealed record WindowDescriptor(
    string Handle,
    string Title,
    string ClassName,
    int ProcessId,
    WindowBounds Bounds);

internal sealed record ImageDimensions(
    int Width,
    int Height);

internal readonly record struct ScreenPoint(
    int X,
    int Y);

internal sealed record UiElementSnapshot(
    string Path,
    string UiPath,
    string Name,
    string ControlType,
    string AutomationId,
    string ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    bool HasKeyboardFocus,
    bool IsKeyboardFocusable,
    IReadOnlyList<string> AvailableActions,
    ElementBounds? Bounds,
    IReadOnlyList<UiElementSnapshot> Children);

internal sealed class UiElementSnapshotJsonConverter : JsonConverter<UiElementSnapshot>
{
    public override UiElementSnapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("UiElementSnapshot deserialization is not supported.");

    public override void Write(Utf8JsonWriter writer, UiElementSnapshot value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("Path", value.Path);
        writer.WriteString("UiPath", value.UiPath);
        WriteStringIfMeaningful(writer, "Name", value.Name);
        writer.WriteString("ControlType", value.ControlType);
        WriteStringIfMeaningful(writer, "AutomationId", value.AutomationId);
        WriteStringIfMeaningful(writer, "ClassName", value.ClassName);
        WriteBooleanIfTrue(writer, "IsEnabled", value.IsEnabled);
        WriteBooleanIfTrue(writer, "IsOffscreen", value.IsOffscreen);
        WriteBooleanIfTrue(writer, "HasKeyboardFocus", value.HasKeyboardFocus);
        WriteBooleanIfTrue(writer, "IsKeyboardFocusable", value.IsKeyboardFocusable);
        WriteArrayIfNotEmpty(writer, "AvailableActions", value.AvailableActions, options);

        if (value.Bounds is not null)
        {
            writer.WritePropertyName("Bounds");
            JsonSerializer.Serialize(writer, value.Bounds, options);
        }

        WriteArrayIfNotEmpty(writer, "Children", value.Children, options);

        writer.WriteEndObject();
    }

    private static void WriteStringIfMeaningful(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteBooleanIfTrue(Utf8JsonWriter writer, string propertyName, bool value)
    {
        if (value)
        {
            writer.WriteBoolean(propertyName, true);
        }
    }

    private static void WriteArrayIfNotEmpty<T>(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<T>? values,
        JsonSerializerOptions options)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        JsonSerializer.Serialize(writer, values, options);
    }
}

internal sealed record WindowTreeResult(
    WindowDescriptor Window,
    int? MaxDepth,
    bool FullDepth,
    UiElementSnapshot ElementTree);

internal sealed record WindowScreenshotResult(
    WindowDescriptor Window,
    string ImagePath,
    string ImageFormat,
    ImageDimensions ImageSize);

internal sealed record FocusedElementTreeResult(
    WindowDescriptor Window,
    int MaxDepth,
    UiElementSnapshot FocusedElement);

internal sealed record FocusedElementResult(
    WindowDescriptor Window,
    UiElementSnapshot FocusedElement,
    string ActionTaken,
    UiSettleResult UiSettle);

internal sealed record ElementClickResult(
    WindowDescriptor Window,
    UiElementSnapshot ClickedElement,
    string MouseButton,
    ScreenPoint ClickPoint,
    string? PreparationActionTaken,
    string ActionTaken,
    UiSettleResult UiSettle);

internal sealed record ElementInvocationResult(
    WindowDescriptor Window,
    UiElementSnapshot InvokedElement,
    string Strategy,
    IReadOnlyList<string> NavigationKeys,
    int NavigationStepCount,
    string ActionTaken,
    UiSettleResult UiSettle);

internal sealed record ElementValueSetResult(
    WindowDescriptor Window,
    UiElementSnapshot TargetElement,
    int TextLength,
    string ActionTaken,
    UiSettleResult UiSettle);

internal sealed record KeyboardInputResult(
    WindowDescriptor Window,
    string InputMode,
    string? Key,
    IReadOnlyList<string> Modifiers,
    int RepeatCount,
    int? TextLength,
    string ActionTaken,
    UiSettleResult UiSettle);

internal sealed record MainMenuListResult(
    WindowDescriptor Window,
    UiElementSnapshot MenuBar,
    IReadOnlyList<MainMenuSection> Menus);

internal sealed record MainMenuSection(
    string Label,
    string MenuPath,
    IReadOnlyList<MenuItemSummary> Items);

internal sealed record ContextMenuListResult(
    WindowDescriptor Window,
    UiElementSnapshot FocusedElement,
    string OpenActionTaken,
    IReadOnlyList<MenuItemSummary> Items);

internal sealed record MenuItemSummary(
    string Label,
    string MenuPath,
    string ControlType,
    bool IsEnabled,
    bool HasSubmenu,
    bool IsSeparator,
    IReadOnlyList<string> AvailableActions);

internal sealed record WindowSelectionResult(
    string Handle,
    string Title,
    string ClassName,
    int ProcessId,
    bool WasFocused,
    UiSettleResult? UiSettle);

internal sealed record TaskbarElementListResult(
    WindowDescriptor TaskbarWindow,
    UiElementSnapshot HostElement,
    IReadOnlyList<TaskbarElementSummary> Elements);

internal sealed record TaskbarElementSummary(
    string Path,
    string Name,
    string ControlType,
    string AutomationId,
    string ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    bool HasKeyboardFocus,
    bool IsKeyboardFocusable,
    IReadOnlyList<string> AvailableActions,
    ElementBounds? Bounds,
    bool IsAppButton);

internal sealed record TaskbarAppActivationResult(
    WindowDescriptor TaskbarWindow,
    UiElementSnapshot HostElement,
    TaskbarElementSummary ActivatedElement,
    string ActionTaken,
    WindowSelectionResult? SelectedWindow,
    UiSettleResult UiSettle);

internal sealed record TaskbarAppSearchResult(
    WindowDescriptor TaskbarWindow,
    UiElementSnapshot HostElement,
    TaskbarElementSummary SearchElement,
    UiElementSnapshot SearchInputElement,
    string Query,
    string SearchActionTaken,
    string TextEntryActionTaken,
    string LaunchActionTaken,
    WindowSelectionResult? SelectedWindow,
    UiSettleResult UiSettle);

internal sealed record MenuInvocationResult(
    string Handle,
    string Title,
    int ProcessId,
    string MenuPath,
    string ActionTaken,
    UiSettleResult UiSettle);

internal sealed record ContextMenuInvocationResult(
    string Handle,
    string Title,
    int ProcessId,
    string MenuPath,
    string OpenActionTaken,
    string ActionTaken,
    UiSettleResult UiSettle);

internal sealed record UiSettleResult(
    string Status,
    bool Completed,
    string? WindowInteractionState,
    int WindowInteractionStateChangeCount,
    int StructureChangedEventCount,
    int AsyncContentLoadedEventCount,
    int ElapsedMilliseconds,
    int InitialDelayMilliseconds,
    IReadOnlyList<string> TraceLines);

internal readonly record struct UiSettleSnapshot(
    bool WindowAvailable,
    WindowInteractionState? WindowInteractionState,
    int WindowInteractionStateChangeCount,
    int StructureChangedEventCount,
    int AsyncContentLoadedEventCount,
    DateTime? LastObservedChangeUtc);

internal readonly record struct UiSettleObserverSnapshot(
    int WindowInteractionStateChangeCount,
    int StructureChangedEventCount,
    int AsyncContentLoadedEventCount,
    DateTime? LastObservedChangeUtc)
{
    internal static UiSettleObserverSnapshot Empty => new(0, 0, 0, null);
}

internal sealed class UiSettleObserver : IDisposable
{
    private readonly nint _handle;
    private readonly AutomationElement _windowElement;
    private readonly AutomationPropertyChangedEventHandler _propertyChangedHandler;
    private readonly StructureChangedEventHandler _structureChangedHandler;
    private readonly AutomationEventHandler _asyncContentLoadedHandler;
    private readonly bool _propertyHandlerRegistered;
    private readonly bool _structureHandlerRegistered;
    private readonly bool _asyncContentLoadedHandlerRegistered;
    private int _disposed;
    private int _windowInteractionStateChangeCount;
    private int _structureChangedEventCount;
    private int _asyncContentLoadedEventCount;
    private long _lastObservedChangeUtcTicks;

    private UiSettleObserver(nint handle, AutomationElement windowElement)
    {
        _handle = handle;
        _windowElement = windowElement;
        _propertyChangedHandler = OnPropertyChanged;
        _structureChangedHandler = OnStructureChanged;
        _asyncContentLoadedHandler = OnAsyncContentLoaded;

        try
        {
            Automation.AddAutomationPropertyChangedEventHandler(
                _windowElement,
                TreeScope.Element,
                _propertyChangedHandler,
                WindowPattern.WindowInteractionStateProperty);
            _propertyHandlerRegistered = true;
        }
        catch
        {
        }

        try
        {
            Automation.AddStructureChangedEventHandler(
                _windowElement,
                TreeScope.Subtree,
                _structureChangedHandler);
            _structureHandlerRegistered = true;
        }
        catch
        {
        }

        try
        {
            Automation.AddAutomationEventHandler(
                AutomationElement.AsyncContentLoadedEvent,
                _windowElement,
                TreeScope.Subtree,
                _asyncContentLoadedHandler);
            _asyncContentLoadedHandlerRegistered = true;
        }
        catch
        {
        }
    }

    public static UiSettleObserver? TryCreate(nint handle)
    {
        if (handle == nint.Zero || !NativeMethods.IsWindow(handle))
        {
            return null;
        }

        try
        {
            return new UiSettleObserver(handle, AutomationElement.FromHandle(handle));
        }
        catch
        {
            return null;
        }
    }

    public UiSettleObserverSnapshot GetSnapshot()
    {
        var ticks = Interlocked.Read(ref _lastObservedChangeUtcTicks);
        DateTime? lastObservedChangeUtc = ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);

        return new UiSettleObserverSnapshot(
            Volatile.Read(ref _windowInteractionStateChangeCount),
            Volatile.Read(ref _structureChangedEventCount),
            Volatile.Read(ref _asyncContentLoadedEventCount),
            lastObservedChangeUtc);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_propertyHandlerRegistered)
        {
            try
            {
                Automation.RemoveAutomationPropertyChangedEventHandler(_windowElement, _propertyChangedHandler);
            }
            catch
            {
            }
        }

        if (_structureHandlerRegistered)
        {
            try
            {
                Automation.RemoveStructureChangedEventHandler(_windowElement, _structureChangedHandler);
            }
            catch
            {
            }
        }

        if (_asyncContentLoadedHandlerRegistered)
        {
            try
            {
                Automation.RemoveAutomationEventHandler(
                    AutomationElement.AsyncContentLoadedEvent,
                    _windowElement,
                    _asyncContentLoadedHandler);
            }
            catch
            {
            }
        }
    }

    private void OnPropertyChanged(object sender, AutomationPropertyChangedEventArgs e)
    {
        if (e.Property != WindowPattern.WindowInteractionStateProperty)
        {
            return;
        }

        var count = Interlocked.Increment(ref _windowInteractionStateChangeCount);
        NoteObservedChange();
        DebugTrace.WriteLine(
            $"ui-settle event handle={FormatHandle(_handle)} type=window_interaction_state count={count} currentState={TryGetCurrentWindowInteractionStateDescription(_windowElement)}");
    }

    private void OnStructureChanged(object sender, StructureChangedEventArgs e)
    {
        var count = Interlocked.Increment(ref _structureChangedEventCount);
        NoteObservedChange();
        DebugTrace.WriteLine(
            $"ui-settle event handle={FormatHandle(_handle)} type=structure_changed count={count} changeType={e.StructureChangeType}");
    }

    private void OnAsyncContentLoaded(object sender, AutomationEventArgs e)
    {
        var count = Interlocked.Increment(ref _asyncContentLoadedEventCount);
        NoteObservedChange();
        DebugTrace.WriteLine(
            $"ui-settle event handle={FormatHandle(_handle)} type=async_content_loaded count={count}");
    }

    private void NoteObservedChange()
    {
        Interlocked.Exchange(ref _lastObservedChangeUtcTicks, DateTime.UtcNow.Ticks);
    }

    private static string TryGetCurrentWindowInteractionStateDescription(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(WindowPattern.Pattern, out var patternObject) &&
                   patternObject is WindowPattern windowPattern
                ? windowPattern.Current.WindowInteractionState.ToString()
                : "null";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string FormatHandle(nint handle) => $"0x{handle.ToInt64():X8}";
}

internal enum TaskbarActivationMode
{
    Invoke,
    Select,
    Toggle,
    FocusAndPressEnter,
    FocusOnly,
}
