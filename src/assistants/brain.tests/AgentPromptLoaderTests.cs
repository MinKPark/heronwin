using System.Text.RegularExpressions;
using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class AgentPromptLoaderTests
{
    [Fact]
    public void LoadSkillPrompts_RecursesNestedDirectories_AndUsesFolderGroupByDefault()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"brain-skill-loader-{Guid.NewGuid():N}");
        var skillsDirectory = Path.Combine(tempRoot, "skills");
        Directory.CreateDirectory(Path.Combine(skillsDirectory, "windows"));
        Directory.CreateDirectory(Path.Combine(skillsDirectory, "any-app"));

        try
        {
            File.WriteAllText(
                Path.Combine(skillsDirectory, "windows", "desktop-launch-and-first-look.skill.md"),
                """
                ---
                id: desktop-launch-and-first-look
                ---
                # Skill
                body
                """);
            File.WriteAllText(
                Path.Combine(skillsDirectory, "any-app", "ui-refresh-and-evidence.skill.md"),
                """
                ---
                id: ui-refresh-and-evidence
                ---
                # Skill
                body
                """);

            var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

            Assert.Equal(2, prompts.Count);
            Assert.Equal("windows", prompts.Single(prompt => prompt.Key == "desktop-launch-and-first-look").Metadata.Group);
            Assert.Equal("any-app", prompts.Single(prompt => prompt.Key == "ui-refresh-and-evidence").Metadata.Group);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadSkillPrompts_PrefersExplicitGroupMetadata_OverFolderName()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"brain-skill-loader-{Guid.NewGuid():N}");
        var skillsDirectory = Path.Combine(tempRoot, "skills");
        Directory.CreateDirectory(Path.Combine(skillsDirectory, "netflix"));

        try
        {
            File.WriteAllText(
                Path.Combine(skillsDirectory, "netflix", "netflix-profile-selection-and-playback.skill.md"),
                """
                ---
                id: netflix-profile-selection-and-playback
                group: streaming
                priority: 400
                ---
                # Skill
                body
                """);

            var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

            var prompt = Assert.Single(prompts);
            Assert.Equal("streaming", prompt.Metadata.Group);
            Assert.Equal(400, prompt.Metadata.Priority);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RepositoryNetflixSkills_AreSplitBySurfaceAndRetainKeyGuidance()
    {
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "shared", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var surfacePrompt = prompts.Single(prompt => prompt.Key == "netflix-surface-and-state");
        var profilePrompt = prompts.Single(prompt => prompt.Key == "netflix-profile-and-pin");
        var searchPrompt = prompts.Single(prompt => prompt.Key == "netflix-search");
        var browsePrompt = prompts.Single(prompt => prompt.Key == "netflix-browse-and-play");
        var playbackPrompt = prompts.Single(prompt => prompt.Key == "netflix-playback-controls");

        Assert.Contains("layered surface", surfacePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title-detail state", surfacePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("website_fallback", surfacePrompt.Metadata.Affordances, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Profile Lock And PIN Rules", profilePrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("one digit at a time", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("four separate single-character actions", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same tool-call response", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one LLM attempt", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not send the full PIN as one", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verify after the final digit", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obvious ASR variants", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Stable Search Entry Batch", searchPrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("invoke_window_element", searchPrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("type_window_text", searchPrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("set_window_element_text", searchPrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("same tool-call response", searchPrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only opens the Search control but omits the known query is incomplete", searchPrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not return only `invoke_window_element`", searchPrompt.PromptText, StringComparison.Ordinal);

        Assert.Contains("Shows`, `Movies`, `Games`", browsePrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("Back to Browse", browsePrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("Do not claim that Netflix started playback", browsePrompt.PromptText, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("turn off subtitles", playbackPrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reveal the playback controls", playbackPrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Minimize`, `Restore`, and `Close`", playbackPrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("do not click `Audio & Subtitles` again", playbackPrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target the exact visible `Off` option", playbackPrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("English (CC)", playbackPrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("Do not say `I'm turning them off`", playbackPrompt.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryCoreAgent_IncludesSkillSplitGuardrail()
    {
        var corePromptPath = Path.Combine(FindRepoRoot(), ".github", "agents", "shared", "heronwin.core.md");
        var corePrompt = File.ReadAllText(corePromptPath);

        Assert.Contains("split by independently activatable UI surface and distinct decision logic", corePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not fragment one app into many tiny skills", corePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryBrowserSkill_IncludesOfficialInstructionLookupGuidance()
    {
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "shared", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var prompt = prompts.Single(prompt => prompt.Key == "browser-navigation-and-web-operations");
        Assert.Contains("instruction lookup", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("official help, support, or documentation pages", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("support.microsoft.com", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("vendor's own help center domain", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("help.netflix.com", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryBrowserSkill_IncludesAddressBarUrlSubmissionBatchingGuidance()
    {
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "shared", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var prompt = prompts.Single(prompt => prompt.Key == "browser-navigation-and-web-operations");
        Assert.Contains("setting the address-bar value is not complete until the URL is submitted", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same tool-call response", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set_window_element_text", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("press_window_key", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("separate LLM attempt", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit exception to the default one-tool-at-a-time preference", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verify the destination after the submitted navigation", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only sets the address bar but omits `Enter` is incomplete", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_AvaProfile_UsesAvaPromptAndAccessibilitySkill()
    {
        var repoRoot = FindRepoRoot();
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalAgentOverride = Environment.GetEnvironmentVariable("AGENT_DEFINITION_PATH");
        var originalAvaOverride = Environment.GetEnvironmentVariable("AVA_AGENT_DEFINITION_PATH");

        try
        {
            Environment.SetEnvironmentVariable("AGENT_DEFINITION_PATH", null);
            Environment.SetEnvironmentVariable("AVA_AGENT_DEFINITION_PATH", null);
            Directory.SetCurrentDirectory(repoRoot);

            var catalog = AgentPromptLoader.Load("ava");

            Assert.EndsWith(Path.Combine(".github", "agents", "ava", "ava.agent.md"), catalog.FallbackDefinitionPath);
            Assert.Contains("accessibility validation assistant", catalog.FallbackDefinition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AVA Accessibility Validation Policy", catalog.CoreDefinition, StringComparison.Ordinal);
            Assert.Contains(catalog.Skills, skill => skill.Key == "accessibility-validation-policy");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Environment.SetEnvironmentVariable("AGENT_DEFINITION_PATH", originalAgentOverride);
            Environment.SetEnvironmentVariable("AVA_AGENT_DEFINITION_PATH", originalAvaOverride);
        }
    }

    [Fact]
    public void RepositoryDesktopLaunchSkill_IncludesHandleActivationAndContinueGuidance()
    {
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "shared", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var prompt = prompts.Single(prompt => prompt.Key == "desktop-launch-and-first-look");
        Assert.Contains("with that exact `windowHandle`", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("titleContains", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("continue into that next action", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh startup inventory", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not call `cognition/list_windows` again", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryGenericAppSkill_IncludesCloseAndWindowTargetingGuidance()
    {
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "shared", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var prompt = prompts.Single(prompt => prompt.Key == "generic-app-policy");
        Assert.Equal("generic-app", prompt.Metadata.Group);
        Assert.Contains("stable target identifier such as `windowHandle`", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("prefer closing the currently selected window", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alt+F4", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("official help, support, or documentation pages", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryCoreAndCrossAppPrompts_AvoidAppSpecificWorkflowNames()
    {
        var repoRoot = FindRepoRoot();
        var restrictedPaths = new List<string>
        {
            Path.Combine(repoRoot, ".github", "agents", "shared", "heronwin.core.md"),
            Path.Combine(repoRoot, ".github", "agents", "cursor", "cursor.agent.core.md"),
            Path.Combine(repoRoot, ".github", "agents", "tars", "tars.agent.core.md")
        };

        restrictedPaths.AddRange(Directory.GetFiles(
            Path.Combine(repoRoot, ".github", "agents", "shared", "skills", "any-app"),
            "*.skill.md",
            SearchOption.AllDirectories));
        restrictedPaths.AddRange(Directory.GetFiles(
            Path.Combine(repoRoot, ".github", "agents", "shared", "skills", "generic-app"),
            "*.skill.md",
            SearchOption.AllDirectories));
        restrictedPaths.AddRange(Directory.GetFiles(
            Path.Combine(repoRoot, ".github", "agents", "shared", "skills", "edge"),
            "*.skill.md",
            SearchOption.AllDirectories));

        var forbiddenTerms = new[]
        {
            "netflix",
            "spotify",
            "youtube",
            "outlook",
            "hulu",
            "prime video",
            "discord",
            "gmail",
            "chatgpt",
            "claude",
            "reddit",
            "slack",
            "teams",
            "tiktok",
            "linkedin",
            "instagram",
            "facebook"
        };

        foreach (var path in restrictedPaths)
        {
            var content = File.ReadAllText(path);
            foreach (var term in forbiddenTerms)
            {
                var pattern = $@"\b{Regex.Escape(term)}\b";
                Assert.False(
                    Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    $"Restricted prompt file '{path}' contains app-specific term '{term}'.");
            }
        }
    }

    [Fact]
    public void RepositoryRestrictedRuntimeFiles_AvoidAppSpecificWorkflowNames()
    {
        var repoRoot = FindRepoRoot();
        var restrictedPaths = new[]
        {
            Path.Combine(repoRoot, "src", "assistants", "brain", "Conversation.cs"),
            Path.Combine(repoRoot, "src", "assistants", "brain", "AgentPrompts.cs"),
            Path.Combine(repoRoot, "src", "assistants", "brain", "ConsoleMode.cs"),
        };

        var forbiddenTerms = new[]
        {
            "netflix",
            "spotify",
            "youtube",
            "outlook",
            "hulu",
            "prime video",
            "discord",
            "gmail",
            "reddit",
            "slack",
            "teams",
            "tiktok",
            "linkedin",
            "instagram",
            "facebook"
        };

        foreach (var path in restrictedPaths)
        {
            var content = File.ReadAllText(path);
            foreach (var term in forbiddenTerms)
            {
                var pattern = $@"\b{Regex.Escape(term)}\b";
                Assert.False(
                    Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    $"Restricted runtime file '{path}' contains app-specific term '{term}'.");
            }
        }
    }

    [Fact]
    public void RepositoryCoreAndCrossAppBoundaries_AvoidLegacyProfileSurfaceWorkflowTerms()
    {
        var repoRoot = FindRepoRoot();
        var restrictedPaths = new List<string>
        {
            Path.Combine(repoRoot, "src", "assistants", "brain", "Conversation.cs"),
            Path.Combine(repoRoot, ".github", "agents", "shared", "heronwin.core.md"),
            Path.Combine(repoRoot, ".github", "agents", "cursor", "cursor.agent.core.md"),
            Path.Combine(repoRoot, ".github", "agents", "tars", "tars.agent.core.md")
        };

        restrictedPaths.AddRange(Directory.GetFiles(
            Path.Combine(repoRoot, ".github", "agents", "shared", "skills", "any-app"),
            "*.skill.md",
            SearchOption.AllDirectories));
        restrictedPaths.AddRange(Directory.GetFiles(
            Path.Combine(repoRoot, ".github", "agents", "shared", "skills", "generic-app"),
            "*.skill.md",
            SearchOption.AllDirectories));
        restrictedPaths.AddRange(Directory.GetFiles(
            Path.Combine(repoRoot, ".github", "agents", "shared", "skills", "edge"),
            "*.skill.md",
            SearchOption.AllDirectories));

        var forbiddenTerms = new[]
        {
            "profile picker",
            "manage profiles",
            "add profile",
            "profile lock",
        };

        foreach (var path in restrictedPaths)
        {
            var content = File.ReadAllText(path);
            foreach (var term in forbiddenTerms)
            {
                var pattern = Regex.Escape(term);
                Assert.False(
                    Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    $"Restricted boundary file '{path}' contains legacy profile-surface term '{term}'.");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                ".github",
                "agents",
                "shared",
                "skills",
                "netflix",
                "netflix-surface-and-state.skill.md");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test base directory.");
    }
}
