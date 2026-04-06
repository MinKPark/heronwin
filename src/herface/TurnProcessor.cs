namespace HeronWin.HerFace;

internal sealed record ProcessedTurn(
    long TurnId,
    string UserText,
    AgentReply Reply,
    ComposedAgentPrompt Prompt,
    int ContextTokenEstimate,
    AppConfig? UpdatedConfig = null);

internal static class HerfaceTurnProcessor
{
    public static async Task<ProcessedTurn> ProcessAsync(
        long turnId,
        string userText,
        List<AgentMessage> history,
        AppConfig config,
        ILlmClient llmClient,
        McpClientManager mcpManager,
        CancellationToken cancellationToken,
        string turnSource,
        Func<string, CancellationToken, Task>? intermediateStepNarrator = null)
    {
        DebugTrace.WriteStructuredEvent(
            "agent.turn.processing_start",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["source"] = turnSource,
                ["historyMessages"] = history.Count,
                ["queuedText"] = DebugTrace.Preview(userText, 500),
            });

        var tools = await mcpManager.ListAllToolsAsync(cancellationToken);
        var originalUserText = userText;
        PendingAppSkillOffer approvedOffer;
        var generationMode = AppSkillGenerationCoordinator.TryBuildApprovedGenerationRequest(
            history,
            userText,
            out approvedOffer,
            out var generationUserText);
        if (generationMode)
        {
            userText = generationUserText;
        }

        var composedPrompt = AgentPromptComposer.Compose(config.AgentPrompts, userText, history, tools);
        if (AppSkillGenerationCoordinator.TryBuildUnknownAppSkillOffer(
                originalUserText,
                history,
                config.AgentPrompts,
                out var offerReply))
        {
            DebugTrace.WriteEvent(
                "agent.prompt.composed",
                $"turn={turnId}, source={composedPrompt.SourceDescription}, fallback={composedPrompt.UsesFallbackDefinition}, skills={string.Join(", ", composedPrompt.ActiveSkills.Select(skill => skill.Key).DefaultIfEmpty("(none)"))}");

            Display.UserMessage(originalUserText);
            Display.AssistantReply(offerReply.SpokenText, offerReply.LogText);
            history.Add(new AgentMessage.User(originalUserText));
            history.Add(new AgentMessage.Assistant(offerReply.RawText));

            var immediateTokenEstimate = ContextManager.EstimateTokens(history, composedPrompt.SystemPrompt);
            Display.ContextUsage(immediateTokenEstimate, config.MaxContextTokens);
            DebugTrace.WriteStructuredEvent(
                "assistant.reply",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["elapsedMs"] = 0,
                    ["attempts"] = 0,
                    ["usedAnyTools"] = false,
                    ["performedDesktopAction"] = false,
                    ["performedConfidenceEvidenceRetry"] = false,
                    ["sayText"] = offerReply.SpokenText,
                    ["logText"] = offerReply.LogText,
                    ["sayPreview"] = DebugTrace.Preview(offerReply.SpokenText, 400),
                    ["logPreview"] = DebugTrace.Preview(offerReply.LogText, 900),
                    ["rawPreview"] = DebugTrace.Preview(offerReply.RawText, 1200),
                });

            return new ProcessedTurn(turnId, originalUserText, offerReply, composedPrompt, immediateTokenEstimate);
        }

        if (generationMode)
        {
            var systemPrompt = string.Join(
                "\n\n",
                new[]
                {
                    composedPrompt.SystemPrompt,
                    AppSkillGenerationCoordinator.BuildGenerationPromptAugmentation(approvedOffer)
                }.Where(section => !string.IsNullOrWhiteSpace(section)));
            composedPrompt = composedPrompt with { SystemPrompt = systemPrompt };
        }

        DebugTrace.WriteEvent(
            "agent.prompt.composed",
            $"turn={turnId}, source={composedPrompt.SourceDescription}, fallback={composedPrompt.UsesFallbackDefinition}, skills={string.Join(", ", composedPrompt.ActiveSkills.Select(skill => skill.Key).DefaultIfEmpty("(none)"))}");

        await ContextManager.EnsureCapacityAsync(
            history,
            userText,
            composedPrompt.SystemPrompt,
            config.MaxContextTokens,
            llmClient.ModelProfile,
            llmClient,
            cancellationToken);

        var reply = await AgentRunner.RunTurnAsync(
            turnId,
            userText,
            history,
            tools,
            composedPrompt,
            llmClient,
            mcpManager,
            cancellationToken,
            intermediateStepNarrator);

        AppConfig? updatedConfig = null;
        if (generationMode &&
            AppSkillGenerationCoordinator.TryPersistGeneratedSkillGroup(
                reply.RawText,
                approvedOffer,
                config.AgentPrompts,
                out var refreshedCatalog,
                out var persistenceSummary))
        {
            var augmentedLog = string.IsNullOrWhiteSpace(reply.LogText)
                ? persistenceSummary
                : $"{reply.LogText} {persistenceSummary}";
            var augmentedSay = string.IsNullOrWhiteSpace(reply.SpokenText)
                ? $"I saved the new {approvedOffer.AppName} skill group draft."
                : $"{reply.SpokenText} I saved the new {approvedOffer.AppName} skill group draft.";
            reply = reply with
            {
                LogText = augmentedLog,
                SpokenText = augmentedSay,
            };
            updatedConfig = config with
            {
                AgentDefinitionPath = refreshedCatalog.FallbackDefinitionPath,
                AgentDefinition = refreshedCatalog.FallbackDefinition,
                AgentPrompts = refreshedCatalog,
            };
            DebugTrace.WriteStructuredEvent(
                "agent.skill_generation_saved",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["appName"] = approvedOffer.AppName,
                    ["group"] = approvedOffer.Group,
                    ["summary"] = persistenceSummary,
                });
        }

        history.Add(new AgentMessage.User(originalUserText));
        history.Add(new AgentMessage.Assistant(reply.RawText));

        var tokenEstimate = ContextManager.EstimateTokens(history, composedPrompt.SystemPrompt);
        Display.ContextUsage(tokenEstimate, config.MaxContextTokens);
        DebugTrace.WriteEvent(
            "agent.turn.complete",
            $"turn={turnId}, source={turnSource}, spoken={DebugTrace.Preview(reply.SpokenText, 300)}, log={DebugTrace.Preview(reply.LogText, 600)}");

        return new ProcessedTurn(turnId, originalUserText, reply, composedPrompt, tokenEstimate, updatedConfig);
    }
}
