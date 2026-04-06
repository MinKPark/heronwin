using Xunit;

namespace HeronWin.Brain.Tests;

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
            ["desktop-launch-and-first-look", "generic-app-policy", "ui-refresh-and-evidence"],
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
    public void Compose_ActivatesGenericAppPolicySkill_FromToolAvailability()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Close this window.",
            [
                new ToolDefinition("select_window", "desc", default),
                new ToolDefinition("describe_selected_window", "desc", default),
                new ToolDefinition("send_input_to_window", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "generic-app-policy");
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
    public void Compose_ActivatesBrowserSkill_ForInstructionLookupRequests()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "How do I turn off subtitles in Hulu? Look up the official instructions.",
            [
                new ToolDefinition("describe_selected_window", "desc", default),
                new ToolDefinition("send_input_to_window", "desc", default),
                new ToolDefinition("capture_selected_window_screenshot", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "browser-navigation-and-web-operations");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "ui-refresh-and-evidence");
        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "action-discovery-and-invocation");
    }

    [Fact]
    public void Compose_DoesNotActivateBrowserSkill_ForGenericHowDoIAppActionRequests()
    {
        var catalog = CreateCatalog();

        var actual = AgentPromptComposer.Compose(
            catalog,
            "How do I click the Save button?",
            [
                new ToolDefinition("click_selected_window_element", "desc", default),
                new ToolDefinition("describe_selected_window", "desc", default)
            ]);

        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "browser-navigation-and-web-operations");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "action-discovery-and-invocation");
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

    [Fact]
    public void Compose_GroupsActiveSkillsByGroupAndPriority()
    {
        var catalog = new AgentPromptCatalog(
            FallbackDefinitionPath: "fallback/her.agent.md",
            FallbackDefinition: "fallback prompt",
            CoreDefinitionPath: "core/her.agent.core.md",
            CoreDefinition: "core prompt",
            Skills:
            [
                CreateSkill(
                    "desktop-launch-and-first-look",
                    "launch skill",
                    Activation(whenAnyIntents: ["launch_request"], whenAnyTools: ["list_windows"]),
                    group: "windows",
                    priority: 100),
                CreateSkill(
                    "ui-refresh-and-evidence",
                    "refresh skill",
                    Activation(whenAnyTools: ["describe_selected_window"]),
                    group: "any-app",
                    priority: 150),
                CreateSkill(
                    "netflix-surface-and-state",
                    "netflix core skill",
                    Activation(whenAnyKeywords: ["netflix"], whenAnyTools: ["describe_selected_window"]),
                    group: "netflix",
                    priority: 350),
                CreateSkill(
                    "netflix-profile-and-pin",
                    "netflix profile skill",
                    Activation(
                        whenAllKeywords: ["netflix"],
                        whenAnyKeywords: ["profile", "profiles"],
                        whenAnyTools: ["describe_selected_window"]),
                    group: "netflix",
                    priority: 400)
            ]);

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Open Netflix profiles.",
            [],
            [
                new ToolDefinition("list_windows", "desc", default),
                new ToolDefinition("describe_selected_window", "desc", default)
            ]);

        Assert.Contains("### Windows Skill Group", actual.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("### Any App Skill Group", actual.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("### Netflix Skill Group", actual.SystemPrompt, StringComparison.Ordinal);
        Assert.True(
            actual.SystemPrompt.IndexOf("### Windows Skill Group", StringComparison.Ordinal)
            < actual.SystemPrompt.IndexOf("### Any App Skill Group", StringComparison.Ordinal));
        Assert.True(
            actual.SystemPrompt.IndexOf("### Any App Skill Group", StringComparison.Ordinal)
            < actual.SystemPrompt.IndexOf("### Netflix Skill Group", StringComparison.Ordinal));
        Assert.Equal(
            [
                "desktop-launch-and-first-look",
                "ui-refresh-and-evidence",
                "netflix-surface-and-state",
                "netflix-profile-and-pin"
            ],
            actual.ActiveSkills.Select(skill => skill.Key));
    }

    [Fact]
    public void Compose_ActivatesKeywordSkill_FromRecentHistoryContext()
    {
        var catalog = new AgentPromptCatalog(
            FallbackDefinitionPath: "fallback/her.agent.md",
            FallbackDefinition: "fallback prompt",
            CoreDefinitionPath: "core/her.agent.core.md",
            CoreDefinition: "core prompt",
            Skills:
            [
                CreateSkill(
                    "netflix-surface-and-state",
                    "netflix core skill",
                    Activation(
                        whenAnyKeywords: ["netflix"],
                        whenAnyTools: ["describe_selected_window"]),
                    group: "netflix",
                    priority: 350),
                CreateSkill(
                    "netflix-profile-and-pin",
                    "netflix profile skill",
                    Activation(
                        whenAllKeywords: ["netflix"],
                        whenAnyKeywords: ["profile", "profiles"],
                        whenAnyTools: ["describe_selected_window"]),
                    group: "netflix",
                    priority: 400)
            ]);

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Which profiles are available?",
            [
                new AgentMessage.Summary("Environment context: selected window is Netflix - Microsoft Edge. Visible cue: profile picker.")
            ],
            [
                new ToolDefinition("describe_selected_window", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "netflix-surface-and-state");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "netflix-profile-and-pin");
    }

    [Fact]
    public void Compose_ActivatesPlaybackControlsSkill_ForNetflixSubtitleRequests()
    {
        var catalog = new AgentPromptCatalog(
            FallbackDefinitionPath: "fallback/her.agent.md",
            FallbackDefinition: "fallback prompt",
            CoreDefinitionPath: "core/her.agent.core.md",
            CoreDefinition: "core prompt",
            Skills:
            [
                CreateSkill(
                    "netflix-surface-and-state",
                    "netflix core skill",
                    Activation(
                        whenAnyKeywords: ["netflix"],
                        whenAnyTools: ["describe_selected_window"]),
                    group: "netflix",
                    priority: 350),
                CreateSkill(
                    "netflix-playback-controls",
                    "netflix playback controls skill",
                    Activation(
                        whenAllKeywords: ["netflix"],
                        whenAnyKeywords: ["subtitle", "subtitles", "audio"],
                        whenAnyTools: ["describe_selected_window"]),
                    group: "netflix",
                    priority: 500),
                CreateSkill(
                    "netflix-profile-and-pin",
                    "netflix profile skill",
                    Activation(
                        whenAllKeywords: ["netflix"],
                        whenAnyKeywords: ["profile", "profiles", "pin"],
                        whenAnyTools: ["describe_selected_window"]),
                    group: "netflix",
                    priority: 400)
            ]);

        var actual = AgentPromptComposer.Compose(
            catalog,
            "Turn off subtitles.",
            [
                new AgentMessage.Summary("Environment context: selected window is Netflix. Visible cue: Back to Browse and Audio & Subtitles.")
            ],
            [
                new ToolDefinition("describe_selected_window", "desc", default)
            ]);

        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "netflix-surface-and-state");
        Assert.Contains(actual.ActiveSkills, skill => skill.Key == "netflix-playback-controls");
        Assert.DoesNotContain(actual.ActiveSkills, skill => skill.Key == "netflix-profile-and-pin");
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
                    "generic-app-policy",
                    "generic app policy skill",
                    Activation(
                        whenAnyTools:
                        [
                            "select_window",
                            "launch_app_via_taskbar_search",
                            "describe_selected_window",
                            "capture_selected_window_screenshot",
                            "send_input_to_window",
                            "click_selected_window_element",
                            "invoke_selected_window_element",
                            "focus_selected_window_element",
                            "set_selected_window_element_value",
                            "invoke_main_menu_item",
                            "invoke_context_menu_item",
                            "select_taskbar_app"
                        ]),
                    group: "generic-app",
                    priority: 140),
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
                        ]),
                    group: "any-app",
                    priority: 200),
                CreateSkill(
                    "browser-navigation-and-web-operations",
                    "browser skill",
                    Activation(
                        whenAnyIntents: ["browser_request", "instruction_lookup_request"],
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
                        ]),
                    group: "edge",
                    priority: 300),
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
                        ]),
                    group: "windows",
                    priority: 100),
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
                        ]),
                    group: "any-app",
                    priority: 210),
                CreateSkill(
                    "ui-refresh-and-evidence",
                    "refresh skill",
                    Activation(
                        whenAnyTools:
                        [
                            "describe_selected_window",
                            "describe_selected_window_focus",
                            "capture_selected_window_screenshot"
                        ]),
                    group: "any-app",
                    priority: 150)
            ]);

    private static AgentSkillPrompt CreateSkill(
        string key,
        string promptText,
        AgentSkillActivation activation,
        string group = "general",
        int priority = 1000)
        => new(
            key,
            $"skills/{key}.skill.md",
            promptText,
            new AgentSkillMetadata(
                key,
                Summary: null,
                PreferredTools: [],
                AppliesWhen: [],
                Group: group,
                Priority: priority,
                Activation: activation));

    private static AgentSkillActivation Activation(
        string[]? whenAnyIntents = null,
        string[]? whenAllIntents = null,
        string[]? unlessAnyIntents = null,
        string[]? whenAnyTools = null,
        string[]? whenAllTools = null,
        string[]? whenAnyKeywords = null,
        string[]? whenAllKeywords = null,
        string[]? unlessAnyKeywords = null)
        => new(
            whenAnyIntents ?? Array.Empty<string>(),
            whenAllIntents ?? Array.Empty<string>(),
            unlessAnyIntents ?? Array.Empty<string>(),
            whenAnyTools ?? Array.Empty<string>(),
            whenAllTools ?? Array.Empty<string>(),
            whenAnyKeywords ?? Array.Empty<string>(),
            whenAllKeywords ?? Array.Empty<string>(),
            unlessAnyKeywords ?? Array.Empty<string>());

    private static AgentSkillActivation EmptyActivation
        => Activation();
}
