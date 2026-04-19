namespace HeronWin.Face.Models;

public sealed class FaceStatusMessage
{
    public string State { get; set; } = string.Empty;

    public string Headline { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string? Transcript { get; set; }

    public string? ToolName { get; set; }

    public string? TimestampUtc { get; set; }
}