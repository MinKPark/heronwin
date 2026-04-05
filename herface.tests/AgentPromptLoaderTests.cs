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
}
