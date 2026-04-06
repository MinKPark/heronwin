using System.Text.Json;

namespace HeronWin.Brain;

internal sealed record ProcessedTurn(
    long TurnId,
    string UserText,
    AgentReply Reply,
    ComposedAgentPrompt Prompt,
    int ContextTokenEstimate,
    AppConfig? UpdatedConfig = null);

internal static class BrainTurnProcessor
{
    private const int GenerationContinuationTokenReserve = 4096;

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
        PendingAppSkillOffer declinedOffer;
        var generationMode = AppSkillGenerationCoordinator.TryBuildApprovedGenerationRequest(
            history,
            userText,
            out approvedOffer,
            out var generationUserText);
        if (generationMode)
        {
            userText = generationUserText;
        }
        else if (AppSkillGenerationCoordinator.TryBuildDeclinedLaunchRequest(
                     history,
                     userText,
                     out declinedOffer,
                     out var launchUserText))
        {
            userText = launchUserText;
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
        var deferredGenerationSummary = string.Empty;
        if (generationMode &&
            AppSkillGenerationCoordinator.TryPersistGeneratedSkillGroup(
                reply.RawText,
                approvedOffer,
                config.AgentPrompts,
                out var refreshedCatalog,
                out var persistenceSummary))
        {
            deferredGenerationSummary = persistenceSummary;
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

        if (generationMode && updatedConfig is not null)
        {
            var continuationRequest = $"Open {approvedOffer.AppName}.";
            var continuationHistory = new List<AgentMessage>(history)
            {
                new AgentMessage.User(originalUserText),
                new AgentMessage.Summary(
                    $"Internal continuation: saved and reloaded the `{approvedOffer.Group}` skill group for {approvedOffer.AppName}. Resume the pending app launch now using the new skills."),
                new AgentMessage.Assistant(reply.RawText)
            };
            var continuationPrompt = AgentPromptComposer.Compose(updatedConfig.AgentPrompts, continuationRequest, continuationHistory, tools);
            DebugTrace.WriteEvent(
                "agent.prompt.composed",
                $"turn={turnId}, source={continuationPrompt.SourceDescription}, fallback={continuationPrompt.UsesFallbackDefinition}, skills={string.Join(", ", continuationPrompt.ActiveSkills.Select(skill => skill.Key).DefaultIfEmpty("(none)"))}, continuation=post_skill_generation");

            var continuationContextBudget = Math.Max(0, updatedConfig.MaxContextTokens - GenerationContinuationTokenReserve);
            await ContextManager.EnsureCapacityAsync(
                continuationHistory,
                continuationRequest,
                continuationPrompt.SystemPrompt,
                continuationContextBudget > 0 ? continuationContextBudget : updatedConfig.MaxContextTokens,
                llmClient.ModelProfile,
                llmClient,
                cancellationToken);

            var continuationReply = await AgentRunner.RunTurnAsync(
                turnId,
                continuationRequest,
                continuationHistory,
                tools,
                continuationPrompt,
                llmClient,
                mcpManager,
                cancellationToken,
                intermediateStepNarrator,
                displayUserMessage: false);

            var combinedLog = string.IsNullOrWhiteSpace(deferredGenerationSummary)
                ? continuationReply.LogText
                : string.IsNullOrWhiteSpace(continuationReply.LogText)
                    ? deferredGenerationSummary
                    : $"{deferredGenerationSummary} {continuationReply.LogText}";
            var combinedSay = BuildCombinedContinuationSpeech(
                approvedOffer.AppName,
                continuationReply.SpokenText,
                deferredGenerationSummary);
            reply = new AgentReply(
                combinedLog,
                combinedSay,
                JsonSerializer.Serialize(new
                {
                    say = combinedSay,
                    log = combinedLog
                }));
            composedPrompt = continuationPrompt;
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

    private static string BuildCombinedContinuationSpeech(
        string appName,
        string continuationSpeech,
        string generationSummary)
    {
        var generationSpeech = string.IsNullOrWhiteSpace(generationSummary)
            ? $"I saved the new {appName} skill group draft."
            : $"I saved the new {appName} skill group draft.";

        if (string.IsNullOrWhiteSpace(continuationSpeech))
        {
            return generationSpeech;
        }

        return $"{generationSpeech} {continuationSpeech}";
    }
}
