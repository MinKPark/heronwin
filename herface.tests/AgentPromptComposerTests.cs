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
    public void Compose_ActivatesBrowserSkill_WhenClickToolCanDriveWebControls()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Go to the Netflix website.",
            [
                new ToolDefinition("describe_selected_window", "desc", default),
                new ToolDefinition("click_selected_window_element", "desc", default),
                new ToolDefinition("capture_selected_window_screenshot", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "browser-navigation-and-web-operations");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "ui-refresh-and-evidence");
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

    [Fact]
    public void Compose_ActivatesSearchSkill_ForInAppSearchRequests()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Search for Boyfriend on Demand within Netflix using the visible Search control.",
            [
                new ToolDefinition("describe_selected_window", "desc", default),
                new ToolDefinition("set_selected_window_element_value", "desc", default),
                new ToolDefinition("send_input_to_window", "desc", default),
                new ToolDefinition("capture_selected_window_screenshot", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "search-and-enumeration");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "ui-refresh-and-evidence");
        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "browser-navigation-and-web-operations");
    }

    [Fact]
    public void Compose_ActivatesBrowserAndSearchSkills_ForWebsiteSearchRequests()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Search for Boyfriend on Demand within the Netflix website.",
            [
                new ToolDefinition("describe_selected_window", "desc", default),
                new ToolDefinition("invoke_selected_window_element", "desc", default),
                new ToolDefinition("set_selected_window_element_value", "desc", default),
                new ToolDefinition("send_input_to_window", "desc", default),
                new ToolDefinition("capture_selected_window_screenshot", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "browser-navigation-and-web-operations");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "search-and-enumeration");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "ui-refresh-and-evidence");
    }

    [Fact]
    public void Compose_ActivatesActionSkill_WhenClickToolCanActivateVisibleControl()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Click the visible Boyfriend on Demand result.",
            [
                new ToolDefinition("click_selected_window_element", "desc", default),
                new ToolDefinition("describe_selected_window", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "action-discovery-and-invocation");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "ui-refresh-and-evidence");
        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "browser-navigation-and-web-operations");
    }

    [Fact]
    public void Compose_UsesLegacyCompatibility_WhenSkillLacksStructuredActivationMetadata()
    {
        var catalog = new AgentPromptCatalog(
            FallbackDefinitionPath: "fallback/her.agent.md",
            FallbackDefinition: "fallback prompt",
            CoreDefinitionPath: "core/her.agent.core.md",
            CoreDefinition: "core prompt",
            Skills:
            [
                CreateSkill("desktop-launch-and-first-look", "launch skill", EmptyActivation),
                CreateSkill("ui-refresh-and-evidence", "refresh skill", EmptyActivation)
            ]);

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Open Spotify.",
            [
                new ToolDefinition("list_windows", "desc", default),
                new ToolDefinition("select_window", "desc", default),
                new ToolDefinition("describe_selected_window", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "desktop-launch-and-first-look");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "ui-refresh-and-evidence");
    }

    private static AgentPromptCatalog CreateCatalog()
        => new(
            FallbackDefinitionPath: "fallback/her.agent.md",
            FallbackDefinition: "fallback prompt",
            CoreDefinitionPath: "core/her.agent.core.md",
            CoreDefinition: "core prompt",
            Skills:
            [
                CreateSkill(
                    "action-discovery-and-invocation",
                    "action skill",
                    Activation(
                        whenAnyIntents: ["action_request"],
                        unlessAnyIntents: ["browser_request"],
                        whenAnyTools:
                        [
                            "list_main_menu_items",
                            "list_context_menu_items",
                            "invoke_main_menu_item",
                            "invoke_context_menu_item",
                            "invoke_selected_window_element",
                            "click_selected_window_element",
                            "focus_selected_window_element",
                            "send_input_to_window"
                        ])),
                CreateSkill(
                    "browser-navigation-and-web-operations",
                    "browser skill",
                    Activation(
                        whenAnyIntents: ["browser_request"],
                        whenAnyTools:
                        [
                            "describe_selected_window",
                            "describe_selected_window_focus",
                            "invoke_selected_window_element",
                            "click_selected_window_element",
                            "focus_selected_window_element",
                            "set_selected_window_element_value",
                            "send_input_to_window",
                            "capture_selected_window_screenshot"
                        ])),
                CreateSkill(
                    "desktop-launch-and-first-look",
                    "launch skill",
                    Activation(
                        whenAnyIntents: ["launch_request", "direct_browser_navigation_request"],
                        whenAnyTools:
                        [
                            "list_windows",
                            "select_window",
                            "list_taskbar_elements",
                            "select_taskbar_app",
                            "launch_app_via_taskbar_search"
                        ])),
                CreateSkill(
                    "search-and-enumeration",
                    "search skill",
                    Activation(
                        whenAnyIntents: ["search_or_enumeration_request"],
                        whenAnyTools:
                        [
                            "describe_selected_window",
                            "describe_selected_window_focus",
                            "capture_selected_window_screenshot"
                        ])),
                CreateSkill(
                    "ui-refresh-and-evidence",
                    "refresh skill",
                    Activation(
                        whenAnyTools:
                        [
                            "describe_selected_window",
                            "describe_selected_window_focus",
                            "capture_selected_window_screenshot"
                        ]))
            ]);

    private static AgentSkillPrompt CreateSkill(
        string key,
        string promptText,
        AgentSkillActivation activation)
        => new(
            key,
            $"skills/{key}.skill.md",
            promptText,
            new AgentSkillMetadata(
                key,
                Summary: null,
                PreferredTools: [],
                AppliesWhen: [],
                Activation: activation));

    private static AgentSkillActivation Activation(
        string[]? whenAnyIntents = null,
        string[]? whenAllIntents = null,
        string[]? unlessAnyIntents = null,
        string[]? whenAnyTools = null,
        string[]? whenAllTools = null)
        => new(
            whenAnyIntents ?? Array.Empty<string>(),
            whenAllIntents ?? Array.Empty<string>(),
            unlessAnyIntents ?? Array.Empty<string>(),
            whenAnyTools ?? Array.Empty<string>(),
            whenAllTools ?? Array.Empty<string>());

    private static AgentSkillActivation EmptyActivation
        => Activation();
}
