namespace HeronWin.Face.Models;

public sealed class FaceAppSettings
{
    public string PipeName { get; set; } = "heronwin.face";

    public string EnvFilePath { get; set; } = string.Empty;

    public bool IsPinned { get; set; } = true;
}