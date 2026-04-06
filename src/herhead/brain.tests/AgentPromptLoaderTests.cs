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
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var surfacePrompt = prompts.Single(prompt => prompt.Key == "netflix-surface-and-state");
        var profilePrompt = prompts.Single(prompt => prompt.Key == "netflix-profile-and-pin");
        var browsePrompt = prompts.Single(prompt => prompt.Key == "netflix-browse-and-play");
        var playbackPrompt = prompts.Single(prompt => prompt.Key == "netflix-playback-controls");

        Assert.Contains("layered surface", surfacePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title-detail state", surfacePrompt.PromptText, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Profile Lock And PIN Rules", profilePrompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("one digit at a time", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not send the full PIN as one", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obvious ASR variants", profilePrompt.PromptText, StringComparison.OrdinalIgnoreCase);

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
        var corePromptPath = Path.Combine(FindRepoRoot(), ".github", "agents", "her.agent.core.md");
        var corePrompt = File.ReadAllText(corePromptPath);

        Assert.Contains("split by independently activatable UI surface and distinct decision logic", corePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not fragment one app into many tiny skills", corePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryBrowserSkill_IncludesOfficialInstructionLookupGuidance()
    {
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var prompt = prompts.Single(prompt => prompt.Key == "browser-navigation-and-web-operations");
        Assert.Contains("instruction lookup", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("official help, support, or documentation pages", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("support.microsoft.com", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("help.netflix.com", prompt.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryGenericAppSkill_IncludesCloseAndWindowTargetingGuidance()
    {
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var prompt = prompts.Single(prompt => prompt.Key == "generic-app-policy");
        Assert.Equal("generic-app", prompt.Metadata.Group);
        Assert.Contains("stable target identifier such as `windowHandle`", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("prefer closing the currently selected window", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alt+F4", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("official help, support, or documentation pages", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
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
