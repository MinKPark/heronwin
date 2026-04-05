namespace HeronWin.HerFace;

internal sealed record ProcessedTurn(
    long TurnId,
    string UserText,
    AgentReply Reply,
    ComposedAgentPrompt Prompt,
    int ContextTokenEstimate);

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
        var composedPrompt = AgentPromptComposer.Compose(config.AgentPrompts, userText, history, tools);
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

        history.Add(new AgentMessage.User(userText));
        history.Add(new AgentMessage.Assistant(reply.RawText));

        var tokenEstimate = ContextManager.EstimateTokens(history, composedPrompt.SystemPrompt);
        Display.ContextUsage(tokenEstimate, config.MaxContextTokens);
        DebugTrace.WriteEvent(
            "agent.turn.complete",
            $"turn={turnId}, source={turnSource}, spoken={DebugTrace.Preview(reply.SpokenText, 300)}, log={DebugTrace.Preview(reply.LogText, 600)}");

        return new ProcessedTurn(turnId, userText, reply, composedPrompt, tokenEstimate);
    }
}
