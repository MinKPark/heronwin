using System.Text;
using System.Text.RegularExpressions;

namespace HeronWin.HerFace;

internal sealed record AgentSkillActivation(
    IReadOnlyList<string> WhenAnyIntents,
    IReadOnlyList<string> WhenAllIntents,
    IReadOnlyList<string> UnlessAnyIntents,
    IReadOnlyList<string> WhenAnyTools,
    IReadOnlyList<string> WhenAllTools)
{
    public bool HasCriteria =>
        WhenAnyIntents.Count > 0 ||
        WhenAllIntents.Count > 0 ||
        UnlessAnyIntents.Count > 0 ||
        WhenAnyTools.Count > 0 ||
        WhenAllTools.Count > 0;
}

internal sealed record AgentSkillMetadata(
    string Id,
    string? Summary,
    IReadOnlyList<string> PreferredTools,
    IReadOnlyList<string> AppliesWhen,
    AgentSkillActivation Activation)
{
    public bool HasStructuredActivation => Activation.HasCriteria;
}

internal sealed record AgentSkillPrompt(
    string Key,
    string FilePath,
    string PromptText,
    AgentSkillMetadata Metadata);

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
            var prompt = LoadSkillPrompt(path);
            if (prompt is not null)
            {
                skills.Add(prompt);
            }
        }

        return skills;
    }

    private static AgentSkillPrompt? LoadSkillPrompt(string path)
    {
        var rawContent = LoadPromptFile(
            path,
            $"agent skill \"{Path.GetFileName(path)}\"",
            warnIfMissing: false);
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        var splitContent = SplitFrontMatter(rawContent);
        if (string.IsNullOrWhiteSpace(splitContent.BodyText))
        {
            return null;
        }

        var metadata = ParseSkillMetadata(path, splitContent.HasFrontMatter ? splitContent.FrontMatterText : null);
        return new AgentSkillPrompt(
            metadata.Id,
            path,
            splitContent.BodyText,
            metadata);
    }

    private static AgentSkillMetadata ParseSkillMetadata(string path, string? frontMatterText)
    {
        var fallbackId = GetSkillKey(path);
        if (string.IsNullOrWhiteSpace(frontMatterText))
        {
            return BuildDefaultSkillMetadata(fallbackId);
        }

        try
        {
            if (HerfaceYamlParser.Parse(frontMatterText) is not HerfaceYamlMapping mapping)
            {
                throw new InvalidOperationException("Skill frontmatter must be a YAML mapping.");
            }

            var id = ReadOptionalScalar(mapping, "id");
            var summary = ReadOptionalScalar(mapping, "summary");

            return new AgentSkillMetadata(
                string.IsNullOrWhiteSpace(id) ? fallbackId : id.Trim(),
                string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
                ReadStringValues(mapping, "preferred_tools"),
                ReadStringValues(mapping, "applies_when"),
                ParseSkillActivation(mapping));
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Warning: failed to parse agent skill metadata at \"{path}\"; continuing with filename-based compatibility. {ex.Message}");
            return BuildDefaultSkillMetadata(fallbackId);
        }
    }

    private static AgentSkillMetadata BuildDefaultSkillMetadata(string fallbackId)
        => new(
            fallbackId,
            Summary: null,
            PreferredTools: [],
            AppliesWhen: [],
            Activation: new AgentSkillActivation([], [], [], [], []));

    private static AgentSkillActivation ParseSkillActivation(HerfaceYamlMapping mapping)
    {
        if (!mapping.TryGetValue("activation", out var activationNode))
        {
            return new AgentSkillActivation([], [], [], [], []);
        }

        if (activationNode is not HerfaceYamlMapping activationMapping)
        {
            throw new InvalidOperationException("Skill activation metadata must be a YAML mapping.");
        }

        return new AgentSkillActivation(
            ReadStringValues(activationMapping, "when_any_intents"),
            ReadStringValues(activationMapping, "when_all_intents"),
            ReadStringValues(activationMapping, "unless_any_intents"),
            ReadStringValues(activationMapping, "when_any_tools"),
            ReadStringValues(activationMapping, "when_all_tools"));
    }

    private static IReadOnlyList<string> ReadStringValues(HerfaceYamlMapping mapping, string key)
    {
        if (!mapping.TryGetValue(key, out var node))
        {
            return [];
        }

        return node switch
        {
            HerfaceYamlScalar scalar when !string.IsNullOrWhiteSpace(scalar.Value)
                => [scalar.Value.Trim()],
            HerfaceYamlSequence sequence => sequence.Items
                .OfType<HerfaceYamlScalar>()
                .Select(item => item.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray(),
            _ => []
        };
    }

    private static string? ReadOptionalScalar(HerfaceYamlMapping mapping, string key)
        => mapping.TryGetValue(key, out var node) && node is HerfaceYamlScalar scalar
            ? scalar.Value
            : null;

    private static string LoadPromptText(
        string path,
        string description,
        bool warnIfMissing,
        bool stripFrontMatter)
    {
        var content = LoadPromptFile(path, description, warnIfMissing);
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return stripFrontMatter
            ? StripFrontMatter(content)
            : content;
    }

    private static string LoadPromptFile(
        string path,
        string description,
        bool warnIfMissing)
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
            return File.ReadAllText(path).Trim();
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

        var splitContent = SplitFrontMatter(text);
        return splitContent.HasFrontMatter
            ? splitContent.BodyText
            : text.Trim();
    }

    private static FrontMatterSplitResult SplitFrontMatter(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new FrontMatterSplitResult(false, string.Empty, string.Empty);
        }

        using var reader = new StringReader(text);
        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine?.Trim(), "---", StringComparison.Ordinal))
        {
            return new FrontMatterSplitResult(false, string.Empty, text.Trim());
        }

        var frontMatter = new StringBuilder();
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
                    continue;
                }

                frontMatter.AppendLine(line);
                continue;
            }

            body.AppendLine(line);
        }

        return closingMarkerFound
            ? new FrontMatterSplitResult(true, frontMatter.ToString().Trim(), body.ToString().Trim())
            : new FrontMatterSplitResult(false, string.Empty, text.Trim());
    }

    private static string GetSkillKey(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".skill.md", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".skill.md".Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private sealed record FrontMatterSplitResult(
        bool HasFrontMatter,
        string FrontMatterText,
        string BodyText);
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

        var requestIntents = BuildRequestIntentSet(userText);
        var availableToolNames = tools
            .Select(tool => NormalizeToolIdentifier(tool.Name))
            .Where(toolName => !string.IsNullOrWhiteSpace(toolName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return catalog.Skills
            .Where(skill => ShouldActivateSkill(skill, requestIntents, availableToolNames))
            .ToList();
    }

    private static bool ShouldActivateSkill(
        AgentSkillPrompt skill,
        IReadOnlySet<string> requestIntents,
        IReadOnlySet<string> availableToolNames)
    {
        if (skill.Metadata.HasStructuredActivation)
        {
            return EvaluateStructuredSkillActivation(skill.Metadata.Activation, requestIntents, availableToolNames);
        }

        return EvaluateLegacySkillActivation(skill.Key, requestIntents, availableToolNames);
    }

    private static bool EvaluateStructuredSkillActivation(
        AgentSkillActivation activation,
        IReadOnlySet<string> requestIntents,
        IReadOnlySet<string> availableToolNames)
    {
        if (!activation.HasCriteria)
        {
            return false;
        }

        if (activation.WhenAnyIntents.Count > 0 &&
            !MatchesAny(requestIntents, activation.WhenAnyIntents, NormalizeIntentIdentifier))
        {
            return false;
        }

        if (activation.WhenAllIntents.Count > 0 &&
            !MatchesAll(requestIntents, activation.WhenAllIntents, NormalizeIntentIdentifier))
        {
            return false;
        }

        if (activation.UnlessAnyIntents.Count > 0 &&
            MatchesAny(requestIntents, activation.UnlessAnyIntents, NormalizeIntentIdentifier))
        {
            return false;
        }

        if (activation.WhenAnyTools.Count > 0 &&
            !MatchesAny(availableToolNames, activation.WhenAnyTools, NormalizeToolIdentifier))
        {
            return false;
        }

        if (activation.WhenAllTools.Count > 0 &&
            !MatchesAll(availableToolNames, activation.WhenAllTools, NormalizeToolIdentifier))
        {
            return false;
        }

        return true;
    }

    private static bool EvaluateLegacySkillActivation(
        string skillKey,
        IReadOnlySet<string> requestIntents,
        IReadOnlySet<string> availableToolNames)
    {
        var hasLaunchTools = HasAnyNamedTools(
            availableToolNames,
            "list_windows",
            "select_window",
            "list_taskbar_elements",
            "select_taskbar_app",
            "launch_app_via_taskbar_search");

        return skillKey switch
        {
            "ui-refresh-and-evidence" => HasAnyNamedTools(
                availableToolNames,
                "describe_selected_window",
                "describe_selected_window_focus",
                "capture_selected_window_screenshot"),
            "desktop-launch-and-first-look"
                => (requestIntents.Contains("launch_request") || requestIntents.Contains("direct_browser_navigation_request"))
                   && hasLaunchTools,
            "browser-navigation-and-web-operations"
                => requestIntents.Contains("browser_request")
                   && HasAnyNamedTools(
                       availableToolNames,
                       "describe_selected_window",
                       "describe_selected_window_focus",
                       "invoke_selected_window_element",
                       "click_selected_window_element",
                       "focus_selected_window_element",
                       "send_input_to_window",
                       "capture_selected_window_screenshot"),
            "search-and-enumeration"
                => requestIntents.Contains("search_or_enumeration_request")
                   && HasAnyNamedTools(
                       availableToolNames,
                       "describe_selected_window",
                       "describe_selected_window_focus",
                       "capture_selected_window_screenshot"),
            "action-discovery-and-invocation"
                => !requestIntents.Contains("browser_request")
                   && requestIntents.Contains("action_request")
                   && HasAnyNamedTools(
                       availableToolNames,
                       "list_main_menu_items",
                       "list_context_menu_items",
                       "invoke_main_menu_item",
                       "invoke_context_menu_item",
                       "invoke_selected_window_element",
                       "click_selected_window_element",
                       "focus_selected_window_element",
                       "send_input_to_window"),
            _ => false
        };
    }

    private static ComposedAgentPrompt BuildFallbackPrompt(AgentPromptCatalog catalog)
        => new(
            catalog.FallbackDefinition,
            $"fallback:{Path.GetFileName(catalog.FallbackDefinitionPath)}",
            UsesFallbackDefinition: true,
            []);

    private static IReadOnlySet<string> BuildRequestIntentSet(string userText)
    {
        var normalizedText = NormalizeText(userText);
        var requestIntents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchesBrowserRequest = MatchesBrowserRequest(userText, normalizedText);

        if (MatchesLaunchRequest(normalizedText))
        {
            requestIntents.Add("launch_request");
        }

        if (matchesBrowserRequest)
        {
            requestIntents.Add("browser_request");
        }

        if (MatchesDirectBrowserNavigationRequest(userText, normalizedText))
        {
            requestIntents.Add("direct_browser_navigation_request");
        }

        if (MatchesSearchOrEnumerationRequest(normalizedText))
        {
            requestIntents.Add("search_or_enumeration_request");
        }

        if (MatchesActionRequest(normalizedText))
        {
            requestIntents.Add("action_request");
        }

        return requestIntents;
    }

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

    private static bool MatchesDirectBrowserNavigationRequest(string rawText, string normalizedText)
    {
        if (!MatchesBrowserRequest(rawText, normalizedText))
        {
            return false;
        }

        return ContainsAny(
                   normalizedText,
                   "go to",
                   "visit",
                   "website",
                   "web site",
                   "url",
                   "address bar",
                   "webpage",
                   "web page",
                   "open site",
                   "open website",
                   "open url")
               || Regex.IsMatch(rawText, @"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)
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

    private static string NormalizeIntentIdentifier(string intent)
        => string.IsNullOrWhiteSpace(intent)
            ? string.Empty
            : intent.Trim().ToLowerInvariant();

    private static string NormalizeToolIdentifier(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return string.Empty;
        }

        var normalized = toolName.Trim();
        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex >= 0 && separatorIndex < normalized.Length - 1
            ? normalized[(separatorIndex + 1)..]
            : normalized;
    }

    private static bool HasAnyNamedTools(IReadOnlySet<string> availableToolNames, params string[] candidateNames)
        => MatchesAny(availableToolNames, candidateNames, NormalizeToolIdentifier);

    private static bool MatchesAny(
        IReadOnlySet<string> availableValues,
        IEnumerable<string> candidates,
        Func<string, string> normalize)
        => candidates
            .Select(normalize)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Any(availableValues.Contains);

    private static bool MatchesAll(
        IReadOnlySet<string> availableValues,
        IEnumerable<string> candidates,
        Func<string, string> normalize)
        => candidates
            .Select(normalize)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .All(availableValues.Contains);

    private static bool ContainsAny(string text, params string[] candidates)
        => candidates.Any(candidate => text.Contains(candidate, StringComparison.Ordinal));
}
