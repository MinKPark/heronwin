using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class AgentPromptLoaderTests
{
    [Fact]
    public void LoadSkillPrompts_RecursesNestedDirectories_AndUsesFolderGroupByDefault()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"herface-skill-loader-{Guid.NewGuid():N}");
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
        var tempRoot = Path.Combine(Path.GetTempPath(), $"herface-skill-loader-{Guid.NewGuid():N}");
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
    public void RepositoryNetflixSkill_IncludesPinHomeNavigationAndSubtitleGuidance()
    {
        var skillsDirectory = Path.Combine(FindRepoRoot(), ".github", "agents", "skills");

        var prompts = AgentPromptLoader.LoadSkillPrompts(skillsDirectory);

        var prompt = prompts.Single(prompt => prompt.Key == "netflix-profile-selection-and-playback");
        Assert.Contains("Profile Lock And PIN Rules", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("one digit at a time", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not send `3579` as one", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obvious ASR variants", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Shows`, `Movies`, `Games`", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("Back to Browse", prompt.PromptText, StringComparison.Ordinal);
        Assert.Contains("turn off subtitles", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reveal the playback controls", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not say `I'm turning them off`", prompt.PromptText, StringComparison.Ordinal);
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
                "netflix-profile-selection-and-playback.skill.md");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test base directory.");
    }
}
