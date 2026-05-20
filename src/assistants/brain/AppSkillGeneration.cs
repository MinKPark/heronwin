using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace HeronWin.Brain;

internal sealed record PendingAppSkillOffer(string AppName, string Group);

internal sealed record GeneratedSkillFileDraft(string FileName, string Content);

internal sealed record GeneratedSkillGroupDraft(
    string AppName,
    string Group,
    string? SourceUrl,
    IReadOnlyList<GeneratedSkillFileDraft> Files);

internal static class AppSkillGenerationCoordinator
{
    private static readonly HashSet<string> ReservedGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows",
        "any-app",
        "general"
    };

    public static bool TryBuildUnknownAppSkillOffer(
        string userText,
        IReadOnlyList<AgentMessage> history,
        AgentPromptCatalog catalog,
        out AgentReply reply)
    {
        reply = new AgentReply(string.Empty, string.Empty, string.Empty);

        if (TryGetPendingOffer(history, out _)
            || !AgentRunner.TryExtractRequestedAppLaunchName(userText, out var appName)
            || HasAppSkillGroup(catalog, appName))
        {
            return false;
        }

        var group = BuildGroupSlug(appName);
        var say = $"I don't have a dedicated {appName} skill group yet. Do you want me to generate one first from the app's official quickstart instructions, or should I just open {appName} now?";
        var log = $"Brain does not have a dedicated app skill group for {appName}. Say yes to generate a new `{group}` skill group first from the app's official quickstart guidance, or say no to continue opening {appName} without one.";
        var rawText = JsonSerializer.Serialize(new
        {
            say,
            log,
            skill_offer = new
            {
                app_name = appName,
                group
            }
        });

        reply = new AgentReply(log, say, rawText);
        return true;
    }

    public static bool TryBuildApprovedGenerationRequest(
        IReadOnlyList<AgentMessage> history,
        string userText,
        out PendingAppSkillOffer offer,
        out string generationUserText)
    {
        generationUserText = string.Empty;
        if (!TryGetPendingOffer(history, out offer) || !LooksAffirmative(userText))
        {
            offer = new PendingAppSkillOffer(string.Empty, string.Empty);
            return false;
        }

        generationUserText =
            $"Generate the {offer.AppName} skill group first. Use the browser and official website or official documentation for {offer.AppName} to find the quickstart, onboarding, or first-use instructions. Then draft the new `{offer.Group}` skill group files for Brain instead of opening the app yet.";
        return true;
    }

    public static bool TryBuildDeclinedLaunchRequest(
        IReadOnlyList<AgentMessage> history,
        string userText,
        out PendingAppSkillOffer offer,
        out string launchUserText)
    {
        launchUserText = string.Empty;
        if (!TryGetPendingOffer(history, out offer) || !LooksNegative(userText))
        {
            offer = new PendingAppSkillOffer(string.Empty, string.Empty);
            return false;
        }

        launchUserText = $"Open {offer.AppName}.";
        return true;
    }

    public static bool TryGetPendingOffer(
        IReadOnlyList<AgentMessage> history,
        out PendingAppSkillOffer offer)
    {
        offer = new PendingAppSkillOffer(string.Empty, string.Empty);
        var lastAssistant = history.LastOrDefault(message => message is AgentMessage.Assistant { ToolCalls: null }) as AgentMessage.Assistant;
        if (lastAssistant is null || string.IsNullOrWhiteSpace(lastAssistant.Content))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(lastAssistant.Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(document.RootElement, "skill_offer", out var offerElement) ||
                offerElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var appName = TryGetString(offerElement, "app_name") ?? TryGetString(offerElement, "appName");
            var group = TryGetString(offerElement, "group");
            if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(group))
            {
                return false;
            }

            offer = new PendingAppSkillOffer(appName.Trim(), group.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string BuildGenerationPromptAugmentation(PendingAppSkillOffer offer)
        => $@"Special task: the user approved generating a new app skill group for {offer.AppName} before app launch.

Do not launch or operate {offer.AppName} yet.
Use browser-capable tools to reach the official website, official help center, or official documentation for {offer.AppName}. Prefer the vendor's own domain over third-party guides. Look for quickstart, onboarding, getting started, or first-use instructions.

When you have enough evidence, reply as strict JSON with `say`, `log`, and optional `skill_generation`.

If you include `skill_generation`, use this shape:
{{
    ""say"": ""..."",
    ""log"": ""..."",
    ""skill_generation"": {{
        ""app_name"": ""{offer.AppName}"",
        ""group"": ""{offer.Group}"",
        ""source_url"": ""https://official-source.example/..."",
        ""files"": [
            {{ ""file_name"": ""{offer.Group}-surface-and-state.skill.md"", ""content"": ""full file text"" }}
        ]
    }}
}}

File rules:
- Draft 1 to 3 `.skill.md` files only.
- Keep the skill group split by independently activatable UI surface and distinct decision logic.
- Prefer high-value guidance over over-fragmentation.
- Each file must be complete markdown with YAML frontmatter.
- Keep the `group` as `{offer.Group}`.
- If the official source is insufficient or uncertain, omit `skill_generation` and explain the blocker plainly in `log`.";

    public static bool TryPersistGeneratedSkillGroup(
        string rawAssistantText,
        PendingAppSkillOffer expectedOffer,
        AgentPromptCatalog currentCatalog,
        out AgentPromptCatalog refreshedCatalog,
        out string persistenceSummary)
    {
        refreshedCatalog = currentCatalog;
        persistenceSummary = string.Empty;

        if (!TryParseGenerationDraft(rawAssistantText, out var draft) ||
            !GroupMatchesExpected(draft.Group, expectedOffer.Group) ||
            draft.Files.Count == 0 ||
            !TryGetSkillsRootDirectory(currentCatalog, out var skillsRootDirectory))
        {
            return false;
        }

        var groupDirectory = Path.Combine(skillsRootDirectory, expectedOffer.Group);
        Directory.CreateDirectory(groupDirectory);

        foreach (var file in draft.Files)
        {
            if (!IsSafeSkillFileName(file.FileName))
            {
                continue;
            }

            var destinationPath = Path.Combine(groupDirectory, file.FileName);
            File.WriteAllText(destinationPath, NormalizeFileContent(file.Content), Encoding.UTF8);
        }

        refreshedCatalog = ReloadCatalogAfterSkillGeneration(currentCatalog, skillsRootDirectory);

        persistenceSummary = string.IsNullOrWhiteSpace(draft.SourceUrl)
            ? $"Saved {draft.Files.Count} skill file(s) for the `{expectedOffer.Group}` group."
            : $"Saved {draft.Files.Count} skill file(s) for the `{expectedOffer.Group}` group from {draft.SourceUrl}.";
        return true;
    }

    internal static bool HasAppSkillGroup(AgentPromptCatalog catalog, string appName)
    {
        var normalizedApp = NormalizeIdentifier(appName);
        if (string.IsNullOrWhiteSpace(normalizedApp))
        {
            return false;
        }

        foreach (var group in catalog.Skills
                     .Select(skill => skill.Metadata.Group)
                     .Where(group => !string.IsNullOrWhiteSpace(group))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ReservedGroups.Contains(group))
            {
                continue;
            }

            var groupSegments = group.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in groupSegments)
            {
                var normalizedSegment = NormalizeIdentifier(segment);
                if (IdentifiersMatch(normalizedApp, normalizedSegment))
                {
                    return true;
                }
            }

            var skillKeywords = catalog.Skills
                .Where(skill => string.Equals(skill.Metadata.Group, group, StringComparison.OrdinalIgnoreCase))
                .SelectMany(skill => skill.Metadata.Activation.WhenAnyKeywords)
                .Concat(catalog.Skills
                    .Where(skill => string.Equals(skill.Metadata.Group, group, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(skill => skill.Metadata.Activation.WhenAllKeywords));
            foreach (var keyword in skillKeywords)
            {
                var normalizedKeyword = NormalizeIdentifier(keyword);
                if (IdentifiersMatch(normalizedApp, normalizedKeyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static string BuildGroupSlug(string appName)
    {
        var words = Regex.Matches(appName, @"[A-Za-z0-9]+")
            .Select(match => match.Value.ToLowerInvariant())
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToArray();

        return words.Length == 0 ? "new-app" : string.Join('-', words);
    }

    private static bool TryParseGenerationDraft(string rawAssistantText, out GeneratedSkillGroupDraft draft)
    {
        draft = new GeneratedSkillGroupDraft(string.Empty, string.Empty, null, []);
        if (string.IsNullOrWhiteSpace(rawAssistantText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawAssistantText);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(document.RootElement, "skill_generation", out var generationElement) ||
                generationElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var appName = TryGetString(generationElement, "app_name") ?? TryGetString(generationElement, "appName") ?? string.Empty;
            var group = TryGetString(generationElement, "group") ?? string.Empty;
            var sourceUrl = TryGetString(generationElement, "source_url") ?? TryGetString(generationElement, "sourceUrl");
            if (string.IsNullOrWhiteSpace(group) ||
                !TryGetProperty(generationElement, "files", out var filesElement) ||
                filesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var files = new List<GeneratedSkillFileDraft>();
            foreach (var fileElement in filesElement.EnumerateArray())
            {
                if (fileElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var fileName = TryGetString(fileElement, "file_name") ?? TryGetString(fileElement, "fileName");
                var content = TryGetString(fileElement, "content");
                if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                files.Add(new GeneratedSkillFileDraft(fileName.Trim(), content));
            }

            if (files.Count == 0)
            {
                return false;
            }

            draft = new GeneratedSkillGroupDraft(appName.Trim(), group.Trim(), sourceUrl?.Trim(), files);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetSkillsRootDirectory(AgentPromptCatalog catalog, out string skillsRootDirectory)
    {
        skillsRootDirectory = string.Empty;

        if (TryGetSharedSkillsRootDirectory(catalog, out skillsRootDirectory))
        {
            Directory.CreateDirectory(skillsRootDirectory);
            return true;
        }

        var coreDefinitionPaths = SplitCatalogPathList(catalog.CoreDefinitionPath);
        var promptPath = coreDefinitionPaths.FirstOrDefault()
            ?? catalog.FallbackDefinitionPath;
        var agentDirectory = Path.GetDirectoryName(promptPath);
        if (string.IsNullOrWhiteSpace(agentDirectory))
        {
            return false;
        }

        skillsRootDirectory = Path.Combine(agentDirectory, "skills");
        Directory.CreateDirectory(skillsRootDirectory);
        return true;
    }

    private static AgentPromptCatalog ReloadCatalogAfterSkillGeneration(
        AgentPromptCatalog currentCatalog,
        string skillsRootDirectory)
    {
        var coreDefinitionPaths = SplitCatalogPathList(currentCatalog.CoreDefinitionPath)
            .Where(File.Exists)
            .ToArray();

        if (coreDefinitionPaths.Length <= 1)
        {
            return AgentPromptLoader.LoadFromResolvedPaths(
                currentCatalog.FallbackDefinitionPath,
                coreDefinitionPaths.FirstOrDefault());
        }

        var skillDirectories = ResolveSkillDirectoriesForCorePaths(coreDefinitionPaths, skillsRootDirectory);
        return AgentPromptLoader.LoadFromResolvedProfile(
            currentCatalog.FallbackDefinitionPath,
            coreDefinitionPaths,
            skillDirectories);
    }

    private static IReadOnlyList<string> ResolveSkillDirectoriesForCorePaths(
        IReadOnlyList<string> coreDefinitionPaths,
        string skillsRootDirectory)
    {
        var skillDirectories = new List<string>();
        foreach (var coreDefinitionPath in coreDefinitionPaths)
        {
            var coreDirectory = Path.GetDirectoryName(coreDefinitionPath);
            if (string.IsNullOrWhiteSpace(coreDirectory))
            {
                continue;
            }

            var candidateDirectory = Path.Combine(coreDirectory, "skills");
            if (Directory.Exists(candidateDirectory))
            {
                skillDirectories.Add(candidateDirectory);
            }
        }

        if (Directory.Exists(skillsRootDirectory))
        {
            skillDirectories.Add(skillsRootDirectory);
        }

        return skillDirectories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetSharedSkillsRootDirectory(AgentPromptCatalog catalog, out string skillsRootDirectory)
    {
        foreach (var skill in catalog.Skills)
        {
            if (TryFindSharedSkillsRootDirectory(skill.FilePath, out skillsRootDirectory))
            {
                return true;
            }
        }

        foreach (var coreDefinitionPath in SplitCatalogPathList(catalog.CoreDefinitionPath))
        {
            var coreDirectory = Path.GetDirectoryName(coreDefinitionPath);
            if (!string.IsNullOrWhiteSpace(coreDirectory) &&
                string.Equals(Path.GetFileName(coreDirectory), "shared", StringComparison.OrdinalIgnoreCase))
            {
                skillsRootDirectory = Path.Combine(coreDirectory, "skills");
                return true;
            }
        }

        skillsRootDirectory = string.Empty;
        return false;
    }

    private static bool TryFindSharedSkillsRootDirectory(string skillPath, out string skillsRootDirectory)
    {
        var skillDirectory = Path.GetDirectoryName(skillPath);
        if (string.IsNullOrWhiteSpace(skillDirectory))
        {
            skillsRootDirectory = string.Empty;
            return false;
        }

        var currentDirectory = new DirectoryInfo(skillDirectory);
        while (currentDirectory is not null)
        {
            if (string.Equals(currentDirectory.Name, "skills", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(currentDirectory.Parent?.Name, "shared", StringComparison.OrdinalIgnoreCase))
            {
                skillsRootDirectory = currentDirectory.FullName;
                return true;
            }

            currentDirectory = currentDirectory.Parent;
        }

        skillsRootDirectory = string.Empty;
        return false;
    }

    private static IReadOnlyList<string> SplitCatalogPathList(string? pathList)
        => string.IsNullOrWhiteSpace(pathList)
            ? []
            : pathList.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeFileContent(string content)
        => content.Replace("\r\n", "\n").Trim() + Environment.NewLine;

    private static bool IsSafeSkillFileName(string fileName)
        => !string.IsNullOrWhiteSpace(fileName)
           && fileName.EndsWith(".skill.md", StringComparison.OrdinalIgnoreCase)
           && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
           && !fileName.Contains(Path.DirectorySeparatorChar)
           && !fileName.Contains(Path.AltDirectorySeparatorChar)
           && !fileName.Contains("..", StringComparison.Ordinal);

    private static bool GroupMatchesExpected(string actualGroup, string expectedGroup)
        => string.Equals(NormalizeIdentifier(actualGroup), NormalizeIdentifier(expectedGroup), StringComparison.Ordinal);

    private static bool LooksAffirmative(string userText)
    {
        var normalized = NormalizePlainText(userText);
        return normalized is "yes" or "y" or "sure" or "ok" or "okay" or "yep" or "yeah"
               || normalized.Contains("generate it", StringComparison.Ordinal)
               || normalized.Contains("create it", StringComparison.Ordinal)
               || normalized.Contains("generate the skill", StringComparison.Ordinal)
               || normalized.Contains("create the skill", StringComparison.Ordinal)
               || normalized.Contains("skill group first", StringComparison.Ordinal)
               || normalized.Contains("do that", StringComparison.Ordinal);
    }

    private static bool LooksNegative(string userText)
    {
        var normalized = NormalizePlainText(userText);
        return normalized is "no" or "n" or "nope" or "nah"
               || normalized.Contains("just open it", StringComparison.Ordinal)
               || normalized.Contains("open it now", StringComparison.Ordinal)
               || normalized.Contains("just open the app", StringComparison.Ordinal)
               || normalized.Contains("skip generation", StringComparison.Ordinal)
               || normalized.Contains("skip the skill", StringComparison.Ordinal)
               || normalized.Contains("without one", StringComparison.Ordinal)
               || normalized.Contains("do not generate", StringComparison.Ordinal)
               || normalized.Contains("don't generate", StringComparison.Ordinal)
               || normalized.Contains("dont generate", StringComparison.Ordinal);
    }

    private static string NormalizeIdentifier(string text)
        => string.Join(
            string.Empty,
            Regex.Matches(text ?? string.Empty, @"[A-Za-z0-9]+")
                .Select(match => match.Value.ToLowerInvariant()));

    private static string NormalizePlainText(string text)
        => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();

    private static bool IdentifiersMatch(string appIdentifier, string candidateIdentifier)
    {
        if (string.IsNullOrWhiteSpace(appIdentifier) || string.IsNullOrWhiteSpace(candidateIdentifier))
        {
            return false;
        }

        if (string.Equals(appIdentifier, candidateIdentifier, StringComparison.Ordinal))
        {
            return true;
        }

        if (candidateIdentifier.Length >= 4 && appIdentifier.Contains(candidateIdentifier, StringComparison.Ordinal))
        {
            return true;
        }

        return appIdentifier.Length >= 4 && candidateIdentifier.Contains(appIdentifier, StringComparison.Ordinal);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        var camelCaseName = propertyName.Contains('_', StringComparison.Ordinal)
            ? ConvertSnakeToCamelCase(propertyName)
            : propertyName;
        return element.TryGetProperty(camelCaseName, out property);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string ConvertSnakeToCamelCase(string text)
    {
        var parts = text.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return text;
        }

        return parts[0] + string.Concat(parts.Skip(1).Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
