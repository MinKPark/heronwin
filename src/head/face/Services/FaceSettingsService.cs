using HeronWin.Face.Models;
using System.IO;
using System.Text.Json;

namespace HeronWin.Face.Services;

public sealed class FaceSettingsService
{
    private const string SettingsFileName = "face.settings.json";

    public async Task<FaceAppSettings> LoadAsync()
    {
        var defaultSettings = new FaceAppSettings
        {
            EnvFilePath = GuessBrainEnvPath()
        };
        var path = GetSettingsFilePath();
        if (!File.Exists(path))
        {
            ApplyEnvOverrides(defaultSettings);
            return defaultSettings;
        }

        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<FaceAppSettings>(stream);
        var mergedSettings = settings ?? defaultSettings;
        if (string.IsNullOrWhiteSpace(mergedSettings.EnvFilePath))
        {
            mergedSettings.EnvFilePath = defaultSettings.EnvFilePath;
        }

        ApplyEnvOverrides(mergedSettings);
        return mergedSettings;
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
            var candidate = Path.Combine(current.FullName, "src", "head", "brain", ".env");
            if (File.Exists(candidate) || Directory.Exists(Path.GetDirectoryName(candidate)!))
            {
                return candidate;
            }

            var directCandidate = Path.Combine(current.FullName, "head", "brain", ".env");
            if (File.Exists(directCandidate) || Directory.Exists(Path.GetDirectoryName(directCandidate)!))
            {
                return directCandidate;
            }

            current = current.Parent;
        }

        return Path.Combine(Environment.CurrentDirectory, ".env");
    }

    private static void ApplyEnvOverrides(FaceAppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.EnvFilePath) || !File.Exists(settings.EnvFilePath))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(settings.EnvFilePath))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            {
                value = value[1..^1];
            }

            if (key.Equals("FACE_PIPE_NAME", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(value))
            {
                settings.PipeName = value;
            }
        }
    }
}
