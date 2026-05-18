using System.Runtime.InteropServices;

namespace HeronWin.Tools.DesktopAutomation;

internal static class NativeUia
{
    private const int TreeScopeChildren = 0x2;
    private const int TreeScopeDescendants = 0x4;

    private const int UIA_BoundingRectanglePropertyId = 30001;
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_HasKeyboardFocusPropertyId = 30008;
    private const int UIA_IsKeyboardFocusablePropertyId = 30009;
    private const int UIA_IsEnabledPropertyId = 30010;
    private const int UIA_AutomationIdPropertyId = 30011;
    private const int UIA_ClassNamePropertyId = 30012;
    private const int UIA_IsOffscreenPropertyId = 30022;
    private const int UIA_IsDockPatternAvailablePropertyId = 30027;
    private const int UIA_IsExpandCollapsePatternAvailablePropertyId = 30028;
    private const int UIA_IsInvokePatternAvailablePropertyId = 30031;
    private const int UIA_IsRangeValuePatternAvailablePropertyId = 30033;
    private const int UIA_IsScrollPatternAvailablePropertyId = 30034;
    private const int UIA_IsScrollItemPatternAvailablePropertyId = 30035;
    private const int UIA_IsSelectionItemPatternAvailablePropertyId = 30036;
    private const int UIA_IsTogglePatternAvailablePropertyId = 30041;
    private const int UIA_IsTransformPatternAvailablePropertyId = 30042;
    private const int UIA_IsValuePatternAvailablePropertyId = 30043;
    private const int UIA_IsWindowPatternAvailablePropertyId = 30044;
    private const int UIA_AriaRolePropertyId = 30101;
    private const int UIA_AriaPropertiesPropertyId = 30102;
    private const int UIA_IsSelectedPropertyId = 30079;
    private const int UIA_CanMovePropertyId = 30087;
    private const int UIA_CanResizePropertyId = 30088;
    private const int UIA_CanRotatePropertyId = 30089;

    private static readonly IReadOnlyDictionary<int, string> ControlTypeNames =
        new Dictionary<int, string>
        {
            [50000] = "Button",
            [50001] = "Calendar",
            [50002] = "CheckBox",
            [50003] = "ComboBox",
            [50004] = "Edit",
            [50005] = "Hyperlink",
            [50006] = "Image",
            [50007] = "ListItem",
            [50008] = "List",
            [50009] = "Menu",
            [50010] = "MenuBar",
            [50011] = "MenuItem",
            [50012] = "ProgressBar",
            [50013] = "RadioButton",
            [50014] = "ScrollBar",
            [50015] = "Slider",
            [50016] = "Spinner",
            [50017] = "StatusBar",
            [50018] = "Tab",
            [50019] = "TabItem",
            [50020] = "Text",
            [50021] = "ToolBar",
            [50022] = "ToolTip",
            [50023] = "Tree",
            [50024] = "TreeItem",
            [50025] = "Custom",
            [50026] = "Group",
            [50027] = "Thumb",
            [50028] = "DataGrid",
            [50029] = "DataItem",
            [50030] = "Document",
            [50031] = "SplitButton",
            [50032] = "Window",
            [50033] = "Pane",
            [50034] = "Header",
            [50035] = "HeaderItem",
            [50036] = "Table",
            [50037] = "TitleBar",
            [50038] = "Separator",
        };

    public static UiElementSnapshot CaptureWindowTree(nint windowHandle)
    {
        var automation = CreateAutomation();
        ThrowIfFailed(automation.ElementFromHandle(windowHandle, out var windowElement), "ElementFromHandle");
        return CaptureElementTree(automation, windowElement, remainingLevels: null, "root", "root");
    }

    public static NativeFocusedElementTree CaptureFocusedElementTree(nint windowHandle)
    {
        var automation = CreateAutomation();
        ThrowIfFailed(automation.ElementFromHandle(windowHandle, out var windowElement), "ElementFromHandle");
        ThrowIfFailed(automation.GetFocusedElement(out var focusedElement), "GetFocusedElement");

        var focusedElementUiPath = TryFindElementPath(automation, windowElement, focusedElement, "root")
            ?? throw new InvalidOperationException(
                "Could not resolve the focused UI element's path within the selected window.");

        var focusedTree = CaptureElementTree(
            automation,
            focusedElement,
            remainingLevels: null,
            "focused",
            focusedElementUiPath);
        return new NativeFocusedElementTree(focusedElementUiPath, focusedTree);
    }

    internal static string ControlTypeNameFromId(int controlTypeId)
        => ControlTypeNames.TryGetValue(controlTypeId, out var name)
            ? name
            : $"ControlType_{controlTypeId}";

    private static UiElementSnapshot CaptureElementTree(
        IUIAutomation automation,
        IUIAutomationElement element,
        int? remainingLevels,
        string path,
        string uiPath)
    {
        var children = CaptureChildSnapshots(automation, element, remainingLevels, path, uiPath);
        return BuildElementSnapshot(element, path, children, includeChildren: true, uiPath);
    }

    private static List<UiElementSnapshot> CaptureChildSnapshots(
        IUIAutomation automation,
        IUIAutomationElement element,
        int? remainingLevels,
        string path,
        string uiPath)
    {
        var children = new List<UiElementSnapshot>();
        if (remainingLevels.HasValue && remainingLevels.Value <= 1)
        {
            return children;
        }

        var childElements = FindChildren(automation, element);
        int? nextRemainingLevels = remainingLevels.HasValue ? remainingLevels.Value - 1 : null;
        for (var i = 0; i < childElements.Count; i++)
        {
            var childPath = BuildChildPath(path, i);
            var childUiPath = BuildChildPath(uiPath, i);
            if (nextRemainingLevels.HasValue)
            {
                AppendBoundedChildSnapshot(
                    automation,
                    children,
                    childElements[i],
                    childPath,
                    childUiPath,
                    nextRemainingLevels.Value);
                continue;
            }

            children.Add(CaptureElementTree(automation, childElements[i], remainingLevels: null, childPath, childUiPath));
        }

        return children;
    }

    private static void AppendBoundedChildSnapshot(
        IUIAutomation automation,
        List<UiElementSnapshot> snapshots,
        IUIAutomationElement element,
        string path,
        string uiPath,
        int remainingLevels)
    {
        if (ShouldPromoteChildrenInBoundedTree(element))
        {
            var childElements = FindChildren(automation, element);
            if (childElements.Count > 0)
            {
                for (var i = 0; i < childElements.Count; i++)
                {
                    AppendBoundedChildSnapshot(
                        automation,
                        snapshots,
                        childElements[i],
                        BuildChildPath(path, i),
                        BuildChildPath(uiPath, i),
                        remainingLevels);
                }

                return;
            }
        }

        var snapshot = CaptureElementTree(automation, element, remainingLevels, path, uiPath);
        if (WindowAutomation.ShouldOmitElementInBoundedTree(
            snapshot.ControlType,
            snapshot.Name,
            snapshot.AutomationId,
            snapshot.AvailableActions,
            snapshot.IsKeyboardFocusable,
            snapshot.HasKeyboardFocus,
            snapshot.Children.Count))
        {
            return;
        }

        snapshots.Add(snapshot);
    }

    private static IReadOnlyList<IUIAutomationElement> FindChildren(
        IUIAutomation automation,
        IUIAutomationElement element)
    {
        ThrowIfFailed(automation.get_RawViewCondition(out var condition), "RawViewCondition");
        ThrowIfFailed(element.FindAll(TreeScopeChildren, condition, out var children), "FindAll children");
        ThrowIfFailed(children.get_Length(out var count), "ElementArray.Length");

        var elements = new List<IUIAutomationElement>(count);
        for (var i = 0; i < count; i++)
        {
            ThrowIfFailed(children.GetElement(i, out var child), "ElementArray.GetElement");
            elements.Add(child);
        }

        return elements;
    }

    private static string? TryFindElementPath(
        IUIAutomation automation,
        IUIAutomationElement root,
        IUIAutomationElement target,
        string path)
    {
        if (AreSameElement(automation, root, target))
        {
            return path;
        }

        var childElements = FindChildren(automation, root);
        for (var i = 0; i < childElements.Count; i++)
        {
            var childPath = BuildChildPath(path, i);
            var found = TryFindElementPath(automation, childElements[i], target, childPath);
            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }
        }

        return null;
    }

    private static bool AreSameElement(
        IUIAutomation automation,
        IUIAutomationElement first,
        IUIAutomationElement second)
    {
        var hr = automation.CompareElements(first, second, out var areSame);
        return hr >= 0 && areSame != 0;
    }

    private static bool ShouldPromoteChildrenInBoundedTree(IUIAutomationElement element)
        => WindowAutomation.ShouldPromoteChildrenInBoundedTree(
            GetControlTypeName(element),
            GetStringProperty(element, UIA_ClassNamePropertyId),
            GetStringProperty(element, UIA_NamePropertyId),
            GetStringProperty(element, UIA_AutomationIdPropertyId),
            GetAvailableActions(element),
            GetBooleanProperty(element, UIA_IsKeyboardFocusablePropertyId),
            GetBooleanProperty(element, UIA_HasKeyboardFocusPropertyId));

    private static UiElementSnapshot BuildElementSnapshot(
        IUIAutomationElement element,
        string path,
        IReadOnlyList<UiElementSnapshot> children,
        bool includeChildren,
        string? uiPath = null)
        => new(
            path,
            uiPath ?? path,
            GetStringProperty(element, UIA_NamePropertyId),
            GetControlTypeName(element),
            GetStringProperty(element, UIA_AutomationIdPropertyId),
            GetStringProperty(element, UIA_ClassNamePropertyId),
            GetBooleanProperty(element, UIA_IsEnabledPropertyId),
            GetBooleanProperty(element, UIA_IsOffscreenPropertyId),
            GetBooleanProperty(element, UIA_HasKeyboardFocusPropertyId),
            GetBooleanProperty(element, UIA_IsKeyboardFocusablePropertyId),
            GetBooleanProperty(element, UIA_IsSelectedPropertyId),
            GetAvailableActions(element),
            GetBoundingRectangle(element),
            includeChildren ? children : [],
            GetStringProperty(element, UIA_AriaRolePropertyId),
            GetStringProperty(element, UIA_AriaPropertiesPropertyId));

    private static IReadOnlyList<string> GetAvailableActions(IUIAutomationElement element)
    {
        var actions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        var hasSelectionPattern = GetBooleanProperty(element, UIA_IsSelectionItemPatternAvailablePropertyId);
        if (GetBooleanProperty(element, UIA_IsKeyboardFocusablePropertyId) || hasSelectionPattern)
        {
            actions.Add("focus");
        }

        if (GetBooleanProperty(element, UIA_IsInvokePatternAvailablePropertyId))
        {
            actions.Add("invoke");
        }

        if (hasSelectionPattern)
        {
            actions.Add("select");
        }

        if (GetBooleanProperty(element, UIA_IsExpandCollapsePatternAvailablePropertyId))
        {
            actions.Add("expand");
            actions.Add("collapse");
        }

        if (GetBooleanProperty(element, UIA_IsTogglePatternAvailablePropertyId))
        {
            actions.Add("toggle");
        }

        if (GetBooleanProperty(element, UIA_IsValuePatternAvailablePropertyId))
        {
            actions.Add("set_value");
        }

        if (GetBooleanProperty(element, UIA_IsRangeValuePatternAvailablePropertyId))
        {
            actions.Add("set_range_value");
        }

        if (GetBooleanProperty(element, UIA_IsScrollItemPatternAvailablePropertyId))
        {
            actions.Add("scroll_into_view");
        }

        if (GetBooleanProperty(element, UIA_IsScrollPatternAvailablePropertyId))
        {
            actions.Add("scroll");
        }

        if (GetBooleanProperty(element, UIA_IsWindowPatternAvailablePropertyId))
        {
            actions.Add("close");
            actions.Add("maximize");
            actions.Add("minimize");
            actions.Add("restore");
        }

        if (GetBooleanProperty(element, UIA_IsDockPatternAvailablePropertyId))
        {
            actions.Add("dock");
        }

        if (GetBooleanProperty(element, UIA_IsTransformPatternAvailablePropertyId))
        {
            if (GetBooleanProperty(element, UIA_CanMovePropertyId))
            {
                actions.Add("move");
            }

            if (GetBooleanProperty(element, UIA_CanResizePropertyId))
            {
                actions.Add("resize");
            }

            if (GetBooleanProperty(element, UIA_CanRotatePropertyId))
            {
                actions.Add("rotate");
            }
        }

        return actions.ToArray();
    }

    private static string GetControlTypeName(IUIAutomationElement element)
    {
        var value = GetIntProperty(element, UIA_ControlTypePropertyId);
        return value.HasValue ? ControlTypeNameFromId(value.Value) : "Unknown";
    }

    private static ElementBounds? GetBoundingRectangle(IUIAutomationElement element)
    {
        var value = GetPropertyValue(element, UIA_BoundingRectanglePropertyId);
        if (value is not double[] { Length: 4 } values)
        {
            return null;
        }

        var left = values[0];
        var top = values[1];
        var width = values[2];
        var height = values[3];
        if (double.IsNaN(left) ||
            double.IsNaN(top) ||
            double.IsNaN(width) ||
            double.IsNaN(height) ||
            width <= 0 ||
            height <= 0)
        {
            return null;
        }

        return new ElementBounds(left, top, width, height);
    }

    private static string GetStringProperty(IUIAutomationElement element, int propertyId)
        => NormalizeStringPropertyValue(GetPropertyValue(element, propertyId));

    internal static string NormalizeStringPropertyValue(object? value)
    {
        if (value is null || Marshal.IsComObject(value))
        {
            return string.Empty;
        }

        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return IsUnsupportedPropertyValue(text) ? string.Empty : text;
    }

    private static int? GetIntProperty(IUIAutomationElement element, int propertyId)
    {
        var value = GetPropertyValue(element, propertyId);
        return value switch
        {
            int intValue => intValue,
            short shortValue => shortValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            _ => null,
        };
    }

    private static bool GetBooleanProperty(IUIAutomationElement element, int propertyId)
    {
        var value = GetPropertyValue(element, propertyId);
        return value switch
        {
            bool boolValue => boolValue,
            int intValue => intValue != 0,
            short shortValue => shortValue != 0,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false,
        };
    }

    private static object? GetPropertyValue(IUIAutomationElement element, int propertyId)
    {
        try
        {
            var hr = element.GetCurrentPropertyValueEx(propertyId, ignoreDefaultValue: 1, out var value);
            return hr < 0 ? null : value;
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string BuildChildPath(string path, int childIndex)
        => path is "root" or "focused"
            ? $"{childIndex}"
            : $"{path}/{childIndex}";

    private static bool IsUnsupportedPropertyValue(string value)
        => value.Contains("Unsupported Property", StringComparison.OrdinalIgnoreCase) &&
           value.Contains("ArgumentException", StringComparison.OrdinalIgnoreCase);

    private static IUIAutomation CreateAutomation()
        => (IUIAutomation)new CUIAutomation();

    private static void ThrowIfFailed(int hr, string operation)
    {
        if (hr >= 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Native UIA operation failed: {operation} returned 0x{hr:X8}.",
            Marshal.GetExceptionForHR(hr));
    }
}

internal sealed record NativeFocusedElementTree(
    string UiPath,
    UiElementSnapshot FocusedElement);

[ComImport]
[Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
internal class CUIAutomation
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
internal interface IUIAutomation
{
    [PreserveSig]
    int CompareElements(
        IUIAutomationElement element1,
        IUIAutomationElement element2,
        out int areSame);

    [PreserveSig]
    int CompareRuntimeIds(
        IntPtr runtimeId1,
        IntPtr runtimeId2,
        out int areSame);

    [PreserveSig]
    int GetRootElement(out IUIAutomationElement root);

    [PreserveSig]
    int ElementFromHandle(nint hwnd, out IUIAutomationElement element);

    [PreserveSig]
    int ElementFromPoint(NativeUiaPoint pt, out IUIAutomationElement element);

    [PreserveSig]
    int GetFocusedElement(out IUIAutomationElement element);

    [PreserveSig]
    int GetRootElementBuildCache(IntPtr cacheRequest, out IUIAutomationElement root);

    [PreserveSig]
    int ElementFromHandleBuildCache(
        nint hwnd,
        IntPtr cacheRequest,
        out IUIAutomationElement element);

    [PreserveSig]
    int ElementFromPointBuildCache(
        NativeUiaPoint pt,
        IntPtr cacheRequest,
        out IUIAutomationElement element);

    [PreserveSig]
    int GetFocusedElementBuildCache(
        IntPtr cacheRequest,
        out IUIAutomationElement element);

    [PreserveSig]
    int CreateTreeWalker(IntPtr condition, out IntPtr walker);

    [PreserveSig]
    int get_ControlViewWalker(out IntPtr walker);

    [PreserveSig]
    int get_ContentViewWalker(out IntPtr walker);

    [PreserveSig]
    int get_RawViewWalker(out IntPtr walker);

    [PreserveSig]
    int get_RawViewCondition(out IUIAutomationCondition condition);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
internal interface IUIAutomationElement
{
    [PreserveSig]
    int SetFocus();

    [PreserveSig]
    int GetRuntimeId(out IntPtr runtimeId);

    [PreserveSig]
    int FindFirst(
        int scope,
        IUIAutomationCondition condition,
        out IUIAutomationElement found);

    [PreserveSig]
    int FindAll(
        int scope,
        IUIAutomationCondition condition,
        out IUIAutomationElementArray found);

    [PreserveSig]
    int FindFirstBuildCache(
        int scope,
        IUIAutomationCondition condition,
        IntPtr cacheRequest,
        out IUIAutomationElement found);

    [PreserveSig]
    int FindAllBuildCache(
        int scope,
        IUIAutomationCondition condition,
        IntPtr cacheRequest,
        out IUIAutomationElementArray found);

    [PreserveSig]
    int BuildUpdatedCache(
        IntPtr cacheRequest,
        out IUIAutomationElement updatedElement);

    [PreserveSig]
    int GetCurrentPropertyValue(
        int propertyId,
        [MarshalAs(UnmanagedType.Struct)] out object value);

    [PreserveSig]
    int GetCurrentPropertyValueEx(
        int propertyId,
        int ignoreDefaultValue,
        [MarshalAs(UnmanagedType.Struct)] out object value);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("14314595-B4BC-4055-95F2-58F2E42C9855")]
internal interface IUIAutomationElementArray
{
    [PreserveSig]
    int get_Length(out int length);

    [PreserveSig]
    int GetElement(int index, out IUIAutomationElement element);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9")]
internal interface IUIAutomationCondition
{
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeUiaPoint(int X, int Y);
