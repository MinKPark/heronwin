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

    public CompactRenderedImage? RenderedImage { get; init; }
}

internal sealed class CompactSourceStats
{
    public required int SourceNodeCount { get; init; }

    public required int KeptNodeCount { get; init; }

    public required int OmittedNodeCount { get; init; }

    public required string AlgorithmVersion { get; init; }

    public required int BudgetHintChars { get; init; }
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

internal static class CompactUiSnapshotBuilder
{
    internal const string AlgorithmVersion = "compact-tree-v1";

    private const int DefaultWindowBudgetHintChars = 7_200;
    private const int DefaultFocusBudgetHintChars = 3_600;
    private const int MinimumBudgetHintChars = 900;
    private const int BudgetReserveChars = 260;
    private const int MaxSiblingContextPerParent = 2;
    private const int MaxRenderDimension = 1600;
    private const int MinRenderWidth = 480;
    private const int MinRenderHeight = 320;
    private const int FallbackLaneWidth = 280;
    private const int MaxFallbackLaneItems = 18;
    private const int MaxLabelLength = 60;

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

    internal static int NormalizeBudgetHintChars(int? budgetHintChars, bool focusMode)
    {
        var fallback = focusMode ? DefaultFocusBudgetHintChars : DefaultWindowBudgetHintChars;
        if (!budgetHintChars.HasValue)
        {
            return fallback;
        }

        return Math.Max(MinimumBudgetHintChars, budgetHintChars.Value);
    }

    internal static CompactSnapshotResponse BuildWindowResponse(
        WindowDescriptor window,
        UiElementSnapshot sourceTree,
        int? budgetHintChars,
        bool includeImage,
        string? debugArtifactDirectory = null)
        => BuildResponse(
            window,
            sourceTree,
            focusMode: false,
            NormalizeBudgetHintChars(budgetHintChars, focusMode: false),
            includeImage,
            debugArtifactDirectory);

    internal static CompactSnapshotResponse BuildFocusResponse(
        WindowDescriptor window,
        UiElementSnapshot sourceTree,
        int? budgetHintChars,
        bool includeImage,
        string? debugArtifactDirectory = null)
        => BuildResponse(
            window,
            sourceTree,
            focusMode: true,
            NormalizeBudgetHintChars(budgetHintChars, focusMode: true),
            includeImage,
            debugArtifactDirectory);

    private static CompactSnapshotResponse BuildResponse(
        WindowDescriptor window,
        UiElementSnapshot sourceTree,
        bool focusMode,
        int budgetHintChars,
        bool includeImage,
        string? debugArtifactDirectory)
    {
        var nodes = new List<NodeInfo>();
        var root = BuildNodeInfo(sourceTree, parent: null, depth: 0, ordinal: 0, nodes);
        var budgetForTree = Math.Max(MinimumBudgetHintChars / 2, budgetHintChars - BudgetReserveChars);
        var keep = SelectNodes(root, nodes, budgetForTree, focusMode);
        var compactTree = BuildCompactNode(root, keep)
                          ?? throw new InvalidOperationException("The compact tree root could not be built.");
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
                BudgetHintChars = budgetHintChars,
            },
            CompactTree = compactTree,
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
        node.EstimatedSelfChars = EstimateSelfChars(node);
        return node;
    }

    private static HashSet<NodeInfo> SelectNodes(
        NodeInfo root,
        IReadOnlyList<NodeInfo> nodes,
        int budgetForTree,
        bool focusMode)
    {
        var keep = new HashSet<NodeInfo>();
        var usedChars = 0;
        AddChain(root, force: true);

        foreach (var candidate in nodes
                     .Where(node => node != root && (node.HasKeyboardFocus || node.IsSelected))
                     .OrderByDescending(node => GetPriority(node, focusMode))
                     .ThenBy(node => node.Ordinal))
        {
            AddChain(candidate, force: true);
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

            AddChain(candidate, force: false);
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

                if (AddChain(sibling, force: false))
                {
                    addedForParent++;
                }
            }
        }

        return keep;

        bool AddChain(NodeInfo candidate, bool force)
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

            var extraChars = missing.Sum(node => node.EstimatedSelfChars);
            if (!force &&
                keep.Count > 0 &&
                usedChars + extraChars > budgetForTree)
            {
                return false;
            }

            while (missing.Count > 0)
            {
                var node = missing.Pop();
                if (keep.Add(node))
                {
                    usedChars += node.EstimatedSelfChars;
                }
            }

            return true;
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

    private static int EstimateSelfChars(NodeInfo node)
    {
        var total = 48 + node.Path.Length + node.UiPath.Length + node.ControlType.Length;
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            total += node.Name.Length + 10;
        }

        if (!string.IsNullOrWhiteSpace(node.AutomationId))
        {
            total += node.AutomationId.Length + 18;
        }

        if (!string.IsNullOrWhiteSpace(node.ClassName))
        {
            total += node.ClassName.Length + 14;
        }

        if (node.IsOffscreen)
        {
            total += 16;
        }

        if (node.HasKeyboardFocus)
        {
            total += 20;
        }

        if (node.IsKeyboardFocusable)
        {
            total += 24;
        }

        if (node.IsSelected)
        {
            total += 18;
        }

        if (node.AvailableActions.Count > 0)
        {
            total += node.AvailableActions.Sum(action => action.Length + 4) + 24;
        }

        if (node.Bounds is not null)
        {
            total += 80;
        }

        return total;
    }

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

    private static CompactRenderedImage RenderCompactTree(
        WindowDescriptor window,
        CompactUiNode root,
        string? debugArtifactDirectory)
    {
        var boundedNodes = Flatten(root)
            .Where(node => HasRenderableBounds(node.Bounds))
            .ToArray();
        var laneNodes = Flatten(root)
            .Where(node => !HasRenderableBounds(node.Bounds))
            .Take(MaxFallbackLaneItems)
            .ToArray();

        var scale = ComputeRenderScale(window.Bounds.Width, window.Bounds.Height);
        var contentWidth = Math.Max(MinRenderWidth, (int)Math.Ceiling(window.Bounds.Width * scale));
        var contentHeight = Math.Max(MinRenderHeight, (int)Math.Ceiling(window.Bounds.Height * scale));
        var laneWidth = laneNodes.Length > 0 ? FallbackLaneWidth : 0;
        var imageWidth = contentWidth + laneWidth;
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

        if (laneNodes.Length > 0)
        {
            DrawFallbackLane(graphics, laneNodes, contentWidth, imageHeight);
        }

        bitmap.Save(imagePath, ImageFormat.Png);
        return new CompactRenderedImage
        {
            ImagePath = imagePath,
            ImageFormat = "png",
            ImageSize = new ImageDimensions(bitmap.Width, bitmap.Height),
        };
    }

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

    private static void DrawFallbackLane(
        Graphics graphics,
        IReadOnlyList<CompactUiNode> laneNodes,
        int laneLeft,
        int imageHeight)
    {
        using var laneBrush = new SolidBrush(Color.FromArgb(232, 237, 242));
        graphics.FillRectangle(laneBrush, laneLeft, 0, FallbackLaneWidth, imageHeight);
        using var dividerPen = new Pen(Color.FromArgb(120, 45, 59, 86), 1f);
        graphics.DrawLine(dividerPen, laneLeft, 0, laneLeft, imageHeight);

        using var headingFont = new Font(SystemFonts.DialogFont.FontFamily, 9f, FontStyle.Bold, GraphicsUnit.Point);
        using var bodyFont = new Font(SystemFonts.DialogFont.FontFamily, 8f, FontStyle.Regular, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Color.FromArgb(235, 23, 31, 44));
        graphics.DrawString("Unbounded retained nodes", headingFont, textBrush, laneLeft + 10, 10);

        var y = 34f;
        foreach (var node in laneNodes)
        {
            var box = new RectangleF(laneLeft + 10, y, FallbackLaneWidth - 20, 36);
            using var fillBrush = new SolidBrush(GetDepthFillColor(GetNodeDepth(node)));
            using var pen = new Pen(
                node.HasKeyboardFocus == true
                    ? Color.FromArgb(220, 196, 54, 50)
                    : Color.FromArgb(160, 35, 53, 84),
                node.HasKeyboardFocus == true ? 2.4f : 1.2f);
            graphics.FillRectangle(fillBrush, box);
            graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            graphics.DrawString(BuildNodeLabel(node), bodyFont, textBrush, box, new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            });

            y += 42f;
            if (y + 38f > imageHeight)
            {
                break;
            }
        }
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

        internal int EstimatedSelfChars { get; set; }
    }

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
