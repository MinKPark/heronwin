using HeronWin.Brain;

namespace HeronWin.Ava;

internal sealed record AvaRunBundle(
    string Name,
    string UxScenarioPath,
    string ValidationConfigPath);

internal static class AvaRunBundleLoader
{
    public static AvaRunBundle LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("AVA run bundle file was not found.", fullPath);
        }

        return Parse(File.ReadAllText(fullPath), fullPath);
    }

    internal static AvaRunBundle Parse(string yaml, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new InvalidOperationException("AVA run bundle YAML was empty.");
        }

        if (BrainYamlParser.Parse(yaml) is not BrainYamlMapping mapping)
        {
            throw new InvalidOperationException("AVA run bundle YAML must be a mapping.");
        }

        var bundleDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
        var uxScenario = RequireString(mapping, "uxScenario");
        var validationConfig = RequireString(mapping, "validationConfig");

        return new AvaRunBundle(
            GetString(mapping, "name") ?? Path.GetFileNameWithoutExtension(sourcePath),
            ResolveRelativePath(bundleDirectory, uxScenario),
            ResolveRelativePath(bundleDirectory, validationConfig));
    }

    private static string ResolveRelativePath(string baseDirectory, string path)
        => Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path));

    private static string RequireString(BrainYamlMapping mapping, string propertyName)
        => GetString(mapping, propertyName) ??
           throw new InvalidOperationException($"AVA run bundle must provide {propertyName}.");

    private static string? GetString(BrainYamlMapping mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var node) ||
            node is not BrainYamlScalar scalar ||
            string.IsNullOrWhiteSpace(scalar.Value))
        {
            return null;
        }

        return scalar.Value.Trim();
    }
}
