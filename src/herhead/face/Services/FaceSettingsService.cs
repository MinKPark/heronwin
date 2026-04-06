using HeronWin.Face.Models;
using System.IO;
using System.Text.Json;

namespace HeronWin.Face.Services;

public sealed class FaceSettingsService
{
    private const string SettingsFileName = "face.settings.json";

    public async Task<FaceAppSettings> LoadAsync()
    {
        var path = GetSettingsFilePath();
        if (!File.Exists(path))
        {
            return new FaceAppSettings
            {
                EnvFilePath = GuessBrainEnvPath()
            };
        }

        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<FaceAppSettings>(stream);
        return settings ?? new FaceAppSettings { EnvFilePath = GuessBrainEnvPath() };
    }

    public async Task SaveAsync(FaceAppSettings settings)
    {
        var path = GetSettingsFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string GetSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "HeronWin", SettingsFileName);
    }

    private static string GuessBrainEnvPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "herhead", "brain", ".env");
            if (File.Exists(candidate) || Directory.Exists(Path.GetDirectoryName(candidate)!))
            {
                return candidate;
            }

            var directCandidate = Path.Combine(current.FullName, "herhead", "brain", ".env");
            if (File.Exists(directCandidate) || Directory.Exists(Path.GetDirectoryName(directCandidate)!))
            {
                return directCandidate;
            }

            current = current.Parent;
        }

        return Path.Combine(Environment.CurrentDirectory, ".env");
    }
}