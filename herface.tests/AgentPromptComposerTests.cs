using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class AgentPromptComposerTests
{
    [Fact]
    public void Compose_UsesFallbackDefinition_WhenSplitPromptsAreUnavailable()
    {
        var catalog = new AgentPromptCatalog(
            FallbackDefinitionPath: "fallback/her.agent.md",
            FallbackDefinition: "fallback prompt",
            CoreDefinitionPath: null,
            CoreDefinition: string.Empty,
            Skills: []);

        var actual = AgentPromptComposer.Compose(catalog, "open spotify", []);

        Assert.True(actual.UsesFallbackDefinition);
        Assert.Equal("fallback prompt", actual.SystemPrompt);
        Assert.Empty(actual.ActiveSkills);
    }

    [Fact]
    public void Compose_ActivatesLaunchAndRefreshSkills_ForAppLaunchRequests()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Open Spotify.",
            [
                new ToolDefinition("list_windows", "desc", default),
                new ToolDefinition("select_window", "desc", default),
                new ToolDefinition("launch_app_via_taskbar_search", "desc", default),
                new ToolDefinition("describe_selected_window", "desc", default)
            ]);

        Assert.False(actual.UsesFallbackDefinition);
        Assert.Contains("core prompt", actual.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("launch skill", actual.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("refresh skill", actual.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(
            ["desktop-launch-and-first-look", "ui-refresh-and-evidence"],
            actual.ActiveSkills.Select(skill => skill.Key));
    }

    [Fact]
    public void Compose_PrefersActionSkillWithoutLaunchSkill_ForMenuRequests()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Open the File menu.",
            [
                new ToolDefinition("list_main_menu_items", "desc", default),
                new ToolDefinition("invoke_main_menu_item", "desc", default),
                new ToolDefinition("describe_selected_window", "desc", default)
            ]);

        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "desktop-launch-and-first-look");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "action-discovery-and-invocation");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "ui-refresh-and-evidence");
    }

    [Fact]
    public void Compose_ActivatesBrowserSkill_ForWebsiteNavigationRequests()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Can you go to the Netflix website?",
            [
                new ToolDefinition("describe_selected_window", "desc", default),
                new ToolDefinition("invoke_selected_window_element", "desc", default),
                new ToolDefinition("send_input_to_window", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "browser-navigation-and-web-operations");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "ui-refresh-and-evidence");
        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "desktop-launch-and-first-look");
        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "action-discovery-and-invocation");
    }

    [Fact]
    public void Compose_ActivatesLaunchSkill_ForWebsiteOpenRequest_WhenLaunchToolsExist()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Open the Netflix website in Edge.",
            [
                new ToolDefinition("list_windows", "desc", default),
                new ToolDefinition("select_window", "desc", default),
                new ToolDefinition("launch_app_via_taskbar_search", "desc", default),
                new ToolDefinition("describe_selected_window", "desc", default),
                new ToolDefinition("send_input_to_window", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "browser-navigation-and-web-operations");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "desktop-launch-and-first-look");
        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "action-discovery-and-invocation");
    }

    private static AgentPromptCatalog CreateCatalog()
        => new(
            FallbackDefinitionPath: "fallback/her.agent.md",
            FallbackDefinition: "fallback prompt",
            CoreDefinitionPath: "core/her.agent.core.md",
            CoreDefinition: "core prompt",
            Skills:
            [
                new AgentSkillPrompt("action-discovery-and-invocation", "skills/action.skill.md", "action skill"),
                new AgentSkillPrompt("browser-navigation-and-web-operations", "skills/browser.skill.md", "browser skill"),
                new AgentSkillPrompt("desktop-launch-and-first-look", "skills/launch.skill.md", "launch skill"),
                new AgentSkillPrompt("search-and-enumeration", "skills/search.skill.md", "search skill"),
                new AgentSkillPrompt("ui-refresh-and-evidence", "skills/refresh.skill.md", "refresh skill")
            ]);
}
