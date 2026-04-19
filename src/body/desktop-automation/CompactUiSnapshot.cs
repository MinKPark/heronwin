using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeronWin.Body.DesktopAutomation;

internal static class CompactUiSnapshotJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    internal static string Serialize(object value)
        => JsonSerializer.Serialize(value, JsonOptions);
}

internal sealed class CompactSnapshotResponse
{
    public required WindowDescriptor Window { get; init; }

    public required CompactSourceStats SourceStats { get; init; }

    public required CompactUiNode CompactTree { get; init; }

    public required LlmUiNode LlmTree { get; init; }

    public CompactRenderedImage? RenderedImage { get; init; }
}

internal sealed class CompactSourceStats
{
    public required int SourceNodeCount { get; init; }

    public required int KeptNodeCount { get; init; }

    public required int OmittedNodeCount { get; init; }

    public required string AlgorithmVersion { get; init; }
}

internal sealed class CompactRenderedImage
{
    public required string ImagePath { get; init; }

    public required string ImageFormat { get; init; }

    public required ImageDimensions ImageSize { get; init; }
}

internal sealed class CompactUiNode
{
    public required string Path { get; init; }

    public required string UiPath { get; init; }

    public required string ControlType { get; init; }

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    public string? ClassName { get; init; }

    public bool? IsOffscreen { get; init; }

    public bool? HasKeyboardFocus { get; init; }

    public bool? IsKeyboardFocusable { get; init; }

    public bool? IsSelected { get; init; }

    public IReadOnlyList<string>? AvailableActions { get; init; }

    public ElementBounds? Bounds { get; init; }

    public IReadOnlyList<CompactUiNode>? Children { get; init; }
}

internal sealed class LlmUiNode
{
    public required string UiPath { get; init; }

    public required string ControlType { get; init; }

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    public IReadOnlyList<string>? State { get; init; }

    public IReadOnlyList<string>? AvailableActions { get; init; }

    public int? OmittedChildren { get; init; }

    public IReadOnlyList<LlmUiNode>? Children { get; init; }
}

internal static class CompactUiSnapshotBuilder
{
    internal const string AlgorithmVersion = "compact-tree-v1";

    private const int MaxSiblingContextPerParent = 2;
    private const int MaxRenderDimension = 1600;
    private const int MinRenderWidth = 480;
    private const int MinRenderHeight = 320;
    private const int MaxLabelLength = 60;
    private const int LlmKeepThreshold = 650;
    private const int LlmStrongChildThreshold = 1200;
    private const int LlmCriticalThreshold = 1800;

    private static readonly HashSet<string> HighValueControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Window",
        "Dialog",
        "Pane",
        "ToolBar",
        "Tab",
        "TabItem",
        "Button",
        "Edit",
        "ComboBox",
        "List",
        "ListItem",
        "Link",
        "Hyperlink",
        "Menu",
        "MenuItem",
        "Tree",
        "TreeItem",
        "Document",
    };

    internal static CompactSnapshotResponse BuildWindowResponse(
        WindowDescriptor window,
        UiElementSnapshot sourceTree,
        bool includeImage,
        string? debugArtifactDirectory = null)
        => BuildResponse(
            window,
            sourceTree,
            focusMode: false,
            includeImage,
            debugArtifactDirectory);

    internal static CompactSnapshotResponse BuildFocusResponse(
        WindowDescriptor window,
        UiElementSnapshot sourceTree,
        bool includeImage,
        string? debugArtifactDirectory = null)
        => BuildResponse(
            window,
            sourceTree,
            focusMode: true,
            includeImage,
            debugArtifactDirectory);

    private static CompactSnapshotResponse BuildResponse(
        WindowDescriptor window,
        UiElementSnapshot sourceTree,
        bool focusMode,
        bool includeImage,
        string? debugArtifactDirectory)
    {
        var nodes = new List<NodeInfo>();
        var root = BuildNodeInfo(sourceTree, parent: null, depth: 0, ordinal: 0, nodes);
        var keep = SelectNodes(root, nodes, focusMode);
        var compactTree = BuildCompactNode(root, keep)
                          ?? throw new InvalidOperationException("The compact tree root could not be built.");
        var llmTree = BuildLlmNode(compactTree);
        var keptNodeCount = CountCompactNodes(compactTree);
        CompactRenderedImage? renderedImage = null;
        if (includeImage)
        {
            renderedImage = RenderCompactTree(window, compactTree, debugArtifactDirectory);
        }

        return new CompactSnapshotResponse
        {
            Window = window,
            SourceStats = new CompactSourceStats
            {
                SourceNodeCount = nodes.Count,
                KeptNodeCount = keptNodeCount,
                OmittedNodeCount = Math.Max(0, nodes.Count - keptNodeCount),
                AlgorithmVersion = AlgorithmVersion,
            },
            CompactTree = compactTree,
            LlmTree = llmTree,
            RenderedImage = renderedImage,
        };
    }

    private static NodeInfo BuildNodeInfo(
        UiElementSnapshot snapshot,
        NodeInfo? parent,
        int depth,
        int ordinal,
        List<NodeInfo> nodes)
    {
        var node = new NodeInfo(snapshot, parent, depth, ordinal);
        nodes.Add(node);

        var nextOrdinal = ordinal;
        foreach (var child in snapshot.Children)
        {
            nextOrdinal++;
            var childNode = BuildNodeInfo(child, node, depth + 1, nextOrdinal, nodes);
            node.Children.Add(childNode);
            nextOrdinal = childNode.MaxOrdinalInSubtree;
        }

        node.MaxOrdinalInSubtree = nextOrdinal;
        node.Priority = GetTraversalPriority(node, focusMode: false);
        node.FocusModePriority = GetTraversalPriority(node, focusMode: true);
        return node;
    }

    private static HashSet<NodeInfo> SelectNodes(
        NodeInfo root,
        IReadOnlyList<NodeInfo> nodes,
        bool focusMode)
    {
        var keep = new HashSet<NodeInfo>();
        AddChain(root);

        foreach (var candidate in nodes
                     .Where(node => node != root && (node.HasKeyboardFocus || node.IsSelected))
                     .OrderByDescending(node => GetPriority(node, focusMode))
                     .ThenBy(node => node.Ordinal))
        {
            AddChain(candidate);
            AddFocusedSiblingContext(candidate, focusMode);
        }

        foreach (var candidate in nodes
                     .Where(node => node != root)
                     .OrderByDescending(node => GetPriority(node, focusMode))
                     .ThenBy(node => node.Depth)
                     .ThenBy(node => node.Ordinal))
        {
            if (!ShouldConsiderForRetention(candidate, focusMode))
            {
                continue;
            }

            AddChain(candidate);
        }

        foreach (var parent in keep.ToArray())
        {
            if (parent.Children.Count < 2)
            {
                continue;
            }

            var addedForParent = 0;
            foreach (var sibling in parent.Children
                         .Where(child => !keep.Contains(child))
                         .OrderByDescending(child => GetPriority(child, focusMode))
                         .ThenBy(child => child.Ordinal))
            {
                if (addedForParent >= MaxSiblingContextPerParent)
                {
                    break;
                }

                if (GetPriority(sibling, focusMode) < 180)
                {
                    break;
                }

                if (AddChain(sibling))
                {
                    addedForParent++;
                }
            }
        }

        return keep;

        bool AddChain(NodeInfo candidate)
        {
            var missing = new Stack<NodeInfo>();
            for (var current = candidate; current is not null && !keep.Contains(current); current = current.Parent)
            {
                missing.Push(current);
            }

            if (missing.Count == 0)
            {
                return false;
            }

            while (missing.Count > 0)
            {
                var node = missing.Pop();
                keep.Add(node);
            }

            return true;
        }

        void AddFocusedSiblingContext(NodeInfo anchor, bool focusMode)
        {
            if (anchor.Parent is null ||
                anchor.Parent.Children.Count < 2 ||
                !LooksLikeFocusedContextParent(anchor.Parent))
            {
                return;
            }

            foreach (var sibling in anchor.Parent.Children
                         .Where(child => child != anchor && !keep.Contains(child))
                         .OrderByDescending(child => GetPriority(child, focusMode))
                         .ThenBy(child => child.Ordinal))
            {
                if (!ShouldForceKeepFocusedContextSibling(sibling, focusMode))
                {
                    continue;
                }

                AddChain(sibling);
            }
        }
    }

    private static bool ShouldConsiderForRetention(NodeInfo node, bool focusMode)
    {
        if (ShouldIncludeElement(node, focusMode))
        {
            return true;
        }

        return GetPriority(node, focusMode) >= 150;
    }

    private static int GetPriority(NodeInfo node, bool focusMode)
        => focusMode ? node.FocusModePriority : node.Priority;

    private static CompactUiNode? BuildCompactNode(NodeInfo node, HashSet<NodeInfo> keep)
    {
        if (!keep.Contains(node))
        {
            return null;
        }

        var children = node.Children
            .Select(child => BuildCompactNode(child, keep))
            .Where(child => child is not null)
            .Cast<CompactUiNode>()
            .ToArray();

        var actions = node.AvailableActions.Count == 0 ? null : node.AvailableActions;
        return new CompactUiNode
        {
            Path = node.Path,
            UiPath = node.UiPath,
            ControlType = node.ControlType,
            Name = node.Name,
            AutomationId = node.AutomationId,
            ClassName = node.ClassName,
            IsOffscreen = node.IsOffscreen ? true : null,
            HasKeyboardFocus = node.HasKeyboardFocus ? true : null,
            IsKeyboardFocusable = node.IsKeyboardFocusable ? true : null,
            IsSelected = node.IsSelected ? true : null,
            AvailableActions = actions,
            Bounds = node.Bounds,
            Children = children.Length == 0 ? null : children,
        };
    }

    private static int CountCompactNodes(CompactUiNode node)
    {
        var count = 1;
        if (node.Children is null)
        {
            return count;
        }

        foreach (var child in node.Children)
        {
            count += CountCompactNodes(child);
        }

        return count;
    }

    private static LlmUiNode BuildLlmNode(CompactUiNode node)
        => BuildLlmProjectionCandidate(node, depth: 0, parentName: null)?.Node
           ?? throw new InvalidOperationException("The llmTree root could not be built.");

    private static LlmProjectionCandidate? BuildLlmProjectionCandidate(
        CompactUiNode node,
        int depth,
        string? parentName)
    {
        var childCandidates = node.Children?
            .Select(child => BuildLlmProjectionCandidate(child, depth + 1, node.Name))
            .Where(static child => child is not null)
            .Cast<LlmProjectionCandidate>()
            .ToArray() ?? [];
        var selectedChildren = SelectLlmChildren(node, childCandidates, depth);
        var score = ScoreLlmNode(node, depth);
        var filteredActions = FilterLlmActions(node, score, hasProjectedChildren: selectedChildren.Length > 0);
        var state = BuildLlmState(node);
        var name = GetLlmName(node, depth, parentName, score, selectedChildren.Length > 0);
        var hasOwnSignal = !string.IsNullOrWhiteSpace(name)
                           || filteredActions.Count > 0
                           || state is not null
                           || ShouldIncludeLlmAutomationId(node, name, score);

        if (depth > 0 &&
            !hasOwnSignal &&
            selectedChildren.Length == 0 &&
            score < LlmKeepThreshold)
        {
            return null;
        }

        var omittedChildren = childCandidates.Length - selectedChildren.Length;
        var llmNode = new LlmUiNode
        {
            UiPath = node.UiPath,
            ControlType = node.ControlType,
            Name = name,
            AutomationId = ShouldIncludeLlmAutomationId(node, name, score) ? node.AutomationId : null,
            State = state,
            AvailableActions = filteredActions.Count == 0 ? null : filteredActions,
            OmittedChildren = omittedChildren > 0 ? omittedChildren : null,
            Children = selectedChildren.Length == 0
                ? null
                : selectedChildren.Select(candidate => candidate.Node).ToArray(),
        };

        return new LlmProjectionCandidate(
            llmNode,
            score,
            IsCriticalLlmNode(node, score) || selectedChildren.Any(static child => child.HasCriticalPath));
    }

    private static IReadOnlyList<string>? BuildLlmState(CompactUiNode node)
    {
        List<string>? state = null;

        void AddState(string value)
        {
            state ??= [];
            state.Add(value);
        }

        if (node.HasKeyboardFocus == true)
        {
            AddState("focused");
        }

        if (node.IsSelected == true)
        {
            AddState("selected");
        }

        if (node.IsOffscreen == true)
        {
            AddState("offscreen");
        }

        return state;
    }

    private static LlmProjectionCandidate[] SelectLlmChildren(
        CompactUiNode parent,
        IReadOnlyList<LlmProjectionCandidate> childCandidates,
        int depth)
    {
        if (childCandidates.Count == 0)
        {
            return [];
        }

        var childBudget = GetLlmChildBudget(parent, depth);
        var prioritized = childCandidates
            .OrderByDescending(candidate => candidate.HasCriticalPath)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Node.UiPath, StringComparer.Ordinal)
            .ToArray();

        var selectedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var critical in prioritized.Where(static candidate => candidate.HasCriticalPath))
        {
            selectedPaths.Add(critical.Node.UiPath);
        }

        foreach (var candidate in prioritized)
        {
            if (selectedPaths.Count >= childBudget &&
                !candidate.HasCriticalPath)
            {
                break;
            }

            if (selectedPaths.Contains(candidate.Node.UiPath))
            {
                continue;
            }

            if (candidate.Score < LlmKeepThreshold &&
                !candidate.HasCriticalPath)
            {
                break;
            }

            selectedPaths.Add(candidate.Node.UiPath);
        }

        return childCandidates
            .Where(candidate => selectedPaths.Contains(candidate.Node.UiPath))
            .ToArray();
    }

    private static int GetLlmChildBudget(CompactUiNode node, int depth)
    {
        var budget = depth switch
        {
            0 => 7,
            1 => 6,
            2 => 5,
            3 => 4,
            _ => 3,
        };

        if (string.Equals(node.ControlType, "Document", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.ControlType, "List", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.ControlType, "Menu", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.ControlType, "Tree", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.ControlType, "Tab", StringComparison.OrdinalIgnoreCase))
        {
            budget += 2;
        }

        if (node.HasKeyboardFocus == true || node.IsSelected == true)
        {
            budget += 1;
        }

        return budget;
    }

    private static int ScoreLlmNode(CompactUiNode node, int depth)
    {
        var score = depth == 0 ? 4_000 : 0;

        if (node.HasKeyboardFocus == true)
        {
            score += 2_600;
        }

        if (node.IsSelected == true)
        {
            score += 2_200;
        }

        if (LooksLikeNamedActionablePageContent(
                node.Name,
                node.ControlType,
                node.ClassName,
                node.AvailableActions ?? []))
        {
            score += 1_600;
        }

        if (LooksLikeProfilePickerTile(node.Name, node.ControlType, node.ClassName))
        {
            score += 1_500;
        }

        if (LooksLikeMeaningfulPageContent(node.Name, node.ControlType, node.ClassName))
        {
            score += 900;
        }

        if (HasInterestingLlmAction(node))
        {
            score += 700;
        }

        if (HighValueControlTypes.Contains(node.ControlType))
        {
            score += 180;
        }

        if (node.IsKeyboardFocusable == true)
        {
            score += 120;
        }

        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            score += depth <= 2 ? 180 : 80;
        }

        if (LooksLikeBrowserChrome(node.Name, node.ControlType, node.ClassName))
        {
            score -= 250;
        }

        if (LooksLikeWindowCaptionButton(node.Name, node.ControlType))
        {
            score -= 1_200;
        }

        if (node.IsOffscreen == true)
        {
            score -= 150;
        }

        if (IsGenericContainerControlType(node.ControlType) &&
            string.IsNullOrWhiteSpace(node.Name) &&
            !HasInterestingLlmAction(node))
        {
            score -= 500;
        }

        return score;
    }

    private static IReadOnlyList<string> FilterLlmActions(
        CompactUiNode node,
        int score,
        bool hasProjectedChildren)
    {
        if (node.AvailableActions is null || node.AvailableActions.Count == 0)
        {
            return [];
        }

        var filtered = node.AvailableActions
            .Where(ShouldKeepLlmAction)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (filtered.Count == 0)
        {
            return [];
        }

        if (filtered.Count > 1)
        {
            filtered.RemoveAll(action => string.Equals(action, "focus", StringComparison.OrdinalIgnoreCase));
        }

        if (filtered.Count == 0 ||
            string.Equals(node.ControlType, "Window", StringComparison.OrdinalIgnoreCase) ||
            LooksLikeWindowCaptionButton(node.Name, node.ControlType))
        {
            return [];
        }

        var shouldExposeActions =
            node.HasKeyboardFocus == true ||
            node.IsSelected == true ||
            score >= LlmStrongChildThreshold ||
            (!hasProjectedChildren && score >= LlmKeepThreshold);

        return shouldExposeActions ? filtered.ToArray() : [];
    }

    private static bool ShouldKeepLlmAction(string action)
        => !string.Equals(action, "scroll_into_view", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "scroll", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "close", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "maximize", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "minimize", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "restore", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "dock", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "move", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "resize", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(action, "rotate", StringComparison.OrdinalIgnoreCase);

    private static string? GetLlmName(
        CompactUiNode node,
        int depth,
        string? parentName,
        int score,
        bool hasProjectedChildren)
    {
        if (string.IsNullOrWhiteSpace(node.Name))
        {
            return null;
        }

        if (depth > 0 &&
            string.Equals(node.Name, parentName, StringComparison.OrdinalIgnoreCase) &&
            IsGenericContainerControlType(node.ControlType) &&
            score < LlmStrongChildThreshold)
        {
            return null;
        }

        if (IsGenericContainerControlType(node.ControlType) &&
            !hasProjectedChildren &&
            score < LlmKeepThreshold)
        {
            return null;
        }

        return node.Name;
    }

    private static bool ShouldIncludeLlmAutomationId(
        CompactUiNode node,
        string? llmName,
        int score)
        => string.IsNullOrWhiteSpace(llmName) &&
           score >= LlmKeepThreshold &&
           !string.IsNullOrWhiteSpace(node.AutomationId);

    private static bool HasInterestingLlmAction(CompactUiNode node)
        => node.AvailableActions is not null &&
           node.AvailableActions.Any(ShouldKeepLlmAction);

    private static bool IsCriticalLlmNode(CompactUiNode node, int score)
        => node.HasKeyboardFocus == true
           || node.IsSelected == true
           || score >= LlmCriticalThreshold;

    private static bool IsGenericContainerControlType(string controlType)
        => string.Equals(controlType, "Pane", StringComparison.OrdinalIgnoreCase)
           || string.Equals(controlType, "Group", StringComparison.OrdinalIgnoreCase)
           || string.Equals(controlType, "Custom", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldIncludeElement(NodeInfo node, bool focusMode)
    {
        if (node.Depth == 0)
        {
            return true;
        }

        if (node.HasKeyboardFocus || node.IsSelected || LooksLikeBrowserChrome(node.Name, node.ControlType, node.ClassName))
        {
            return true;
        }

        if (node.Depth <= 1 &&
            (!string.IsNullOrWhiteSpace(node.Name) || node.HasInterestingAction))
        {
            return true;
        }

        if (node.Depth <= 2 &&
            (HighValueControlTypes.Contains(node.ControlType) || node.IsKeyboardFocusable) &&
            (!string.IsNullOrWhiteSpace(node.Name) || node.HasInterestingAction))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(node.Name) &&
            (node.Depth <= 4 || node.LooksLikeMeaningfulPageContent))
        {
            return true;
        }

        if (!focusMode &&
            node.HasInterestingAction &&
            node.LooksLikeNamedActionablePageContent)
        {
            return true;
        }

        if (!focusMode &&
            node.IsLikelyVisible &&
            node.LooksLikeProfilePickerTile)
        {
            return true;
        }

        if (!focusMode &&
            node.IsLikelyVisible &&
            node.HasInterestingAction &&
            node.LooksLikeMeaningfulPageContent)
        {
            return true;
        }

        return focusMode &&
               node.Depth <= 3 &&
               node.IsKeyboardFocusable &&
               node.HasInterestingAction;
    }

    private static int GetTraversalPriority(NodeInfo node, bool focusMode)
    {
        var priority = 0;
        if (node.HasKeyboardFocus)
        {
            priority += 400;
        }

        if (node.IsSelected)
        {
            priority += 350;
        }

        if (node.LooksLikeDocumentRoot)
        {
            priority += 300;
        }

        if (node.LooksLikeNamedActionablePageContent)
        {
            priority += 320;
        }

        if (node.LooksLikeProfilePickerTile)
        {
            priority += 340;
        }

        if (node.LooksLikeMeaningfulPageContent)
        {
            priority += 220;
        }

        if (string.Equals(node.ControlType, "Text", StringComparison.OrdinalIgnoreCase) &&
            node.LooksLikeMeaningfulPageContent)
        {
            priority += 160;
        }

        if (node.IsLikelyVisible &&
            node.HasInterestingAction &&
            node.LooksLikeMeaningfulPageContent)
        {
            priority += 260;
        }

        if (LooksLikeBrowserChrome(node.Name, node.ControlType, node.ClassName))
        {
            priority += 140;
        }

        if (LooksLikeSiteBrandOrLogo(node.Name, node.ControlType, node.ClassName))
        {
            priority -= 220;
        }

        if (LooksLikeWindowCaptionButton(node.Name, node.ControlType))
        {
            priority -= 120;
        }

        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            priority += 20;
        }

        if (node.HasInterestingAction)
        {
            priority += 10;
        }

        if (focusMode && node.IsKeyboardFocusable)
        {
            priority += 60;
        }

        return priority;
    }

    private static bool LooksLikeFocusedContextParent(NodeInfo node)
        => string.Equals(node.ControlType, "Window", StringComparison.OrdinalIgnoreCase)
           || string.Equals(node.ControlType, "Dialog", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldForceKeepFocusedContextSibling(NodeInfo node, bool focusMode)
    {
        if (node.HasKeyboardFocus || node.IsSelected)
        {
            return true;
        }

        if (!node.IsLikelyVisible)
        {
            return false;
        }

        if (node.LooksLikeNamedActionablePageContent)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(node.Name) && node.LooksLikeMeaningfulPageContent)
        {
            return true;
        }

        return GetPriority(node, focusMode) >= 280 &&
               node.HasInterestingAction &&
               node.IsKeyboardFocusable;
    }

    private static CompactRenderedImage RenderCompactTree(
        WindowDescriptor window,
        CompactUiNode root,
        string? debugArtifactDirectory)
    {
        var boundedNodes = GetRenderableImageNodes(root);

        var scale = ComputeRenderScale(window.Bounds.Width, window.Bounds.Height);
        var contentWidth = Math.Max(MinRenderWidth, (int)Math.Ceiling(window.Bounds.Width * scale));
        var contentHeight = Math.Max(MinRenderHeight, (int)Math.Ceiling(window.Bounds.Height * scale));
        var imageWidth = contentWidth;
        var imageHeight = contentHeight;
        var imageDirectory = Path.Combine(WindowAutomation.GetScreenshotDirectory(debugArtifactDirectory), "compact-tree");
        Directory.CreateDirectory(imageDirectory);
        var imagePath = Path.Combine(
            imageDirectory,
            BuildCompactTreeFileName(window.Handle, window.Title));

        using var bitmap = new Bitmap(imageWidth, imageHeight);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.FromArgb(246, 248, 251));

        using var windowBorderPen = new Pen(Color.FromArgb(60, 35, 45, 70), 2f);
        graphics.DrawRectangle(windowBorderPen, 1, 1, contentWidth - 3, contentHeight - 3);

        foreach (var node in boundedNodes.OrderBy(node => GetNodeDepth(node)).ThenBy(node => node.UiPath, StringComparer.Ordinal))
        {
            DrawBoundedNode(graphics, node, window.Bounds, scale);
        }

        bitmap.Save(imagePath, ImageFormat.Png);
        return new CompactRenderedImage
        {
            ImagePath = imagePath,
            ImageFormat = "png",
            ImageSize = new ImageDimensions(bitmap.Width, bitmap.Height),
        };
    }

    internal static CompactUiNode[] GetRenderableImageNodes(CompactUiNode root)
        => Flatten(root)
            .Where(node => HasRenderableBounds(node.Bounds))
            .Where(node => !HasRenderableDescendantWithExactBounds(node, node.Bounds!))
            .ToArray();

    private static void DrawBoundedNode(
        Graphics graphics,
        CompactUiNode node,
        WindowBounds windowBounds,
        double scale)
    {
        var bounds = node.Bounds!;
        var left = (float)Math.Round((bounds.Left - windowBounds.Left) * scale, MidpointRounding.AwayFromZero);
        var top = (float)Math.Round((bounds.Top - windowBounds.Top) * scale, MidpointRounding.AwayFromZero);
        var width = (float)Math.Max(2d, Math.Round(bounds.Width * scale, MidpointRounding.AwayFromZero));
        var height = (float)Math.Max(2d, Math.Round(bounds.Height * scale, MidpointRounding.AwayFromZero));
        var depth = GetNodeDepth(node);
        var fillColor = GetDepthFillColor(depth);
        var strokeColor = node.HasKeyboardFocus == true
            ? Color.FromArgb(220, 196, 54, 50)
            : node.IsSelected == true
                ? Color.FromArgb(220, 40, 110, 58)
                : Color.FromArgb(180, 35, 53, 84);
        var strokeWidth = node.HasKeyboardFocus == true || node.IsSelected == true ? 3f : 1.6f;

        var rect = new RectangleF(left, top, width, height);
        using var fillBrush = new SolidBrush(fillColor);
        using var strokePen = new Pen(strokeColor, strokeWidth);
        graphics.FillRectangle(fillBrush, rect);
        graphics.DrawRectangle(strokePen, rect.X, rect.Y, rect.Width, rect.Height);

        if (rect.Width < 44f || rect.Height < 18f)
        {
            return;
        }

        var label = BuildNodeLabel(node);
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        using var font = new Font(SystemFonts.DialogFont.FontFamily, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Color.FromArgb(240, 18, 24, 36));
        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        var textRect = RectangleF.Inflate(rect, -4f, -3f);
        graphics.DrawString(label, font, textBrush, textRect, labelFormat);
    }

    private static IEnumerable<CompactUiNode> Flatten(CompactUiNode root)
    {
        yield return root;
        if (root.Children is null)
        {
            yield break;
        }

        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool HasRenderableDescendantWithExactBounds(CompactUiNode node, ElementBounds ancestorBounds)
    {
        if (node.Children is null)
        {
            return false;
        }

        foreach (var child in node.Children)
        {
            if (HasRenderableBounds(child.Bounds) &&
                HaveExactSameBounds(ancestorBounds, child.Bounds!))
            {
                return true;
            }

            if (HasRenderableDescendantWithExactBounds(child, ancestorBounds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HaveExactSameBounds(ElementBounds left, ElementBounds right)
        => left.Left.Equals(right.Left)
           && left.Top.Equals(right.Top)
           && left.Width.Equals(right.Width)
           && left.Height.Equals(right.Height);

    private static bool HasRenderableBounds(ElementBounds? bounds)
        => bounds is not null && bounds.Width > 1d && bounds.Height > 1d;

    private static double ComputeRenderScale(int width, int height)
    {
        var maxDimension = Math.Max(Math.Max(width, height), 1);
        return maxDimension <= MaxRenderDimension
            ? 1d
            : MaxRenderDimension / (double)maxDimension;
    }

    private static int GetNodeDepth(CompactUiNode node)
        => string.IsNullOrWhiteSpace(node.UiPath) || node.UiPath is "root" or "focused"
            ? 0
            : node.UiPath.Count(character => character == '/') + 1;

    private static Color GetDepthFillColor(int depth)
    {
        var palette = new[]
        {
            Color.FromArgb(80, 88, 137, 204),
            Color.FromArgb(78, 92, 184, 92),
            Color.FromArgb(78, 227, 160, 70),
            Color.FromArgb(78, 212, 95, 118),
            Color.FromArgb(78, 128, 118, 220),
        };

        return palette[Math.Abs(depth) % palette.Length];
    }

    private static string BuildNodeLabel(CompactUiNode node)
    {
        var label = string.IsNullOrWhiteSpace(node.Name)
            ? node.ControlType
            : $"{node.Name} [{node.ControlType}]";
        if (label.Length <= MaxLabelLength)
        {
            return label;
        }

        return label[..(MaxLabelLength - 3)].TrimEnd() + "...";
    }

    private static string BuildCompactTreeFileName(string windowHandle, string title)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        return $"{timestamp}-{SanitizeFileNameSegment(title)}-{SanitizeFileNameSegment(windowHandle)}-compact-tree.png";
    }

    private static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "window";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "\\s+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "window" : sanitized;
    }

    private static bool LooksLikeBrowserChrome(string? name, string controlType, string? className)
    {
        if (!string.IsNullOrWhiteSpace(className) &&
            (className.Contains("Omnibox", StringComparison.OrdinalIgnoreCase)
             || className.Contains("EdgeTab", StringComparison.OrdinalIgnoreCase)
             || className.Contains("EdgeToolbar", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
        {
            return string.Equals(controlType, "Tab", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(controlType, "TabItem", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(controlType, "ToolBar", StringComparison.OrdinalIgnoreCase);
        }

        return normalizedName.Contains("address and search bar", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("new tab", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("tab bar", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("search tabs", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Back", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Forward", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Refresh", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Reload", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Home", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("site information", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWindowCaptionButton(string? name, string controlType)
    {
        if (!string.Equals(controlType, "Button", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Equals("Minimize", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Maximize", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Restore", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Close", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeNamedActionablePageContent(
        string? name,
        string controlType,
        string? className,
        IReadOnlyList<string> actions)
    {
        if (!LooksLikeMeaningfulPageContent(name, controlType, className) ||
            string.IsNullOrWhiteSpace(name) ||
            LooksLikeSiteBrandOrLogo(name, controlType, className))
        {
            return false;
        }

        return actions.Any(action => !string.Equals(action, "scroll_into_view", StringComparison.OrdinalIgnoreCase))
               || string.Equals(controlType, "Hyperlink", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "Link", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "Button", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "ListItem", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSiteBrandOrLogo(string? name, string controlType, string? className)
        => !string.IsNullOrWhiteSpace(className) &&
           className.Contains("logo", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeMeaningfulPageContent(string? name, string controlType, string? className)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            LooksLikeSiteBrandOrLogo(name, controlType, className) ||
            LooksLikeBrowserChrome(name, controlType, className) ||
            LooksLikeWindowCaptionButton(name, controlType))
        {
            return false;
        }

        if (name.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(controlType, "Text", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "ListItem", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "Hyperlink", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "Link", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "Button", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "MenuItem", StringComparison.OrdinalIgnoreCase)
               || string.Equals(controlType, "Document", StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(className) &&
                   className.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeProfilePickerTile(string? name, string controlType, string? className)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !string.Equals(controlType, "ListItem", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(className) &&
                className.Contains("profile", StringComparison.OrdinalIgnoreCase))
               || name.Contains("add profile", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NodeInfo
    {
        internal NodeInfo(UiElementSnapshot snapshot, NodeInfo? parent, int depth, int ordinal)
        {
            Snapshot = snapshot;
            Parent = parent;
            Depth = depth;
            Ordinal = ordinal;
            Path = snapshot.Path;
            UiPath = snapshot.UiPath;
            Name = NormalizeInlineText(snapshot.Name);
            ControlType = string.IsNullOrWhiteSpace(snapshot.ControlType) ? "Element" : snapshot.ControlType;
            AutomationId = NormalizeInlineText(snapshot.AutomationId);
            ClassName = NormalizeInlineText(snapshot.ClassName);
            IsOffscreen = snapshot.IsOffscreen;
            HasKeyboardFocus = snapshot.HasKeyboardFocus;
            IsKeyboardFocusable = snapshot.IsKeyboardFocusable;
            IsSelected = snapshot.IsSelected;
            AvailableActions = snapshot.AvailableActions
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Select(action => action.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Bounds = snapshot.Bounds;

            HasInterestingAction = AvailableActions.Any(
                action => !string.Equals(action, "scroll_into_view", StringComparison.OrdinalIgnoreCase));
            LooksLikeDocumentRoot = string.Equals(ControlType, "Document", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(AutomationId, "RootWebArea", StringComparison.OrdinalIgnoreCase);
            IsLikelyVisible = !IsOffscreen;
            LooksLikeMeaningfulPageContent = CompactUiSnapshotBuilder.LooksLikeMeaningfulPageContent(Name, ControlType, ClassName);
            LooksLikeNamedActionablePageContent = CompactUiSnapshotBuilder.LooksLikeNamedActionablePageContent(
                Name,
                ControlType,
                ClassName,
                AvailableActions);
            LooksLikeProfilePickerTile = CompactUiSnapshotBuilder.LooksLikeProfilePickerTile(Name, ControlType, ClassName);
        }

        internal UiElementSnapshot Snapshot { get; }

        internal NodeInfo? Parent { get; }

        internal List<NodeInfo> Children { get; } = [];

        internal int Depth { get; }

        internal int Ordinal { get; }

        internal int MaxOrdinalInSubtree { get; set; }

        internal string Path { get; }

        internal string UiPath { get; }

        internal string? Name { get; }

        internal string ControlType { get; }

        internal string? AutomationId { get; }

        internal string? ClassName { get; }

        internal bool IsOffscreen { get; }

        internal bool HasKeyboardFocus { get; }

        internal bool IsKeyboardFocusable { get; }

        internal bool IsSelected { get; }

        internal IReadOnlyList<string> AvailableActions { get; }

        internal ElementBounds? Bounds { get; }

        internal bool HasInterestingAction { get; }

        internal bool LooksLikeDocumentRoot { get; }

        internal bool IsLikelyVisible { get; }

        internal bool LooksLikeMeaningfulPageContent { get; }

        internal bool LooksLikeNamedActionablePageContent { get; }

        internal bool LooksLikeProfilePickerTile { get; }

        internal int Priority { get; set; }

        internal int FocusModePriority { get; set; }
    }

    private sealed record LlmProjectionCandidate(
        LlmUiNode Node,
        int Score,
        bool HasCriticalPath);

    private static string? NormalizeInlineText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
