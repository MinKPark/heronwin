namespace HeronWin.Brain;

internal sealed record DesktopEvidenceMetadata(
    long SourceTurnId,
    string SourceKind,
    DateTimeOffset CapturedAtUtc,
    bool IsPostActionSnapshot);

internal sealed class DesktopSessionContext
{
    public string? CurrentWindowHandle { get; set; }

    public string? CurrentWindowTitle { get; set; }

    public string? RecentListWindowsOutput { get; set; }

    public string? RecentWindowContext { get; set; }

    public string? RecentUiTreeContext { get; set; }

    public DesktopEvidenceMetadata? RecentUiTreeEvidenceMetadata { get; set; }

    public string? RecentFocusContext { get; set; }

    public DesktopEvidenceMetadata? RecentFocusEvidenceMetadata { get; set; }

    public string? CurrentUiElementContext { get; set; }

    public string? CurrentFocusElementContext { get; set; }
}
