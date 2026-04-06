using System.Text.Json;
using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class TurnProcessorSkillGenerationTests
{
    [Fact]
    public async Task ProcessAsync_OffersThenGeneratesUnknownAppSkillGroup()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"herface-turn-skill-gen-{Guid.NewGuid():N}");
        var agentsDirectory = Path.Combine(tempRoot, ".github", "agents");
        var skillsDirectory = Path.Combine(agentsDirectory, "skills");
        Directory.CreateDirectory(Path.Combine(skillsDirectory, "windows"));
        Directory.CreateDirectory(Path.Combine(skillsDirectory, "any-app"));

        try
        {
            WriteFile(Path.Combine(agentsDirectory, "her.agent.md"), "fallback prompt");
            WriteFile(Path.Combine(agentsDirectory, "her.agent.core.md"), "core prompt");
            WriteFile(
                Path.Combine(skillsDirectory, "windows", "desktop-launch-and-first-look.skill.md"),
                """
                ---
                id: desktop-launch-and-first-look
                group: windows
                ---

                # Skill

                launch
                """);
            WriteFile(
                Path.Combine(skillsDirectory, "any-app", "ui-refresh-and-evidence.skill.md"),
                """
                ---
                id: ui-refresh-and-evidence
                group: any-app
                ---

                # Skill

                refresh
                """);

            var catalog = AgentPromptLoader.LoadFromResolvedPaths(
                Path.Combine(agentsDirectory, "her.agent.md"),
                Path.Combine(agentsDirectory, "her.agent.core.md"));
            var config = CreateConfig(catalog);
            var llmClient = new QueuedLlmClient(
            [
                new ChatResult(
                    """
                    {
                      "say": "I drafted the Spotify skill group from the official quickstart.",
                      "log": "I used Spotify's official getting started documentation to draft the new Spotify skill group.",
                      "skill_generation": {
                        "app_name": "Spotify",
                        "group": "spotify",
                        "source_url": "https://support.spotify.com/us/article/getting-started/",
                        "files": [
                          {
                            "file_name": "spotify-surface-and-state.skill.md",
                            "content": "---\nid: spotify-surface-and-state\ngroup: spotify\npriority: 350\nsummary: \"Shared Spotify surface model and state verification rules.\"\nactivation:\n  when_any_keywords:\n    - spotify\n---\n\n# Skill: Spotify Surface And State\n\n- Verify the first visible Spotify surface before deeper actions."
                          }
                        ]
                      }
                    }
                    """,
                                        []),
                                new ChatResult(
                                        """
                                        {
                                            "say": "Spotify is open now.",
                                            "log": "Spotify is open and ready for the next action."
                                        }
                                        """,
                    [])
            ]);
            await using var mcpManager = new McpClientManager(
                _ => Task.FromResult<IReadOnlyList<ToolDefinition>>(
                [
                    CreateToolDefinition("list_windows"),
                    CreateToolDefinition("launch_app_via_taskbar_search"),
                    CreateToolDefinition("describe_selected_window"),
                    CreateToolDefinition("send_input_to_window")
                ]),
                null);

            var history = new List<AgentMessage>();

            var firstTurn = await HerfaceTurnProcessor.ProcessAsync(
                turnId: 1,
                userText: "Open Spotify.",
                history,
                config,
                llmClient,
                mcpManager,
                CancellationToken.None,
                turnSource: "test");

            Assert.Equal(0, llmClient.CallCount);
            Assert.Contains("dedicated Spotify skill group", firstTurn.Reply.SpokenText, StringComparison.Ordinal);
            Assert.True(AppSkillGenerationCoordinator.TryGetPendingOffer(history, out var offer));
            Assert.Equal("Spotify", offer.AppName);
            Assert.Equal("spotify", offer.Group);

            var secondTurn = await HerfaceTurnProcessor.ProcessAsync(
                turnId: 2,
                userText: "yes, generate it first",
                history,
                config,
                llmClient,
                mcpManager,
                CancellationToken.None,
                turnSource: "test");

            Assert.Equal(2, llmClient.CallCount);
            Assert.NotNull(secondTurn.UpdatedConfig);
            Assert.Contains("saved the new Spotify skill group draft", secondTurn.Reply.SpokenText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Spotify is open now", secondTurn.Reply.SpokenText, StringComparison.Ordinal);
            Assert.Contains("Special task: the user approved generating a new app skill group for Spotify", llmClient.SystemPrompts[0], StringComparison.Ordinal);
            Assert.Contains("Spotify Surface And State", llmClient.SystemPrompts[1], StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(skillsDirectory, "spotify", "spotify-surface-and-state.skill.md")));
            Assert.Contains(secondTurn.UpdatedConfig!.AgentPrompts.Skills, skill => skill.Key == "spotify-surface-and-state");
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
    public async Task ProcessAsync_OffersThenDeclinesGeneration_AndContinuesNormalLaunchFlow()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"herface-turn-skill-skip-{Guid.NewGuid():N}");
        var agentsDirectory = Path.Combine(tempRoot, ".github", "agents");
        var skillsDirectory = Path.Combine(agentsDirectory, "skills");
        Directory.CreateDirectory(Path.Combine(skillsDirectory, "windows"));
        Directory.CreateDirectory(Path.Combine(skillsDirectory, "any-app"));

        try
        {
            WriteFile(Path.Combine(agentsDirectory, "her.agent.md"), "fallback prompt");
            WriteFile(Path.Combine(agentsDirectory, "her.agent.core.md"), "core prompt");
            WriteFile(
                Path.Combine(skillsDirectory, "windows", "desktop-launch-and-first-look.skill.md"),
                """
                ---
                id: desktop-launch-and-first-look
                group: windows
                ---

                # Skill

                launch
                """);
            WriteFile(
                Path.Combine(skillsDirectory, "any-app", "ui-refresh-and-evidence.skill.md"),
                """
                ---
                id: ui-refresh-and-evidence
                group: any-app
                ---

                # Skill

                refresh
                """);

            var catalog = AgentPromptLoader.LoadFromResolvedPaths(
                Path.Combine(agentsDirectory, "her.agent.md"),
                Path.Combine(agentsDirectory, "her.agent.core.md"));
            var config = CreateConfig(catalog);
            var llmClient = new QueuedLlmClient(
            [
                new ChatResult(
                    """
                    {
                      "say": "Okay, I’ll open Spotify now.",
                      "log": "The user declined skill generation, so I am proceeding with the normal Spotify launch flow without creating a new skill group."
                    }
                    """,
                    [])
            ]);
            await using var mcpManager = new McpClientManager(
                _ => Task.FromResult<IReadOnlyList<ToolDefinition>>(
                [
                    CreateToolDefinition("list_windows"),
                    CreateToolDefinition("launch_app_via_taskbar_search"),
                    CreateToolDefinition("describe_selected_window")
                ]),
                null);

            var history = new List<AgentMessage>();

            var firstTurn = await HerfaceTurnProcessor.ProcessAsync(
                turnId: 1,
                userText: "Open Spotify.",
                history,
                config,
                llmClient,
                mcpManager,
                CancellationToken.None,
                turnSource: "test");

            Assert.Equal(0, llmClient.CallCount);
            Assert.Contains("dedicated Spotify skill group", firstTurn.Reply.SpokenText, StringComparison.Ordinal);

            var secondTurn = await HerfaceTurnProcessor.ProcessAsync(
                turnId: 2,
                userText: "Just open it.",
                history,
                config,
                llmClient,
                mcpManager,
                CancellationToken.None,
                turnSource: "test");

            Assert.Equal(1, llmClient.CallCount);
            Assert.Null(secondTurn.UpdatedConfig);
            Assert.Contains("open Spotify now", secondTurn.Reply.SpokenText, StringComparison.Ordinal);
            Assert.Equal("Open Spotify.", llmClient.UserMessages.Single());
            Assert.DoesNotContain("Special task: the user approved generating a new app skill group for Spotify", llmClient.SystemPrompts.SingleOrDefault() ?? string.Empty, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(skillsDirectory, "spotify")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n"));
    }

    private static ToolDefinition CreateToolDefinition(string name)
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return new ToolDefinition(name, $"{name} description", document.RootElement.Clone());
    }

    private static AppConfig CreateConfig(AgentPromptCatalog catalog)
        => new(
            LlmProvider: LlmProviderId.OpenAiApi,
            AgentDefinitionPath: catalog.FallbackDefinitionPath,
            AgentDefinition: catalog.FallbackDefinition,
            AgentPrompts: catalog,
            DebugAudioPlayback: false,
            EnableDebugTrace: false,
            OpenAiApiKey: string.Empty,
            OpenAiModel: "gpt-5.4-mini",
            LlmTemperature: 0,
            TtsModel: "tts",
            TtsVoice: "voice",
            TtsInstructions: string.Empty,
            AnthropicApiKey: string.Empty,
            AnthropicModel: string.Empty,
            WhisperModel: string.Empty,
            VoiceLanguages: [],
            MaxRecordMs: 10_000,
            ActiveIdleTimeoutMs: 10_000,
            MaxContextTokens: 128_000,
            WakeWord: "hello there",
            McpServers: []);

    private sealed class QueuedLlmClient(IReadOnlyList<ChatResult> responses) : ILlmClient
    {
        private readonly Queue<ChatResult> _responses = new(responses);

        public int CallCount { get; private set; }

        public List<string?> SystemPrompts { get; } = [];

        public List<string> UserMessages { get; } = [];

        public LlmProviderId ProviderId => LlmProviderId.OpenAiApi;

        public string DisplayName => "Queued Test LLM";

        public LlmModelProfile ModelProfile { get; } = new(
            LlmProviderId.OpenAiApi,
            "gpt-5.4-mini",
            ContextCompressionTriggerRatio: 0.55,
            WindowSnapshotCharBudget: 4_800,
            FocusSnapshotCharBudget: 2_800,
            MaxThrottleRetries: 1);

        public Task<ChatResult> ChatAsync(
            IReadOnlyList<AgentMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            string? systemPrompt,
            CancellationToken cancellationToken)
        {
            CallCount += 1;
            SystemPrompts.Add(systemPrompt);
            var lastUserMessage = messages.OfType<AgentMessage.User>().LastOrDefault()?.Content;
            if (!string.IsNullOrWhiteSpace(lastUserMessage))
            {
                UserMessages.Add(lastUserMessage);
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued LLM responses remain.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}