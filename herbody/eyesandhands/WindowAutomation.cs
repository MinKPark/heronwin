using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace HeronWin.HerBody.EyesAndHands;

internal static class WindowAutomation
{
    private const int MaxUiDepth = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly Condition MenuBarCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar);

    private static readonly Condition MenuItemCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem);

    private static readonly string[] TaskbarHostAutomationIds = ["TaskbarFrame"];
    private static readonly string[] TaskbarHostClassNames =
    [
        "Taskbar.TaskbarFrameAutomationPeer",
        "MSTaskSwWClass",
        "MSTaskListWClass",
    ];

    private static readonly TimeSpan MenuSearchTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MenuSearchInterval = TimeSpan.FromMilliseconds(150);

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
        var handle = ResolveWindowHandle(windowHandle, titleContains);
        EnsureWindowExists(handle);
        FocusWindow(handle);
        selectionState.SetSelectedHandle(handle);

        return BuildSelectionResult(handle, wasFocused: true);
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

    internal static TaskbarAppActivationResult ActivateTaskbarApp(
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
        FocusWindow(taskbarHandle);

        var targetElement = ResolveElementPath(taskbarRoot, target.Path);
        var actionTaken = ActivateTaskbarElement(targetElement);

        return new TaskbarAppActivationResult(
            BuildWindowDescriptor(taskbarHandle),
            BuildElementSnapshot(hostElement, hostPath, [], includeChildren: false),
            BuildTaskbarElementSummary(targetElement, target.Path),
            actionTaken);
    }

    internal static WindowTreeResult DescribeActiveWindow(
        WindowSelectionState selectionState,
        int maxDepth)
    {
        var normalizedDepth = NormalizeDepth(maxDepth);
        var handle = ResolveInteractionWindowHandle(selectionState);

        var windowElement = AutomationElement.FromHandle(handle);
        return new WindowTreeResult(
            BuildWindowDescriptor(handle),
            normalizedDepth,
            CaptureElementTree(windowElement, normalizedDepth, "root"));
    }

    internal static FocusedElementResult FocusActiveWindowElement(
        WindowSelectionState selectionState,
        string elementPath)
    {
        var handle = ResolveInteractionWindowHandle(selectionState);

        var normalizedPath = NormalizeElementPath(elementPath);
        var windowElement = AutomationElement.FromHandle(handle);
        var targetElement = ResolveElementPath(windowElement, normalizedPath);
        var focusedTarget = FocusAutomationElementOrDescendant(targetElement, normalizedPath);

        return new FocusedElementResult(
            BuildWindowDescriptor(handle),
            BuildElementSnapshot(focusedTarget.Element, focusedTarget.Path, [], includeChildren: false),
            focusedTarget.ActionTaken);
    }

    internal static FocusedElementTreeResult DescribeFocusedElement(
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

        return new FocusedElementTreeResult(
            BuildWindowDescriptor(handle),
            normalizedDepth,
            CaptureElementTree(focusedElement, normalizedDepth, "focused"));
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

        var segments = menuPath.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("menuPath must contain at least one menu item.");
        }

        var windowElement = AutomationElement.FromHandle(handle);
        var menuBar = windowElement.FindFirst(TreeScope.Descendants, MenuBarCondition);
        if (menuBar is null)
        {
            throw new InvalidOperationException("The selected window does not expose a main menu through UI Automation.");
        }

        var topLevelItem =
            FindBestMatch(Enumerate(menuBar.FindAll(TreeScope.Children, MenuItemCondition)), segments[0]) ??
            FindBestMatch(Enumerate(menuBar.FindAll(TreeScope.Descendants, MenuItemCondition)), segments[0]);

        if (topLevelItem is null)
        {
            throw new InvalidOperationException($"Could not find the top-level menu item '{segments[0]}'.");
        }

        string actionTaken = "none";
        if (segments.Length == 1)
        {
            actionTaken = InvokeAction(topLevelItem, allowExpandOnly: true);
            return BuildMenuInvocationResult(handle, menuPath, actionTaken);
        }

        ExpandMenu(topLevelItem);

        var processId = GetProcessId(handle);
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

        return BuildMenuInvocationResult(handle, menuPath, actionTaken);
    }

    private static WindowSelectionResult BuildSelectionResult(nint handle, bool wasFocused)
    {
        _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);

        return new WindowSelectionResult(
            FormatHandle(handle),
            NativeMethods.GetWindowText(handle),
            NativeMethods.GetClassName(handle),
            unchecked((int)processId),
            wasFocused);
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

    private static UiElementSnapshot CaptureElementTree(
        AutomationElement element,
        int remainingLevels,
        string path)
    {
        var children = new List<UiElementSnapshot>();
        if (remainingLevels > 1)
        {
            var childElements = element.FindAll(TreeScope.Children, Condition.TrueCondition);
            for (var i = 0; i < childElements.Count; i++)
            {
                var childPath = path is "root" or "focused"
                    ? $"{i}"
                    : $"{path}/{i}";
                children.Add(CaptureElementTree(childElements[i], remainingLevels - 1, childPath));
            }
        }

        return BuildElementSnapshot(element, path, children, includeChildren: true);
    }

    private static UiElementSnapshot BuildElementSnapshot(
        AutomationElement element,
        string path,
        IReadOnlyList<UiElementSnapshot> children,
        bool includeChildren)
    {
        return new UiElementSnapshot(
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

    private static MenuInvocationResult BuildMenuInvocationResult(nint handle, string menuPath, string actionTaken)
    {
        var selection = BuildSelectionResult(handle, wasFocused: true);

        return new MenuInvocationResult(
            selection.Handle,
            selection.Title,
            selection.ProcessId,
            menuPath,
            actionTaken);
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

    private static TaskbarElementSummary ResolveTaskbarAppTarget(
        IReadOnlyList<TaskbarElementSummary> visibleApps,
        string? elementPath,
        string? titleContains,
        string? automationIdContains)
    {
        if (!string.IsNullOrWhiteSpace(elementPath))
        {
            var normalizedPath = NormalizeElementPath(elementPath);
            return visibleApps.FirstOrDefault(app => string.Equals(app.Path, normalizedPath, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
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

        return matches.Length switch
        {
            0 => throw new InvalidOperationException(
                $"No visible taskbar app button matched {argumentName} '{expectedSubstring}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Multiple visible taskbar app buttons matched '{expectedSubstring}': {string.Join(", ", matches.Select(match => $"{match.Path} ({match.Name})"))}. Use elementPath instead."),
        };
    }

    private static string ActivateTaskbarElement(AutomationElement element)
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

        if (TryGetPattern<TogglePattern>(element, TogglePattern.Pattern, out var togglePattern))
        {
            togglePattern.Toggle();
            Thread.Sleep(150);
            return "toggled";
        }

        element.SetFocus();
        Thread.Sleep(150);
        return "focused";
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
        if (maxDepth < 1 || maxDepth > MaxUiDepth)
        {
            throw new InvalidOperationException($"maxDepth must be between 1 and {MaxUiDepth}.");
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

    private static void FocusWindow(nint handle)
    {
        if (NativeMethods.IsIconic(handle))
        {
            _ = NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
        }

        _ = NativeMethods.BringWindowToTop(handle);
        _ = NativeMethods.SetForegroundWindow(handle);
        AutomationElement.FromHandle(handle).SetFocus();
        Thread.Sleep(150);
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

internal sealed record UiElementSnapshot(
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
    IReadOnlyList<UiElementSnapshot> Children);

internal sealed record WindowTreeResult(
    WindowDescriptor Window,
    int MaxDepth,
    UiElementSnapshot ElementTree);

internal sealed record FocusedElementTreeResult(
    WindowDescriptor Window,
    int MaxDepth,
    UiElementSnapshot FocusedElement);

internal sealed record FocusedElementResult(
    WindowDescriptor Window,
    UiElementSnapshot FocusedElement,
    string ActionTaken);

internal sealed record WindowSelectionResult(
    string Handle,
    string Title,
    string ClassName,
    int ProcessId,
    bool WasFocused);

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
    string ActionTaken);

internal sealed record MenuInvocationResult(
    string Handle,
    string Title,
    int ProcessId,
    string MenuPath,
    string ActionTaken);
