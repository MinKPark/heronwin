using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class AppSkillGenerationCoordinatorTests
{
    [Fact]
    public void TryBuildUnknownAppSkillOffer_ReturnsTrue_ForUnknownAppLaunch()
    {
        var catalog = CreateCatalog(
            CreateSkill("desktop-launch-and-first-look", "windows"),
            CreateSkill("ui-refresh-and-evidence", "any-app"),
            CreateSkill("netflix-surface-and-state", "netflix", whenAnyKeywords: ["netflix"]));

        var actual = AppSkillGenerationCoordinator.TryBuildUnknownAppSkillOffer(
            "Open Spotify.",
            [],
            catalog,
            out var reply);

        Assert.True(actual);
        Assert.Contains("Spotify", reply.SpokenText, StringComparison.Ordinal);
        Assert.Contains("skill group", reply.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.True(AppSkillGenerationCoordinator.TryGetPendingOffer(
            [new AgentMessage.Assistant(reply.RawText)],
            out var offer));
        Assert.Equal("Spotify", offer.AppName);
        Assert.Equal("spotify", offer.Group);
    }

    [Fact]
    public void TryBuildUnknownAppSkillOffer_ReturnsFalse_ForKnownAppGroup()
    {
        var catalog = CreateCatalog(
            CreateSkill("browser-navigation-and-web-operations", "edge"),
            CreateSkill("netflix-surface-and-state", "netflix", whenAnyKeywords: ["netflix"]));

        var edgeActual = AppSkillGenerationCoordinator.TryBuildUnknownAppSkillOffer(
            "Open Microsoft Edge.",
            [],
            catalog,
            out _);
        var netflixActual = AppSkillGenerationCoordinator.TryBuildUnknownAppSkillOffer(
            "Start Netflix.",
            [],
            catalog,
            out _);

        Assert.False(edgeActual);
        Assert.False(netflixActual);
    }

    [Fact]
    public void TryBuildApprovedGenerationRequest_UsesPendingOfferAndAffirmativeReply()
    {
        const string offerJson = """
        {
          "say": "I can generate it first.",
          "log": "Offer pending.",
          "skill_offer": {
            "app_name": "Spotify",
            "group": "spotify"
          }
        }
        """;

        var actual = AppSkillGenerationCoordinator.TryBuildApprovedGenerationRequest(
            [new AgentMessage.Assistant(offerJson)],
            "yes, generate it first",
            out var offer,
            out var generationUserText);

        Assert.True(actual);
        Assert.Equal("Spotify", offer.AppName);
        Assert.Equal("spotify", offer.Group);
        Assert.Contains("official website", generationUserText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Spotify", generationUserText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryPersistGeneratedSkillGroup_WritesFilesAndRefreshesCatalog()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"brain-skill-gen-{Guid.NewGuid():N}");
        var agentsDirectory = Path.Combine(tempRoot, ".github", "agents");
        var skillsDirectory = Path.Combine(agentsDirectory, "skills");
        Directory.CreateDirectory(skillsDirectory);

        try
        {
            File.WriteAllText(Path.Combine(agentsDirectory, "her.agent.md"), "fallback prompt");
            File.WriteAllText(Path.Combine(agentsDirectory, "her.agent.core.md"), "core prompt");

            var catalog = AgentPromptLoader.LoadFromResolvedPaths(
                Path.Combine(agentsDirectory, "her.agent.md"),
                Path.Combine(agentsDirectory, "her.agent.core.md"));

            var rawReply = """
            {
              "say": "I drafted the Spotify skill group.",
              "log": "Using Spotify's official getting started docs, I drafted the new Spotify skill group.",
              "skill_generation": {
                "app_name": "Spotify",
                "group": "spotify",
                "source_url": "https://support.spotify.com/us/article/getting-started/",
                "files": [
                  {
                    "file_name": "spotify-surface-and-state.skill.md",
                    "content": "---\nid: spotify-surface-and-state\ngroup: spotify\npriority: 350\nsummary: \"Shared Spotify surface model and first-state verification rules.\"\nactivation:\n  when_any_keywords:\n    - spotify\n---\n\n# Skill: Spotify Surface And State\n\n- Verify the first visible Spotify surface before deeper actions."
                  },
                  {
                    "file_name": "spotify-browse-and-play.skill.md",
                    "content": "---\nid: spotify-browse-and-play\ngroup: spotify\npriority: 360\nsummary: \"Handle Spotify browse, search, and playback start flows.\"\nactivation:\n  when_any_keywords:\n    - spotify\n    - playlist\n    - album\n---\n\n# Skill: Spotify Browse And Play\n\n- Prefer exact visible Spotify titles and playlists over generic containers."
                  }
                ]
              }
            }
            """;

            var actual = AppSkillGenerationCoordinator.TryPersistGeneratedSkillGroup(
                rawReply,
                new PendingAppSkillOffer("Spotify", "spotify"),
                catalog,
                out var refreshedCatalog,
                out var summary);

            Assert.True(actual);
            Assert.Contains("spotify", summary, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(skillsDirectory, "spotify", "spotify-surface-and-state.skill.md")));
            Assert.True(File.Exists(Path.Combine(skillsDirectory, "spotify", "spotify-browse-and-play.skill.md")));
            Assert.Contains(refreshedCatalog.Skills, skill => skill.Key == "spotify-surface-and-state");
            Assert.Contains(refreshedCatalog.Skills, skill => skill.Key == "spotify-browse-and-play");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static AgentPromptCatalog CreateCatalog(params AgentSkillPrompt[] skills)
        => new(
            FallbackDefinitionPath: "fallback/her.agent.md",
            FallbackDefinition: "fallback prompt",
            CoreDefinitionPath: "core/her.agent.core.md",
            CoreDefinition: "core prompt",
            Skills: skills);

    private static AgentSkillPrompt CreateSkill(
        string key,
        string group,
        IReadOnlyList<string>? whenAnyKeywords = null)
    {
        var activation = new AgentSkillActivation(
            WhenAnyIntents: [],
            WhenAllIntents: [],
            UnlessAnyIntents: [],
            WhenAnyTools: [],
            WhenAllTools: [],
            WhenAnyKeywords: whenAnyKeywords ?? [],
            WhenAllKeywords: [],
            UnlessAnyKeywords: []);
        return new AgentSkillPrompt(
            key,
            $"skills/{group}/{key}.skill.md",
            $"# Skill\n{key}",
            new AgentSkillMetadata(key, null, [], [], group, 100, activation, []));
    }
}
