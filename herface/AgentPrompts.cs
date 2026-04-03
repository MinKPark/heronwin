using System.Text;
using System.Text.RegularExpressions;

namespace HeronWin.HerFace;

internal sealed record AgentSkillPrompt(
    string Key,
    string FilePath,
    string PromptText);

internal sealed record AgentPromptCatalog(
    string FallbackDefinitionPath,
    string FallbackDefinition,
    string? CoreDefinitionPath,
    string CoreDefinition,
    IReadOnlyList<AgentSkillPrompt> Skills)
{
    public bool HasSplitPrompts => !string.IsNullOrWhiteSpace(CoreDefinition);
}

internal sealed record ComposedAgentPrompt(
    string SystemPrompt,
    string SourceDescription,
    bool UsesFallbackDefinition,
    IReadOnlyList<AgentSkillPrompt> ActiveSkills);

internal static class AgentPromptLoader
{
    public static AgentPromptCatalog Load()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var configuredPath = Environment.GetEnvironmentVariable("AGENT_DEFINITION_PATH")?.Trim();
        var fallbackDefinitionPath = ResolveFallbackDefinitionPath(currentDirectory, configuredPath);
        var fallbackDefinition = LoadPromptText(
            fallbackDefinitionPath,
            "agent definition",
            warnIfMissing: true,
            stripFrontMatter: false);

        var coreDefinitionPath = ResolveCoreDefinitionPath(currentDirectory, fallbackDefinitionPath);
        var coreDefinition = string.IsNullOrWhiteSpace(coreDefinitionPath)
            ? string.Empty
            : LoadPromptText(
                coreDefinitionPath,
                "agent core definition",
                warnIfMissing: false,
                stripFrontMatter: true);

        var skills = string.IsNullOrWhiteSpace(coreDefinitionPath)
            ? []
            : LoadSkillPrompts(Path.Combine(Path.GetDirectoryName(coreDefinitionPath)!, "skills"));

        return new AgentPromptCatalog(
            fallbackDefinitionPath,
            fallbackDefinition,
            coreDefinitionPath,
            coreDefinition,
            skills);
    }

    private static string ResolveFallbackDefinitionPath(string currentDirectory, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Path.Combine(currentDirectory, configuredPath));
        }

        foreach (var candidatePath in new[] { "her.agent.md", ".github/agents/her.agent.md" })
        {
            var resolved = Path.GetFullPath(Path.Combine(currentDirectory, candidatePath));
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        return Path.GetFullPath(Path.Combine(currentDirectory, "her.agent.md"));
    }

    private static string? ResolveCoreDefinitionPath(string currentDirectory, string fallbackDefinitionPath)
    {
        var candidatePaths = new List<string>();
        var fallbackDirectory = Path.GetDirectoryName(fallbackDefinitionPath);
        if (!string.IsNullOrWhiteSpace(fallbackDirectory))
        {
            candidatePaths.Add(Path.Combine(fallbackDirectory, "her.agent.core.md"));
        }

        candidatePaths.Add(Path.GetFullPath(Path.Combine(currentDirectory, "her.agent.core.md")));
        candidatePaths.Add(Path.GetFullPath(Path.Combine(currentDirectory, ".github/agents/her.agent.core.md")));

        return candidatePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static IReadOnlyList<AgentSkillPrompt> LoadSkillPrompts(string skillsDirectory)
    {
        if (!Directory.Exists(skillsDirectory))
        {
            return [];
        }

        var skills = new List<AgentSkillPrompt>();
        foreach (var path in Directory.EnumerateFiles(skillsDirectory, "*.skill.md").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var promptText = LoadPromptText(
                path,
                $"agent skill \"{Path.GetFileName(path)}\"",
                warnIfMissing: false,
                stripFrontMatter: true);
            if (string.IsNullOrWhiteSpace(promptText))
            {
                continue;
            }

            skills.Add(new AgentSkillPrompt(
                GetSkillKey(path),
                path,
                promptText));
        }

        return skills;
    }

    private static string LoadPromptText(
        string path,
        string description,
        bool warnIfMissing,
        bool stripFrontMatter)
    {
        if (!File.Exists(path))
        {
            if (warnIfMissing)
            {
                Console.WriteLine(
                    $"Warning: {description} file not found at \"{path}\"; continuing without it.");
            }

            return string.Empty;
        }

        try
        {
            var content = File.ReadAllText(path).Trim();
            return stripFrontMatter
                ? StripFrontMatter(content)
                : content;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Warning: failed to read {description} at \"{path}\"; continuing without it. {ex.Message}");
            return string.Empty;
        }
    }

    private static string StripFrontMatter(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        using var reader = new StringReader(text);
        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine?.Trim(), "---", StringComparison.Ordinal))
        {
            return text.Trim();
        }

        var body = new StringBuilder();
        var closingMarkerFound = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!closingMarkerFound)
            {
                if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
                {
                    closingMarkerFound = true;
                }

                continue;
            }

            body.AppendLine(line);
        }

        return closingMarkerFound
            ? body.ToString().Trim()
            : text.Trim();
    }

    private static string GetSkillKey(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".skill.md", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".skill.md".Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }
}

internal static class AgentPromptComposer
{
    public static ComposedAgentPrompt Compose(
        AgentPromptCatalog catalog,
        string userText,
        IReadOnlyList<ToolDefinition> tools)
    {
        if (!catalog.HasSplitPrompts)
        {
            return BuildFallbackPrompt(catalog);
        }

        var activeSkills = SelectActiveSkills(catalog, userText, tools);
        var sections = new List<string> { catalog.CoreDefinition.Trim() };
        if (activeSkills.Count > 0)
        {
            sections.Add("## Active Skills\nUse the following additional skill guidance for this turn.");
            sections.AddRange(activeSkills.Select(skill => skill.PromptText));
        }

        var systemPrompt = string.Join(
            "\n\n",
            sections.Where(section => !string.IsNullOrWhiteSpace(section)));
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            return BuildFallbackPrompt(catalog);
        }

        var sourceDescription = activeSkills.Count == 0
            ? $"core:{Path.GetFileName(catalog.CoreDefinitionPath)}"
            : $"core+skills:{string.Join(", ", activeSkills.Select(skill => skill.Key))}";

        return new ComposedAgentPrompt(
            systemPrompt,
            sourceDescription,
            UsesFallbackDefinition: false,
            activeSkills);
    }

    internal static IReadOnlyList<AgentSkillPrompt> SelectActiveSkills(
        AgentPromptCatalog catalog,
        string userText,
        IReadOnlyList<ToolDefinition> tools)
    {
        if (!catalog.HasSplitPrompts || catalog.Skills.Count == 0)
        {
            return [];
        }

        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedText = NormalizeText(userText);

        if (HasAnyTool(toolNames, "describe_selected_window", "describe_selected_window_focus", "capture_selected_window_screenshot"))
        {
            selectedKeys.Add("ui-refresh-and-evidence");
        }

        if (MatchesLaunchRequest(normalizedText)
            && HasAnyTool(toolNames, "list_windows", "select_window", "list_taskbar_elements", "select_taskbar_app", "launch_app_via_taskbar_search"))
        {
            selectedKeys.Add("desktop-launch-and-first-look");
        }

        if (MatchesBrowserRequest(userText, normalizedText)
            && HasAnyTool(toolNames, "describe_selected_window", "describe_selected_window_focus", "invoke_selected_window_element", "focus_selected_window_element", "send_input_to_window", "capture_selected_window_screenshot"))
        {
            selectedKeys.Add("browser-navigation-and-web-operations");
        }

        if (MatchesSearchOrEnumerationRequest(normalizedText)
            && HasAnyTool(toolNames, "describe_selected_window", "describe_selected_window_focus", "capture_selected_window_screenshot"))
        {
            selectedKeys.Add("search-and-enumeration");
        }

        if (MatchesActionRequest(normalizedText)
            && HasAnyTool(toolNames, "list_main_menu_items", "list_context_menu_items", "invoke_main_menu_item", "invoke_context_menu_item", "invoke_selected_window_element", "focus_selected_window_element", "send_input_to_window"))
        {
            selectedKeys.Add("action-discovery-and-invocation");
        }

        return catalog.Skills
            .Where(skill => selectedKeys.Contains(skill.Key))
            .ToList();
    }

    private static ComposedAgentPrompt BuildFallbackPrompt(AgentPromptCatalog catalog)
        => new(
            catalog.FallbackDefinition,
            $"fallback:{Path.GetFileName(catalog.FallbackDefinitionPath)}",
            UsesFallbackDefinition: true,
            []);

    private static bool MatchesLaunchRequest(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        if (ContainsAny(normalizedText, "launch", "start", "switch to", "bring forward", "bring up", "bring to front"))
        {
            return true;
        }

        return normalizedText.Contains("open ", StringComparison.Ordinal)
               && !ContainsAny(
                   normalizedText,
                   " website",
                   " web site",
                   " site",
                   " url",
                   " address bar",
                   " browser",
                   " tab",
                   " link",
                   " menu",
                   " button",
                   " dialog",
                   " tab",
                   " pane",
                   " panel",
                   " field",
                   " box",
                   " textbox",
                   " text box",
                   " search",
                   " result",
                   " item",
                   " row",
                   " column",
                   " cell",
                   " folder",
                   " file",
                   " document",
                   " page",
                   " prompt",
                   " message",
                   " control");
    }

    private static bool MatchesBrowserRequest(string rawText, string normalizedText)
    {
        if (ContainsAny(
                normalizedText,
                "website",
                "web site",
                "url",
                "address bar",
                "browser",
                "webpage",
                "web page",
                "open site",
                "open website",
                "open url"))
        {
            return true;
        }

        if (ContainsAny(normalizedText, "edge", "chrome", "firefox", "safari")
            && ContainsAny(
                normalizedText,
                "tab",
                "new tab",
                "back",
                "forward",
                "refresh",
                "reload",
                "home page",
                "homepage"))
        {
            return true;
        }

        return Regex.IsMatch(rawText, @"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)
               || Regex.IsMatch(rawText, @"\b[\w-]+\.(?:com|org|net|io|ai|gov|edu|app|dev|tv|co)\b", RegexOptions.IgnoreCase);
    }

    private static bool MatchesSearchOrEnumerationRequest(string normalizedText)
        => ContainsAny(
            normalizedText,
            "search",
            "find ",
            "look for",
            "what is visible",
            "what s visible",
            "what do you see",
            "what is on screen",
            "what s on screen",
            "which items",
            "what results",
            "show me",
            "list ",
            "enumerate");

    private static bool MatchesActionRequest(string normalizedText)
    {
        if (ContainsAny(
                normalizedText,
                "click",
                "press",
                "select",
                "choose",
                "save",
                "rename",
                "delete",
                "copy",
                "paste",
                "close",
                "maximize",
                "minimize",
                "focus",
                "invoke",
                "scroll",
                "type ",
                "enter ",
                "menu"))
        {
            return true;
        }

        return normalizedText.Contains("open ", StringComparison.Ordinal)
               && !MatchesLaunchRequest(normalizedText);
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return string.Join(
            ' ',
            builder
                .ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool HasAnyTool(IReadOnlySet<string> toolNames, params string[] candidateNames)
        => candidateNames.Any(toolNames.Contains);

    private static bool ContainsAny(string text, params string[] candidates)
        => candidates.Any(candidate => text.Contains(candidate, StringComparison.Ordinal));
}
