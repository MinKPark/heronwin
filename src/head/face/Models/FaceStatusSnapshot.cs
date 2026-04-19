namespace HeronWin.Face.Models;

public sealed record FaceStatusSnapshot(
    FaceStatusKind Kind,
    string Headline,
    string Detail,
    DateTimeOffset Timestamp,
    string? Transcript = null,
    string? ToolName = null,
    bool IsDemo = false);