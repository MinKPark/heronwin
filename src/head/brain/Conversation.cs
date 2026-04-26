using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace HeronWin.Brain;

internal sealed record ToolDefinition(string Name, string Description, JsonElement Parameters);

internal sealed record ToolCallRequest(string Id, string Name, string Arguments);

internal sealed record ToolImage(string MimeType, string Base64Data, string Detail = "low");

internal sealed record ToolCallOutcome(
    string Text,
    IReadOnlyList<ToolImage> Images,
    bool IsError = false,
    long? McpCallId = null);

internal sealed record AgentReply(
    string LogText,
    string SpokenText,
    string RawText,
    ScriptedLookaheadDecision? LookaheadDecision = null);

internal sealed record ToolStepNarrationPlan(string Text, string Source);

internal sealed record ScriptedTurnStartCarryForwardContext(
    long SourceTurnId,
    string SourceKind,
    long EvidenceAgeMs,
    string? WindowHandle,
    string? WindowTitle,
    string UiContext,
    string? FocusContext,
    bool IsPostActionSnapshot);

internal sealed record StartupWindowInventoryContext(
    long SourceTurnId,
    string SourceKind,
    long EvidenceAgeMs,
    string? SelectedWindowHandle,
    int WindowCount,
    int OmittedWindowCount,
    string InventoryContext);

internal sealed record CompactWindowInventoryWindow(
    string? Handle,
    string? Title,
    string? ClassName,
    bool IsSelected,
    bool HasUsableBounds);

internal sealed record CompactWindowInventorySnapshot(
    string? SelectedWindowHandle,
    IReadOnlyList<CompactWindowInventoryWindow> Windows,
    int WindowCount,
    int OmittedWindowCount);

internal abstract record AgentMessage(string Role)
{
    public sealed record User(string Content) : AgentMessage("user");

    public sealed record Summary(string Content) : AgentMessage("summary");

    public sealed record VisualContext(string Content, IReadOnlyList<ToolImage> Images) : AgentMessage("user_visual");

    public sealed record Assistant(string? Content, IReadOnlyList<ToolCallRequest>? ToolCalls = null)
        : AgentMessage("assistant");

    public sealed record ToolResult(
        string ToolCallId,
        string ToolName,
        string Content,
        IReadOnlyList<ToolImage>? Images = null)
        : AgentMessage("tool_result");
}

internal sealed record ChatResult(string? Text, IReadOnlyList<ToolCallRequest> ToolCalls);

internal interface ILlmClient
{
    LlmProviderId ProviderId { get; }
    string DisplayName { get; }
    LlmModelProfile ModelProfile { get; }
    Task<ChatResult> ChatAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        string? systemPrompt,
        CancellationToken cancellationToken);
}

internal interface IAudioTranscriber
{
    string DisplayName { get; }
    Task<string> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken);
}

internal static class AgentRunner
{
    private static readonly TimeSpan ScriptedTurnStartEvidenceMaxAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupWindowInventoryMaxAge = TimeSpan.FromSeconds(30);

    public static async Task<AgentReply> RunTurnAsync(
        long turnId,
        string userText,
        List<AgentMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        ComposedAgentPrompt composedPrompt,
        ILlmClient llmClient,
        McpClientManager mcpManager,
        DesktopSessionContext desktopSession,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task>? intermediateStepNarrator = null,
        bool displayUserMessage = true,
        bool scriptedMode = false,
        ScriptedLookaheadContext? scriptedLookahead = null)
    {
        var turnStopwatch = Stopwatch.StartNew();
        var messages = history.ToList();
        var availableToolNames = tools
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        var ordinalActionSummary = BuildOrdinalActionReferenceSummary(userText, history);
        if (!string.IsNullOrWhiteSpace(ordinalActionSummary))
        {
            messages.Add(new AgentMessage.Summary(ordinalActionSummary));
        }

        var usedAnyTools = false;
        var performedDesktopAction = false;
        var additionalDesktopEvidenceAttempts = 0;
        const int maxAdditionalDesktopEvidenceAttempts = 2;
        var llmAttempt = 0;
        var attemptedNamedChoiceAutoFollowThrough = false;
        var attemptedDiscreteSlotAutoFollowThrough = false;
        var internalContinuationSequence = 0;
        string? recentListWindowsOutput = desktopSession.RecentListWindowsOutput;
        DesktopEvidenceMetadata? recentListWindowsEvidenceMetadata = desktopSession.RecentListWindowsEvidenceMetadata;
        string? recentWindowInventoryModelContext = desktopSession.RecentWindowInventoryModelContext;
        string? recentWindowContext = desktopSession.RecentWindowContext;
        string? recentUiTreeContext = desktopSession.RecentUiTreeContext;
        DesktopEvidenceMetadata? recentUiTreeEvidenceMetadata = desktopSession.RecentUiTreeEvidenceMetadata;
        string? recentFocusContext = desktopSession.RecentFocusContext;
        DesktopEvidenceMetadata? recentFocusEvidenceMetadata = desktopSession.RecentFocusEvidenceMetadata;
        string? currentUiElementContext = desktopSession.CurrentUiElementContext;
        string? currentFocusElementContext = desktopSession.CurrentFocusElementContext;
        string? lastCompletedToolNameInTurn = null;
        AgentReply? forcedReply = null;
        var scriptedTurnStartCarryForwardUsed = false;
        var startupWindowInventoryUsed = false;
        var scriptedLookaheadInstructionAdded = false;
        var scriptedLookaheadDecisionLogged = false;

        void SyncDesktopSession()
        {
            desktopSession.RecentListWindowsOutput = recentListWindowsOutput;
            desktopSession.RecentListWindowsEvidenceMetadata = recentListWindowsEvidenceMetadata;
            desktopSession.RecentWindowInventoryModelContext = recentWindowInventoryModelContext;
            desktopSession.RecentWindowContext = recentWindowContext;
            desktopSession.RecentUiTreeContext = recentUiTreeContext;
            desktopSession.RecentUiTreeEvidenceMetadata = recentUiTreeEvidenceMetadata;
            desktopSession.RecentFocusContext = recentFocusContext;
            desktopSession.RecentFocusEvidenceMetadata = recentFocusEvidenceMetadata;
            desktopSession.CurrentUiElementContext = currentUiElementContext;
            desktopSession.CurrentFocusElementContext = currentFocusElementContext;
        }

        if (scriptedMode)
        {
            var readyStateStopwatch = Stopwatch.StartNew();
            var carryForwardContext = TryGetScriptedTurnStartCarryForwardContext(
                turnId,
                userText,
                desktopSession,
                DateTimeOffset.UtcNow,
                out var carryForwardSkipReason);
            var readyStateElapsedMs = (int)Math.Round(
                readyStateStopwatch.Elapsed.TotalMilliseconds,
                MidpointRounding.AwayFromZero);

            if (carryForwardContext is not null)
            {
                scriptedTurnStartCarryForwardUsed = true;
                messages.Add(new AgentMessage.Summary(BuildScriptedTurnStartCarryForwardSummary(carryForwardContext)));
                messages.Add(new AgentMessage.User(BuildScriptedTurnStartCarryForwardUserMessage(carryForwardContext)));
                DebugTrace.WriteStructuredEvent(
                    "agent.turn.ready_state_used",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["sourceTurn"] = carryForwardContext.SourceTurnId,
                        ["windowHandle"] = carryForwardContext.WindowHandle,
                        ["windowTitle"] = carryForwardContext.WindowTitle,
                        ["evidenceAgeMs"] = carryForwardContext.EvidenceAgeMs,
                        ["snapshotSource"] = carryForwardContext.SourceKind,
                        ["snapshotWasPostAction"] = carryForwardContext.IsPostActionSnapshot,
                        ["hadUiTree"] = true,
                        ["hadFocusContext"] = !string.IsNullOrWhiteSpace(carryForwardContext.FocusContext),
                        ["activationAttempted"] = false,
                        ["activationSucceeded"] = false,
                        ["elapsedMs"] = readyStateElapsedMs,
                    });
                DebugTrace.WriteStructuredEvent(
                    "agent.turn.carry_forward_evidence_used",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["sourceTurn"] = carryForwardContext.SourceTurnId,
                        ["windowHandle"] = carryForwardContext.WindowHandle,
                        ["windowTitle"] = carryForwardContext.WindowTitle,
                        ["evidenceAgeMs"] = carryForwardContext.EvidenceAgeMs,
                        ["snapshotSource"] = carryForwardContext.SourceKind,
                        ["snapshotWasPostAction"] = carryForwardContext.IsPostActionSnapshot,
                        ["hadUiTree"] = true,
                        ["hadFocusContext"] = !string.IsNullOrWhiteSpace(carryForwardContext.FocusContext),
                    });
            }
            else
            {
                DebugTrace.WriteStructuredEvent(
                    "agent.turn.ready_state_skipped",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["sourceTurn"] = recentUiTreeEvidenceMetadata?.SourceTurnId,
                        ["windowHandle"] = desktopSession.CurrentWindowHandle,
                        ["windowTitle"] = desktopSession.CurrentWindowTitle,
                        ["evidenceAgeMs"] = GetEvidenceAgeMs(recentUiTreeEvidenceMetadata, DateTimeOffset.UtcNow),
                        ["snapshotSource"] = recentUiTreeEvidenceMetadata?.SourceKind,
                        ["snapshotWasPostAction"] = recentUiTreeEvidenceMetadata?.IsPostActionSnapshot,
                        ["hadUiTree"] = SnapshotContainsElementTree(GetCurrentUiTreeContext(recentWindowContext, recentUiTreeContext)),
                        ["hadFocusContext"] = !string.IsNullOrWhiteSpace(currentFocusElementContext)
                                              || !string.IsNullOrWhiteSpace(recentFocusContext),
                        ["activationAttempted"] = false,
                        ["activationSucceeded"] = false,
                        ["skipReason"] = carryForwardSkipReason,
                        ["elapsedMs"] = readyStateElapsedMs,
                    });
                DebugTrace.WriteStructuredEvent(
                    "agent.turn.carry_forward_evidence_skipped",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["sourceTurn"] = recentUiTreeEvidenceMetadata?.SourceTurnId,
                        ["windowHandle"] = desktopSession.CurrentWindowHandle,
                        ["windowTitle"] = desktopSession.CurrentWindowTitle,
                        ["evidenceAgeMs"] = GetEvidenceAgeMs(recentUiTreeEvidenceMetadata, DateTimeOffset.UtcNow),
                        ["snapshotSource"] = recentUiTreeEvidenceMetadata?.SourceKind,
                        ["snapshotWasPostAction"] = recentUiTreeEvidenceMetadata?.IsPostActionSnapshot,
                        ["skipReason"] = carryForwardSkipReason,
                    });
            }
        }

        var startupInventorySkipReason = "startup_inventory_not_considered";
        if (ShouldOfferStartupWindowInventory(
                userText,
                composedPrompt.ActiveSkills,
                recentWindowContext,
                recentUiTreeContext,
                out startupInventorySkipReason))
        {
            var startupInventoryContext = TryGetStartupWindowInventoryContext(
                recentListWindowsOutput,
                recentListWindowsEvidenceMetadata,
                recentWindowInventoryModelContext,
                DateTimeOffset.UtcNow,
                out startupInventorySkipReason);
            var startupInventoryRefreshed = false;

            if (startupInventoryContext is null &&
                availableToolNames.Contains("list_windows"))
            {
                var inventoryStopwatch = Stopwatch.StartNew();
                try
                {
                    var listResult = await CallToolWithDesktopSessionAsync(
                        "list_windows",
                        new Dictionary<string, object?>());
                    var elapsedMs = (int)Math.Round(inventoryStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero);
                    DebugTrace.WriteStructuredEvent(
                        "agent.turn.startup_inventory_refreshed",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["sourceTurn"] = recentListWindowsEvidenceMetadata?.SourceTurnId,
                            ["snapshotSource"] = recentListWindowsEvidenceMetadata?.SourceKind,
                            ["isError"] = listResult.IsError,
                            ["elapsedMs"] = elapsedMs,
                            ["reason"] = startupInventorySkipReason,
                            ["resultPreview"] = DebugTrace.Preview(listResult.Text, 700),
                        });

                    if (!listResult.IsError)
                    {
                        startupInventoryRefreshed = true;
                        RememberRecentWindowInventory(listResult.Text, "list_windows");
                        startupInventoryContext = TryGetStartupWindowInventoryContext(
                            recentListWindowsOutput,
                            recentListWindowsEvidenceMetadata,
                            recentWindowInventoryModelContext,
                            DateTimeOffset.UtcNow,
                            out startupInventorySkipReason);
                    }
                }
                catch (Exception ex)
                {
                    DebugTrace.WriteStructuredEvent(
                        "agent.turn.startup_inventory_refresh_failed",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["sourceTurn"] = recentListWindowsEvidenceMetadata?.SourceTurnId,
                            ["snapshotSource"] = recentListWindowsEvidenceMetadata?.SourceKind,
                            ["reason"] = startupInventorySkipReason,
                            ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                        });
                    startupInventorySkipReason = "startup_inventory_refresh_failed";
                }
            }

            if (startupInventoryContext is not null)
            {
                startupWindowInventoryUsed = true;
                messages.Add(new AgentMessage.Summary(BuildStartupWindowInventorySummary(startupInventoryContext)));
                messages.Add(new AgentMessage.User(BuildStartupWindowInventoryUserMessage(startupInventoryContext)));
                DebugTrace.WriteStructuredEvent(
                    startupInventoryRefreshed
                        ? "agent.turn.startup_inventory_used"
                        : "agent.turn.startup_inventory_reused",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["sourceTurn"] = startupInventoryContext.SourceTurnId,
                        ["snapshotSource"] = startupInventoryContext.SourceKind,
                        ["evidenceAgeMs"] = startupInventoryContext.EvidenceAgeMs,
                        ["selectedWindowHandle"] = startupInventoryContext.SelectedWindowHandle,
                        ["windowCount"] = startupInventoryContext.WindowCount,
                        ["omittedWindowCount"] = startupInventoryContext.OmittedWindowCount,
                    });
            }
            else
            {
                DebugTrace.WriteStructuredEvent(
                    "agent.turn.startup_inventory_skipped",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["sourceTurn"] = recentListWindowsEvidenceMetadata?.SourceTurnId,
                        ["snapshotSource"] = recentListWindowsEvidenceMetadata?.SourceKind,
                        ["evidenceAgeMs"] = GetEvidenceAgeMs(recentListWindowsEvidenceMetadata, DateTimeOffset.UtcNow),
                        ["skipReason"] = startupInventorySkipReason,
                    });
            }
        }

        messages.Add(new AgentMessage.User(userText));
        DebugTrace.WriteStructuredEvent(
            "agent.turn.start",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["historyMessages"] = history.Count,
                ["toolsAvailable"] = tools.Count,
                ["provider"] = llmClient.DisplayName,
                ["promptSource"] = composedPrompt.SourceDescription,
                ["usedFallbackPrompt"] = composedPrompt.UsesFallbackDefinition,
                ["activeSkills"] = composedPrompt.ActiveSkills.Select(skill => skill.Key).ToArray(),
                ["scriptedMode"] = scriptedMode,
                ["turnStartCarryForwardUsed"] = scriptedTurnStartCarryForwardUsed,
                ["startupWindowInventoryUsed"] = startupWindowInventoryUsed,
            });
        if (displayUserMessage)
        {
            Display.UserMessage(userText);
        }

        AgentReply FinalizeTurnReply(AgentReply reply)
        {
            Display.AssistantReply(reply.SpokenText, reply.LogText);
            DebugTrace.WriteStructuredEvent(
                "assistant.reply",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["elapsedMs"] = (int)Math.Round(turnStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                    ["attempts"] = llmAttempt,
                    ["usedAnyTools"] = usedAnyTools,
                    ["performedDesktopAction"] = performedDesktopAction,
                    ["performedConfidenceEvidenceRetry"] = additionalDesktopEvidenceAttempts > 0,
                    ["additionalDesktopEvidenceAttempts"] = additionalDesktopEvidenceAttempts,
                    ["sayText"] = reply.SpokenText,
                    ["logText"] = reply.LogText,
                    ["sayPreview"] = DebugTrace.Preview(reply.SpokenText, 400),
                    ["logPreview"] = DebugTrace.Preview(reply.LogText, 900),
                    ["rawPreview"] = DebugTrace.Preview(reply.RawText, 1200),
                });
            SyncDesktopSession();
            return reply;
        }

        void RememberRecentWindowSnapshot(
            string snapshotText,
            string? snapshotToolName = null,
            bool isPostActionSnapshot = false)
        {
            if (DescribePrimaryWindowFromToolOutput(snapshotText) is not null)
            {
                recentWindowContext = snapshotText;
            }

            if (TryGetPrimaryWindowReference(snapshotText, out var windowHandle, out var windowTitle))
            {
                desktopSession.CurrentWindowHandle = windowHandle;
                desktopSession.CurrentWindowTitle = windowTitle;
            }

            if (SnapshotContainsElementTree(snapshotText))
            {
                recentUiTreeContext = snapshotText;
                recentUiTreeEvidenceMetadata = new DesktopEvidenceMetadata(
                    turnId,
                    snapshotToolName ?? "window_snapshot",
                    DateTimeOffset.UtcNow,
                    isPostActionSnapshot);
                currentUiElementContext = null;
                currentFocusElementContext = null;

                if (string.Equals(snapshotToolName, "describe_window", StringComparison.Ordinal))
                {
                    currentUiElementContext = GetCompactSnapshotModelContext(snapshotText);
                }
            }

            SyncDesktopSession();
        }

        void RememberRecentWindowInventory(
            string inventoryText,
            string sourceKind = "list_windows")
        {
            if (string.IsNullOrWhiteSpace(inventoryText))
            {
                return;
            }

            recentListWindowsOutput = inventoryText;
            recentListWindowsEvidenceMetadata = new DesktopEvidenceMetadata(
                turnId,
                sourceKind,
                DateTimeOffset.UtcNow,
                false);
            recentWindowInventoryModelContext = TryBuildCompactWindowInventoryModelContext(inventoryText, out var compactInventoryContext)
                ? compactInventoryContext
                : null;
            SyncDesktopSession();
        }

        string? ResolveActionableUiTreeContext()
            => GetCurrentUiTreeContext(recentWindowContext, recentUiTreeContext);

        string? ResolveCurrentUiElementContext()
        {
            var actionableUiTreeContext = ResolveActionableUiTreeContext();
            if (actionableUiTreeContext is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(currentUiElementContext))
            {
                return currentUiElementContext;
            }

            currentUiElementContext = actionableUiTreeContext;
            return currentUiElementContext;
        }

        string? ResolveCurrentFocusElementContext()
        {
            if (!string.IsNullOrWhiteSpace(currentFocusElementContext))
            {
                return currentFocusElementContext;
            }

            if (string.IsNullOrWhiteSpace(recentFocusContext))
            {
                return null;
            }

            currentFocusElementContext = recentFocusContext;
            return currentFocusElementContext;
        }

        bool HasFreshPostActionUiEvidence()
            => recentUiTreeEvidenceMetadata is { IsPostActionSnapshot: true } metadata
               && metadata.SourceTurnId == turnId;

        void MaybeAddScriptedLookaheadInstruction()
        {
            if (!scriptedMode ||
                scriptedLookahead is null ||
                scriptedLookaheadInstructionAdded)
            {
                return;
            }

            if (!HasFreshPostActionUiEvidence())
            {
                DebugTrace.WriteStructuredEvent(
                    "agent.lookahead.fallback",
                    new Dictionary<string, object?>
                    {
                        ["sourceTurn"] = turnId,
                        ["targetTurn"] = scriptedLookahead.NextTurnId,
                        ["reason"] = "fresh_post_action_evidence_required",
                    });
                return;
            }

            scriptedLookaheadInstructionAdded = true;
            messages.Add(new AgentMessage.User(
                ScriptedLookahead.BuildInstruction(turnId, userText, scriptedLookahead)));
            DebugTrace.WriteStructuredEvent(
                "agent.lookahead.requested",
                new Dictionary<string, object?>
                {
                    ["sourceTurn"] = turnId,
                    ["targetTurn"] = scriptedLookahead.NextTurnId,
                    ["currentCommandPreview"] = DebugTrace.Preview(userText, 300),
                    ["nextCommandPreview"] = DebugTrace.Preview(scriptedLookahead.NextCommand, 300),
                    ["maxDepth"] = 1,
                    ["mode"] = "next_noop_only",
                });
        }

        ScriptedLookaheadDecision? TryParseScriptedLookaheadDecision(string responseText)
        {
            if (!scriptedLookaheadInstructionAdded ||
                scriptedLookahead is null ||
                scriptedLookaheadDecisionLogged)
            {
                return null;
            }

            scriptedLookaheadDecisionLogged = true;
            if (!ScriptedLookahead.TryParseDecision(
                    turnId,
                    scriptedLookahead.NextTurnId,
                    responseText,
                    out var decision,
                    out var skipReason))
            {
                DebugTrace.WriteStructuredEvent(
                    "agent.lookahead.fallback",
                    new Dictionary<string, object?>
                    {
                        ["sourceTurn"] = turnId,
                        ["targetTurn"] = scriptedLookahead.NextTurnId,
                        ["reason"] = skipReason,
                    });
                return null;
            }

            DebugTrace.WriteStructuredEvent(
                "agent.lookahead.decision",
                new Dictionary<string, object?>
                {
                    ["sourceTurn"] = turnId,
                    ["targetTurn"] = scriptedLookahead.NextTurnId,
                    ["currentTurnStatus"] = decision.CurrentTurnStatus,
                    ["nextTurnStatus"] = decision.NextTurnStatus,
                    ["currentTurnComplete"] = decision.CurrentTurnComplete,
                    ["nextTurnCompleteNoOp"] = decision.NextTurnCompleteNoOp,
                    ["reason"] = decision.Reason,
                });
            return decision;
        }

        void RememberRecentFocusSnapshot(
            string snapshotText,
            string sourceKind = "describe_window_focus",
            bool isPostActionSnapshot = false)
        {
            if (string.IsNullOrWhiteSpace(snapshotText))
            {
                return;
            }

            if (TryGetPrimaryWindowReference(snapshotText, out var windowHandle, out var windowTitle))
            {
                desktopSession.CurrentWindowHandle = windowHandle;
                desktopSession.CurrentWindowTitle = windowTitle;
            }

            recentFocusContext = snapshotText;
            recentFocusEvidenceMetadata = new DesktopEvidenceMetadata(
                turnId,
                sourceKind,
                DateTimeOffset.UtcNow,
                isPostActionSnapshot);
            currentFocusElementContext = GetCompactSnapshotModelContext(snapshotText);
            SyncDesktopSession();
        }

        void InvalidateRecentFocusSnapshot()
        {
            recentFocusContext = null;
            recentFocusEvidenceMetadata = null;
            currentFocusElementContext = null;
            SyncDesktopSession();
        }

        static Dictionary<string, object?> BuildWindowSnapshotArguments(bool debugMode)
            => CreateWindowSnapshotArguments(debugMode);

        static Dictionary<string, object?> BuildFocusSnapshotArguments(bool debugMode)
            => CreateFocusSnapshotArguments(debugMode);

        Dictionary<string, object?> PrepareDesktopToolArguments(
            string toolName,
            IReadOnlyDictionary<string, object?> args)
            => PrepareToolArgumentsForDesktopSession(toolName, args, desktopSession);

        async Task<ToolCallOutcome> CallToolWithDesktopSessionAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> args)
        {
            var preparedArgs = PrepareDesktopToolArguments(toolName, args);
            return await mcpManager.CallToolAsync(toolName, preparedArgs, cancellationToken);
        }

        string CreateInternalContinuationId(string policyName)
            => $"{turnId}:{llmAttempt}:{++internalContinuationSequence}:{policyName}";

        void WriteInternalContinuationEvent(
            string category,
            string continuationId,
            string policyName,
            string continuationKind,
            string triggerReason,
            string assistantResponseText,
            string? userIntentSummary,
            IReadOnlyList<string>? affordances = null,
            object? surfaceSummary = null,
            object? targetSummary = null,
            object? plannedSteps = null,
            int? stepIndex = null,
            string? stepAction = null,
            object? stepTargetSummary = null,
            string? result = null,
            string? skipReason = null,
            string? abortReason = null,
            string? preActionWindow = null,
            string? postActionWindow = null,
            bool? preflightRefreshPerformed = null,
            bool? preflightInvalidatedStoredEvidence = null,
            int? slotOrdinal = null,
            int? remainingCharacterCount = null,
            int? inputLength = null)
        {
            DebugTrace.WriteStructuredEvent(
                category,
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["continuationId"] = continuationId,
                    ["policyName"] = policyName,
                    ["continuationKind"] = continuationKind,
                    ["triggerReason"] = triggerReason,
                    ["userTextPreview"] = DebugTrace.Preview(userText, 500),
                    ["assistantReplyPreview"] = DebugTrace.Preview(assistantResponseText, 400),
                    ["userIntentSummary"] = userIntentSummary,
                    ["affordances"] = affordances,
                    ["surfaceSummary"] = surfaceSummary,
                    ["targetSummary"] = targetSummary,
                    ["plannedSteps"] = plannedSteps,
                    ["stepIndex"] = stepIndex,
                    ["stepAction"] = stepAction,
                    ["stepTargetSummary"] = stepTargetSummary,
                    ["result"] = result,
                    ["skipReason"] = skipReason,
                    ["abortReason"] = abortReason,
                    ["preActionWindow"] = preActionWindow,
                    ["postActionWindow"] = postActionWindow,
                    ["preflightRefreshPerformed"] = preflightRefreshPerformed,
                    ["preflightInvalidatedStoredEvidence"] = preflightInvalidatedStoredEvidence,
                    ["slotOrdinal"] = slotOrdinal,
                    ["remainingCharacterCount"] = remainingCharacterCount,
                    ["inputLength"] = inputLength,
                });
        }

        static string BuildAffordancePolicyName(string group, string affordance)
        {
            var normalizedGroup = NormalizeForNameMatching(group).Trim();
            if (string.IsNullOrWhiteSpace(normalizedGroup))
            {
                return affordance;
            }

            return normalizedGroup.Replace(' ', '_') + "_" + affordance;
        }

        bool TryResolveActiveSkillAffordance(
            string affordance,
            out AgentSkillPrompt skill,
            out string policyName,
            out string[] affordances)
        {
            foreach (var candidate in composedPrompt.ActiveSkills)
            {
                if (!candidate.Metadata.Affordances.Contains(affordance, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                skill = candidate;
                policyName = BuildAffordancePolicyName(candidate.Metadata.Group, affordance);
                affordances = candidate.Metadata.Affordances.ToArray();
                return true;
            }

            skill = default!;
            policyName = string.Empty;
            affordances = [];
            return false;
        }

        static object[]? BuildSingleStepPlan(string stepAction, object? stepTargetSummary)
            => stepTargetSummary is null
                ? null
                : [
                    new Dictionary<string, object?>
                    {
                        ["stepIndex"] = 1,
                        ["stepAction"] = stepAction,
                        ["stepTargetSummary"] = stepTargetSummary,
                    }
                ];

        async Task<string> CapturePostContinuationWindowSnapshotAsync(string actionSummary)
        {
            var postActionSnapshot = await CallToolWithDesktopSessionAsync(
                "describe_window",
                BuildWindowSnapshotArguments(debugMode: DebugTrace.IsEnabled));
            Display.ToolResult("describe_window", postActionSnapshot.Text, postActionSnapshot.Images.Count);
            RememberRecentWindowSnapshot(postActionSnapshot.Text, "describe_window", isPostActionSnapshot: true);
            if (!string.IsNullOrWhiteSpace(currentUiElementContext))
            {
                messages.Add(new AgentMessage.User(
                    $"Fresh post-action UI snapshot after internally {actionSummary}:\n{currentUiElementContext}"));
            }

            messages.Add(new AgentMessage.User(
                "Use the newest compact UI snapshot as the source of truth for the current screen. Any debug-only evidence attached to that snapshot is for inspection, not for the model's answer."));
            return postActionSnapshot.Text;
        }

        async Task<bool> MaybeContinueNamedChoiceAsync(string assistantResponseText)
        {
            if (!TryResolveActiveSkillAffordance(
                    "named_choice_continuation",
                    out _,
                    out var policyName,
                    out var affordances))
            {
                return false;
            }

            var continuationId = CreateInternalContinuationId(policyName);
            var actionableUiTreeContext = ResolveActionableUiTreeContext();
            var preActionWindow = DescribePrimaryWindowFromToolOutput(actionableUiTreeContext);
            var hasTarget = TryBuildVisibleNamedChoiceContinuation(
                userText,
                actionableUiTreeContext,
                out var targetPath,
                out var skipReason,
                out var surfaceSummary,
                out var targetSummary);
            var plannedSteps = BuildSingleStepPlan("invoke_window_element", targetSummary);

            WriteInternalContinuationEvent(
                "agent.internal_continuation_considered",
                continuationId,
                policyName,
                "select_visible_named_choice",
                "assistant_reply_stopped_before_named_choice_activation",
                assistantResponseText,
                "select_exact_visible_named_choice_from_user_text",
                affordances,
                surfaceSummary: surfaceSummary,
                targetSummary: targetSummary,
                plannedSteps: plannedSteps,
                preActionWindow: preActionWindow);

            if (attemptedNamedChoiceAutoFollowThrough)
            {
                WriteInternalContinuationEvent(
                    "agent.internal_continuation_skipped",
                    continuationId,
                    policyName,
                    "select_visible_named_choice",
                    "assistant_reply_stopped_before_named_choice_activation",
                    assistantResponseText,
                    "select_exact_visible_named_choice_from_user_text",
                    affordances,
                    surfaceSummary: surfaceSummary,
                    targetSummary: targetSummary,
                    plannedSteps: plannedSteps,
                    skipReason: "already_attempted_this_turn",
                    preActionWindow: preActionWindow);
                return false;
            }

            if (!hasTarget)
            {
                WriteInternalContinuationEvent(
                    "agent.internal_continuation_skipped",
                    continuationId,
                    policyName,
                    "select_visible_named_choice",
                    "assistant_reply_stopped_before_named_choice_activation",
                    assistantResponseText,
                    "select_exact_visible_named_choice_from_user_text",
                    affordances,
                    surfaceSummary: surfaceSummary,
                    targetSummary: targetSummary,
                    plannedSteps: plannedSteps,
                    skipReason: skipReason,
                    preActionWindow: preActionWindow);
                return false;
            }

            attemptedNamedChoiceAutoFollowThrough = true;
            WriteInternalContinuationEvent(
                "agent.internal_continuation_started",
                continuationId,
                policyName,
                "select_visible_named_choice",
                "assistant_reply_stopped_before_named_choice_activation",
                assistantResponseText,
                "select_exact_visible_named_choice_from_user_text",
                affordances,
                surfaceSummary: surfaceSummary,
                targetSummary: targetSummary,
                plannedSteps: plannedSteps,
                preActionWindow: preActionWindow);

            var invokeArgs = new Dictionary<string, object?> { ["elementPath"] = targetPath };
            var invokeArgsText = JsonSerializer.Serialize(invokeArgs, JsonSerializerOptionsCache.Default);
            Display.ToolCall("invoke_window_element", invokeArgsText);
            var invokeResult = await CallToolWithDesktopSessionAsync("invoke_window_element", invokeArgs);
            Display.ToolResult("invoke_window_element", invokeResult.Text, invokeResult.Images.Count);
            usedAnyTools = true;
            performedDesktopAction = true;
            lastCompletedToolNameInTurn = "invoke_window_element";
            if (!invokeResult.IsError)
            {
                RememberRecentWindowSnapshot(invokeResult.Text);
            }

            WriteInternalContinuationEvent(
                invokeResult.IsError
                    ? "agent.internal_continuation_aborted"
                    : "agent.internal_continuation_step_completed",
                continuationId,
                policyName,
                "select_visible_named_choice",
                "assistant_reply_stopped_before_named_choice_activation",
                assistantResponseText,
                "select_exact_visible_named_choice_from_user_text",
                affordances,
                surfaceSummary: surfaceSummary,
                targetSummary: targetSummary,
                plannedSteps: plannedSteps,
                stepIndex: 1,
                stepAction: "invoke_window_element",
                stepTargetSummary: targetSummary,
                result: DebugTrace.Preview(invokeResult.Text, 500),
                abortReason: invokeResult.IsError ? "invoke_window_element_failed" : null,
                preActionWindow: preActionWindow,
                postActionWindow: DescribePrimaryWindowFromToolOutput(invokeResult.Text));

            if (invokeResult.IsError)
            {
                return false;
            }

            messages.Add(new AgentMessage.Assistant(assistantResponseText));
            messages.Add(new AgentMessage.User(
                "Internal follow-through: the user made an explicit activation request, and the current UI still exposed one exact visible named target. That target was invoked internally because the prior draft stopped before completing the requested activation. Use the fresh post-action evidence below as the source of truth."));

            var postActionSnapshotText = await CapturePostContinuationWindowSnapshotAsync("invoking the requested named choice");
            WriteInternalContinuationEvent(
                "agent.internal_continuation_completed",
                continuationId,
                policyName,
                "select_visible_named_choice",
                "assistant_reply_stopped_before_named_choice_activation",
                assistantResponseText,
                "select_exact_visible_named_choice_from_user_text",
                affordances,
                surfaceSummary: surfaceSummary,
                targetSummary: targetSummary,
                plannedSteps: plannedSteps,
                preActionWindow: preActionWindow,
                postActionWindow: DescribePrimaryWindowFromToolOutput(postActionSnapshotText));

            return true;
        }

        async Task<bool> MaybeContinueDiscreteSlotTextEntryAsync(string assistantResponseText)
        {
            if (!TryResolveActiveSkillAffordance(
                    "discrete_slot_text_entry",
                    out _,
                    out var policyName,
                    out var affordances))
            {
                return false;
            }

            var continuationId = CreateInternalContinuationId(policyName);
            var actionableUiTreeContext = ResolveActionableUiTreeContext();
            var preActionWindow = DescribePrimaryWindowFromToolOutput(actionableUiTreeContext);
            var slotWindowVisible = SnapshotLooksLikeDiscreteSlotTextWindow(actionableUiTreeContext);
            var slotFocusVisible = SnapshotLooksLikeDiscreteSlotTextFocus(recentFocusContext);
            var slotOrdinalBeforeRefresh = TryExtractDiscreteSlotInputOrdinal(recentFocusContext, out var extractedSlotOrdinal)
                ? extractedSlotOrdinal
                : (int?)null;
            var initialSurfaceSummary = new Dictionary<string, object?>
            {
                ["discreteSlotPromptVisible"] = slotWindowVisible || slotFocusVisible,
                ["windowSurfaceVisible"] = slotWindowVisible,
                ["focusSurfaceVisible"] = slotFocusVisible,
                ["slotOrdinal"] = slotOrdinalBeforeRefresh,
                ["window"] = preActionWindow,
            };

            WriteInternalContinuationEvent(
                "agent.internal_continuation_considered",
                continuationId,
                policyName,
                "enter_remaining_discrete_text",
                "assistant_reply_stopped_before_discrete_text_completion",
                assistantResponseText,
                "enter_remaining_discrete_text_provided_by_user",
                affordances,
                surfaceSummary: initialSurfaceSummary,
                preActionWindow: preActionWindow,
                slotOrdinal: slotOrdinalBeforeRefresh);

            if (attemptedDiscreteSlotAutoFollowThrough)
            {
                WriteInternalContinuationEvent(
                    "agent.internal_continuation_skipped",
                    continuationId,
                    policyName,
                    "enter_remaining_discrete_text",
                    "assistant_reply_stopped_before_discrete_text_completion",
                    assistantResponseText,
                    "enter_remaining_discrete_text_provided_by_user",
                    affordances,
                    surfaceSummary: initialSurfaceSummary,
                    skipReason: "already_attempted_this_turn",
                    preActionWindow: preActionWindow,
                    slotOrdinal: slotOrdinalBeforeRefresh);
                return false;
            }

            var effectiveFocusContext = recentFocusContext;
            var preflightRefreshPerformed = false;
            var preflightInvalidatedStoredEvidence = false;
            if (ShouldRefreshDiscreteSlotFocusBeforeContinuation(actionableUiTreeContext, effectiveFocusContext) &&
                availableToolNames.Contains("describe_window_focus"))
            {
                preflightRefreshPerformed = true;
                var focusSnapshot = await CallToolWithDesktopSessionAsync(
                    "describe_window_focus",
                    BuildFocusSnapshotArguments(debugMode: DebugTrace.IsEnabled));
                Display.ToolResult("describe_window_focus", focusSnapshot.Text, focusSnapshot.Images.Count);
                if (focusSnapshot.IsError)
                {
                    DebugTrace.WriteStructuredEvent(
                        "agent.internal_continuation_preflight_focus_snapshot_failed",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["continuationId"] = continuationId,
                            ["policyName"] = policyName,
                            ["affordances"] = affordances,
                            ["assistantReplyPreview"] = DebugTrace.Preview(assistantResponseText, 400),
                            ["preActionWindow"] = preActionWindow,
                            ["error"] = DebugTrace.Preview(focusSnapshot.Text, 700),
                        });
                }
                else
                {
                    preflightInvalidatedStoredEvidence = !string.IsNullOrWhiteSpace(recentFocusContext);
                    RememberRecentFocusSnapshot(focusSnapshot.Text);
                    effectiveFocusContext = recentFocusContext;
                    DebugTrace.WriteStructuredEvent(
                        "agent.internal_continuation_preflight_focus_snapshot",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["continuationId"] = continuationId,
                            ["policyName"] = policyName,
                            ["affordances"] = affordances,
                            ["assistantReplyPreview"] = DebugTrace.Preview(assistantResponseText, 400),
                            ["preActionWindow"] = preActionWindow,
                            ["focusedElement"] = DescribeFocusedElementFromToolOutput(focusSnapshot.Text),
                            ["invalidatedStoredFocusContext"] = preflightInvalidatedStoredEvidence,
                            ["resultPreview"] = DebugTrace.Preview(focusSnapshot.Text, 700),
                        });
                }
            }

            var hasRemainingText = TryBuildDiscreteSlotTextContinuation(
                userText,
                actionableUiTreeContext,
                effectiveFocusContext,
                out var remainingText,
                out var skipReason,
                out var surfaceSummary);
            var slotOrdinal = surfaceSummary.TryGetValue("slotOrdinal", out var slotOrdinalValue) && slotOrdinalValue is int ordinalValue
                ? ordinalValue
                : surfaceSummary.TryGetValue("slotOrdinal", out slotOrdinalValue) && slotOrdinalValue is JsonElement ordinalElement && ordinalElement.TryGetInt32(out var ordinalFromJson)
                    ? ordinalFromJson
                    : (int?)null;
            var targetSummary = hasRemainingText
                ? new Dictionary<string, object?>
                {
                    ["inputKind"] = "sequential_discrete_text_slots",
                    ["remainingCharacterCount"] = remainingText.Length,
                    ["inputLength"] = remainingText.Length,
                }
                : null;
            var plannedSteps = BuildSingleStepPlan("type_window_text", targetSummary);

            if (!hasRemainingText)
            {
                WriteInternalContinuationEvent(
                    "agent.internal_continuation_skipped",
                    continuationId,
                    policyName,
                    "enter_remaining_discrete_text",
                    "assistant_reply_stopped_before_discrete_text_completion",
                    assistantResponseText,
                    "enter_remaining_discrete_text_provided_by_user",
                    affordances,
                    surfaceSummary: surfaceSummary,
                    targetSummary: targetSummary,
                    plannedSteps: plannedSteps,
                    skipReason: skipReason,
                    preActionWindow: preActionWindow,
                    preflightRefreshPerformed: preflightRefreshPerformed,
                    preflightInvalidatedStoredEvidence: preflightInvalidatedStoredEvidence,
                    slotOrdinal: slotOrdinal);
                return false;
            }

            attemptedDiscreteSlotAutoFollowThrough = true;
            WriteInternalContinuationEvent(
                "agent.internal_continuation_started",
                continuationId,
                policyName,
                "enter_remaining_discrete_text",
                "assistant_reply_stopped_before_discrete_text_completion",
                assistantResponseText,
                "enter_remaining_discrete_text_provided_by_user",
                affordances,
                surfaceSummary: surfaceSummary,
                targetSummary: targetSummary,
                plannedSteps: plannedSteps,
                preActionWindow: preActionWindow,
                preflightRefreshPerformed: preflightRefreshPerformed,
                preflightInvalidatedStoredEvidence: preflightInvalidatedStoredEvidence,
                slotOrdinal: slotOrdinal,
                remainingCharacterCount: remainingText.Length,
                inputLength: remainingText.Length);

            Display.ToolCall("type_window_text", """{"text":"[discrete text entry redacted]"}""");
            var entryResult = await ExecuteSequentialDiscreteTextEntryAsync(
                turnId,
                $"internal-discrete-text-{llmAttempt}",
                remainingText,
                mcpManager,
                desktopSession,
                cancellationToken);
            Display.ToolResult("type_window_text", entryResult.Text, entryResult.Images.Count);
            usedAnyTools = true;
            performedDesktopAction = true;
            lastCompletedToolNameInTurn = "type_window_text";

            WriteInternalContinuationEvent(
                entryResult.IsError
                    ? "agent.internal_continuation_aborted"
                    : "agent.internal_continuation_step_completed",
                continuationId,
                policyName,
                "enter_remaining_discrete_text",
                "assistant_reply_stopped_before_discrete_text_completion",
                assistantResponseText,
                "enter_remaining_discrete_text_provided_by_user",
                affordances,
                surfaceSummary: surfaceSummary,
                targetSummary: targetSummary,
                plannedSteps: plannedSteps,
                stepIndex: 1,
                stepAction: "type_window_text",
                stepTargetSummary: targetSummary,
                result: DebugTrace.Preview(entryResult.Text, 500),
                abortReason: entryResult.IsError ? "sequential_discrete_text_entry_failed" : null,
                preActionWindow: preActionWindow,
                preflightRefreshPerformed: preflightRefreshPerformed,
                preflightInvalidatedStoredEvidence: preflightInvalidatedStoredEvidence,
                slotOrdinal: slotOrdinal,
                remainingCharacterCount: remainingText.Length,
                inputLength: remainingText.Length);

            if (entryResult.IsError)
            {
                return false;
            }

            messages.Add(new AgentMessage.Assistant(assistantResponseText));
            messages.Add(new AgentMessage.User(
                "Internal follow-through: the user had already provided the remaining code value for a visible structured entry surface, and the prior draft stopped before finishing entry. The remaining characters were entered internally one at a time. Use the fresh post-action evidence below as the source of truth."));

            var postActionSnapshotText = await CapturePostContinuationWindowSnapshotAsync("entering the remaining structured text one character at a time");
            WriteInternalContinuationEvent(
                "agent.internal_continuation_completed",
                continuationId,
                policyName,
                "enter_remaining_discrete_text",
                "assistant_reply_stopped_before_discrete_text_completion",
                assistantResponseText,
                "enter_remaining_discrete_text_provided_by_user",
                affordances,
                surfaceSummary: surfaceSummary,
                targetSummary: targetSummary,
                plannedSteps: plannedSteps,
                preActionWindow: preActionWindow,
                postActionWindow: DescribePrimaryWindowFromToolOutput(postActionSnapshotText),
                preflightRefreshPerformed: preflightRefreshPerformed,
                preflightInvalidatedStoredEvidence: preflightInvalidatedStoredEvidence,
                slotOrdinal: slotOrdinal,
                remainingCharacterCount: remainingText.Length,
                inputLength: remainingText.Length);

            return true;
        }

        async Task RefreshCurrentWindowEvidenceBeforeInternalContinuationAsync(string assistantResponseText)
        {
            if (!performedDesktopAction ||
                !availableToolNames.Contains("describe_window"))
            {
                return;
            }

            var previousWindow = DescribePrimaryWindowFromToolOutput(ResolveActionableUiTreeContext());
            var hadStoredFocusContext = !string.IsNullOrWhiteSpace(recentFocusContext);

            try
            {
                var refreshedSnapshot = await CallToolWithDesktopSessionAsync(
                    "describe_window",
                    BuildWindowSnapshotArguments(debugMode: DebugTrace.IsEnabled));
                if (refreshedSnapshot.IsError)
                {
                    DebugTrace.WriteStructuredEvent(
                        "agent.internal_continuation_preflight_snapshot_failed",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["assistantReplyPreview"] = DebugTrace.Preview(assistantResponseText, 400),
                            ["lastCompletedTool"] = lastCompletedToolNameInTurn,
                            ["previousWindow"] = previousWindow,
                            ["error"] = DebugTrace.Preview(refreshedSnapshot.Text, 700),
                        });
                    return;
                }

                RememberRecentWindowSnapshot(refreshedSnapshot.Text, "describe_window");
                InvalidateRecentFocusSnapshot();
                DebugTrace.WriteStructuredEvent(
                    "agent.internal_continuation_preflight_snapshot",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["assistantReplyPreview"] = DebugTrace.Preview(assistantResponseText, 400),
                        ["lastCompletedTool"] = lastCompletedToolNameInTurn,
                        ["previousWindow"] = previousWindow,
                        ["refreshedWindow"] = DescribePrimaryWindowFromToolOutput(refreshedSnapshot.Text),
                        ["invalidatedStoredFocusContext"] = hadStoredFocusContext,
                        ["resultPreview"] = DebugTrace.Preview(refreshedSnapshot.Text, 700),
                    });
            }
            catch (Exception ex)
            {
                DebugTrace.WriteStructuredEvent(
                    "agent.internal_continuation_preflight_snapshot_failed",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["assistantReplyPreview"] = DebugTrace.Preview(assistantResponseText, 400),
                        ["lastCompletedTool"] = lastCompletedToolNameInTurn,
                        ["previousWindow"] = previousWindow,
                        ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                    });
            }
        }

        while (true)
        {
            llmAttempt += 1;
            DebugTrace.WriteLlmRequest(
                turnId,
                llmAttempt,
                llmClient.DisplayName,
                messages,
                tools,
                composedPrompt.SystemPrompt,
                composedPrompt.SourceDescription);
            var result = await llmClient.ChatAsync(messages, tools, composedPrompt.SystemPrompt, cancellationToken);
            DebugTrace.WriteLlmResponse(turnId, llmAttempt, llmClient.DisplayName, result);
            if (result.ToolCalls.Count == 0)
            {
                var responseText = result.Text ?? """{"say":"","log":"(no response)"}""";
                var parsedReply = AssistantResponseParser.Parse(responseText);
                var repairReason = GetRepairReason(responseText, parsedReply, usedAnyTools, performedDesktopAction);
                if (!string.IsNullOrWhiteSpace(repairReason))
                {
                    DebugTrace.WriteStructuredEvent(
                        "agent.reply_repair_requested",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["attempt"] = llmAttempt,
                            ["rule"] = repairReason,
                            ["usedAnyTools"] = usedAnyTools,
                            ["performedDesktopAction"] = performedDesktopAction,
                            ["rawPreview"] = DebugTrace.Preview(responseText, 600),
                        });
                    llmAttempt += 1;
                    var repairMessages = new List<AgentMessage>(messages)
                    {
                        new AgentMessage.Assistant(responseText),
                        new AgentMessage.User(BuildRepairInstruction(performedDesktopAction))
                    };
                    DebugTrace.WriteLlmRequest(
                        turnId,
                        llmAttempt,
                        $"{llmClient.DisplayName} repair",
                        repairMessages,
                        Array.Empty<ToolDefinition>(),
                        composedPrompt.SystemPrompt,
                        $"{composedPrompt.SourceDescription} (repair)");
                    var repairedReply = await llmClient.ChatAsync(
                        repairMessages,
                        [],
                        composedPrompt.SystemPrompt,
                        cancellationToken);
                    DebugTrace.WriteLlmResponse(turnId, llmAttempt, $"{llmClient.DisplayName} repair", repairedReply);
                    DebugTrace.WriteStructuredEvent(
                        "agent.reply_repair_completed",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["attempt"] = llmAttempt,
                            ["rule"] = repairReason,
                            ["returnedToolCalls"] = repairedReply.ToolCalls.Count,
                            ["rawPreview"] = DebugTrace.Preview(repairedReply.Text, 600),
                        });
                    if (repairedReply.ToolCalls.Count == 0 && !string.IsNullOrWhiteSpace(repairedReply.Text))
                    {
                        responseText = repairedReply.Text;
                        parsedReply = AssistantResponseParser.Parse(responseText);
                    }
                }

                var contradictionRule = GetReplyOutcomeContradictionRule(parsedReply);
                if (!string.IsNullOrWhiteSpace(contradictionRule))
                {
                    DebugTrace.WriteStructuredEvent(
                        "agent.reply_contradiction_detected",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["attempt"] = llmAttempt,
                            ["rule"] = contradictionRule,
                            ["sayPreview"] = DebugTrace.Preview(parsedReply.SpokenText, 400),
                            ["logPreview"] = DebugTrace.Preview(parsedReply.LogText, 900),
                        });
                }

                parsedReply = AlignReplyOutcomeConsistency(parsedReply);
                if (TryParseScriptedLookaheadDecision(responseText) is { } lookaheadDecision)
                {
                    parsedReply = parsedReply with { LookaheadDecision = lookaheadDecision };
                }

                if (performedDesktopAction
                    && additionalDesktopEvidenceAttempts < maxAdditionalDesktopEvidenceAttempts
                    && NeedsAdditionalDesktopEvidence(parsedReply))
                {
                    additionalDesktopEvidenceAttempts++;
                    DebugTrace.WriteStructuredEvent(
                        "agent.additional_desktop_evidence_requested",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["reason"] = "assistant_uncertain",
                            ["attempt"] = additionalDesktopEvidenceAttempts,
                            ["sayPreview"] = DebugTrace.Preview(parsedReply.SpokenText, 300),
                            ["logPreview"] = DebugTrace.Preview(parsedReply.LogText, 500),
                        });
                    messages.Add(new AgentMessage.Assistant(responseText));
                    messages.AddRange(await CollectAdditionalConfidenceEvidenceAsync(
                        turnId,
                        mcpManager,
                        desktopSession,
                        cancellationToken));
                    continue;
                }

                await RefreshCurrentWindowEvidenceBeforeInternalContinuationAsync(responseText);

                if (await MaybeContinueNamedChoiceAsync(responseText))
                {
                    continue;
                }

                if (await MaybeContinueDiscreteSlotTextEntryAsync(responseText))
                {
                    continue;
                }

                messages.Add(new AgentMessage.Assistant(responseText));
                return FinalizeTurnReply(parsedReply with { RawText = responseText });
            }

            messages.Add(new AgentMessage.Assistant(result.Text, result.ToolCalls));
            var followUpEvidence = new List<AgentMessage>();

            foreach (var toolCall in result.ToolCalls)
            {
                usedAnyTools = true;
                DebugTrace.WriteStructuredEvent(
                    "agent.tool_call_requested",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["toolCallId"] = toolCall.Id,
                        ["tool"] = toolCall.Name,
                        ["argumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600),
                    });
                object parsedArgs = new Dictionary<string, object?>();
                try
                {
                    parsedArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                     toolCall.Arguments,
                                     JsonSerializerOptionsCache.Default)
                                 ?? new Dictionary<string, object?>();
                }
                catch
                {
                    // Keep empty object.
                    DebugTrace.WriteEvent(
                        "agent.tool_call_argument_parse_fallback",
                        $"turn={turnId}, toolCallId={toolCall.Id}, tool={toolCall.Name}");
                }

                var parsedArgsDictionary = parsedArgs as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();

                var executableArgs = CloneArguments(parsedArgsDictionary);
                var effectiveArgumentsText = toolCall.Arguments;
                var executableToolName = toolCall.Name;
                string? toolTraceRewriteReason = null;
                string? argumentTraceRewriteReason = null;
                string? toolRewriteNote = null;
                string? browserSearchFieldReplacementPath = null;
                string? freshToolUiElementContext = null;
                string? freshToolFocusElementContext = null;
                var rewroteBrowserSearchFieldValueEntry = false;
                var attemptedBrowserFullscreenExit = false;
                var attemptedBrowserWindowPreflight = false;
                var attemptedBrowserSearchFieldTypingPrime = false;
                var requestedLaunchAppName = string.Equals(executableToolName, "launch_application", StringComparison.Ordinal)
                    ? TryGetStringArgument(executableArgs, "appName")
                    : null;

                async Task MaybeExitBrowserFullscreenAsync(string reason)
                {
                    if (attemptedBrowserFullscreenExit ||
                        !ShouldExitBrowserFullscreenBeforeBrowserShortcut(ResolveActionableUiTreeContext()))
                    {
                        return;
                    }

                    attemptedBrowserFullscreenExit = true;
                    var exitArgs = new Dictionary<string, object?>
                    {
                        ["key"] = "Escape",
                    };

                    try
                    {
                        var exitStopwatch = Stopwatch.StartNew();
                        var exitResult = await CallToolWithDesktopSessionAsync("press_window_key", exitArgs);
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_fullscreen_exit_completed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["elapsedMs"] = (int)Math.Round(exitStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                                ["isError"] = exitResult.IsError,
                                ["resultPreview"] = DebugTrace.Preview(exitResult.Text, 600),
                            });

                        if (!exitResult.IsError)
                        {
                            RememberRecentWindowSnapshot(exitResult.Text);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_fullscreen_exit_failed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                        });
                    }
                }

                async Task MaybeEnsureBrowserWindowBeforeBrowserNavigationAsync(string reason)
                {
                    if (attemptedBrowserWindowPreflight ||
                        !NeedsBrowserWindowPreflight(userText, executableToolName, executableArgs, recentWindowContext))
                    {
                        return;
                    }

                    attemptedBrowserWindowPreflight = true;

                    if (string.IsNullOrWhiteSpace(recentListWindowsOutput) &&
                        availableToolNames.Contains("list_windows"))
                    {
                        try
                        {
                            var listResult = await CallToolWithDesktopSessionAsync(
                                "list_windows",
                                new Dictionary<string, object?>());
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_window_preflight_list_windows",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["toolCallId"] = toolCall.Id,
                                    ["reason"] = reason,
                                    ["isError"] = listResult.IsError,
                                    ["resultPreview"] = DebugTrace.Preview(listResult.Text, 700),
                                });

                            if (!listResult.IsError)
                            {
                                RememberRecentWindowInventory(listResult.Text, "list_windows");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_window_preflight_list_windows_failed",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["toolCallId"] = toolCall.Id,
                                    ["reason"] = reason,
                                    ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                                });
                        }
                    }

                    if (availableToolNames.Contains("activate_window") &&
                        TryBuildBrowserSelectionArguments(recentListWindowsOutput, out var browserSelectionArgs))
                    {
                        try
                        {
                            var selectionResult = await CallToolWithDesktopSessionAsync(
                                "activate_window",
                                browserSelectionArgs);
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_window_preflight_activate_window",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["toolCallId"] = toolCall.Id,
                                    ["reason"] = reason,
                                    ["arguments"] = browserSelectionArgs,
                                    ["isError"] = selectionResult.IsError,
                                    ["selectedWindow"] = DescribePrimaryWindowFromToolOutput(selectionResult.Text),
                                    ["resultPreview"] = DebugTrace.Preview(selectionResult.Text, 700),
                                });

                            if (!selectionResult.IsError &&
                                DescribePrimaryWindowFromToolOutput(selectionResult.Text) is not null)
                            {
                                RememberRecentWindowSnapshot(selectionResult.Text);
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_window_preflight_activate_window_failed",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["toolCallId"] = toolCall.Id,
                                    ["reason"] = reason,
                                    ["arguments"] = browserSelectionArgs,
                                    ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                                });
                        }
                    }

                    if (!availableToolNames.Contains("launch_application"))
                    {
                        return;
                    }

                    try
                    {
                        var launchArgs = new Dictionary<string, object?>
                        {
                            ["appName"] = "Microsoft Edge",
                        };
                        var launchResult = await CallToolWithDesktopSessionAsync(
                            "launch_application",
                            launchArgs);
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_window_preflight_launch_browser",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["arguments"] = launchArgs,
                                ["isError"] = launchResult.IsError,
                                ["selectedWindow"] = DescribePrimaryWindowFromToolOutput(launchResult.Text),
                                ["resultPreview"] = DebugTrace.Preview(launchResult.Text, 700),
                            });

                        if (!launchResult.IsError &&
                            DescribePrimaryWindowFromToolOutput(launchResult.Text) is not null)
                        {
                            RememberRecentWindowSnapshot(launchResult.Text);
                        }

                        var followUpSelectionArgs = TryBuildLaunchFollowUpSelectionArguments(launchResult.Text);
                        if (followUpSelectionArgs is not null &&
                            availableToolNames.Contains("activate_window"))
                        {
                            var followUpResult = await CallToolWithDesktopSessionAsync(
                                "activate_window",
                                followUpSelectionArgs);
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_window_preflight_select_launched_browser",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["toolCallId"] = toolCall.Id,
                                    ["reason"] = reason,
                                    ["arguments"] = followUpSelectionArgs,
                                    ["isError"] = followUpResult.IsError,
                                    ["selectedWindow"] = DescribePrimaryWindowFromToolOutput(followUpResult.Text),
                                    ["resultPreview"] = DebugTrace.Preview(followUpResult.Text, 700),
                                });

                            if (!followUpResult.IsError &&
                                DescribePrimaryWindowFromToolOutput(followUpResult.Text) is not null)
                            {
                                RememberRecentWindowSnapshot(followUpResult.Text);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_window_preflight_launch_browser_failed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                            });
                    }
                }

                async Task MaybePrimeBrowserSearchFieldForReplacementTypingAsync(string reason)
                {
                    if (attemptedBrowserSearchFieldTypingPrime ||
                        string.IsNullOrWhiteSpace(browserSearchFieldReplacementPath))
                    {
                        return;
                    }

                    attemptedBrowserSearchFieldTypingPrime = true;

                    if (availableToolNames.Contains("focus_window_element"))
                    {
                        try
                        {
                            var focusArgs = new Dictionary<string, object?>
                            {
                                ["elementPath"] = browserSearchFieldReplacementPath,
                            };
                            var focusResult = await CallToolWithDesktopSessionAsync(
                                "focus_window_element",
                                focusArgs);
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_site_search_field_focus_completed",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["toolCallId"] = toolCall.Id,
                                    ["reason"] = reason,
                                    ["elementPath"] = browserSearchFieldReplacementPath,
                                    ["isError"] = focusResult.IsError,
                                    ["resultPreview"] = DebugTrace.Preview(focusResult.Text, 600),
                                });

                            if (!focusResult.IsError)
                            {
                                RememberRecentWindowSnapshot(focusResult.Text);

                                recentFocusContext = focusResult.Text;
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_site_search_field_focus_failed",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["toolCallId"] = toolCall.Id,
                                    ["reason"] = reason,
                                    ["elementPath"] = browserSearchFieldReplacementPath,
                                    ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                                });
                        }
                    }

                    try
                    {
                        var selectAllArgs = new Dictionary<string, object?>
                        {
                            ["key"] = "A",
                            ["modifiers"] = new[] { "Control" },
                        };
                        var selectAllResult = await CallToolWithDesktopSessionAsync(
                            "press_window_key",
                            selectAllArgs);
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_site_search_field_select_all_completed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["elementPath"] = browserSearchFieldReplacementPath,
                                ["isError"] = selectAllResult.IsError,
                                ["resultPreview"] = DebugTrace.Preview(selectAllResult.Text, 600),
                            });

                        if (!selectAllResult.IsError &&
                            DescribePrimaryWindowFromToolOutput(selectAllResult.Text) is not null)
                        {
                            RememberRecentWindowSnapshot(selectAllResult.Text);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_site_search_field_select_all_failed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["elementPath"] = browserSearchFieldReplacementPath,
                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                            });
                    }
                }

                async Task MaybeSubmitBrowserSearchFieldAfterTypingAsync(string reason)
                {
                    if (!rewroteBrowserSearchFieldValueEntry)
                    {
                        return;
                    }

                    try
                    {
                        var submitArgs = new Dictionary<string, object?>
                        {
                            ["key"] = "Enter",
                        };
                        var submitResult = await CallToolWithDesktopSessionAsync(
                            "press_window_key",
                            submitArgs);
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_site_search_field_submit_completed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["elementPath"] = browserSearchFieldReplacementPath,
                                ["isError"] = submitResult.IsError,
                                ["resultPreview"] = DebugTrace.Preview(submitResult.Text, 600),
                            });

                        if (!submitResult.IsError &&
                            DescribePrimaryWindowFromToolOutput(submitResult.Text) is not null)
                        {
                            RememberRecentWindowSnapshot(submitResult.Text);
                        }

                        await Task.Delay(1200, cancellationToken);
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_site_search_field_submit_wait_complete",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["waitMs"] = 1200,
                            });
                    }
                    catch (Exception ex)
                    {
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_site_search_field_submit_failed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["reason"] = reason,
                                ["elementPath"] = browserSearchFieldReplacementPath,
                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                            });
                    }
                }

                if (toolCall.Name == "activate_window" &&
                    string.IsNullOrWhiteSpace(recentListWindowsOutput) &&
                    availableToolNames.Contains("list_windows"))
                {
                    try
                    {
                        var listResult = await CallToolWithDesktopSessionAsync(
                            "list_windows",
                            new Dictionary<string, object?>());
                        DebugTrace.WriteStructuredEvent(
                            "agent.activate_window_preflight_list_windows",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["isError"] = listResult.IsError,
                                ["resultPreview"] = DebugTrace.Preview(listResult.Text, 700),
                            });

                        if (!listResult.IsError)
                        {
                            RememberRecentWindowInventory(listResult.Text, "list_windows");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugTrace.WriteStructuredEvent(
                            "agent.activate_window_preflight_list_windows_failed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                            });
                    }
                }

                if (toolCall.Name == "activate_window" &&
                    TryRewriteSelectWindowArguments(executableArgs, recentListWindowsOutput, out var rewrittenSelectArgs))
                {
                    executableArgs = rewrittenSelectArgs;
                    effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
                    argumentTraceRewriteReason = "recent_list_windows_handle_preferred";
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_arguments_rewritten",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["reason"] = "recent_list_windows_handle_preferred",
                            ["originalArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, effectiveArgumentsText, 600),
                        });
                }

                if (toolCall.Name == "activate_window" &&
                    TryRewriteSelectWindowForRequestedApp(
                        userText,
                        executableArgs,
                        recentListWindowsOutput,
                        availableToolNames.Contains("launch_application"),
                        out var rewrittenSelectToolName,
                        out var rewrittenRequestedAppArgs))
                {
                    executableToolName = rewrittenSelectToolName;
                    executableArgs = rewrittenRequestedAppArgs;
                    effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
                    var requestedAppRewriteReason = executableToolName == "launch_application"
                        ? "requested_app_launch_preferred_over_unrelated_window"
                        : "requested_app_window_match_preferred";
                    toolTraceRewriteReason = !string.Equals(executableToolName, toolCall.Name, StringComparison.Ordinal)
                        ? requestedAppRewriteReason
                        : toolTraceRewriteReason;
                    argumentTraceRewriteReason = requestedAppRewriteReason;
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_arguments_rewritten",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["reason"] = requestedAppRewriteReason,
                            ["originalArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.PreviewToolArguments(executableToolName, effectiveArgumentsText, 600),
                        });
                }

                var actionableUiTreeContext = ResolveActionableUiTreeContext();

                if (TryRewriteBrowserSearchControlAction(
                        userText,
                        toolCall.Name,
                        executableArgs,
                        actionableUiTreeContext,
                        out var rewrittenBrowserSearchArgs))
                {
                    executableArgs = rewrittenBrowserSearchArgs;
                    effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
                    argumentTraceRewriteReason = "browser_site_search_control_preferred";
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_arguments_rewritten",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["reason"] = "browser_site_search_control_preferred",
                            ["originalArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, effectiveArgumentsText, 600),
                        });
                }

                if (IsGenericNamedTargetRewriteTool(toolCall.Name))
                {
                    var namedTargetRewriteEvaluation = EvaluateGenericContainerActionToNamedTarget(
                        userText,
                        toolCall.Name,
                        executableArgs,
                        actionableUiTreeContext);
                    DebugTrace.WriteStructuredEvent(
                        "agent.named_target_rewrite_evaluated",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["userTextPreview"] = DebugTrace.Preview(userText, 500),
                            ["requestedPath"] = namedTargetRewriteEvaluation.RequestedPath,
                            ["requestedElementResolved"] = namedTargetRewriteEvaluation.RequestedElementResolved,
                            ["requestedElementSummary"] = namedTargetRewriteEvaluation.RequestedElementSummary,
                            ["requiredAction"] = namedTargetRewriteEvaluation.RequiredAction,
                            ["userRequestedActivation"] = namedTargetRewriteEvaluation.UserRequestedActivation,
                            ["snapshotContainsNamedChoiceSurface"] = namedTargetRewriteEvaluation.SnapshotContainsNamedChoiceSurface,
                            ["matchedPath"] = namedTargetRewriteEvaluation.MatchedPath,
                            ["matchedElementSummary"] = namedTargetRewriteEvaluation.MatchedElementSummary,
                            ["rewritten"] = namedTargetRewriteEvaluation.Rewritten,
                            ["skipReason"] = namedTargetRewriteEvaluation.SkipReason,
                        });
                    if (namedTargetRewriteEvaluation.Rewritten &&
                        namedTargetRewriteEvaluation.RewrittenArgs is not null)
                    {
                        executableArgs = namedTargetRewriteEvaluation.RewrittenArgs;
                        effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
                        DebugTrace.WriteStructuredEvent(
                            "agent.tool_call_arguments_rewritten",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["tool"] = toolCall.Name,
                                ["reason"] = "exact_named_visible_target_preferred",
                                ["originalArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600),
                                ["rewrittenArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, effectiveArgumentsText, 600),
                            });
                    }
                }

                if (TryRewriteBrowserSearchFieldValueEntryToTyping(
                        userText,
                        toolCall.Name,
                        executableArgs,
                        actionableUiTreeContext,
                        out var rewrittenBrowserSearchTypingArgs,
                        out var rewrittenBrowserSearchFieldPath))
                {
                    executableToolName = "type_window_text";
                    executableArgs = rewrittenBrowserSearchTypingArgs;
                    effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
                    browserSearchFieldReplacementPath = rewrittenBrowserSearchFieldPath;
                    rewroteBrowserSearchFieldValueEntry = true;
                    toolTraceRewriteReason = "browser_site_search_field_value_entry_rewrite";
                    argumentTraceRewriteReason = toolTraceRewriteReason;
                    toolRewriteNote =
                        "Internal site-search field fallback used real keyboard typing in the focused search field because some browser sites do not react reliably to direct value-setting alone.";
                    DebugTrace.WriteStructuredEvent(
                        "agent.browser_site_search_field_value_entry_rewritten",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["requestedTool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["elementPath"] = browserSearchFieldReplacementPath,
                            ["originalArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.PreviewToolArguments(executableToolName, effectiveArgumentsText, 600),
                        });
                }

                if (TryRewriteBrowserAddressBarActionToShortcut(
                        toolCall.Name,
                        executableArgs,
                        actionableUiTreeContext,
                        out var rewrittenBrowserAddressBarArgs))
                {
                    await MaybeExitBrowserFullscreenAsync("browser_address_bar_shortcut_rewrite");
                    executableToolName = "press_window_key";
                    executableArgs = rewrittenBrowserAddressBarArgs;
                    effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
                    toolTraceRewriteReason = "browser_address_bar_shortcut_rewrite";
                    argumentTraceRewriteReason = toolTraceRewriteReason;
                    toolRewriteNote =
                        "Internal browser address-bar fallback used Control+L instead of UI Automation element activation because browser chrome can be hidden or offscreen during fullscreen content.";
                    DebugTrace.WriteStructuredEvent(
                        "agent.browser_address_bar_action_rewritten",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["requestedTool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["originalArgumentsPreview"] = DebugTrace.PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.PreviewToolArguments(executableToolName, effectiveArgumentsText, 600),
                        });
                }

                var preparedExecutableArgs = PrepareDesktopToolArguments(executableToolName, executableArgs);
                var preparedArgumentsText = JsonSerializer.Serialize(preparedExecutableArgs, JsonSerializerOptionsCache.Default);
                if (!string.Equals(preparedArgumentsText, effectiveArgumentsText, StringComparison.Ordinal))
                {
                    var argumentInjectionReason = "desktop_session_window_handle_injected";
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_arguments_injected",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["reason"] = argumentInjectionReason,
                            ["originalArgumentsPreview"] = DebugTrace.PreviewToolArguments(executableToolName, effectiveArgumentsText, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.PreviewToolArguments(executableToolName, preparedArgumentsText, 600),
                        });
                    argumentTraceRewriteReason ??= argumentInjectionReason;
                    executableArgs = preparedExecutableArgs;
                    effectiveArgumentsText = preparedArgumentsText;
                }

                await MaybeEnsureBrowserWindowBeforeBrowserNavigationAsync("browser_navigation_preflight");

                if (string.Equals(executableToolName, "type_window_text", StringComparison.Ordinal) &&
                    LooksLikeUrl(TryGetStringArgument(executableArgs, "text") ?? string.Empty))
                {
                    await MaybeExitBrowserFullscreenAsync("browser_url_entry");
                }

                if (string.Equals(executableToolName, "type_window_text", StringComparison.Ordinal) &&
                    ShouldOpenNewTabBeforeBrowserUrlEntry(userText, executableArgs, recentWindowContext))
                {
                    var newTabArgs = new Dictionary<string, object?>
                    {
                        ["key"] = "T",
                        ["modifiers"] = new[] { "Control" },
                    };

                    try
                    {
                        var newTabStopwatch = Stopwatch.StartNew();
                        var newTabResult = await CallToolWithDesktopSessionAsync("press_window_key", newTabArgs);
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_new_tab_prime_completed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["elapsedMs"] = (int)Math.Round(newTabStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                                ["isError"] = newTabResult.IsError,
                                ["resultPreview"] = DebugTrace.Preview(newTabResult.Text, 600),
                            });

                        if (!newTabResult.IsError &&
                            DescribePrimaryWindowFromToolOutput(newTabResult.Text) is not null)
                        {
                            RememberRecentWindowSnapshot(newTabResult.Text);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_new_tab_prime_failed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                            });
                    }
                }

                if (string.Equals(executableToolName, "type_window_text", StringComparison.Ordinal) &&
                    ShouldPrimeBrowserAddressBarForUrlEntry(executableArgs, recentWindowContext, recentFocusContext))
                {
                    var primeArgs = new Dictionary<string, object?>
                    {
                        ["key"] = "L",
                        ["modifiers"] = new[] { "Control" },
                    };

                    try
                    {
                        var primeStopwatch = Stopwatch.StartNew();
                        var primeResult = await CallToolWithDesktopSessionAsync("press_window_key", primeArgs);
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_url_entry_prime_completed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["elapsedMs"] = (int)Math.Round(primeStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                                ["isError"] = primeResult.IsError,
                                ["resultPreview"] = DebugTrace.Preview(primeResult.Text, 600),
                            });

                        if (!primeResult.IsError &&
                            DescribePrimaryWindowFromToolOutput(primeResult.Text) is not null)
                        {
                            RememberRecentWindowSnapshot(primeResult.Text);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugTrace.WriteStructuredEvent(
                            "agent.browser_url_entry_prime_failed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                            });
                    }
                }

                if (ShouldBlockTaskbarSearchForBrowserContentQuery(
                        userText,
                        executableToolName,
                        recentWindowContext))
                {
                    Display.ToolCall(executableToolName, effectiveArgumentsText);
                    var blockedMessage =
                        "Blocked internal policy: the current request is about finding content within the already selected website in the browser. Do not use Windows/taskbar app search here. Stay in the current browser window and use the website's own search or navigation controls instead.";
                    Display.ToolResult(executableToolName, blockedMessage, 0);
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_blocked",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["reason"] = "browser_content_search_must_stay_in_browser",
                            ["resultPreview"] = DebugTrace.Preview(blockedMessage, 900),
                        });
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_completed",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["requestedTool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["rewriteReason"] = toolTraceRewriteReason,
                            ["argumentRewriteReason"] = argumentTraceRewriteReason,
                            ["mcpCallId"] = null,
                            ["elapsedMs"] = 0,
                            ["isError"] = true,
                            ["images"] = 0,
                            ["resultPreview"] = DebugTrace.Preview(blockedMessage, 900),
                        });
                    messages.Add(new AgentMessage.ToolResult(toolCall.Id, toolCall.Name, blockedMessage));
                    continue;
                }

                if (ShouldBlockProcessLaunchForBrowserRequest(
                        userText,
                        executableToolName,
                        recentWindowContext))
                {
                    Display.ToolCall(executableToolName, effectiveArgumentsText);
                    var blockedMessage =
                        "Blocked internal policy: this request is about browser navigation or in-browser content, so do not use process-manager to launch a Store link, URI, or other OS process. Use the browser flow instead: select or launch the browser, then use the address bar or the current website's own controls.";
                    Display.ToolResult(executableToolName, blockedMessage, 0);
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_blocked",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["reason"] = "browser_request_must_not_use_process_manager_launch",
                            ["resultPreview"] = DebugTrace.Preview(blockedMessage, 900),
                        });
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_completed",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["requestedTool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["rewriteReason"] = toolTraceRewriteReason,
                            ["argumentRewriteReason"] = argumentTraceRewriteReason,
                            ["mcpCallId"] = null,
                            ["elapsedMs"] = 0,
                            ["isError"] = true,
                            ["images"] = 0,
                            ["resultPreview"] = DebugTrace.Preview(blockedMessage, 900),
                        });
                    messages.Add(new AgentMessage.ToolResult(toolCall.Id, toolCall.Name, blockedMessage));
                    continue;
                }

                if (ShouldBlockUnrequestedNamedChoiceSurfaceAction(
                        userText,
                        executableToolName,
                        executableArgs,
                        actionableUiTreeContext,
                        out var blockedNamedChoiceSurfaceMessage))
                {
                    Display.ToolCall(executableToolName, effectiveArgumentsText);
                    Display.ToolResult(executableToolName, blockedNamedChoiceSurfaceMessage, 0);
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_blocked",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["reason"] = "unnamed_named_choice_surface_target_blocked",
                            ["resultPreview"] = DebugTrace.Preview(blockedNamedChoiceSurfaceMessage, 900),
                        });
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_completed",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["requestedTool"] = toolCall.Name,
                            ["executedTool"] = executableToolName,
                            ["rewriteReason"] = toolTraceRewriteReason,
                            ["argumentRewriteReason"] = argumentTraceRewriteReason,
                            ["mcpCallId"] = null,
                            ["elapsedMs"] = 0,
                            ["isError"] = true,
                            ["images"] = 0,
                            ["resultPreview"] = DebugTrace.Preview(blockedNamedChoiceSurfaceMessage, 900),
                        });
                    messages.Add(new AgentMessage.ToolResult(toolCall.Id, toolCall.Name, blockedNamedChoiceSurfaceMessage));
                    continue;
                }

                if (string.Equals(executableToolName, "type_window_text", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(browserSearchFieldReplacementPath))
                {
                    await MaybePrimeBrowserSearchFieldForReplacementTypingAsync("browser_site_search_field_typing");
                }

                var discreteSlotRewriteAllowed = TryResolveActiveSkillAffordance(
                    "discrete_slot_text_rewrite",
                    out _,
                    out var discreteSlotRewritePolicyName,
                    out var discreteSlotRewriteAffordances);
                var requestedDiscreteSlotText = TryGetStringArgument(executableArgs, "text");
                var discreteSlotWindowVisible = SnapshotLooksLikeDiscreteSlotTextWindow(recentWindowContext);
                var discreteSlotFocusVisible = SnapshotLooksLikeDiscreteSlotTextFocus(recentFocusContext);
                var discreteSlotSurfaceDetected = discreteSlotWindowVisible || discreteSlotFocusVisible;
                var discreteSlotOrdinal = TryExtractDiscreteSlotInputOrdinal(recentFocusContext, out var extractedDiscreteSlotOrdinal)
                    ? extractedDiscreteSlotOrdinal
                    : (int?)null;
                var discreteSlotInputLength = TryExtractMultiCharacterDiscreteSlotText(requestedDiscreteSlotText, out var structuredDiscreteSlotText)
                    ? structuredDiscreteSlotText.Length
                    : (!string.IsNullOrWhiteSpace(requestedDiscreteSlotText)
                        ? requestedDiscreteSlotText.Trim().Length
                        : 0);
                var useStructuredDiscreteSlotEntry = discreteSlotRewriteAllowed &&
                                                     TryExtractStructuredDiscreteSlotText(
                                                         executableToolName,
                                                         executableArgs,
                                                         recentWindowContext,
                                                         recentFocusContext,
                                                         out structuredDiscreteSlotText);
                var discreteSlotRewriteSkipReason = useStructuredDiscreteSlotEntry
                    ? null
                    : !discreteSlotRewriteAllowed
                        ? "affordance_not_enabled"
                        : !string.Equals(executableToolName, "type_window_text", StringComparison.Ordinal)
                            ? "unsupported_tool"
                            : !TryExtractMultiCharacterDiscreteSlotText(requestedDiscreteSlotText, out _)
                                ? "input_not_multi_character_discrete_text"
                                : !discreteSlotSurfaceDetected
                                    ? "discrete_slot_surface_not_visible"
                                    : "rewrite_not_applicable";
                DebugTrace.WriteStructuredEvent(
                    "agent.discrete_slot_text_rewrite_evaluated",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["toolCallId"] = toolCall.Id,
                        ["tool"] = toolCall.Name,
                        ["policyName"] = discreteSlotRewriteAllowed ? discreteSlotRewritePolicyName : null,
                        ["affordances"] = discreteSlotRewriteAllowed ? discreteSlotRewriteAffordances : null,
                        ["preActionWindow"] = DescribePrimaryWindowFromToolOutput(actionableUiTreeContext),
                        ["windowSurfaceVisible"] = discreteSlotWindowVisible,
                        ["focusSurfaceVisible"] = discreteSlotFocusVisible,
                        ["structuredSurfaceDetected"] = discreteSlotSurfaceDetected,
                        ["slotOrdinal"] = discreteSlotOrdinal,
                        ["inputLength"] = discreteSlotInputLength,
                        ["rewriteApplied"] = useStructuredDiscreteSlotEntry,
                        ["rewriteSkipReason"] = discreteSlotRewriteSkipReason,
                    });
                if (useStructuredDiscreteSlotEntry)
                {
                    toolTraceRewriteReason = "discrete_slot_text_entry";
                    toolRewriteNote =
                        "Internal structured text fallback entered the code one character at a time because the visible prompt uses separate single-character entry boxes.";
                    effectiveArgumentsText = """{"text":"[discrete slot text redacted]"}""";
                }

                var intermediateStep = ResolveToolStepNarration(
                    result.Text,
                    result.ToolCalls.Count,
                    executableToolName,
                    executableArgs,
                    lastCompletedToolNameInTurn);
                var intermediateStepNarrationTask = StartToolStepNarrationAsync(
                    turnId,
                    toolCall,
                    intermediateStep,
                    intermediateStepNarrator,
                    cancellationToken);
                Display.ToolCall(executableToolName, effectiveArgumentsText);

                var toolCallStopwatch = Stopwatch.StartNew();
                ToolCallOutcome toolOutput;
                try
                {
                    toolOutput = useStructuredDiscreteSlotEntry
                        ? await ExecuteSequentialDiscreteTextEntryAsync(
                            turnId,
                            toolCall.Id,
                            structuredDiscreteSlotText,
                            mcpManager,
                            desktopSession,
                            cancellationToken)
                        : await CallToolWithDesktopSessionAsync(executableToolName, executableArgs);
                }
                catch (Exception ex)
                {
                    toolOutput = new ToolCallOutcome($"Error: {ex.Message}", [], true);
                }
                await intermediateStepNarrationTask;

                Display.ToolResult(executableToolName, toolOutput.Text, toolOutput.Images.Count);
                DebugTrace.WriteStructuredEvent(
                    "agent.tool_call_completed",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["toolCallId"] = toolCall.Id,
                        ["tool"] = toolCall.Name,
                        ["requestedTool"] = toolCall.Name,
                        ["executedTool"] = executableToolName,
                        ["rewriteReason"] = toolTraceRewriteReason,
                        ["argumentRewriteReason"] = argumentTraceRewriteReason,
                        ["mcpCallId"] = toolOutput.McpCallId,
                        ["elapsedMs"] = (int)Math.Round(toolCallStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                        ["isError"] = toolOutput.IsError,
                        ["images"] = toolOutput.Images.Count,
                        ["resultPreview"] = DebugTrace.Preview(toolOutput.Text, 900),
                    });
                lastCompletedToolNameInTurn = executableToolName;

                if (!toolOutput.IsError)
                {
                    if (rewroteBrowserSearchFieldValueEntry &&
                        string.Equals(executableToolName, "type_window_text", StringComparison.Ordinal))
                    {
                        await MaybeSubmitBrowserSearchFieldAfterTypingAsync("browser_site_search_field_typing_submit");
                    }

                    if (toolCall.Name == "list_windows")
                    {
                        RememberRecentWindowInventory(toolOutput.Text, executableToolName);
                    }

                    if (string.Equals(executableToolName, "describe_window", StringComparison.Ordinal))
                    {
                        RememberRecentWindowSnapshot(toolOutput.Text, executableToolName);
                        freshToolUiElementContext = currentUiElementContext;
                    }
                    else if (string.Equals(executableToolName, "describe_window_focus", StringComparison.Ordinal))
                    {
                        RememberRecentFocusSnapshot(toolOutput.Text);
                        freshToolFocusElementContext = currentFocusElementContext;
                    }
                    else
                    {
                        RememberRecentWindowSnapshot(toolOutput.Text, executableToolName);
                        if (toolCall.Name == "focus_window_element")
                        {
                            recentFocusContext = toolOutput.Text;
                            recentFocusEvidenceMetadata = new DesktopEvidenceMetadata(
                                turnId,
                                executableToolName,
                                DateTimeOffset.UtcNow,
                                false);
                            currentFocusElementContext = GetCompactSnapshotModelContext(toolOutput.Text);
                            freshToolFocusElementContext = currentFocusElementContext;
                        }
                    }
                }

                if (toolOutput.Images.Count > 0)
                {
                    followUpEvidence.Add(new AgentMessage.VisualContext(
                        $"Supplemental screenshot output from tool \"{executableToolName}\". Treat these images as the source of truth for what is visibly on screen before answering.",
                        toolOutput.Images));
                }

                if (IsDesktopActionTool(executableToolName))
                {
                    performedDesktopAction = true;
                    DebugTrace.WriteStructuredEvent(
                        "agent.desktop_action_tool_detected",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["requestedTool"] = toolCall.Name,
                            ["tool"] = executableToolName,
                            ["executedTool"] = executableToolName,
                            ["rewriteReason"] = toolTraceRewriteReason,
                            ["mcpCallId"] = toolOutput.McpCallId,
                            ["toolIsError"] = toolOutput.IsError,
                            ["reportedWindow"] = DescribePrimaryWindowFromToolOutput(toolOutput.Text),
                        });
                    try
                    {
                        if (executableToolName == "launch_application")
                        {
                            var appName = TryGetStringArgument(executableArgs, "appName");
                            if (!string.IsNullOrWhiteSpace(appName))
                            {
                                var followUpSelectionArgs = TryBuildLaunchFollowUpSelectionArguments(toolOutput.Text);
                                if (followUpSelectionArgs is not null)
                                {
                                    try
                                    {
                                        var followUpSelectionStopwatch = Stopwatch.StartNew();
                                        var selectResult = await CallToolWithDesktopSessionAsync(
                                            "activate_window",
                                            followUpSelectionArgs);
                                        Display.ToolResult("activate_window", selectResult.Text, selectResult.Images.Count);
                                        if (!selectResult.IsError)
                                        {
                                            RememberRecentWindowSnapshot(selectResult.Text);
                                        }

                                        if (selectResult.Images.Count > 0)
                                        {
                                            followUpEvidence.Add(new AgentMessage.VisualContext(
                                                "Internal screenshot evidence after re-selecting the launched app window. Treat the screenshot as authoritative for the current visible screen.",
                                                selectResult.Images));
                                        }
                                        DebugTrace.WriteStructuredEvent(
                                            "agent.desktop_followup_activate_window",
                                            new Dictionary<string, object?>
                                            {
                                                ["turn"] = turnId,
                                                ["appName"] = appName,
                                                ["target"] = DescribeLaunchFollowUpSelectionTarget(toolOutput.Text) ?? appName,
                                                ["elapsedMs"] = (int)Math.Round(followUpSelectionStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                                                ["selectedWindow"] = DescribePrimaryWindowFromToolOutput(selectResult.Text),
                                                ["images"] = selectResult.Images.Count,
                                                ["resultPreview"] = DebugTrace.Preview(selectResult.Text, 600),
                                            });
                                    }
                                    catch (Exception ex)
                                    {
                                        followUpEvidence.Add(new AgentMessage.User(
                                            $"Attempt to re-select the launched app window for \"{appName}\" was unavailable: {ex.Message}"));
                                        DebugTrace.WriteStructuredEvent(
                                            "agent.desktop_followup_activate_window_failed",
                                            new Dictionary<string, object?>
                                            {
                                                ["turn"] = turnId,
                                                ["appName"] = appName,
                                                ["target"] = DescribeLaunchFollowUpSelectionTarget(toolOutput.Text) ?? appName,
                                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                                            });
                                    }
                                }
                                else
                                {
                                    followUpEvidence.Add(new AgentMessage.User(
                                        $"Internal window re-selection after launching \"{appName}\" was skipped because the launch tool did not surface a launched app window. Treat the launch as unconfirmed and rely on the fresh UI snapshot before deciding what to do next."));
                                    DebugTrace.WriteStructuredEvent(
                                        "agent.desktop_followup_activate_window_skipped",
                                        new Dictionary<string, object?>
                                        {
                                            ["turn"] = turnId,
                                            ["appName"] = appName,
                                            ["launchResultPreview"] = DebugTrace.Preview(toolOutput.Text, 700),
                                        });
                                }
                            }
                        }

                        var postActionSnapshotStopwatch = Stopwatch.StartNew();
                        var postActionSnapshot = await CallToolWithDesktopSessionAsync(
                            "describe_window",
                            BuildWindowSnapshotArguments(debugMode: DebugTrace.IsEnabled));
                        Display.ToolResult("describe_window", postActionSnapshot.Text, postActionSnapshot.Images.Count);
                        RememberRecentWindowSnapshot(postActionSnapshot.Text, "describe_window", isPostActionSnapshot: true);
                        freshToolUiElementContext = currentUiElementContext;
                        if (!string.IsNullOrWhiteSpace(currentUiElementContext))
                        {
                            followUpEvidence.Add(new AgentMessage.User(
                                $"Fresh post-action UI snapshot after tool \"{executableToolName}\" supersedes any older pre-action UI tree for the current screen. Use this newest snapshot as the source of truth before deciding what happened next:\n{currentUiElementContext}"));
                        }
                        DebugTrace.WriteStructuredEvent(
                            "agent.desktop_followup_snapshot",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["requestedTool"] = toolCall.Name,
                                ["tool"] = executableToolName,
                                ["executedTool"] = executableToolName,
                                ["rewriteReason"] = toolTraceRewriteReason,
                                ["actionMcpCallId"] = toolOutput.McpCallId,
                                ["mcpCallId"] = postActionSnapshot.McpCallId,
                                ["elapsedMs"] = (int)Math.Round(postActionSnapshotStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                                ["snapshotWindow"] = DescribePrimaryWindowFromToolOutput(postActionSnapshot.Text),
                                ["images"] = postActionSnapshot.Images.Count,
                                ["resultPreview"] = DebugTrace.Preview(postActionSnapshot.Text, 700),
                            });
                        DebugTrace.WriteStructuredEvent(
                            "agent.desktop_state_transition",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["toolCallId"] = toolCall.Id,
                                ["requestedTool"] = toolCall.Name,
                                ["tool"] = executableToolName,
                                ["executedTool"] = executableToolName,
                                ["rewriteReason"] = toolTraceRewriteReason,
                                ["actionMcpCallId"] = toolOutput.McpCallId,
                                ["snapshotMcpCallId"] = postActionSnapshot.McpCallId,
                                ["actionReportedWindow"] = DescribePrimaryWindowFromToolOutput(toolOutput.Text),
                                ["snapshotWindow"] = DescribePrimaryWindowFromToolOutput(postActionSnapshot.Text),
                                ["sourceOfTruth"] = "uia_tree",
                            });

                        if (!string.IsNullOrWhiteSpace(requestedLaunchAppName) &&
                            ShouldAskToFallbackToWebsite(
                                userText,
                                requestedLaunchAppName,
                                toolOutput.Text,
                                postActionSnapshot.Text,
                                composedPrompt.ActiveSkills))
                        {
                            forcedReply = BuildWebsiteFallbackConfirmationReply(requestedLaunchAppName);
                            DebugTrace.WriteStructuredEvent(
                                "agent.website_fallback_confirmation_required",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["tool"] = executableToolName,
                                    ["appName"] = requestedLaunchAppName,
                                    ["launchWindow"] = DescribePrimaryWindowFromToolOutput(toolOutput.Text),
                                    ["snapshotWindow"] = DescribePrimaryWindowFromToolOutput(postActionSnapshot.Text),
                                });
                        }

                        if (forcedReply is null &&
                            ShouldCollectFocusSnapshotAfterAction(executableToolName, executableArgs))
                        {
                            var focusSnapshotStopwatch = Stopwatch.StartNew();
                            var focusSnapshot = await CallToolWithDesktopSessionAsync(
                                "describe_window_focus",
                                BuildFocusSnapshotArguments(debugMode: DebugTrace.IsEnabled));
                            Display.ToolResult("describe_window_focus", focusSnapshot.Text, focusSnapshot.Images.Count);
                            RememberRecentFocusSnapshot(focusSnapshot.Text, isPostActionSnapshot: true);
                            freshToolFocusElementContext = currentFocusElementContext;
                            var compactFocusSnapshot = ResolveCurrentFocusElementContext() ?? focusSnapshot.Text;
                            followUpEvidence.Add(new AgentMessage.User(
                                $"Post-action focused element snapshot after tool \"{executableToolName}\":\n{compactFocusSnapshot}\nTreat this focused subtree as fresher evidence than any older focus assumptions before sending more navigation keys."));
                            var focusContinuationGuidance = BuildFocusedElementContinuationGuidance(
                                userText,
                                executableToolName,
                                focusSnapshot.Text);
                            if (!string.IsNullOrWhiteSpace(focusContinuationGuidance))
                            {
                                followUpEvidence.Add(new AgentMessage.User(focusContinuationGuidance));
                            }
                            DebugTrace.WriteStructuredEvent(
                                "agent.desktop_followup_focus_snapshot",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["toolCallId"] = toolCall.Id,
                                    ["requestedTool"] = toolCall.Name,
                                    ["tool"] = executableToolName,
                                    ["executedTool"] = executableToolName,
                                    ["rewriteReason"] = toolTraceRewriteReason,
                                    ["actionMcpCallId"] = toolOutput.McpCallId,
                                    ["mcpCallId"] = focusSnapshot.McpCallId,
                                    ["elapsedMs"] = (int)Math.Round(focusSnapshotStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                                    ["images"] = focusSnapshot.Images.Count,
                                    ["resultPreview"] = DebugTrace.Preview(focusSnapshot.Text, 700),
                                });
                        }

                        if (!string.IsNullOrWhiteSpace(toolRewriteNote))
                        {
                            followUpEvidence.Add(new AgentMessage.User(toolRewriteNote));
                        }

                        var toolSpecificGuidance = BuildToolSpecificGuidance(executableToolName, toolOutput.Text, executableArgs);
                        if (!string.IsNullOrWhiteSpace(toolSpecificGuidance))
                        {
                            followUpEvidence.Add(new AgentMessage.User(toolSpecificGuidance));
                        }
                    }
                    catch (Exception ex)
                    {
                        followUpEvidence.Add(new AgentMessage.User(
                            $"Post-action UI snapshot was unavailable after tool \"{toolCall.Name}\": {ex.Message}"));
                        DebugTrace.WriteStructuredEvent(
                            "agent.desktop_followup_snapshot_failed",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["tool"] = toolCall.Name,
                                ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                            });
                    }
                }

                if (forcedReply is not null)
                {
                    break;
                }

                var toolResultContext = ResolveToolResultContextForModel(
                    executableToolName,
                    toolOutput.Text,
                    toolOutput.IsError,
                    recentWindowInventoryModelContext,
                    freshToolUiElementContext
                        ?? (string.Equals(executableToolName, "capture_window_screenshot", StringComparison.Ordinal)
                            ? ResolveCurrentUiElementContext()
                            : null),
                    freshToolFocusElementContext ?? ResolveCurrentFocusElementContext(),
                    llmClient.ModelProfile);
                messages.Add(new AgentMessage.ToolResult(toolCall.Id, toolCall.Name, toolResultContext, toolOutput.Images));
            }

            messages.AddRange(followUpEvidence);
            if (forcedReply is not null)
            {
                messages.Add(new AgentMessage.Assistant(forcedReply.RawText));
                return FinalizeTurnReply(forcedReply);
            }
            MaybeAddScriptedLookaheadInstruction();
        }
    }

    internal static bool IsDesktopActionTool(string toolName)
        => toolName is "launch_application"
            or "activate_taskbar_app"
            or "activate_window"
            or "click_window_element"
            or "focus_window_element"
            or "invoke_window_element"
            or "invoke_window_main_menu_item"
            or "invoke_window_context_menu_item"
            or "press_window_key"
            or "type_window_text"
            or "set_window_element_text";

    internal static bool ShouldCaptureScreenshotAfterAction(
        string? preActionSnapshotText,
        string? postActionSnapshotText)
    {
        if (!SnapshotContainsElementTree(postActionSnapshotText))
        {
            return true;
        }

        if (!SnapshotContainsElementTree(preActionSnapshotText))
        {
            return false;
        }

        return SnapshotsShareSameWindowAndElementTree(preActionSnapshotText!, postActionSnapshotText!);
    }

    internal static string? BuildOrdinalActionReferenceSummary(
        string userText,
        IReadOnlyList<AgentMessage> history)
    {
        var ordinalIndex = TryResolveOrdinalActionIndex(userText);
        if (ordinalIndex is null)
        {
            return null;
        }

        var likelyNextActions = FindMostRecentLikelyNextActions(history);
        if (likelyNextActions.Count == 0)
        {
            return "If the user refers to a first, second, or third action but you did not previously give an explicit likely-next-actions list, ask for clarification instead of inventing a new ordering from the UI tree.";
        }

        if (ordinalIndex.Value >= likelyNextActions.Count)
        {
            return $"The user's ordinal reference exceeds the last explicit likely-next-actions list, which was: {FormatNumberedActions(likelyNextActions)}. Ask for clarification instead of inventing a new ordering from the UI tree.";
        }

        return $"The user's ordinal reference should resolve against the last explicit likely-next-actions list: {FormatNumberedActions(likelyNextActions)}. So \"{GetOrdinalWord(ordinalIndex.Value)} action\" means: {likelyNextActions[ordinalIndex.Value]}. Use that mapping unless the user explicitly overrides it.";
    }

    internal static bool ShouldCollectFocusSnapshotAfterAction(
        string toolName,
        IReadOnlyDictionary<string, object?> args)
    {
        if (!string.Equals(toolName, "press_window_key", StringComparison.Ordinal))
        {
            return false;
        }

        var key = TryGetStringArgument(args, "key");
        return key is not null && key.Trim() is "Tab" or "Left" or "Right" or "Up" or "Down";
    }

    internal static ToolStepNarrationPlan? ResolveToolStepNarration(
        string? assistantContent,
        int toolCallCount,
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? previousToolName = null)
    {
        if (IsSilentInspectionTool(toolName))
        {
            return null;
        }

        if (toolCallCount == 1 &&
            TryExtractToolStepNarrationFromAssistantContent(assistantContent) is { } assistantNarration)
        {
            return new ToolStepNarrationPlan(assistantNarration, "assistant_content");
        }

        var narrationPlan = BuildToolStepNarration(toolName, args) is { } fallbackNarration
            ? new ToolStepNarrationPlan(fallbackNarration, "tool_fallback")
            : null;

        return ShouldSuppressSequentialEvidenceNarration(previousToolName, narrationPlan, toolName)
            ? null
            : narrationPlan;
    }

    internal static string? TryExtractToolStepNarrationFromAssistantContent(string? assistantContent)
    {
        if (string.IsNullOrWhiteSpace(assistantContent))
        {
            return null;
        }

        var parsedReply = AssistantResponseParser.Parse(assistantContent);
        return string.IsNullOrWhiteSpace(parsedReply.SpokenText)
            ? null
            : parsedReply.SpokenText.Trim();
    }

    private static Task StartToolStepNarrationAsync(
        long turnId,
        ToolCallRequest toolCall,
        ToolStepNarrationPlan? narrationPlan,
        Func<string, CancellationToken, Task>? intermediateStepNarrator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(narrationPlan?.Text))
        {
            return Task.CompletedTask;
        }

        Display.IntermediateStep(narrationPlan.Text);
        DebugTrace.WriteStructuredEvent(
            "agent.tool_step_narration",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["toolCallId"] = toolCall.Id,
                ["tool"] = toolCall.Name,
                ["source"] = narrationPlan.Source,
                ["stepPreview"] = DebugTrace.Preview(narrationPlan.Text, 240),
            });

        if (intermediateStepNarrator is null)
        {
            return Task.CompletedTask;
        }

        return NarrateAsync();

        async Task NarrateAsync()
        {
            try
            {
                await intermediateStepNarrator(narrationPlan.Text, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Display.Warn($"Step speech failed: {ex.Message}");
                DebugTrace.WriteEvent(
                    "agent.tool_step_narration_failed",
                    $"turn={turnId}, toolCallId={toolCall.Id}, tool={toolCall.Name}, error={DebugTrace.Preview(ex.ToString(), 700)}");
            }
        }
    }

    private static bool ShouldSuppressSequentialEvidenceNarration(
        string? previousToolName,
        ToolStepNarrationPlan? narrationPlan,
        string currentToolName)
    {
        return narrationPlan is not null
               && string.Equals(narrationPlan.Source, "tool_fallback", StringComparison.Ordinal)
               && IsWindowEvidenceTool(previousToolName)
               && IsWindowEvidenceTool(currentToolName);
    }

    internal static string? BuildToolStepNarration(
        string toolName,
        IReadOnlyDictionary<string, object?> args)
    {
        return toolName switch
        {
            "activate_window" => BuildSelectWindowNarration(args),
            "activate_taskbar_app" => BuildTaskbarSelectionNarration(args),
            "launch_application" => BuildTaskbarSearchLaunchNarration(args),
            "invoke_window_main_menu_item" => "Okay, I'm trying that menu option.",
            "invoke_window_context_menu_item" => "Okay, I'm trying that option.",
            "click_window_element" => "Okay, I'm clicking that.",
            "focus_window_element" => BuildElementFocusNarration(args),
            "invoke_window_element" => BuildElementInvokeNarration(args),
            "set_window_element_text" => "Okay, I'm typing that in.",
            "press_window_key" => BuildSendInputNarration(args),
            "type_window_text" => BuildSendInputNarration(args),
            _ => null
        };
    }

    private static bool IsSilentInspectionTool(string toolName)
        => toolName is "list_windows"
            or "describe_window"
            or "describe_window_focus"
            or "capture_window_screenshot"
            or "list_taskbar_items"
            or "list_window_main_menu_items"
            or "list_window_context_menu_items";

    private static bool IsWindowEvidenceTool(string? toolName)
        => toolName is "describe_window"
            or "capture_window_screenshot";

    internal static string? BuildToolSpecificGuidance(
        string toolName,
        string toolOutputText,
        IReadOnlyDictionary<string, object?> args)
    {
        if (IsLaunchAttemptWithoutSelectedWindow(toolName, toolOutputText))
        {
            return toolName switch
            {
                "activate_taskbar_app" =>
                    "The taskbar app activation did not surface a launched or selected app window. Do not imply that the app opened successfully, and do not treat the unchanged current window as the requested app just because it is still visible. Use fresh evidence before deciding what happened, and if another materially different launch route is available, prefer that over repeating the same route.",
                "launch_application" =>
                    "The taskbar search launch did not surface a launched app window. Do not imply that the app opened successfully, do not assume a same-title app window exists just because Search shows a matching result, and do not treat the unchanged current window as the requested app just because it is still visible. Use fresh evidence before deciding what happened, and if another materially different launch route is available, prefer that next.",
                _ => null
            };
        }

        if (toolName is "click_window_element" or "invoke_window_element")
        {
            return "Treat this direct UI activation as unconfirmed until the freshest post-action snapshot or screenshot shows the requested screen change. A screenshot from before the click does not verify the post-click state, and if the same picker, dialog, or page is still visible after the action, continue from that fresh evidence instead of claiming success.";
        }

        if (toolName == "set_window_element_text")
        {
            return "Treat this direct field entry as unconfirmed until the freshest post-action snapshot or screenshot shows the intended text or the resulting screen change. If the field still looks empty or unchanged after entry, retry with a materially different method or report that the entry is not yet confirmed.";
        }

        if (!IsWindowInputTool(toolName))
        {
            return null;
        }

        var key = TryGetStringArgument(args, "key");
        var hasNavigationKey = key is not null && key.Trim() is "Tab" or "Left" or "Right" or "Up" or "Down";
        if (!hasNavigationKey)
        {
            return null;
        }

        if (args.TryGetValue("modifiers", out var modifiersValue) &&
            modifiersValue is JsonElement modifiersElement &&
            modifiersElement.ValueKind == JsonValueKind.Array &&
            modifiersElement.GetArrayLength() > 0)
        {
            return null;
        }

        return "Standalone navigation keys are a weaker fallback for visible control activation. Refresh focus or window state before sending more navigation keys, and prefer a direct tool-supported target when one is available.";
    }

    internal static string? BuildFocusedElementContinuationGuidance(
        string userText,
        string toolName,
        string focusSnapshotText)
    {
        if (toolName != "press_window_key" && toolName != "focus_window_element")
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(userText) ||
            string.IsNullOrWhiteSpace(focusSnapshotText) ||
            !UserRequestLooksLikeActivation(userText) ||
            UserRequestExplicitlyAsksForFocusOnly(userText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(focusSnapshotText);
            if (!TryGetFocusedSnapshotElement(document.RootElement, out var focusedElement) ||
                !ElementLooksLikePreciseActionTarget(focusedElement))
            {
                return null;
            }

            var name = TryGetJsonStringProperty(focusedElement, "name");
            if (string.IsNullOrWhiteSpace(name) ||
                !UserTextMentionsElementName(userText, name) ||
                !ElementHasAction(focusedElement, "invoke"))
            {
                return null;
            }

            return $"Fresh focus evidence shows the exact requested target \"{name}\" is now focused and supports invoke. Focus alone does not complete the user's action request. Invoke the focused target now instead of stopping at focus.";
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLaunchAttemptWithoutSelectedWindow(string toolName, string toolOutputText)
    {
        if (toolName is not "activate_taskbar_app" and not "launch_application")
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(toolOutputText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(toolOutputText);
            if (!TryGetJsonProperty(document.RootElement, "selectedWindow", out var selectedWindow))
            {
                return false;
            }

            return selectedWindow.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSelectWindowNarration(IReadOnlyDictionary<string, object?> args)
    {
        var title = TryGetShortSpeechLabel(TryGetStringArgument(args, "titleContains"));
        return title is not null
            ? $"Okay, I'm switching to {title}."
            : "Okay, I'm switching over.";
    }

    private static string BuildTaskbarSelectionNarration(IReadOnlyDictionary<string, object?> args)
    {
        var title = TryGetShortSpeechLabel(TryGetStringArgument(args, "titleContains"));
        return title is not null
            ? $"Okay, I'm opening {title}."
            : "Okay, I'm opening it.";
    }

    private static string BuildTaskbarSearchLaunchNarration(IReadOnlyDictionary<string, object?> args)
    {
        var appName = TryGetShortSpeechLabel(TryGetStringArgument(args, "appName"));
        return appName is not null
            ? $"Okay, let me open {appName}."
            : "Okay, let me open that.";
    }

    private static string BuildElementFocusNarration(IReadOnlyDictionary<string, object?> args)
    {
        var elementPath = TryGetStringArgument(args, "elementPath");
        return string.Equals(elementPath, "root", StringComparison.OrdinalIgnoreCase)
            ? "Okay, I'm bringing that back into focus."
            : "Okay, I'm focusing that.";
    }

    private static string BuildElementInvokeNarration(IReadOnlyDictionary<string, object?> args)
    {
        var elementPath = TryGetStringArgument(args, "elementPath");
        return string.Equals(elementPath, "root", StringComparison.OrdinalIgnoreCase)
            ? "Okay, I'm trying that now."
            : "Okay, I'm trying that.";
    }

    private static string BuildSendInputNarration(IReadOnlyDictionary<string, object?> args)
    {
        var text = TryGetStringArgument(args, "text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return LooksLikeUrl(text)
                ? "Okay, I'm putting the site in."
                : "Okay, I'm typing that in.";
        }

        var key = TryGetStringArgument(args, "key");
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Okay, one second.";
        }

        var modifiers = GetModifierNames(args);
        if (modifiers.Count > 0)
        {
            var shortcutParts = modifiers
                .Select(NormalizeModifierLabel)
                .Append(NormalizeKeyLabel(key));
            return $"Okay, I'm pressing {string.Join(" plus ", shortcutParts)}.";
        }

        return key.Trim() switch
        {
            "Tab" => "Okay, moving to the next field.",
            "Left" => "Okay, moving left.",
            "Right" => "Okay, moving right.",
            "Up" => "Okay, moving up.",
            "Down" => "Okay, moving down.",
            _ => $"Okay, I'm pressing {NormalizeKeyLabel(key)}."
        };
    }

    private static IReadOnlyList<string> GetModifierNames(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("modifiers", out var modifiersValue) || modifiersValue is null)
        {
            return [];
        }

        return modifiersValue switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.Array
                => element.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .ToArray(),
            IEnumerable<string> strings => strings.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            IEnumerable<object?> objects => objects
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray(),
            _ => []
        };
    }

    private static string NormalizeModifierLabel(string modifier)
    {
        var normalized = modifier.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "control" or "ctrl" => "Control",
            "alt" => "Alt",
            "shift" => "Shift",
            "windows" or "win" or "meta" => "Windows",
            _ => normalized
        };
    }

    private static string NormalizeKeyLabel(string key)
    {
        var normalized = key.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "pgup" => "Page Up",
            "pgdn" => "Page Down",
            _ when normalized.Length == 1 => normalized.ToUpperInvariant(),
            _ => normalized
        };
    }

    private static string? TryGetShortSpeechLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = Regex.Replace(text, @"\s+", " ").Trim().Trim('"', '\'');
        if (normalized.Length is 0 or > 32)
        {
            return null;
        }

        return normalized;
    }

    private static bool LooksLikeUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"(?:https?://|www\.)|(?:\b[a-z0-9-]+\.[a-z]{2,}\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static int? TryResolveOrdinalActionIndex(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.ToLowerInvariant();
        return normalized switch
        {
            _ when Regex.IsMatch(normalized, @"\bfirst\s+(action|option|one)\b") => 0,
            _ when Regex.IsMatch(normalized, @"\bsecond\s+(action|option|one)\b") => 1,
            _ when Regex.IsMatch(normalized, @"\bthird\s+(action|option|one)\b") => 2,
            _ => null
        };
    }

    internal static IReadOnlyList<string> FindMostRecentLikelyNextActions(IReadOnlyList<AgentMessage> history)
    {
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i] is not AgentMessage.Assistant { ToolCalls: null } assistant ||
                string.IsNullOrWhiteSpace(assistant.Content))
            {
                continue;
            }

            var parsed = AssistantResponseParser.Parse(assistant.Content);
            var actions = ExtractLikelyNextActions(parsed.SpokenText);
            if (actions.Count > 0)
            {
                return actions;
            }

            actions = ExtractLikelyNextActions(parsed.LogText);
            if (actions.Count > 0)
            {
                return actions;
            }
        }

        return [];
    }

    internal static IReadOnlyList<string> ExtractLikelyNextActions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var match = Regex.Match(
            text,
            @"Likely next actions:\s*(.+?)(?:$|\r?\n)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return [];
        }

        var listText = match.Groups[1].Value.Trim();
        listText = listText.TrimEnd('.', '!', '?');
        if (listText.Length == 0)
        {
            return [];
        }

        listText = Regex.Replace(listText, @"\s*,\s*or\s+", "|", RegexOptions.IgnoreCase);
        listText = Regex.Replace(listText, @"\s+or\s+", "|", RegexOptions.IgnoreCase);
        listText = Regex.Replace(listText, @"\s*,\s*", "|");

        return listText
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(action => action.Trim())
            .Where(action => action.Length > 0)
            .ToArray();
    }

    private static string FormatNumberedActions(IReadOnlyList<string> actions)
    {
        return string.Join(" ", actions.Select((action, index) => $"{index + 1}) {action}."));
    }

    private static string GetOrdinalWord(int zeroBasedIndex)
    {
        return zeroBasedIndex switch
        {
            0 => "first",
            1 => "second",
            2 => "third",
            _ => $"{zeroBasedIndex + 1}th"
        };
    }

    private static bool NeedsRepair(string rawText, AgentReply reply, bool usedAnyTools, bool performedDesktopAction)
        => GetRepairReason(rawText, reply, usedAnyTools, performedDesktopAction) is not null;

    private static string? GetRepairReason(string rawText, AgentReply reply, bool usedAnyTools, bool performedDesktopAction)
    {
        var isStructured = AssistantResponseParser.IsStructuredJson(rawText);
        if (!isStructured)
        {
            return "non_structured_reply";
        }

        if (performedDesktopAction && string.IsNullOrWhiteSpace(reply.SpokenText))
        {
            return "missing_spoken_reply_after_desktop_action";
        }

        if (performedDesktopAction && GetReplyOutcomeContradictionRule(reply) is not null)
        {
            return "say_log_outcome_contradiction";
        }

        if (!performedDesktopAction && HasDeferredActionPromise(reply.SpokenText))
        {
            return "deferred_action_promise_without_desktop_action";
        }

        return usedAnyTools && string.IsNullOrWhiteSpace(reply.LogText)
            ? "missing_log_after_tool_use"
            : null;
    }

    internal static AgentReply AlignReplyOutcomeConsistency(AgentReply reply)
    {
        if (GetReplyOutcomeContradictionRule(reply) is null)
        {
            return reply;
        }

        var normalizedSay = AssistantResponseParser.BuildSpeechFallback(reply.LogText);
        return string.IsNullOrWhiteSpace(normalizedSay)
            ? reply
            : reply with { SpokenText = normalizedSay };
    }

    internal static string? GetReplyOutcomeContradictionRule(AgentReply reply)
    {
        if (string.IsNullOrWhiteSpace(reply.LogText) ||
            string.IsNullOrWhiteSpace(reply.SpokenText))
        {
            return null;
        }

        return HasExplicitlyUnresolvedOutcome(reply.LogText) &&
               !HasExplicitlyUnresolvedOutcome(reply.SpokenText)
            ? "log_unresolved_but_say_resolved"
            : null;
    }

    internal static bool NeedsAdditionalDesktopEvidence(AgentReply reply)
    {
        var combined = $"{reply.SpokenText}\n{reply.LogText}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return false;
        }

        return combined.Contains("not confident", StringComparison.Ordinal)
            || combined.Contains("cannot be described confidently", StringComparison.Ordinal)
            || combined.Contains("can't be described confidently", StringComparison.Ordinal)
            || combined.Contains("cannot describe", StringComparison.Ordinal)
            || combined.Contains("cannot confirm", StringComparison.Ordinal)
            || combined.Contains("can't confirm", StringComparison.Ordinal)
            || Regex.IsMatch(combined, @"\buncertain\b", RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                combined,
                @"\buncertainty\b.{0,24}\b(?:is|remains|stays)\s+(?:high|elevated|significant|substantial)\b",
                RegexOptions.CultureInvariant | RegexOptions.Singleline)
            || combined.Contains("ambigu", StringComparison.Ordinal)
            || combined.Contains("too sparse", StringComparison.Ordinal)
            || combined.Contains("not exposed", StringComparison.Ordinal)
            || combined.Contains("unconfirmed", StringComparison.Ordinal)
            || combined.Contains("until i verify", StringComparison.Ordinal)
            || combined.Contains("verify the new screen", StringComparison.Ordinal)
            || combined.Contains("verify the resulting screen", StringComparison.Ordinal)
            || combined.Contains("i am not inferring", StringComparison.Ordinal)
            || combined.Contains("do not infer", StringComparison.Ordinal)
            || combined.Contains("cannot be inferred", StringComparison.Ordinal);
    }

    internal static bool HasExplicitlyUnresolvedOutcome(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var combined = text
            .ToLowerInvariant()
            .Replace('’', '\'')
            .Replace('‘', '\'');
        if (LooksLikeConditionalNoOpOutcome(combined))
        {
            return false;
        }

        if (LooksLikeSatisfiedRequestedConditionOutcome(combined))
        {
            return false;
        }

        return combined.Contains("not complete", StringComparison.Ordinal)
            || combined.Contains("not completed", StringComparison.Ordinal)
            || combined.Contains("not done", StringComparison.Ordinal)
            || combined.Contains("did not", StringComparison.Ordinal)
            || combined.Contains("didn't", StringComparison.Ordinal)
            || combined.Contains("failed", StringComparison.Ordinal)
            || combined.Contains("unable", StringComparison.Ordinal)
            || combined.Contains("could not", StringComparison.Ordinal)
            || combined.Contains("can't open", StringComparison.Ordinal)
            || combined.Contains("cannot open", StringComparison.Ordinal)
            || combined.Contains("can't play", StringComparison.Ordinal)
            || combined.Contains("cannot play", StringComparison.Ordinal)
            || combined.Contains("can't find", StringComparison.Ordinal)
            || combined.Contains("cannot find", StringComparison.Ordinal)
            || combined.Contains("not open", StringComparison.Ordinal)
            || combined.Contains("not opened", StringComparison.Ordinal)
            || combined.Contains("not loaded", StringComparison.Ordinal)
            || combined.Contains("not yet", StringComparison.Ordinal)
            || combined.Contains("not visible yet", StringComparison.Ordinal)
            || combined.Contains("isn't visible yet", StringComparison.Ordinal)
            || combined.Contains("isnt visible yet", StringComparison.Ordinal)
            || combined.Contains("not on screen yet", StringComparison.Ordinal)
            || combined.Contains("has not been brought onto screen", StringComparison.Ordinal)
            || combined.Contains("no search results", StringComparison.Ordinal)
            || combined.Contains("search action didn't take", StringComparison.Ordinal)
            || combined.Contains("search action did not take", StringComparison.Ordinal)
            || combined.Contains("still showing", StringComparison.Ordinal)
            || combined.Contains("still on", StringComparison.Ordinal)
            || combined.Contains("remains uncompleted", StringComparison.Ordinal)
            || combined.Contains("remains incomplete", StringComparison.Ordinal)
            || combined.Contains("next step should be", StringComparison.Ordinal)
            || combined.Contains("next step remains", StringComparison.Ordinal);
    }

    internal static bool HasDeferredActionPromise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var combined = text.ToLowerInvariant();
        return combined.Contains("i'm turning", StringComparison.Ordinal)
               || combined.Contains("i am turning", StringComparison.Ordinal)
               || combined.Contains("i'm opening", StringComparison.Ordinal)
               || combined.Contains("i am opening", StringComparison.Ordinal)
               || combined.Contains("i'm clicking", StringComparison.Ordinal)
               || combined.Contains("i am clicking", StringComparison.Ordinal)
               || combined.Contains("i'm pressing", StringComparison.Ordinal)
               || combined.Contains("i am pressing", StringComparison.Ordinal)
               || combined.Contains("i'll turn", StringComparison.Ordinal)
               || combined.Contains("i will turn", StringComparison.Ordinal)
               || combined.Contains("i'll open", StringComparison.Ordinal)
               || combined.Contains("i will open", StringComparison.Ordinal)
               || combined.Contains("i'll click", StringComparison.Ordinal)
               || combined.Contains("i will click", StringComparison.Ordinal)
               || combined.Contains("i'll press", StringComparison.Ordinal)
               || combined.Contains("i will press", StringComparison.Ordinal)
               || combined.Contains("let me turn", StringComparison.Ordinal)
               || combined.Contains("let me open", StringComparison.Ordinal)
               || combined.Contains("let me click", StringComparison.Ordinal)
               || combined.Contains("let me press", StringComparison.Ordinal)
               || combined.Contains("꺼볼게요", StringComparison.Ordinal)
               || combined.Contains("켜볼게요", StringComparison.Ordinal)
               || combined.Contains("열어볼게요", StringComparison.Ordinal)
               || combined.Contains("눌러볼게요", StringComparison.Ordinal)
               || combined.Contains("해볼게요", StringComparison.Ordinal);
    }

    private static bool LooksLikeConditionalNoOpOutcome(string combinedLowerText)
    {
        return combinedLowerText.Contains("condition was absent", StringComparison.Ordinal)
               || combinedLowerText.Contains("condition is absent", StringComparison.Ordinal)
               || combinedLowerText.Contains("requested condition was absent", StringComparison.Ordinal)
               || combinedLowerText.Contains("conditional no-op", StringComparison.Ordinal)
               || combinedLowerText.Contains("no action was needed", StringComparison.Ordinal)
               || combinedLowerText.Contains("no action needed", StringComparison.Ordinal)
               || LooksLikeSelectionSurfaceNoOpOutcome(combinedLowerText)
               || (combinedLowerText.Contains("no passcode prompt", StringComparison.Ordinal)
                   && combinedLowerText.Contains("not needed", StringComparison.Ordinal))
               || (combinedLowerText.Contains("already active", StringComparison.Ordinal)
                   && combinedLowerText.Contains("did not request any ui action", StringComparison.Ordinal))
               || (combinedLowerText.Contains("already on", StringComparison.Ordinal)
                   && combinedLowerText.Contains("did not request any ui action", StringComparison.Ordinal))
               || LooksLikeSecretPromptNoOpOutcome(combinedLowerText);
    }

    private static bool LooksLikeSatisfiedRequestedConditionOutcome(string combinedLowerText)
    {
        var satisfiedRequestedCondition = combinedLowerText.Contains("requested condition is satisfied", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("requested condition was satisfied", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("requested wait condition is satisfied", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("requested wait condition was satisfied", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("stop condition is satisfied", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("stop condition was satisfied", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("satisfies the requested condition", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("satisfy the requested condition", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("satisfies the requested wait condition", StringComparison.Ordinal)
                                         || combinedLowerText.Contains("satisfy the requested wait condition", StringComparison.Ordinal)
                                         || Regex.IsMatch(
                                             combinedLowerText,
                                             @"\brequested condition\b.{0,80}\b(?:is|was)\s+(?:visible|reached|met)\b",
                                             RegexOptions.CultureInvariant | RegexOptions.Singleline)
                                         || Regex.IsMatch(
                                             combinedLowerText,
                                             @"\brequested wait condition\b.{0,80}\b(?:is|was)\s+(?:visible|reached|met)\b",
                                             RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!satisfiedRequestedCondition)
        {
            return false;
        }

        return !combinedLowerText.Contains("request is not complete", StringComparison.Ordinal)
               && !combinedLowerText.Contains("request is incomplete", StringComparison.Ordinal)
               && !combinedLowerText.Contains("requested action is incomplete", StringComparison.Ordinal)
               && !combinedLowerText.Contains("remains incomplete", StringComparison.Ordinal)
               && !combinedLowerText.Contains("remains uncompleted", StringComparison.Ordinal)
               && !combinedLowerText.Contains("next step should be", StringComparison.Ordinal)
               && !combinedLowerText.Contains("next step remains", StringComparison.Ordinal)
               && !combinedLowerText.Contains("failed", StringComparison.Ordinal)
               && !combinedLowerText.Contains("unable", StringComparison.Ordinal)
               && !combinedLowerText.Contains("could not", StringComparison.Ordinal)
               && !combinedLowerText.Contains("cannot", StringComparison.Ordinal)
               && !combinedLowerText.Contains("can't", StringComparison.Ordinal);
    }

    private static bool LooksLikeSelectionSurfaceNoOpOutcome(string combinedLowerText)
    {
        return (combinedLowerText.Contains("did not click anything", StringComparison.Ordinal)
                && Regex.IsMatch(
                    combinedLowerText,
                    @"\b(?:no|not a|did not see(?: a)?)\s+(?:[a-z-]+\s+){0,3}(?:selection\s+(?:prompt|screen)|picker|chooser)\b",
                    RegexOptions.CultureInvariant))
               || (Regex.IsMatch(
                       combinedLowerText,
                       @"\bdid not see(?: a)?\s+(?:[a-z-]+\s+){0,3}selection\s+screen\b",
                       RegexOptions.CultureInvariant)
                   && Regex.IsMatch(
                       combinedLowerText,
                       @"\bno\s+(?:[a-z-]+\s+){0,2}[a-z-]+\s+to\s+choose\b",
                       RegexOptions.CultureInvariant));
    }

    private static bool LooksLikeSecretPromptNoOpOutcome(string combinedLowerText)
    {
        var skippedEntry = Regex.IsMatch(
            combinedLowerText,
            @"\bdid(?: not|n['’]t)\s+(?:type|enter)\s+(?:the\s+)?(?:[a-z-]+\s+){0,2}(?:pin|passcode|code)\b",
            RegexOptions.CultureInvariant);
        var missingPrompt = Regex.IsMatch(
                                combinedLowerText,
                                @"\b(?:no|not present|not visible)\b.{0,80}\b(?:pin|passcode|code|lock)\b.{0,40}\b(?:prompt|screen|gate)?\b",
                                RegexOptions.CultureInvariant | RegexOptions.Singleline)
                            || Regex.IsMatch(
                                combinedLowerText,
                                @"\b(?:pin|passcode|code|lock)\b.{0,40}\b(?:prompt|screen|gate)\b.{0,24}\b(?:is\s+)?not\s+present\b",
                                RegexOptions.CultureInvariant | RegexOptions.Singleline)
                            || combinedLowerText.Contains("pin prompt is not visible", StringComparison.Ordinal)
                            || combinedLowerText.Contains("passcode prompt is not visible", StringComparison.Ordinal)
                            || combinedLowerText.Contains("lock prompt is not visible", StringComparison.Ordinal);
        var positiveEvidence = combinedLowerText.Contains("home is visible", StringComparison.Ordinal)
                               || combinedLowerText.Contains("browse is visible", StringComparison.Ordinal)
                               || combinedLowerText.Contains("lock is gone", StringComparison.Ordinal)
                               || combinedLowerText.Contains("positive evidence", StringComparison.Ordinal)
                               || combinedLowerText.Contains("confirmed from the active edge ui snapshot", StringComparison.Ordinal);
        return skippedEntry && missingPrompt && positiveEvidence;
    }

    private static async Task<IReadOnlyList<AgentMessage>> CollectAdditionalConfidenceEvidenceAsync(
        long turnId,
        McpClientManager mcpManager,
        DesktopSessionContext desktopSession,
        CancellationToken cancellationToken)
    {
        var evidenceStopwatch = Stopwatch.StartNew();
        DebugTrace.WriteStructuredEvent(
            "agent.additional_desktop_evidence",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["phase"] = "start",
                ["waitBeforeRetryMs"] = 1000,
            });

        var extraEvidence = new List<AgentMessage>
        {
            new AgentMessage.User(
                "Your first draft sounded uncertain about the current visible state. Wait 1 more second, then use the new UI Automation snapshot below before answering.")
        };

        await Task.Delay(1000, cancellationToken);

        try
        {
            var snapshotStopwatch = Stopwatch.StartNew();
            var retrySnapshot = await mcpManager.CallToolAsync(
                "describe_window",
                PrepareToolArgumentsForDesktopSession(
                    "describe_window",
                    CreateWindowSnapshotArguments(debugMode: DebugTrace.IsEnabled),
                    desktopSession),
                cancellationToken);
            Display.ToolResult("describe_window", retrySnapshot.Text, retrySnapshot.Images.Count);
            extraEvidence.Add(new AgentMessage.User(
                $"Second-pass visible UI snapshot after waiting 1 more second:\n{retrySnapshot.Text}"));
            DebugTrace.WriteStructuredEvent(
                "agent.additional_desktop_evidence_snapshot",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["elapsedMs"] = (int)Math.Round(snapshotStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                    ["window"] = DescribePrimaryWindowFromToolOutput(retrySnapshot.Text),
                    ["images"] = retrySnapshot.Images.Count,
                    ["resultPreview"] = DebugTrace.Preview(retrySnapshot.Text, 700),
                });
        }
        catch (Exception ex)
        {
            extraEvidence.Add(new AgentMessage.User(
                $"Second-pass UI Automation snapshot after waiting 1 more second was unavailable: {ex.Message}"));
            DebugTrace.WriteStructuredEvent(
                "agent.additional_desktop_evidence_snapshot_failed",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                });
        }

        DebugTrace.WriteStructuredEvent(
            "agent.additional_desktop_evidence_complete",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["elapsedMs"] = (int)Math.Round(evidenceStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                ["messagesAdded"] = extraEvidence.Count,
            });

        return extraEvidence;
    }

    private static string BuildRepairInstruction(bool performedDesktopAction)
        => performedDesktopAction
            ? "Rewrite your previous answer as strict JSON only: {\"say\":\"...\",\"log\":\"...\"}. Use the post-action UI snapshot first. If the current evidence is too sparse or ambiguous to describe the visible screen confidently, do not guess. `say` and `log` must not contradict each other. If the evidence shows the request is incomplete or failed, `say` must also say that clearly. In say, include the action outcome, the current visible screen state if it is supported by evidence, and 2 or 3 likely next actions. In log, include the fuller evidence-based description and briefly note any uncertainty."
            : "Rewrite your previous answer as strict JSON only: {\"say\":\"...\",\"log\":\"...\"}. Keep say short and spoken-friendly. Put fuller detail in log. Do not promise a UI action unless this turn already performed it. If no desktop action happened yet, describe the current visible state truthfully and, if needed, the next step that is still required instead of saying you are doing it now.";

    private static Dictionary<string, object?> CreateWindowSnapshotArguments(bool debugMode)
        => new()
        {
            ["debugMode"] = debugMode,
        };

    private static Dictionary<string, object?> CreateFocusSnapshotArguments(bool debugMode)
        => new()
        {
            ["debugMode"] = debugMode,
        };

    internal static ScriptedTurnStartCarryForwardContext? TryGetScriptedTurnStartCarryForwardContext(
        long turnId,
        string userText,
        DesktopSessionContext desktopSession,
        DateTimeOffset nowUtc,
        out string skipReason)
    {
        skipReason = string.Empty;
        if (turnId <= 1)
        {
            skipReason = "first_scripted_turn";
            return null;
        }

        if (UserExplicitlyRequestsFreshDesktopDiscovery(userText))
        {
            skipReason = "user_requested_fresh_discovery";
            return null;
        }

        var uiTreeMetadata = desktopSession.RecentUiTreeEvidenceMetadata;
        if (uiTreeMetadata is null)
        {
            skipReason = "no_stored_ui_tree_evidence";
            return null;
        }

        if (uiTreeMetadata.SourceTurnId <= 0 || uiTreeMetadata.SourceTurnId >= turnId)
        {
            skipReason = "no_prior_turn_ui_tree_evidence";
            return null;
        }

        if (!uiTreeMetadata.IsPostActionSnapshot &&
            !string.Equals(uiTreeMetadata.SourceKind, "describe_window", StringComparison.Ordinal))
        {
            skipReason = "ui_tree_source_not_trusted";
            return null;
        }

        var uiTreeEvidenceAgeMs = GetEvidenceAgeMs(uiTreeMetadata, nowUtc);
        if (uiTreeEvidenceAgeMs is null ||
            uiTreeEvidenceAgeMs.Value > ScriptedTurnStartEvidenceMaxAge.TotalMilliseconds)
        {
            skipReason = "ui_tree_evidence_stale";
            return null;
        }

        var actionableUiTreeContext = GetCurrentUiTreeContext(
            desktopSession.RecentWindowContext,
            desktopSession.RecentUiTreeContext);
        if (!SnapshotContainsElementTree(actionableUiTreeContext))
        {
            skipReason = "ui_tree_not_available";
            return null;
        }

        var uiContext = ResolveCarryForwardModelContext(
            desktopSession.CurrentUiElementContext,
            actionableUiTreeContext);
        if (string.IsNullOrWhiteSpace(uiContext))
        {
            skipReason = "ui_model_context_unavailable";
            return null;
        }

        var windowHandle = desktopSession.CurrentWindowHandle;
        var windowTitle = desktopSession.CurrentWindowTitle;
        if (string.IsNullOrWhiteSpace(windowHandle) &&
            string.IsNullOrWhiteSpace(windowTitle))
        {
            TryGetPrimaryWindowReference(
                actionableUiTreeContext!,
                out windowHandle,
                out windowTitle);
        }

        string? focusContext = null;
        var focusMetadata = desktopSession.RecentFocusEvidenceMetadata;
        if (focusMetadata is not null &&
            focusMetadata.SourceTurnId > 0 &&
            focusMetadata.SourceTurnId == uiTreeMetadata.SourceTurnId &&
            focusMetadata.CapturedAtUtc >= uiTreeMetadata.CapturedAtUtc)
        {
            var focusEvidenceAgeMs = GetEvidenceAgeMs(focusMetadata, nowUtc);
            if (focusEvidenceAgeMs is not null &&
                focusEvidenceAgeMs.Value <= ScriptedTurnStartEvidenceMaxAge.TotalMilliseconds)
            {
                focusContext = ResolveCarryForwardModelContext(
                    desktopSession.CurrentFocusElementContext,
                    desktopSession.RecentFocusContext);
            }
        }

        return new ScriptedTurnStartCarryForwardContext(
            uiTreeMetadata.SourceTurnId,
            uiTreeMetadata.SourceKind,
            uiTreeEvidenceAgeMs.Value,
            windowHandle,
            windowTitle,
            uiContext!,
            focusContext,
            uiTreeMetadata.IsPostActionSnapshot);
    }

    internal static string BuildScriptedTurnStartCarryForwardSummary(
        ScriptedTurnStartCarryForwardContext context)
    {
        var windowSummary = BuildWindowSummary(context.WindowHandle, context.WindowTitle);
        if (string.IsNullOrWhiteSpace(windowSummary))
        {
            return "Scripted turn start: the previous turn ended with fresh current-screen evidence. Treat that carry-forward evidence as the current screen unless a later tool result contradicts it, and use it before requesting fresh discovery.";
        }

        return $"Scripted turn start: the previous turn ended with fresh current-screen evidence for {windowSummary}. Treat that carry-forward evidence as the current screen unless a later tool result contradicts it, and use it before requesting fresh discovery.";
    }

    internal static string BuildScriptedTurnStartCarryForwardUserMessage(
        ScriptedTurnStartCarryForwardContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Carry-forward current-screen evidence from the end of scripted turn {context.SourceTurnId}:");
        var windowSummary = BuildWindowSummary(context.WindowHandle, context.WindowTitle);
        if (!string.IsNullOrWhiteSpace(windowSummary))
        {
            builder.AppendLine($"Window: {windowSummary}");
        }

        builder.AppendLine("Current UI snapshot:");
        builder.AppendLine(context.UiContext);

        if (!string.IsNullOrWhiteSpace(context.FocusContext))
        {
            builder.AppendLine();
            builder.AppendLine("Focused element snapshot:");
            builder.AppendLine(context.FocusContext);
        }

        return builder.ToString().TrimEnd();
    }

    internal static bool ShouldOfferStartupWindowInventory(
        string userText,
        IReadOnlyList<AgentSkillPrompt> activeSkills,
        string? recentWindowContext,
        string? recentUiTreeContext,
        out string skipReason)
    {
        skipReason = string.Empty;

        if (!activeSkills.Any(skill => string.Equals(skill.Key, "desktop-launch-and-first-look", StringComparison.Ordinal)))
        {
            skipReason = "launch_skill_inactive";
            return false;
        }

        if (UserExplicitlyRequestsFreshDesktopDiscovery(userText))
        {
            skipReason = "user_requested_fresh_discovery";
            return false;
        }

        var currentSurface = GetCurrentUiTreeContext(recentWindowContext, recentUiTreeContext)
                             ?? recentWindowContext
                             ?? recentUiTreeContext;

        if (UserRequestLooksLikeWebsiteNavigation(userText))
        {
            if (SnapshotLooksLikeBrowserWindow(currentSurface))
            {
                skipReason = "current_surface_already_browser";
                return false;
            }

            skipReason = string.Empty;
            return true;
        }

        if (TryExtractRequestedAppLaunchName(userText, out var requestedAppName))
        {
            if (ToolOutputWindowLooksLikeRequestedApp(currentSurface, requestedAppName))
            {
                skipReason = "current_surface_already_requested_app";
                return false;
            }

            skipReason = string.Empty;
            return true;
        }

        if (UserRequestLooksLikeActivation(userText))
        {
            skipReason = string.Empty;
            return true;
        }

        skipReason = "request_does_not_need_startup_inventory";
        return false;
    }

    internal static StartupWindowInventoryContext? TryGetStartupWindowInventoryContext(
        string? recentListWindowsOutput,
        DesktopEvidenceMetadata? metadata,
        string? recentWindowInventoryModelContext,
        DateTimeOffset nowUtc,
        out string skipReason)
    {
        skipReason = string.Empty;
        if (metadata is null)
        {
            skipReason = "no_stored_window_inventory";
            return null;
        }

        var evidenceAgeMs = GetEvidenceAgeMs(metadata, nowUtc);
        if (evidenceAgeMs is null)
        {
            skipReason = "window_inventory_age_unavailable";
            return null;
        }

        if (evidenceAgeMs.Value > StartupWindowInventoryMaxAge.TotalMilliseconds)
        {
            skipReason = "window_inventory_stale";
            return null;
        }

        if (!TryBuildCompactWindowInventorySnapshot(recentListWindowsOutput, out var inventorySnapshot))
        {
            skipReason = "window_inventory_parse_failed";
            return null;
        }

        var inventoryContext = !string.IsNullOrWhiteSpace(recentWindowInventoryModelContext)
            ? recentWindowInventoryModelContext
            : GetCompactWindowInventoryModelContext(recentListWindowsOutput);
        if (string.IsNullOrWhiteSpace(inventoryContext))
        {
            skipReason = "window_inventory_model_context_unavailable";
            return null;
        }

        return new StartupWindowInventoryContext(
            metadata.SourceTurnId,
            metadata.SourceKind,
            evidenceAgeMs.Value,
            inventorySnapshot.SelectedWindowHandle,
            inventorySnapshot.WindowCount,
            inventorySnapshot.OmittedWindowCount,
            inventoryContext);
    }

    internal static string BuildStartupWindowInventorySummary(StartupWindowInventoryContext context)
    {
        var selectedWindowText = string.IsNullOrWhiteSpace(context.SelectedWindowHandle)
            ? "No selected window is recorded in this inventory."
            : $"Selected window handle: {context.SelectedWindowHandle}.";
        return $"Startup desktop inventory is available from {context.SourceKind}. {selectedWindowText} Use it to decide whether to keep the current app, activate an existing window, or launch a new app before deeper in-app actions.";
    }

    internal static string BuildStartupWindowInventoryUserMessage(StartupWindowInventoryContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Startup desktop inventory from {context.SourceKind} (evidence age {context.EvidenceAgeMs} ms):");
        builder.AppendLine(context.InventoryContext);
        builder.AppendLine();
        builder.Append("Use this inventory only for startup choice: keep the current app, activate an existing window, or launch a new app. After startup, switch back to fresh current-window evidence.");
        return builder.ToString();
    }

    internal static long? GetEvidenceAgeMs(
        DesktopEvidenceMetadata? metadata,
        DateTimeOffset nowUtc)
    {
        if (metadata is null)
        {
            return null;
        }

        var age = nowUtc - metadata.CapturedAtUtc;
        return age < TimeSpan.Zero
            ? 0
            : (long)Math.Round(age.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }

    private static string? ResolveCarryForwardModelContext(
        string? preferredContext,
        string? snapshotText)
    {
        if (!string.IsNullOrWhiteSpace(preferredContext))
        {
            return preferredContext;
        }

        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return null;
        }

        return GetCompactSnapshotModelContext(snapshotText);
    }

    private static string BuildWindowSummary(string? windowHandle, string? windowTitle)
    {
        if (!string.IsNullOrWhiteSpace(windowTitle) &&
            !string.IsNullOrWhiteSpace(windowHandle))
        {
            return $"{windowTitle} ({windowHandle})";
        }

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            return windowTitle;
        }

        return windowHandle ?? string.Empty;
    }

    private static bool UserExplicitlyRequestsFreshDesktopDiscovery(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        return userText.Contains("list windows", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("what windows", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("which windows", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("describe_window", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("describe the window", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("what's on screen", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("what is on screen", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("take a screenshot", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyDictionary<string, object?>? TryBuildLaunchFollowUpSelectionArguments(string toolOutputText)
    {
        if (!TryGetSelectedWindowProperty(toolOutputText, out var selectedWindow))
        {
            return null;
        }

        if (selectedWindow.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (selectedWindow.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetJsonStringProperty(selectedWindow, "handle") is { Length: > 0 } handle)
        {
            return new Dictionary<string, object?> { ["windowHandle"] = handle };
        }

        if (TryGetJsonStringProperty(selectedWindow, "title") is { Length: > 0 } title)
        {
            return new Dictionary<string, object?> { ["titleContains"] = title };
        }

        return null;
    }

    internal static string? DescribeLaunchFollowUpSelectionTarget(string toolOutputText)
    {
        if (!TryGetSelectedWindowProperty(toolOutputText, out var selectedWindow) ||
            selectedWindow.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var handle = TryGetJsonStringProperty(selectedWindow, "handle");
        var title = TryGetJsonStringProperty(selectedWindow, "title");

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(handle))
        {
            return $"{title} ({handle})";
        }

        return !string.IsNullOrWhiteSpace(handle)
            ? handle
            : title;
    }

    internal static string? DescribePrimaryWindowFromToolOutput(string? toolOutputText)
    {
        if (TryGetWindowProperty(toolOutputText, "selectedWindow", out var selectedWindow) &&
            selectedWindow.ValueKind == JsonValueKind.Object)
        {
            return DescribeWindowElement(selectedWindow);
        }

        if (TryGetWindowProperty(toolOutputText, "window", out var window) &&
            window.ValueKind == JsonValueKind.Object)
        {
            return DescribeWindowElement(window);
        }

        return null;
    }

    internal static bool TryGetPrimaryWindowReference(
        string toolOutputText,
        out string? windowHandle,
        out string? windowTitle)
    {
        windowHandle = null;
        windowTitle = null;

        if (TryGetWindowProperty(toolOutputText, "selectedWindow", out var selectedWindow) &&
            selectedWindow.ValueKind == JsonValueKind.Object)
        {
            windowHandle = TryGetJsonStringProperty(selectedWindow, "handle");
            windowTitle = TryGetJsonStringProperty(selectedWindow, "title");
            return !string.IsNullOrWhiteSpace(windowHandle) || !string.IsNullOrWhiteSpace(windowTitle);
        }

        if (TryGetWindowProperty(toolOutputText, "window", out var window) &&
            window.ValueKind == JsonValueKind.Object)
        {
            windowHandle = TryGetJsonStringProperty(window, "handle");
            windowTitle = TryGetJsonStringProperty(window, "title");
            return !string.IsNullOrWhiteSpace(windowHandle) || !string.IsNullOrWhiteSpace(windowTitle);
        }

        return false;
    }

    internal static bool IsWindowInputTool(string toolName)
        => toolName is "press_window_key" or "type_window_text";

    internal static bool ToolUsesCurrentWindow(string toolName)
        => toolName is "activate_window"
            or "describe_window"
            or "capture_window_screenshot"
            or "describe_window_focus"
            or "list_window_main_menu_items"
            or "list_window_context_menu_items"
            or "focus_window_element"
            or "click_window_element"
            or "invoke_window_element"
            or "set_window_element_text"
            or "press_window_key"
            or "type_window_text"
            or "invoke_window_main_menu_item"
            or "invoke_window_context_menu_item";

    internal static Dictionary<string, object?> PrepareToolArgumentsForDesktopSession(
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        DesktopSessionContext desktopSession)
    {
        var preparedArgs = CloneArguments(args);

        if (string.Equals(toolName, "list_windows", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(TryGetStringArgument(preparedArgs, "windowHandle")) &&
                !string.IsNullOrWhiteSpace(desktopSession.CurrentWindowHandle))
            {
                preparedArgs["windowHandle"] = desktopSession.CurrentWindowHandle;
            }

            return preparedArgs;
        }

        if (!ToolUsesCurrentWindow(toolName))
        {
            return preparedArgs;
        }

        if (!string.IsNullOrWhiteSpace(TryGetStringArgument(preparedArgs, "windowHandle")))
        {
            return preparedArgs;
        }

        if (string.Equals(toolName, "activate_window", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(TryGetStringArgument(preparedArgs, "titleContains")))
        {
            return preparedArgs;
        }

        if (!string.IsNullOrWhiteSpace(desktopSession.CurrentWindowHandle))
        {
            preparedArgs["windowHandle"] = desktopSession.CurrentWindowHandle;
        }

        return preparedArgs;
    }

    internal static string? GetCurrentUiTreeContext(
        string? recentWindowContext,
        string? recentUiTreeContext)
    {
        if (!SnapshotContainsElementTree(recentUiTreeContext))
        {
            return SnapshotContainsElementTree(recentWindowContext)
                ? recentWindowContext
                : null;
        }

        if (string.IsNullOrWhiteSpace(recentWindowContext))
        {
            return recentUiTreeContext;
        }

        var uiTreeContext = recentUiTreeContext!;
        var currentWindow = DescribePrimaryWindowFromToolOutput(recentWindowContext);
        var treeWindow = DescribePrimaryWindowFromToolOutput(uiTreeContext);
        if (string.IsNullOrWhiteSpace(currentWindow) ||
            string.IsNullOrWhiteSpace(treeWindow) ||
            string.Equals(currentWindow, treeWindow, StringComparison.OrdinalIgnoreCase))
        {
            return uiTreeContext;
        }

        return SnapshotContainsElementTree(recentWindowContext)
            ? recentWindowContext
            : null;
    }

    internal static string ResolveToolResultContextForModel(
        string toolName,
        string toolText,
        bool toolIsError,
        string? currentWindowInventoryContext,
        string? currentUiElementContext,
        string? currentFocusElementContext,
        LlmModelProfile modelProfile)
    {
        _ = modelProfile;

        if (!toolIsError)
        {
            if (toolName == "list_windows")
            {
                return !string.IsNullOrWhiteSpace(currentWindowInventoryContext)
                    ? currentWindowInventoryContext
                    : GetCompactWindowInventoryModelContext(toolText);
            }

            if (toolName == "describe_window_focus" &&
                !string.IsNullOrWhiteSpace(currentFocusElementContext))
            {
                return currentFocusElementContext;
            }

            if (toolName == "describe_window")
            {
                return !string.IsNullOrWhiteSpace(currentUiElementContext)
                    ? currentUiElementContext
                    : GetCompactSnapshotModelContext(toolText);
            }

            if (toolName == "describe_window_focus")
            {
                return !string.IsNullOrWhiteSpace(currentFocusElementContext)
                    ? currentFocusElementContext
                    : GetCompactSnapshotModelContext(toolText);
            }

            if (ShouldUseStoredUiElementContextForToolResult(toolName) &&
                !string.IsNullOrWhiteSpace(currentUiElementContext))
            {
                return currentUiElementContext;
            }
        }

        return toolText;
    }

    private static string GetCompactWindowInventoryModelContext(string? listWindowsOutput)
        => TryBuildCompactWindowInventoryModelContext(listWindowsOutput, out var modelContext)
            ? modelContext
            : listWindowsOutput ?? string.Empty;

    internal static bool TryBuildCompactWindowInventoryModelContext(
        string? listWindowsOutput,
        out string modelContext)
    {
        modelContext = string.Empty;
        if (!TryBuildCompactWindowInventorySnapshot(listWindowsOutput, out var inventorySnapshot))
        {
            return false;
        }

        var jsonContext = BuildCompactWindowInventoryJsonContext(inventorySnapshot);
        var yamlContext = BuildCompactWindowInventoryYamlContext(inventorySnapshot);
        modelContext = Encoding.UTF8.GetByteCount(yamlContext) < Encoding.UTF8.GetByteCount(jsonContext)
            ? yamlContext
            : jsonContext;
        return true;
    }

    private static bool TryBuildCompactWindowInventorySnapshot(
        string? listWindowsOutput,
        out CompactWindowInventorySnapshot inventorySnapshot)
    {
        inventorySnapshot = new CompactWindowInventorySnapshot(null, [], 0, 0);
        if (string.IsNullOrWhiteSpace(listWindowsOutput))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(listWindowsOutput);
            var root = document.RootElement;
            if (!TryGetJsonProperty(root, "windows", out var windowsElement) ||
                windowsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var selectedWindowHandle = TryGetJsonStringProperty(root, "selectedWindowHandle");
            var windows = new List<CompactWindowInventoryWindow>();
            var totalWindowCount = 0;

            foreach (var window in windowsElement.EnumerateArray())
            {
                totalWindowCount += 1;
                var handle = TryGetJsonStringProperty(window, "handle");
                var title = TryGetJsonStringProperty(window, "title");
                var className = TryGetJsonStringProperty(window, "className");
                var isSelected = TryGetJsonBooleanProperty(window, "isSelected") == true
                                 || (!string.IsNullOrWhiteSpace(selectedWindowHandle)
                                     && string.Equals(handle, selectedWindowHandle, StringComparison.OrdinalIgnoreCase));
                var hasUsableBounds = HasUsableWindowBounds(window);

                if (string.IsNullOrWhiteSpace(handle) &&
                    string.IsNullOrWhiteSpace(title) &&
                    string.IsNullOrWhiteSpace(className) &&
                    !isSelected)
                {
                    continue;
                }

                windows.Add(new CompactWindowInventoryWindow(
                    handle,
                    title,
                    className,
                    isSelected,
                    hasUsableBounds));
            }

            inventorySnapshot = new CompactWindowInventorySnapshot(
                selectedWindowHandle,
                windows,
                totalWindowCount,
                Math.Max(0, totalWindowCount - windows.Count));
            return windows.Count > 0 || totalWindowCount == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildCompactWindowInventoryJsonContext(CompactWindowInventorySnapshot inventorySnapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (inventorySnapshot.SelectedWindowHandle is null)
            {
                writer.WriteNull("selectedWindowHandle");
            }
            else
            {
                writer.WriteString("selectedWindowHandle", inventorySnapshot.SelectedWindowHandle);
            }

            writer.WriteNumber("windowCount", inventorySnapshot.WindowCount);
            writer.WriteNumber("omittedWindowCount", inventorySnapshot.OmittedWindowCount);
            writer.WritePropertyName("windows");
            writer.WriteStartArray();

            foreach (var window in inventorySnapshot.Windows)
            {
                writer.WriteStartObject();
                if (window.Handle is null)
                {
                    writer.WriteNull("handle");
                }
                else
                {
                    writer.WriteString("handle", window.Handle);
                }

                if (window.Title is null)
                {
                    writer.WriteNull("title");
                }
                else
                {
                    writer.WriteString("title", window.Title);
                }

                if (window.ClassName is null)
                {
                    writer.WriteNull("className");
                }
                else
                {
                    writer.WriteString("className", window.ClassName);
                }

                writer.WriteBoolean("isSelected", window.IsSelected);
                writer.WriteBoolean("hasUsableBounds", window.HasUsableBounds);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildCompactWindowInventoryYamlContext(CompactWindowInventorySnapshot inventorySnapshot)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        builder.Append("selectedWindowHandle: ");
        builder.Append(inventorySnapshot.SelectedWindowHandle is null
            ? "null"
            : FormatYamlScalar(inventorySnapshot.SelectedWindowHandle));
        builder.Append(",windowCount: ");
        builder.Append(inventorySnapshot.WindowCount);
        builder.Append(",omittedWindowCount: ");
        builder.Append(inventorySnapshot.OmittedWindowCount);
        builder.Append(",windows: [");

        for (var index = 0; index < inventorySnapshot.Windows.Count; index += 1)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var window = inventorySnapshot.Windows[index];
            builder.Append('{');
            builder.Append("handle: ");
            builder.Append(window.Handle is null ? "null" : FormatYamlScalar(window.Handle));
            builder.Append(",title: ");
            builder.Append(window.Title is null ? "null" : FormatYamlScalar(window.Title));
            builder.Append(",className: ");
            builder.Append(window.ClassName is null ? "null" : FormatYamlScalar(window.ClassName));
            builder.Append(",isSelected: ");
            builder.Append(window.IsSelected ? "true" : "false");
            builder.Append(",hasUsableBounds: ");
            builder.Append(window.HasUsableBounds ? "true" : "false");
            builder.Append('}');
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static string GetCompactSnapshotModelContext(string snapshotText)
        => TryBuildCompactSnapshotModelContext(snapshotText, out var modelContext)
            ? modelContext
            : snapshotText;

    private static bool TryBuildCompactSnapshotModelContext(
        string? snapshotText,
        out string modelContext)
    {
        modelContext = string.Empty;
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            var root = document.RootElement;
            if (!TryGetJsonProperty(root, "llmTree", out var llmTree) ||
                llmTree.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var jsonContext = BuildCompactSnapshotJsonContext(root, llmTree);
            var yamlContext = BuildCompactSnapshotYamlContext(root, llmTree);
            modelContext = Encoding.UTF8.GetByteCount(yamlContext) < Encoding.UTF8.GetByteCount(jsonContext)
                ? yamlContext
                : jsonContext;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildCompactSnapshotJsonContext(JsonElement root, JsonElement llmTree)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (TryGetJsonProperty(root, "window", out var window) &&
                window.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("window");
                window.WriteTo(writer);
            }

            if (TryGetJsonProperty(root, "sourceStats", out var sourceStats) &&
                sourceStats.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("sourceStats");
                sourceStats.WriteTo(writer);
            }

            writer.WritePropertyName("llmTree");
            llmTree.WriteTo(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildCompactSnapshotYamlContext(JsonElement root, JsonElement llmTree)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        var wroteProperty = false;

        void AppendProperty(string propertyName, JsonElement propertyValue)
        {
            if (wroteProperty)
            {
                builder.Append(',');
            }

            builder.Append(propertyName);
            builder.Append(": ");
            AppendFlowYamlValue(builder, propertyValue);
            wroteProperty = true;
        }

        if (TryGetJsonProperty(root, "window", out var window) &&
            window.ValueKind == JsonValueKind.Object)
        {
            AppendProperty("window", window);
        }

        if (TryGetJsonProperty(root, "sourceStats", out var sourceStats) &&
            sourceStats.ValueKind == JsonValueKind.Object)
        {
            AppendProperty("sourceStats", sourceStats);
        }

        AppendProperty("llmTree", llmTree);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendFlowYamlValue(StringBuilder builder, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var wroteProperty = false;
                foreach (var property in element.EnumerateObject())
                {
                    if (wroteProperty)
                    {
                        builder.Append(',');
                    }

                    builder.Append(property.Name);
                    builder.Append(": ");
                    AppendFlowYamlValue(builder, property.Value);
                    wroteProperty = true;
                }

                builder.Append('}');
                return;

            case JsonValueKind.Array:
                builder.Append('[');
                var wroteItem = false;
                foreach (var item in element.EnumerateArray())
                {
                    if (wroteItem)
                    {
                        builder.Append(',');
                    }

                    AppendFlowYamlValue(builder, item);
                    wroteItem = true;
                }

                builder.Append(']');
                return;

            case JsonValueKind.String:
                builder.Append(FormatYamlScalar(element.GetString() ?? string.Empty));
                return;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                builder.Append(element.GetRawText());
                return;

            default:
                builder.Append(FormatYamlScalar(element.GetRawText()));
                return;
        }
    }

    private static string FormatYamlScalar(string value)
        => CanUsePlainYamlScalar(value)
            ? value
            : JsonSerializer.Serialize(value);

    private static bool CanUsePlainYamlScalar(string value)
    {
        if (string.IsNullOrEmpty(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (value.IndexOfAny(['\r', '\n', '\t', '{', '}', '[', ']', ',']) >= 0 ||
            value.Contains(": ", StringComparison.Ordinal))
        {
            return false;
        }

        var firstCharacter = value[0];
        if (firstCharacter is '-' or '?' or ':' or '!' or '&' or '*' or '#' or '|' or '>' or '@' or '`' or '"' or '\'')
        {
            return false;
        }

        if (bool.TryParse(value, out _) ||
            string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "~", StringComparison.Ordinal) ||
            double.TryParse(value, out _))
        {
            return false;
        }

        return true;
    }

    internal static bool ShouldUseStoredUiElementContextForToolResult(string toolName)
        => IsDesktopActionTool(toolName)
            || toolName is "describe_window"
                or "capture_window_screenshot";

    internal static bool TryRewriteSelectWindowArguments(
        IReadOnlyDictionary<string, object?> args,
        string? recentListWindowsOutput,
        out Dictionary<string, object?> rewrittenArgs)
    {
        rewrittenArgs = CloneArguments(args);

        if (!string.IsNullOrWhiteSpace(TryGetStringArgument(args, "windowHandle")))
        {
            return false;
        }

        var titleContains = TryGetStringArgument(args, "titleContains");
        if (string.IsNullOrWhiteSpace(titleContains) ||
            !TryResolveWindowHandleFromRecentWindowList(recentListWindowsOutput, titleContains, out var handle))
        {
            return false;
        }

        rewrittenArgs["windowHandle"] = handle;
        rewrittenArgs.Remove("titleContains");
        return true;
    }

    internal static bool TryRewriteSelectWindowForRequestedApp(
        string userText,
        IReadOnlyDictionary<string, object?> args,
        string? recentListWindowsOutput,
        bool canLaunchRequestedApp,
        out string rewrittenToolName,
        out Dictionary<string, object?> rewrittenArgs)
    {
        rewrittenToolName = "activate_window";
        rewrittenArgs = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (!TryExtractRequestedAppLaunchName(userText, out var requestedAppName) ||
            SelectWindowArgsAlreadyTargetRequestedApp(args, recentListWindowsOutput, requestedAppName))
        {
            return false;
        }

        if (TryResolveWindowHandleFromRecentWindowList(recentListWindowsOutput, requestedAppName, out var matchingHandle))
        {
            rewrittenArgs["windowHandle"] = matchingHandle;
            return true;
        }

        if (!canLaunchRequestedApp)
        {
            return false;
        }

        rewrittenToolName = "launch_application";
        rewrittenArgs["appName"] = requestedAppName;
        return true;
    }

    internal static bool ShouldPrimeBrowserAddressBarForUrlEntry(
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext,
        string? recentFocusContext)
    {
        var text = TryGetStringArgument(args, "text");
        if (!LooksLikeUrl(text ?? string.Empty))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(recentFocusContext))
        {
            return SnapshotContainsBrowserAddressBar(recentFocusContext);
        }

        return SnapshotLooksLikeBrowserWindow(recentWindowContext);
    }

    internal static bool ShouldOpenNewTabBeforeBrowserUrlEntry(
        string userText,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext)
    {
        var text = TryGetStringArgument(args, "text");
        if (!LooksLikeUrl(text ?? string.Empty) ||
            string.IsNullOrWhiteSpace(recentWindowContext))
        {
            return false;
        }

        if (UserExplicitlyRequestsCurrentTabReuse(userText) ||
            !SnapshotLooksLikeEdgeWindow(recentWindowContext) ||
            SnapshotLooksLikeNewTab(recentWindowContext))
        {
            return false;
        }

        return true;
    }

    internal static bool NeedsBrowserWindowPreflight(
        string userText,
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext)
    {
        if (!IsWindowInputTool(toolName) ||
            SnapshotLooksLikeBrowserWindow(recentWindowContext) ||
            !UserRequestLooksLikeWebsiteNavigation(userText))
        {
            return false;
        }

        var text = TryGetStringArgument(args, "text");
        if (LooksLikeUrl(text ?? string.Empty))
        {
            return true;
        }

        var key = TryGetStringArgument(args, "key");
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (string.Equals(key, "F6", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var modifiers = GetModifierNames(args);
        if (!modifiers.Contains("Control", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return key.Trim() is "L" or "l" or "T" or "t";
    }

    internal static bool TryBuildBrowserSelectionArguments(
        string? recentListWindowsOutput,
        out Dictionary<string, object?> selectionArgs)
    {
        selectionArgs = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(recentListWindowsOutput))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(recentListWindowsOutput);
            if (!TryGetJsonProperty(document.RootElement, "windows", out var windowsElement) ||
                windowsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var candidates = windowsElement
                .EnumerateArray()
                .Where(WindowLooksLikeBrowser)
                .Select(window => new BrowserWindowCandidate(
                    TryGetJsonStringProperty(window, "handle"),
                    TryGetJsonStringProperty(window, "title"),
                    TryGetJsonBooleanProperty(window, "isSelected") == true,
                    HasUsableWindowBounds(window),
                    WindowLooksLikeEdge(window)))
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Handle) ||
                                    !string.IsNullOrWhiteSpace(candidate.Title))
                .OrderByDescending(candidate => candidate.IsSelected && candidate.HasUsableBounds)
                .ThenByDescending(candidate => candidate.IsEdge && candidate.HasUsableBounds)
                .ThenByDescending(candidate => candidate.HasUsableBounds)
                .ThenByDescending(candidate => candidate.IsSelected)
                .ThenByDescending(candidate => candidate.IsEdge)
                .ToArray();

            if (candidates.Length == 0)
            {
                return false;
            }

            var chosen = candidates[0];
            if (!string.IsNullOrWhiteSpace(chosen.Handle))
            {
                selectionArgs["windowHandle"] = chosen.Handle;
                return true;
            }

            selectionArgs["titleContains"] = chosen.Title!;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryRewriteBrowserAddressBarActionToShortcut(
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext,
        out Dictionary<string, object?> rewrittenArgs)
    {
        rewrittenArgs = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (toolName is not "invoke_window_element" and not "focus_window_element")
        {
            return false;
        }

        var elementPath = TryGetStringArgument(args, "elementPath") ?? TryGetStringArgument(args, "uiPath");
        if (string.IsNullOrWhiteSpace(elementPath) ||
            !TryFindElementByPath(recentWindowContext, elementPath, out var element) ||
            !ElementLooksLikeBrowserAddressBar(element))
        {
            return false;
        }

        rewrittenArgs["key"] = "L";
        rewrittenArgs["modifiers"] = new[] { "Control" };
        return true;
    }

    internal static bool TryRewriteBrowserSearchControlAction(
        string userText,
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext,
        out Dictionary<string, object?> rewrittenArgs)
    {
        rewrittenArgs = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (toolName is not "invoke_window_element" and not "focus_window_element")
        {
            return false;
        }

        if (!SnapshotLooksLikeBrowserWindow(recentWindowContext) ||
            !UserRequestLooksLikeBrowserContentSearch(userText))
        {
            return false;
        }

        var elementPath = TryGetStringArgument(args, "elementPath") ?? TryGetStringArgument(args, "uiPath");
        var requiredAction = toolName == "focus_window_element" ? "focus" : "invoke";
        if (!string.IsNullOrWhiteSpace(elementPath) &&
            TryFindElementByPath(recentWindowContext, elementPath, out var currentElement) &&
            ElementLooksLikeBrowserSiteSearchControl(currentElement, requiredAction))
        {
            return false;
        }

        if (!TryFindPreferredBrowserSearchControlPath(recentWindowContext, requiredAction, out var repairedPath) ||
            string.Equals(repairedPath, elementPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        rewrittenArgs = CloneArguments(args);
        rewrittenArgs["elementPath"] = repairedPath;
        rewrittenArgs.Remove("uiPath");
        return true;
    }

    internal static bool TryRewriteBrowserSearchFieldValueEntryToTyping(
        string userText,
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext,
        out Dictionary<string, object?> rewrittenArgs,
        out string? browserSearchFieldPath)
    {
        rewrittenArgs = new Dictionary<string, object?>(StringComparer.Ordinal);
        browserSearchFieldPath = null;

        if (toolName != "set_window_element_text" ||
            !SnapshotLooksLikeBrowserWindow(recentWindowContext) ||
            !UserRequestLooksLikeBrowserContentSearch(userText))
        {
            return false;
        }

        var value = TryGetStringArgument(args, "text");
        var elementPath = TryGetStringArgument(args, "elementPath") ?? TryGetStringArgument(args, "uiPath");
        if (string.IsNullOrWhiteSpace(value) ||
            string.IsNullOrWhiteSpace(elementPath) ||
            !TryFindElementByPath(recentWindowContext, elementPath, out var element) ||
            !ElementLooksLikeBrowserSiteSearchField(element))
        {
            return false;
        }

        rewrittenArgs["text"] = value;
        browserSearchFieldPath = elementPath;
        return true;
    }

    internal static bool TryExtractStructuredDiscreteSlotText(
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext,
        string? recentFocusContext,
        out string text)
    {
        text = string.Empty;
        if (!string.Equals(toolName, "type_window_text", StringComparison.Ordinal))
        {
            return false;
        }

        var requestedText = TryGetStringArgument(args, "text");
        if (!TryExtractMultiCharacterDiscreteSlotText(requestedText, out text))
        {
            text = string.Empty;
            return false;
        }

        var matchesDiscreteSlotContext =
            SnapshotLooksLikeDiscreteSlotTextFocus(recentFocusContext)
            || SnapshotLooksLikeDiscreteSlotTextWindow(recentWindowContext);
        if (!matchesDiscreteSlotContext)
        {
            text = string.Empty;
        }

        return matchesDiscreteSlotContext;
    }

    internal static bool TryRewriteGenericContainerActionToNamedTarget(
        string userText,
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext,
        out Dictionary<string, object?> rewrittenArgs)
    {
        var evaluation = EvaluateGenericContainerActionToNamedTarget(
            userText,
            toolName,
            args,
            recentWindowContext);
        rewrittenArgs = evaluation.RewrittenArgs is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(evaluation.RewrittenArgs, StringComparer.Ordinal);
        return evaluation.Rewritten;
    }

    internal static NamedTargetRewriteEvaluation EvaluateGenericContainerActionToNamedTarget(
        string userText,
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext)
    {
        var requestedPath = TryGetStringArgument(args, "elementPath") ?? TryGetStringArgument(args, "uiPath");
        var requiredAction = toolName switch
        {
            "focus_window_element" => "focus",
            "invoke_window_element" => "invoke",
            _ => null
        };
        var userRequestedActivation = UserRequestLooksLikeActivation(userText);

        JsonElement elementTree = default;
        JsonElement requestedElement = default;
        JsonElement matchedElement = default;
        var hasSnapshotTree = false;
        var hasRequestedElement = false;
        var snapshotContainsNamedChoiceSurface = false;

        if (!string.IsNullOrWhiteSpace(recentWindowContext))
        {
            try
            {
                using var document = JsonDocument.Parse(recentWindowContext);
                if (TryGetSnapshotTree(document.RootElement, out elementTree))
                {
                    hasSnapshotTree = true;
                    snapshotContainsNamedChoiceSurface = SnapshotContainsVisibleNamedChoiceSurface(elementTree);
                    hasRequestedElement =
                        !string.IsNullOrWhiteSpace(requestedPath) &&
                        TryFindElementByPath(elementTree, requestedPath, out requestedElement);
                }
            }
            catch
            {
                hasSnapshotTree = false;
            }
        }

        var requestedElementSummary = hasRequestedElement
            ? CreateElementTraceSummary(requestedElement)
            : null;

        NamedTargetRewriteEvaluation Skip(string skipReason, string? matchedPath = null, IReadOnlyDictionary<string, object?>? matchedElementSummary = null)
            => new(
                requestedPath,
                hasRequestedElement,
                requestedElementSummary,
                requiredAction,
                userRequestedActivation,
                snapshotContainsNamedChoiceSurface,
                matchedPath,
                matchedElementSummary,
                false,
                skipReason,
                null);

        if (!IsGenericNamedTargetRewriteTool(toolName))
        {
            return Skip("unsupported_tool");
        }

        if (string.IsNullOrWhiteSpace(recentWindowContext))
        {
            return Skip("missing_window_snapshot");
        }

        if (!hasSnapshotTree)
        {
            return Skip("invalid_window_snapshot");
        }

        if (!userRequestedActivation)
        {
            return Skip("activation_intent_missing");
        }

        if (hasRequestedElement &&
            ElementAlreadyLooksLikeRequestedNamedTarget(requestedElement, userText, requiredAction))
        {
            return Skip("requested_target_already_specific");
        }

        if (hasRequestedElement &&
            ShouldPreserveExplicitRootInvocation(toolName, requestedPath, requestedElement))
        {
            return Skip("explicit_root_invocation_preserved");
        }

        if (!TryFindUniqueNamedActionTargetFromUserText(
                elementTree,
                userText,
                requiredAction,
                out var matchedPath,
                preferredAncestorPath: hasRequestedElement ? requestedPath : null))
        {
            return Skip("no_named_visible_target_match");
        }

        IReadOnlyDictionary<string, object?>? matchedElementSummary = null;
        if (TryFindElementByPath(elementTree, matchedPath, out matchedElement))
        {
            matchedElementSummary = CreateElementTraceSummary(matchedElement);
        }

        if (hasRequestedElement &&
            string.Equals(matchedPath, requestedPath, StringComparison.OrdinalIgnoreCase))
        {
            return Skip("matched_requested_path", matchedPath, matchedElementSummary);
        }

        var rewrittenArgs = CloneArguments(args);
        rewrittenArgs["elementPath"] = matchedPath;
        rewrittenArgs.Remove("uiPath");
        return new NamedTargetRewriteEvaluation(
            requestedPath,
            hasRequestedElement,
            requestedElementSummary,
            requiredAction,
            userRequestedActivation,
            snapshotContainsNamedChoiceSurface,
            matchedPath,
            matchedElementSummary,
            true,
            null,
            rewrittenArgs);
    }

    private static bool ShouldPreserveExplicitRootInvocation(
        string toolName,
        string? elementPath,
        JsonElement requestedElement)
    {
        return string.Equals(toolName, "invoke_window_element", StringComparison.Ordinal) &&
               string.Equals(elementPath, "root", StringComparison.OrdinalIgnoreCase) &&
               ElementHasAction(requestedElement, "close");
    }

    private static bool IsGenericNamedTargetRewriteTool(string toolName)
        => toolName is "click_window_element"
            or "invoke_window_element"
            or "focus_window_element";

    private static async Task<ToolCallOutcome> ExecuteSequentialDiscreteTextEntryAsync(
        long turnId,
        string toolCallId,
        string text,
        McpClientManager mcpManager,
        DesktopSessionContext desktopSession,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            $"Structured discrete-slot text entry used {text.Length} separate character inputs."
        };

        DebugTrace.WriteStructuredEvent(
            "agent.discrete_slot_text_entry.start",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["toolCallId"] = toolCallId,
                ["inputLength"] = text.Length,
            });

        for (var index = 0; index < text.Length; index += 1)
        {
            var inputArgs = new Dictionary<string, object?> { ["text"] = text[index].ToString() };
            var inputResult = await mcpManager.CallToolAsync(
                "type_window_text",
                PrepareToolArgumentsForDesktopSession("type_window_text", inputArgs, desktopSession),
                cancellationToken);
            DebugTrace.WriteStructuredEvent(
                "agent.discrete_slot_text_entry.character_input",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["toolCallId"] = toolCallId,
                    ["characterIndex"] = index + 1,
                    ["isError"] = inputResult.IsError,
                    ["resultPreview"] = DebugTrace.Preview(inputResult.Text, 500),
                });

            if (inputResult.IsError)
            {
                DebugTrace.WriteStructuredEvent(
                    "agent.discrete_slot_text_entry.aborted",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["toolCallId"] = toolCallId,
                        ["characterIndex"] = index + 1,
                        ["abortReason"] = "type_window_text_failed",
                        ["resultPreview"] = DebugTrace.Preview(inputResult.Text, 500),
                    });
                lines.Add($"Character {index + 1} entry failed: {DebugTrace.Preview(inputResult.Text, 220)}");
                return new ToolCallOutcome(string.Join(Environment.NewLine, lines), [], true);
            }

            var focusResult = await mcpManager.CallToolAsync(
                "describe_window_focus",
                PrepareToolArgumentsForDesktopSession(
                    "describe_window_focus",
                    CreateFocusSnapshotArguments(debugMode: DebugTrace.IsEnabled),
                    desktopSession),
                cancellationToken);
            var focusSummary = DescribeFocusedElementFromToolOutput(focusResult.Text);
            DebugTrace.WriteStructuredEvent(
                "agent.discrete_slot_text_entry.focus_verification",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["toolCallId"] = toolCallId,
                    ["characterIndex"] = index + 1,
                    ["isError"] = focusResult.IsError,
                    ["focusedElement"] = focusSummary,
                    ["resultPreview"] = DebugTrace.Preview(focusResult.Text, 500),
                });

            if (focusResult.IsError)
            {
                lines.Add($"Character {index + 1} entered. Focus verification was unavailable.");
                continue;
            }

            lines.Add(
                string.IsNullOrWhiteSpace(focusSummary)
                    ? $"Character {index + 1} entered."
                    : $"Character {index + 1} entered. Focus now: {focusSummary}.");
        }

        DebugTrace.WriteStructuredEvent(
            "agent.discrete_slot_text_entry.completed",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["toolCallId"] = toolCallId,
                ["inputLength"] = text.Length,
            });
        return new ToolCallOutcome(string.Join(Environment.NewLine, lines), []);
    }

    internal static bool ShouldBlockUnrequestedNamedChoiceSurfaceAction(
        string userText,
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        string? recentWindowContext,
        out string blockedMessage)
    {
        blockedMessage = string.Empty;
        if (toolName is not "click_window_element"
            and not "invoke_window_element"
            and not "focus_window_element"
            || string.IsNullOrWhiteSpace(recentWindowContext))
        {
            return false;
        }

        var elementPath = TryGetStringArgument(args, "elementPath") ?? TryGetStringArgument(args, "uiPath");
        if (string.IsNullOrWhiteSpace(elementPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(recentWindowContext);
            if (!TryGetSnapshotTree(document.RootElement, out var elementTree) ||
                !SnapshotContainsVisibleNamedChoiceSurface(elementTree) ||
                !TryFindElementByPath(elementTree, elementPath, out var requestedElement))
            {
                return false;
            }

            var requiredAction = toolName switch
            {
                "focus_window_element" => "focus",
                "invoke_window_element" => "invoke",
                _ => null
            };

            if (ElementAlreadyLooksLikeRequestedNamedTarget(requestedElement, userText, requiredAction))
            {
                return false;
            }

            blockedMessage =
                "Blocked internal policy: a visible named-choice surface is active, but this action does not match an exact named option or named management control requested by the user. Do not guess which choice to select. Report that explicit selection is still required instead.";
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool ShouldExitBrowserFullscreenBeforeBrowserShortcut(string? recentWindowContext)
    {
        if (!SnapshotLooksLikeBrowserWindow(recentWindowContext) ||
            string.IsNullOrWhiteSpace(recentWindowContext))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(recentWindowContext);
            return TryGetSnapshotTree(document.RootElement, out var elementTree) &&
                   ContainsMatchingElement(elementTree, ElementLooksLikeFullscreenBrowserContent);
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, object?> CloneArguments(IReadOnlyDictionary<string, object?> args)
        => args.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    internal static bool TryExtractRequestedAppLaunchName(string userText, out string appName)
    {
        appName = string.Empty;
        if (string.IsNullOrWhiteSpace(userText) ||
            UserRequestLooksLikeWebsiteNavigation(userText) ||
            UserRequestLooksLikeBrowserContentSearch(userText))
        {
            return false;
        }

        var match = Regex.Match(
            userText,
            @"\b(?:let(?:'|’)s\s+)?(?:open|launch|start|run|play|switch to|bring up)\s+(?<target>.+?)(?:\s+(?:app|application|program))?[.!?]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        var requestedTarget = Regex.Replace(
            match.Groups["target"].Value,
            @"^(?:the|a|an)\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        requestedTarget = requestedTarget.Trim().Trim('"', '\'').Trim();
        if (string.IsNullOrWhiteSpace(requestedTarget) ||
            requestedTarget.Length > 48)
        {
            return false;
        }

        var normalizedTarget = NormalizeForNameMatching(requestedTarget).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTarget) ||
            normalizedTarget is "it" or "this" or "that" or "music" or "song" or "video" or "movie" or "show" or "episode" or "playlist" or "track")
        {
            return false;
        }

        appName = requestedTarget;
        return true;
    }

    private static bool SelectWindowArgsAlreadyTargetRequestedApp(
        IReadOnlyDictionary<string, object?> args,
        string? recentListWindowsOutput,
        string requestedAppName)
    {
        var titleContains = TryGetStringArgument(args, "titleContains");
        if (!string.IsNullOrWhiteSpace(titleContains))
        {
            return TextContainsNormalizedName(titleContains, requestedAppName);
        }

        var windowHandle = TryGetStringArgument(args, "windowHandle");
        return !string.IsNullOrWhiteSpace(windowHandle) &&
               TryResolveWindowTitleFromRecentWindowList(recentListWindowsOutput, windowHandle, out var title) &&
               TextContainsNormalizedName(title, requestedAppName);
    }

    private static bool TryResolveWindowHandleFromRecentWindowList(
        string? recentListWindowsOutput,
        string titleContains,
        out string handle)
    {
        handle = string.Empty;
        if (string.IsNullOrWhiteSpace(recentListWindowsOutput) ||
            string.IsNullOrWhiteSpace(titleContains))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(recentListWindowsOutput);
            if (!TryGetJsonProperty(document.RootElement, "windows", out var windowsElement) ||
                windowsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var matches = windowsElement
                .EnumerateArray()
                .Select(window => new WindowListMatch(
                    TryGetJsonStringProperty(window, "handle"),
                    TryGetJsonStringProperty(window, "title"),
                    TryGetJsonStringProperty(window, "className"),
                    TryGetJsonBooleanProperty(window, "isSelected") == true,
                    HasUsableWindowBounds(window)))
                .Where(match => !string.IsNullOrWhiteSpace(match.Handle) &&
                                WindowListMatchContains(match, titleContains))
                .ToArray();

            var selectedMatches = matches.Where(match => match.IsSelected).ToArray();
            if (selectedMatches.Length == 1)
            {
                handle = selectedMatches[0].Handle!;
                return true;
            }

            var usableMatches = matches.Where(match => match.HasUsableBounds).ToArray();
            if (usableMatches.Length == 1)
            {
                handle = usableMatches[0].Handle!;
                return true;
            }

            var exactUsableMatches = matches
                .Where(match => match.HasUsableBounds &&
                                string.Equals(match.Title, titleContains, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactUsableMatches.Length == 1)
            {
                handle = exactUsableMatches[0].Handle!;
                return true;
            }

            var exactMatches = matches
                .Where(match => string.Equals(match.Title, titleContains, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactMatches.Length == 1)
            {
                handle = exactMatches[0].Handle!;
                return true;
            }

            if (matches.Length == 1)
            {
                handle = matches[0].Handle!;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryResolveWindowTitleFromRecentWindowList(
        string? recentListWindowsOutput,
        string windowHandle,
        out string title)
    {
        title = string.Empty;
        if (string.IsNullOrWhiteSpace(recentListWindowsOutput) ||
            string.IsNullOrWhiteSpace(windowHandle))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(recentListWindowsOutput);
            if (!TryGetJsonProperty(document.RootElement, "windows", out var windowsElement) ||
                windowsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var window in windowsElement.EnumerateArray())
            {
                var handle = TryGetJsonStringProperty(window, "handle");
                if (!string.Equals(handle, windowHandle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                title = TryGetJsonStringProperty(window, "title") ?? string.Empty;
                return !string.IsNullOrWhiteSpace(title);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool WindowListMatchContains(WindowListMatch match, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(match.Title) &&
                match.Title.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(match.ClassName) &&
                match.ClassName.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool ShouldBlockTaskbarSearchForBrowserContentQuery(
        string userText,
        string toolName,
        string? recentWindowContext)
    {
        return toolName == "launch_application"
               && SnapshotLooksLikeBrowserWindow(recentWindowContext)
               && UserRequestLooksLikeBrowserContentSearch(userText);
    }

    internal static bool TryFindVisibleNamedChoiceTargetPath(
        string userText,
        string? recentWindowContext,
        out string matchedPath)
    {
        return TryBuildVisibleNamedChoiceContinuation(
            userText,
            recentWindowContext,
            out matchedPath,
            out _,
            out _,
            out _);
    }

    internal static bool TryBuildVisibleNamedChoiceContinuation(
        string userText,
        string? recentWindowContext,
        out string matchedPath,
        out string skipReason,
        out Dictionary<string, object?> surfaceSummary,
        out IReadOnlyDictionary<string, object?>? targetSummary)
    {
        matchedPath = string.Empty;
        skipReason = "unknown";
        targetSummary = null;
        surfaceSummary = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["namedChoiceSurfaceVisible"] = false,
            ["window"] = DescribePrimaryWindowFromToolOutput(recentWindowContext),
        };

        if (string.IsNullOrWhiteSpace(recentWindowContext))
        {
            skipReason = "missing_window_snapshot";
            return false;
        }

        if (!UserRequestLooksLikeActivation(userText))
        {
            skipReason = "request_not_explicit_named_choice_activation";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(recentWindowContext);
            if (!TryGetSnapshotTree(document.RootElement, out var elementTree))
            {
                skipReason = "invalid_window_snapshot";
                return false;
            }

            surfaceSummary["namedChoiceSurfaceVisible"] = true;

            if (!TryFindStrongUniqueNamedActionTargetFromUserText(
                    elementTree,
                    userText,
                    "invoke",
                    out matchedPath))
            {
                skipReason = "no_exact_visible_named_choice_match";
                return false;
            }

            if (TryFindElementByPath(elementTree, matchedPath, out var matchedElement))
            {
                targetSummary = CreateElementTraceSummary(matchedElement);
            }

            skipReason = string.Empty;
            return true;
        }
        catch
        {
            skipReason = "invalid_window_snapshot";
            return false;
        }
    }

    internal static bool TryBuildRemainingDiscreteSlotText(
        string userText,
        string? recentWindowContext,
        string? recentFocusContext,
        out string remainingText)
    {
        remainingText = string.Empty;
        if (!TryExtractRequestedDiscreteSlotTextFromUserText(userText, out var fullText))
        {
            return false;
        }

        var matchesDiscreteSlotContext =
            SnapshotLooksLikeDiscreteSlotTextFocus(recentFocusContext)
            || SnapshotLooksLikeDiscreteSlotTextWindow(recentWindowContext);
        if (!matchesDiscreteSlotContext)
        {
            return false;
        }

        var nextCharacterIndex = 0;
        if (TryExtractDiscreteSlotInputOrdinal(recentFocusContext, out var focusedOrdinal) &&
            focusedOrdinal >= 1)
        {
            nextCharacterIndex = Math.Min(focusedOrdinal - 1, fullText.Length);
        }

        if (nextCharacterIndex >= fullText.Length)
        {
            return false;
        }

        remainingText = fullText[nextCharacterIndex..];
        return remainingText.Length > 0;
    }

    internal static bool TryBuildDiscreteSlotTextContinuation(
        string userText,
        string? recentWindowContext,
        string? recentFocusContext,
        out string remainingText,
        out string skipReason,
        out Dictionary<string, object?> surfaceSummary)
    {
        remainingText = string.Empty;
        var discreteSlotWindowVisible = SnapshotLooksLikeDiscreteSlotTextWindow(recentWindowContext);
        var discreteSlotFocusVisible = SnapshotLooksLikeDiscreteSlotTextFocus(recentFocusContext);
        var focusedOrdinal = TryExtractDiscreteSlotInputOrdinal(recentFocusContext, out var extractedFocusedOrdinal)
            ? extractedFocusedOrdinal
            : (int?)null;
        surfaceSummary = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["discreteSlotPromptVisible"] = discreteSlotWindowVisible || discreteSlotFocusVisible,
            ["windowSurfaceVisible"] = discreteSlotWindowVisible,
            ["focusSurfaceVisible"] = discreteSlotFocusVisible,
            ["slotOrdinal"] = focusedOrdinal,
            ["window"] = DescribePrimaryWindowFromToolOutput(recentWindowContext),
        };

        var hasRequestedValue = TryExtractRequestedDiscreteSlotTextFromUserText(
            userText,
            out _,
            out var valueExtractionPattern,
            out var candidateLength);
        surfaceSummary["valueExtractionMatched"] = hasRequestedValue;
        surfaceSummary["valueExtractionPattern"] = valueExtractionPattern;
        surfaceSummary["candidateLength"] = candidateLength;

        if (!hasRequestedValue)
        {
            skipReason = "no_discrete_slot_text_in_user_text";
            return false;
        }

        if (!(discreteSlotWindowVisible || discreteSlotFocusVisible))
        {
            skipReason = "discrete_slot_surface_not_visible";
            return false;
        }

        if (!TryBuildRemainingDiscreteSlotText(
                userText,
                recentWindowContext,
                recentFocusContext,
                out remainingText))
        {
            skipReason = focusedOrdinal is not null
                ? "no_remaining_discrete_slot_text"
                : "remaining_discrete_slot_text_unavailable";
            return false;
        }

        surfaceSummary["remainingCharacterCount"] = remainingText.Length;
        skipReason = string.Empty;
        return true;
    }

    internal static bool ShouldRefreshDiscreteSlotFocusBeforeContinuation(
        string? recentWindowContext,
        string? recentFocusContext)
    {
        return SnapshotLooksLikeDiscreteSlotTextWindow(recentWindowContext) &&
               !TryExtractDiscreteSlotInputOrdinal(recentFocusContext, out _);
    }

    internal static bool ShouldBlockProcessLaunchForBrowserRequest(
        string userText,
        string toolName,
        string? recentWindowContext)
    {
        if (toolName != "start_process")
        {
            return false;
        }

        return UserRequestLooksLikeWebsiteNavigation(userText)
               || (SnapshotLooksLikeBrowserWindow(recentWindowContext)
                   && UserRequestLooksLikeBrowserContentSearch(userText));
    }

    internal static bool ShouldAskToFallbackToWebsite(
        string userText,
        string requestedAppName,
        string? launchToolOutputText,
        string? postActionSnapshotText,
        IReadOnlyList<AgentSkillPrompt> activeSkills)
    {
        if (string.IsNullOrWhiteSpace(requestedAppName) ||
            UserRequestLooksLikeWebsiteNavigation(userText) ||
            !ActiveSkillsAllowWebsiteFallback(activeSkills, requestedAppName))
        {
            return false;
        }

        return !LaunchResultLooksConfirmedForRequestedApp(
            requestedAppName,
            launchToolOutputText,
            postActionSnapshotText);
    }

    internal static bool LaunchResultLooksConfirmedForRequestedApp(
        string requestedAppName,
        string? launchToolOutputText,
        string? postActionSnapshotText)
    {
        if (string.IsNullOrWhiteSpace(requestedAppName))
        {
            return false;
        }

        return ToolOutputWindowLooksLikeRequestedApp(launchToolOutputText, requestedAppName)
               || ToolOutputWindowLooksLikeRequestedApp(postActionSnapshotText, requestedAppName);
    }

    internal static AgentReply BuildWebsiteFallbackConfirmationReply(string requestedAppName)
    {
        var appLabel = TryGetShortSpeechLabel(requestedAppName) ?? requestedAppName.Trim();
        var say = $"I couldn't confirm that the {appLabel} app opened. Do you want me to continue with the {appLabel} website instead?";
        var log = say;
        return new AgentReply(
            log,
            say,
            JsonSerializer.Serialize(new
            {
                say,
                log
            }));
    }

    private static bool ToolOutputWindowLooksLikeRequestedApp(string? toolOutputText, string requestedAppName)
    {
        return !string.IsNullOrWhiteSpace(toolOutputText)
               && TryGetPrimaryWindowReference(toolOutputText, out _, out var windowTitle)
               && !string.IsNullOrWhiteSpace(windowTitle)
               && TextContainsNormalizedName(windowTitle!, requestedAppName);
    }

    private static bool ActiveSkillsAllowWebsiteFallback(
        IReadOnlyList<AgentSkillPrompt> activeSkills,
        string requestedAppName)
    {
        if (string.IsNullOrWhiteSpace(requestedAppName) ||
            activeSkills.Count == 0)
        {
            return false;
        }

        foreach (var skill in activeSkills)
        {
            if (!skill.Metadata.Affordances.Contains("website_fallback", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TextContainsNormalizedName(skill.Metadata.Group, requestedAppName)
                || TextContainsNormalizedName(skill.Key, requestedAppName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SnapshotContainsBrowserAddressBar(string? snapshotText)
    {
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            if (TryGetFocusedSnapshotElement(document.RootElement, out var focusedElement) &&
                ElementLooksLikeBrowserAddressBar(focusedElement))
            {
                return true;
            }

            return TryGetSnapshotTree(document.RootElement, out var elementTree) &&
                   ContainsMatchingElement(elementTree, ElementLooksLikeBrowserAddressBar);
        }
        catch
        {
            return false;
        }
    }

    private static bool SnapshotLooksLikeDiscreteSlotTextFocus(string? snapshotText)
    {
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            return TryGetFocusedSnapshotElement(document.RootElement, out var focusedElement) &&
                   ElementLooksLikeDiscreteTextSlot(focusedElement);
        }
        catch
        {
            return false;
        }
    }

    private static bool SnapshotLooksLikeDiscreteSlotTextWindow(string? snapshotText)
    {
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            if (!TryGetSnapshotTree(document.RootElement, out var elementTree))
            {
                return false;
            }

            var discreteSlotCount = CountMatchingElements(elementTree, ElementLooksLikeDiscreteTextSlot);
            if (discreteSlotCount >= 2)
            {
                return true;
            }

            var promptCueCount = CountMatchingElements(elementTree, ElementLooksLikeDiscreteTextPrompt);
            return (discreteSlotCount >= 1 && promptCueCount >= 1) || promptCueCount >= 2;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractDiscreteSlotInputOrdinal(string? focusSnapshotText, out int ordinal)
    {
        ordinal = 0;
        if (string.IsNullOrWhiteSpace(focusSnapshotText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(focusSnapshotText);
            if (!TryGetFocusedSnapshotElement(document.RootElement, out var focusedElement))
            {
                return false;
            }

            if (!ElementLooksLikeDiscreteTextSlot(focusedElement))
            {
                return false;
            }

            foreach (var candidateText in new[]
                     {
                         TryGetJsonStringProperty(focusedElement, "name"),
                         TryGetJsonStringProperty(focusedElement, "automationId"),
                         TryGetJsonStringProperty(focusedElement, "className")
                     })
            {
                if (string.IsNullOrWhiteSpace(candidateText))
                {
                    continue;
                }

                var match = Regex.Match(
                    candidateText,
                    @"(?:digit|slot|box|character|field|code|input)\s*(?<ordinal>\d+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success &&
                    int.TryParse(match.Groups["ordinal"].Value, out ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool SnapshotsShareSameWindowAndElementTree(string firstSnapshotText, string secondSnapshotText)
    {
        if (!TryGetSnapshotElementTreeSignature(firstSnapshotText, out var firstWindow, out var firstTree) ||
            !TryGetSnapshotElementTreeSignature(secondSnapshotText, out var secondWindow, out var secondTree))
        {
            return false;
        }

        return string.Equals(firstWindow, secondWindow, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(firstTree, secondTree, StringComparison.Ordinal);
    }

    private static bool TryGetSnapshotElementTreeSignature(
        string? snapshotText,
        out string windowSignature,
        out string elementTreeSignature)
    {
        windowSignature = DescribePrimaryWindowFromToolOutput(snapshotText ?? string.Empty) ?? string.Empty;
        elementTreeSignature = string.Empty;
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            if (!TryGetSnapshotTree(document.RootElement, out var elementTree))
            {
                return false;
            }

            elementTreeSignature = elementTree.GetRawText();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SnapshotContainsElementTree(string? snapshotText)
    {
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            return TryGetSnapshotTree(document.RootElement, out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool SnapshotLooksLikeBrowserWindow(string? snapshotText)
    {
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        if (TryGetWindowProperty(snapshotText, "window", out var window) &&
            WindowLooksLikeBrowser(window))
        {
            return true;
        }

        if (TryGetWindowProperty(snapshotText, "selectedWindow", out var selectedWindow) &&
            WindowLooksLikeBrowser(selectedWindow))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            return TryGetSnapshotTree(document.RootElement, out var elementTree) &&
                   (ContainsMatchingElement(elementTree, ElementLooksLikeBrowserAddressBar)
                    || ContainsMatchingElement(elementTree, ElementLooksLikeBrowserWebDocument));
        }
        catch
        {
            return false;
        }
    }

    private static bool SnapshotLooksLikeEdgeWindow(string? snapshotText)
    {
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        if (TryGetWindowProperty(snapshotText, "window", out var window))
        {
            return WindowLooksLikeEdge(window);
        }

        if (TryGetWindowProperty(snapshotText, "selectedWindow", out var selectedWindow))
        {
            return WindowLooksLikeEdge(selectedWindow);
        }

        return false;
    }

    private static bool SnapshotLooksLikeNewTab(string? snapshotText)
    {
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return false;
        }

        if (!TryGetWindowProperty(snapshotText, "window", out var window) &&
            !TryGetWindowProperty(snapshotText, "selectedWindow", out window))
        {
            return false;
        }

        var title = TryGetJsonStringProperty(window, "title");
        return !string.IsNullOrWhiteSpace(title) &&
               title.Contains("new tab", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsMatchingElement(
        JsonElement element,
        Func<JsonElement, bool> predicate)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (predicate(element))
        {
            return true;
        }

        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var child in children.EnumerateArray())
        {
            if (ContainsMatchingElement(child, predicate))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountMatchingElements(
        JsonElement element,
        Func<JsonElement, bool> predicate)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var count = predicate(element) ? 1 : 0;
        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return count;
        }

        foreach (var child in children.EnumerateArray())
        {
            count += CountMatchingElements(child, predicate);
        }

        return count;
    }

    private static bool TryExtractMultiCharacterDiscreteSlotText(string? text, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length < 2 ||
            trimmed.Length > 12 ||
            trimmed.Any(char.IsWhiteSpace) ||
            trimmed.Any(character => !char.IsLetterOrDigit(character)))
        {
            return false;
        }

        value = trimmed;
        return true;
    }

    private static bool TryFindElementByPath(
        string? snapshotText,
        string elementPath,
        out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(snapshotText) ||
            string.IsNullOrWhiteSpace(elementPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            return TryGetSnapshotTree(document.RootElement, out var elementTree) &&
                   TryFindElementByPath(elementTree, elementPath, out element);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindElementByPath(
        JsonElement element,
        string elementPath,
        out JsonElement match)
    {
        match = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var path = TryGetJsonStringProperty(element, "path");
        var uiPath = TryGetJsonStringProperty(element, "uiPath");
        if (string.Equals(path, elementPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uiPath, elementPath, StringComparison.OrdinalIgnoreCase))
        {
            match = element.Clone();
            return true;
        }

        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var child in children.EnumerateArray())
        {
            if (TryFindElementByPath(child, elementPath, out match))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindUniqueElementPathByNameAndAction(
        string? snapshotText,
        string elementName,
        string requiredAction,
        out string elementPath)
    {
        elementPath = string.Empty;
        if (string.IsNullOrWhiteSpace(snapshotText) ||
            string.IsNullOrWhiteSpace(elementName) ||
            string.IsNullOrWhiteSpace(requiredAction))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            if (!TryGetSnapshotTree(document.RootElement, out var elementTree))
            {
                return false;
            }

            var matches = new List<string>();
            CollectElementPathsByNameAndAction(elementTree, elementName, requiredAction, matches);
            if (matches.Count != 1)
            {
                return false;
            }

            elementPath = matches[0];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindPreferredBrowserSearchControlPath(
        string? snapshotText,
        string requiredAction,
        out string elementPath)
    {
        elementPath = string.Empty;
        if (string.IsNullOrWhiteSpace(snapshotText) ||
            string.IsNullOrWhiteSpace(requiredAction))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            if (!TryGetSnapshotTree(document.RootElement, out var elementTree))
            {
                return false;
            }

            var candidates = new List<(string Path, int Score)>();
            if (TryFindFirstMatchingElement(elementTree, ElementLooksLikeBrowserWebDocument, out var webDocument))
            {
                CollectBrowserSearchControlCandidates(webDocument, requiredAction, candidates);
                if (TryChoosePreferredBrowserSearchControlCandidate(candidates, out elementPath))
                {
                    return true;
                }

                candidates.Clear();
            }

            CollectBrowserSearchControlCandidates(elementTree, requiredAction, candidates);
            return TryChoosePreferredBrowserSearchControlCandidate(candidates, out elementPath);
        }
        catch
        {
            return false;
        }
    }

    private static void CollectBrowserSearchControlCandidates(
        JsonElement element,
        string requiredAction,
        List<(string Path, int Score)> candidates)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var path = TryGetJsonStringProperty(element, "uiPath") ?? TryGetJsonStringProperty(element, "path");
        if (!string.IsNullOrWhiteSpace(path) &&
            ElementLooksLikeBrowserSiteSearchControl(element, requiredAction))
        {
            candidates.Add((path, ScoreBrowserSearchControlCandidate(element)));
        }

        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            CollectBrowserSearchControlCandidates(child, requiredAction, candidates);
        }
    }

    private static bool TryChoosePreferredBrowserSearchControlCandidate(
        IReadOnlyList<(string Path, int Score)> candidates,
        out string elementPath)
    {
        elementPath = string.Empty;
        if (candidates.Count == 0)
        {
            return false;
        }

        var bestScore = candidates.Max(candidate => candidate.Score);
        var bestCandidates = candidates
            .Where(candidate => candidate.Score == bestScore)
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (bestCandidates.Length != 1)
        {
            return false;
        }

        elementPath = bestCandidates[0];
        return true;
    }

    private static void CollectElementPathsByNameAndAction(
        JsonElement element,
        string elementName,
        string requiredAction,
        List<string> matches)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var name = TryGetJsonStringProperty(element, "name");
        var path = TryGetJsonStringProperty(element, "uiPath") ?? TryGetJsonStringProperty(element, "path");
        if (!string.IsNullOrWhiteSpace(path) &&
            !string.IsNullOrWhiteSpace(name) &&
            string.Equals(name, elementName, StringComparison.OrdinalIgnoreCase) &&
            ElementHasAction(element, requiredAction))
        {
            matches.Add(path);
        }

        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            CollectElementPathsByNameAndAction(child, elementName, requiredAction, matches);
        }
    }

    private static bool ElementHasAction(JsonElement element, string requiredAction)
    {
        if (!TryGetJsonProperty(element, "availableActions", out var actionsElement) ||
            actionsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var actionElement in actionsElement.EnumerateArray())
        {
            if (actionElement.ValueKind == JsonValueKind.String &&
                string.Equals(actionElement.GetString(), requiredAction, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindFirstMatchingElement(
        JsonElement element,
        Func<JsonElement, bool> predicate,
        out JsonElement match)
    {
        match = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (predicate(element))
        {
            match = element.Clone();
            return true;
        }

        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var child in children.EnumerateArray())
        {
            if (TryFindFirstMatchingElement(child, predicate, out match))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ElementLooksLikeBrowserAddressBar(JsonElement element)
    {
        var name = TryGetJsonStringProperty(element, "name");
        if (!string.IsNullOrWhiteSpace(name) &&
            name.Contains("Address and search bar", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var className = TryGetJsonStringProperty(element, "className");
        return !string.IsNullOrWhiteSpace(className) &&
               string.Equals(className, "OmniboxViewViews", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ElementLooksLikeFullscreenBrowserContent(JsonElement element)
    {
        var name = TryGetJsonStringProperty(element, "name");
        if (!string.IsNullOrWhiteSpace(name) &&
            name.Contains("fullscreen", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var className = TryGetJsonStringProperty(element, "className");
        if (!string.IsNullOrWhiteSpace(className) &&
            className.Contains("fullscreen", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var automationId = TryGetJsonStringProperty(element, "automationId");
        return !string.IsNullOrWhiteSpace(automationId) &&
               automationId.Contains("fullscreen", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ElementLooksLikeBrowserSiteSearchField(JsonElement element)
    {
        var controlType = TryGetJsonStringProperty(element, "controlType");
        if (!string.Equals(controlType, "Edit", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = TryGetJsonStringProperty(element, "name");
        if (!string.IsNullOrWhiteSpace(name) &&
            name.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var automationId = TryGetJsonStringProperty(element, "automationId");
        return !string.IsNullOrWhiteSpace(automationId) &&
               automationId.Contains("search", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ElementLooksLikeBrowserSiteSearchControl(JsonElement element, string requiredAction)
    {
        if (string.IsNullOrWhiteSpace(requiredAction) ||
            ElementLooksLikeBrowserAddressBar(element) ||
            !ElementHasAction(element, requiredAction))
        {
            return false;
        }

        if (ElementLooksLikeBrowserSiteSearchField(element))
        {
            return true;
        }

        var controlType = TryGetJsonStringProperty(element, "controlType");
        if (controlType is not ("Button" or "Hyperlink" or "MenuItem" or "Edit"))
        {
            return false;
        }

        var name = TryGetJsonStringProperty(element, "name");
        if (!string.IsNullOrWhiteSpace(name) &&
            name.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var automationId = TryGetJsonStringProperty(element, "automationId");
        return !string.IsNullOrWhiteSpace(automationId) &&
               automationId.Contains("search", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreBrowserSearchControlCandidate(JsonElement element)
    {
        var score = 0;
        if (ElementLooksLikeBrowserSiteSearchField(element))
        {
            score += 4;
        }

        var name = TryGetJsonStringProperty(element, "name");
        if (string.Equals(name, "Search", StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }
        else if (!string.IsNullOrWhiteSpace(name) &&
                 name.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        var controlType = TryGetJsonStringProperty(element, "controlType");
        if (string.Equals(controlType, "Edit", StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }
        else if (string.Equals(controlType, "Button", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        var automationId = TryGetJsonStringProperty(element, "automationId");
        if (!string.IsNullOrWhiteSpace(automationId) &&
            automationId.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static bool ElementLooksLikeBrowserWebDocument(JsonElement element)
    {
        var automationId = TryGetJsonStringProperty(element, "automationId");
        if (!string.IsNullOrWhiteSpace(automationId) &&
            string.Equals(automationId, "RootWebArea", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var controlType = TryGetJsonStringProperty(element, "controlType");
        return string.Equals(controlType, "Document", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(TryGetJsonStringProperty(element, "className"));
    }

    private static bool ElementLooksLikeDiscreteTextSlot(JsonElement element)
    {
        var controlType = TryGetJsonStringProperty(element, "controlType");
        var name = TryGetJsonStringProperty(element, "name");
        var className = TryGetJsonStringProperty(element, "className");
        var automationId = TryGetJsonStringProperty(element, "automationId");

        return string.Equals(controlType, "Edit", StringComparison.OrdinalIgnoreCase)
               && (TextHasDiscreteSlotCue(name)
                   || TextHasDiscreteSlotCue(className)
                   || TextHasDiscreteSlotCue(automationId)
                   || TextHasDiscreteSlotOrdinalCue(name)
                   || TextHasDiscreteSlotOrdinalCue(automationId));
    }

    private static bool ElementLooksLikeDiscreteTextPrompt(JsonElement element)
    {
        if (ElementLooksLikeDiscreteTextSlot(element))
        {
            return true;
        }

        var name = TryGetJsonStringProperty(element, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = NormalizeForNameMatching(name);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains(" enter your pin ", StringComparison.Ordinal)
               || normalized.Contains(" forgot pin ", StringComparison.Ordinal)
               || normalized.Contains(" wrong pin ", StringComparison.Ordinal)
               || normalized.Contains(" lock is currently on ", StringComparison.Ordinal)
               || normalized.Contains(" lock is on ", StringComparison.Ordinal)
               || normalized.Contains(" passcode ", StringComparison.Ordinal)
               || normalized.Contains(" otp ", StringComparison.Ordinal)
               || normalized.Contains(" one time code ", StringComparison.Ordinal)
               || normalized.Contains(" verification code ", StringComparison.Ordinal)
               || normalized.Contains(" security code ", StringComparison.Ordinal);
    }

    private static bool TextHasDiscreteSlotCue(string? text)
    {
        var normalized = NormalizeForNameMatching(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains(" pin ", StringComparison.Ordinal)
               || normalized.Contains(" passcode ", StringComparison.Ordinal)
               || normalized.Contains(" otp ", StringComparison.Ordinal)
               || normalized.Contains(" one time code ", StringComparison.Ordinal)
               || normalized.Contains(" verification code ", StringComparison.Ordinal)
               || normalized.Contains(" security code ", StringComparison.Ordinal)
               || normalized.Contains(" digit ", StringComparison.Ordinal)
               || normalized.Contains(" slot ", StringComparison.Ordinal)
               || normalized.Contains(" box ", StringComparison.Ordinal)
               || normalized.Contains(" character ", StringComparison.Ordinal)
               || normalized.Contains(" code ", StringComparison.Ordinal)
               || normalized.Contains(" pin number input ", StringComparison.Ordinal);
    }

    private static bool TextHasDiscreteSlotOrdinalCue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"(?:digit|slot|box|character|field|code|input)\s*\d+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ElementLooksLikeGenericContainerTarget(JsonElement element)
    {
        var controlType = TryGetJsonStringProperty(element, "controlType");
        if (string.IsNullOrWhiteSpace(controlType))
        {
            return false;
        }

        return controlType.Equals("Group", StringComparison.OrdinalIgnoreCase)
               || controlType.Equals("Pane", StringComparison.OrdinalIgnoreCase)
               || controlType.Equals("List", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SnapshotContainsVisibleNamedChoiceSurface(JsonElement elementTree)
        => CountMatchingElements(elementTree, ElementLooksLikeNamedChoiceOption) >= 1
           && CountMatchingElements(elementTree, ElementLooksLikeNamedChoiceSurfaceCue) >= 1;

    private static bool ElementLooksLikeNamedChoiceOption(JsonElement element)
    {
        if (TryGetJsonBooleanProperty(element, "isOffscreen") == true)
        {
            return false;
        }

        var controlType = TryGetJsonStringProperty(element, "controlType");
        var name = TryGetJsonStringProperty(element, "name");
        var className = TryGetJsonStringProperty(element, "className");

        return !string.IsNullOrWhiteSpace(name)
               && string.Equals(controlType, "ListItem", StringComparison.OrdinalIgnoreCase)
               && !TextLooksLikeNamedChoiceManagementControl(name)
               && (string.IsNullOrWhiteSpace(className)
                   || !TextLooksLikeNamedChoiceManagementControl(className));
    }

    private static bool ElementLooksLikeNamedChoiceSurfaceCue(JsonElement element)
    {
        if (TryGetJsonBooleanProperty(element, "isOffscreen") == true)
        {
            return false;
        }

        var name = TryGetJsonStringProperty(element, "name");
        if (TextLooksLikeNamedChoiceManagementControl(name)
            || TextLooksLikeNamedChoiceSelectionCue(name))
        {
            return true;
        }

        var className = TryGetJsonStringProperty(element, "className");
        return TextLooksLikeNamedChoiceSelectionCue(className)
               || TextLooksLikeNamedChoiceManagementControl(className);
    }

    private static bool TextLooksLikeNamedChoiceManagementControl(string? text)
    {
        var normalized = NormalizeForNameMatching(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains(" manage ", StringComparison.Ordinal)
               || normalized.Contains(" edit ", StringComparison.Ordinal)
               || normalized.Contains(" switch ", StringComparison.Ordinal)
               || normalized.Contains(" add ", StringComparison.Ordinal)
               || normalized.Contains(" done ", StringComparison.Ordinal);
    }

    private static bool TextLooksLikeNamedChoiceSelectionCue(string? text)
    {
        var normalized = NormalizeForNameMatching(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains(" choose ", StringComparison.Ordinal)
               || normalized.Contains(" select ", StringComparison.Ordinal)
               || normalized.Contains(" picker ", StringComparison.Ordinal)
               || normalized.Contains(" chooser ", StringComparison.Ordinal)
               || normalized.Contains(" watching ", StringComparison.Ordinal)
               || normalized.Contains(" account ", StringComparison.Ordinal)
               || normalized.Contains(" user ", StringComparison.Ordinal)
               || normalized.Contains(" gate ", StringComparison.Ordinal)
               || normalized.Contains(" selector ", StringComparison.Ordinal);
    }

    private static string? DescribeFocusedElementFromToolOutput(string toolOutputText)
    {
        if (string.IsNullOrWhiteSpace(toolOutputText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(toolOutputText);
            if (!TryGetFocusedSnapshotElement(document.RootElement, out var focusedElement))
            {
                return null;
            }

            var name = TryGetJsonStringProperty(focusedElement, "name");
            var controlType = TryGetJsonStringProperty(focusedElement, "controlType");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(controlType))
            {
                return $"{name} ({controlType})";
            }

            return name ?? controlType;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, object?> CreateElementTraceSummary(JsonElement element)
    {
        string[]? availableActions = null;
        if (TryGetJsonProperty(element, "availableActions", out var actionsElement) &&
            actionsElement.ValueKind == JsonValueKind.Array)
        {
            availableActions = actionsElement
                .EnumerateArray()
                .Where(actionElement => actionElement.ValueKind == JsonValueKind.String)
                .Select(actionElement => actionElement.GetString())
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Cast<string>()
                .ToArray();
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = TryGetJsonStringProperty(element, "path") ?? TryGetJsonStringProperty(element, "uiPath"),
            ["name"] = TryGetJsonStringProperty(element, "name"),
            ["controlType"] = TryGetJsonStringProperty(element, "controlType"),
            ["className"] = TryGetJsonStringProperty(element, "className"),
            ["automationId"] = TryGetJsonStringProperty(element, "automationId"),
            ["availableActions"] = availableActions,
        };
    }

    private static bool TryFindUniqueNamedActionTargetFromUserText(
        string? snapshotText,
        string userText,
        string? requiredAction,
        out string matchedPath,
        string? preferredAncestorPath = null)
    {
        matchedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(snapshotText) ||
            string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            if (!TryGetSnapshotTree(document.RootElement, out var elementTree))
            {
                return false;
            }

            return TryFindUniqueNamedActionTargetFromUserText(
                elementTree,
                userText,
                requiredAction,
                out matchedPath,
                preferredAncestorPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindUniqueNamedActionTargetFromUserText(
        JsonElement elementTree,
        string userText,
        string? requiredAction,
        out string matchedPath,
        string? preferredAncestorPath = null)
    {
        matchedPath = string.Empty;
        var candidateMatches = new List<NamedActionTargetCandidate>();
        if (!string.IsNullOrWhiteSpace(preferredAncestorPath) &&
            TryFindElementByPath(elementTree, preferredAncestorPath, out var preferredAncestor))
        {
            CollectNamedActionTargetCandidatesFromUserText(
                preferredAncestor,
                userText,
                requiredAction,
                candidateMatches);

            if (TryChooseBestNamedActionTargetCandidate(candidateMatches, out matchedPath))
            {
                return true;
            }

            candidateMatches.Clear();
        }

        CollectNamedActionTargetCandidatesFromUserText(
            elementTree,
            userText,
            requiredAction,
            candidateMatches);
        return TryChooseBestNamedActionTargetCandidate(candidateMatches, out matchedPath);
    }

    private static bool TryFindStrongUniqueNamedActionTargetFromUserText(
        JsonElement elementTree,
        string userText,
        string? requiredAction,
        out string matchedPath,
        string? preferredAncestorPath = null)
    {
        matchedPath = string.Empty;
        var candidateMatches = new List<NamedActionTargetCandidate>();
        if (!string.IsNullOrWhiteSpace(preferredAncestorPath) &&
            TryFindElementByPath(elementTree, preferredAncestorPath, out var preferredAncestor))
        {
            CollectStrongNamedActionTargetCandidatesFromUserText(
                preferredAncestor,
                userText,
                requiredAction,
                candidateMatches);

            if (TryChooseBestNamedActionTargetCandidate(candidateMatches, out matchedPath))
            {
                return true;
            }

            candidateMatches.Clear();
        }

        CollectStrongNamedActionTargetCandidatesFromUserText(
            elementTree,
            userText,
            requiredAction,
            candidateMatches);
        return TryChooseBestNamedActionTargetCandidate(candidateMatches, out matchedPath);
    }

    private static void CollectNamedActionTargetCandidatesFromUserText(
        JsonElement element,
        string userText,
        string? requiredAction,
        List<NamedActionTargetCandidate> matches)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var path = TryGetJsonStringProperty(element, "uiPath") ?? TryGetJsonStringProperty(element, "path");
        var name = TryGetJsonStringProperty(element, "name");
        if (!string.IsNullOrWhiteSpace(path) &&
            !string.IsNullOrWhiteSpace(name) &&
            ElementLooksLikePreciseActionTarget(element) &&
            UserTextMentionsElementName(userText, name) &&
            (requiredAction is null || ElementHasAction(element, requiredAction)))
        {
            matches.Add(
                new NamedActionTargetCandidate(
                    path,
                    ElementHasInterestingAction(element),
                    GetNamedActionTargetNameMatchScore(userText, name),
                    GetNamedActionTargetSpecificity(element),
                    GetPathDepth(path)));
        }

        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            CollectNamedActionTargetCandidatesFromUserText(child, userText, requiredAction, matches);
        }
    }

    private static void CollectStrongNamedActionTargetCandidatesFromUserText(
        JsonElement element,
        string userText,
        string? requiredAction,
        List<NamedActionTargetCandidate> matches)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var path = TryGetJsonStringProperty(element, "uiPath") ?? TryGetJsonStringProperty(element, "path");
        var name = TryGetJsonStringProperty(element, "name");
        if (!string.IsNullOrWhiteSpace(path) &&
            !string.IsNullOrWhiteSpace(name) &&
            ElementLooksLikePreciseActionTarget(element) &&
            UserTextStronglyMatchesNamedChoice(userText, name) &&
            (requiredAction is null || ElementHasAction(element, requiredAction)))
        {
            matches.Add(
                new NamedActionTargetCandidate(
                    path,
                    ElementHasInterestingAction(element),
                    GetNamedActionTargetNameMatchScore(userText, name),
                    GetNamedActionTargetSpecificity(element),
                    GetPathDepth(path)));
        }

        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            CollectStrongNamedActionTargetCandidatesFromUserText(child, userText, requiredAction, matches);
        }
    }

    private static bool TryChooseBestNamedActionTargetCandidate(
        IReadOnlyList<NamedActionTargetCandidate> candidates,
        out string matchedPath)
    {
        matchedPath = string.Empty;
        if (candidates.Count == 0)
        {
            return false;
        }

        if (candidates.Count == 1)
        {
            matchedPath = candidates[0].Path;
            return true;
        }

        var remaining = candidates.ToArray();
        if (TryFilterNamedActionTargetCandidates(
                remaining,
                candidate => candidate.HasInterestingAction,
                out remaining,
                out matchedPath))
        {
            return true;
        }

        var highestNameMatchScore = remaining.Max(candidate => candidate.NameMatchScore);
        if (TryFilterNamedActionTargetCandidates(
                remaining,
                candidate => candidate.NameMatchScore == highestNameMatchScore,
                out remaining,
                out matchedPath))
        {
            return true;
        }

        var highestSpecificity = remaining.Max(candidate => candidate.Specificity);
        if (TryFilterNamedActionTargetCandidates(
                remaining,
                candidate => candidate.Specificity == highestSpecificity,
                out remaining,
                out matchedPath))
        {
            return true;
        }

        var deepestPath = remaining.Max(candidate => candidate.PathDepth);
        return TryFilterNamedActionTargetCandidates(
            remaining,
            candidate => candidate.PathDepth == deepestPath,
            out _,
            out matchedPath);
    }

    private static bool TryFilterNamedActionTargetCandidates(
        IReadOnlyList<NamedActionTargetCandidate> candidates,
        Func<NamedActionTargetCandidate, bool> predicate,
        out NamedActionTargetCandidate[] remaining,
        out string matchedPath)
    {
        matchedPath = string.Empty;
        remaining = candidates.Where(predicate).ToArray();
        if (remaining.Length == 0)
        {
            remaining = candidates.ToArray();
            return false;
        }

        if (remaining.Length == 1)
        {
            matchedPath = remaining[0].Path;
            return true;
        }

        return false;
    }

    private static bool ElementLooksLikePreciseActionTarget(JsonElement element)
    {
        var controlType = TryGetJsonStringProperty(element, "controlType");
        return controlType is not null &&
               (controlType.Equals("Button", StringComparison.OrdinalIgnoreCase)
                || controlType.Equals("Hyperlink", StringComparison.OrdinalIgnoreCase)
                || controlType.Equals("Link", StringComparison.OrdinalIgnoreCase)
                || controlType.Equals("ListItem", StringComparison.OrdinalIgnoreCase)
                || controlType.Equals("MenuItem", StringComparison.OrdinalIgnoreCase)
                || controlType.Equals("TreeItem", StringComparison.OrdinalIgnoreCase)
                || controlType.Equals("TabItem", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ElementHasInterestingAction(JsonElement element)
    {
        if (!TryGetJsonProperty(element, "availableActions", out var actionsElement) ||
            actionsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var actionElement in actionsElement.EnumerateArray())
        {
            if (actionElement.ValueKind == JsonValueKind.String &&
                !string.Equals(actionElement.GetString(), "scroll_into_view", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetNamedActionTargetSpecificity(JsonElement element)
    {
        var controlType = TryGetJsonStringProperty(element, "controlType");
        return controlType switch
        {
            "Button" => 5,
            "Hyperlink" => 5,
            "Link" => 5,
            "MenuItem" => 4,
            "TreeItem" => 4,
            "TabItem" => 4,
            "ListItem" => 3,
            _ => 1
        };
    }

    private static int GetNamedActionTargetNameMatchScore(string userText, string elementName)
    {
        if (string.IsNullOrWhiteSpace(userText) ||
            string.IsNullOrWhiteSpace(elementName))
        {
            return 0;
        }

        if (TextContainsNormalizedName(userText, elementName))
        {
            return 100;
        }

        var userTokens = GetNormalizedTokens(userText);
        var elementNameTokens = GetNormalizedTokens(elementName)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (userTokens.Length == 0 || elementNameTokens.Length == 0)
        {
            return 0;
        }

        var score = 0;
        foreach (var elementNameToken in elementNameTokens)
        {
            if (userTokens.Contains(elementNameToken, StringComparer.Ordinal))
            {
                score += 10;
                continue;
            }

            if (userTokens.Any(userToken => LooksLikeObviousAsrVariant(userToken, elementNameToken)))
            {
                score += 4;
            }
        }

        return score;
    }

    private static int GetPathDepth(string path)
        => path.Count(character => character == '/');

    private static bool ElementAlreadyLooksLikeRequestedNamedTarget(
        JsonElement element,
        string userText,
        string? requiredAction)
    {
        var elementName = TryGetJsonStringProperty(element, "name");
        if (string.IsNullOrWhiteSpace(elementName) ||
            !UserTextMentionsElementName(userText, elementName) ||
            !ElementLooksLikePreciseActionTarget(element))
        {
            return false;
        }

        return requiredAction is null || ElementHasAction(element, requiredAction);
    }

    private static bool UserTextMentionsElementName(string userText, string elementName)
    {
        if (TextContainsNormalizedName(userText, elementName))
        {
            return true;
        }

        var userTokens = GetNormalizedTokens(userText);
        var elementNameTokens = GetNormalizedTokens(elementName);
        if (userTokens.Length == 0 || elementNameTokens.Length == 0)
        {
            return false;
        }

        foreach (var elementNameToken in elementNameTokens)
        {
            if (elementNameToken.Length < 3)
            {
                continue;
            }

            foreach (var userToken in userTokens)
            {
                if (LooksLikeObviousAsrVariant(userToken, elementNameToken))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TextContainsNormalizedName(string text, string name)
    {
        var normalizedText = NormalizeForNameMatching(text);
        var normalizedName = NormalizeForNameMatching(name).Trim();
        if (string.IsNullOrWhiteSpace(normalizedText) ||
            string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        return normalizedText.Contains($" {normalizedName} ", StringComparison.Ordinal);
    }

    private static string NormalizeForNameMatching(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 2);
        builder.Append(' ');
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(' ');
            }
        }

        builder.Append(' ');
        return Regex.Replace(builder.ToString(), @"\s+", " ");
    }

    private static string[] GetNormalizedTokens(string text)
    {
        var normalized = NormalizeForNameMatching(text).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? []
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool LooksLikeObviousAsrVariant(string userToken, string elementNameToken)
    {
        if (string.IsNullOrWhiteSpace(userToken) ||
            string.IsNullOrWhiteSpace(elementNameToken) ||
            string.Equals(userToken, elementNameToken, StringComparison.Ordinal))
        {
            return false;
        }

        if (userToken.Length < 3 ||
            elementNameToken.Length < 3 ||
            Math.Abs(userToken.Length - elementNameToken.Length) > 2)
        {
            return false;
        }

        if (ComputeLevenshteinDistance(userToken, elementNameToken) <= 1)
        {
            return true;
        }

        return userToken[0] == elementNameToken[0] &&
               string.Equals(
                   BuildLoosePhoneticKey(userToken),
                   BuildLoosePhoneticKey(elementNameToken),
                   StringComparison.Ordinal);
    }

    private static string BuildLoosePhoneticKey(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(token.Length);
        for (var index = 0; index < token.Length; index += 1)
        {
            var character = token[index];
            if (index == 0)
            {
                builder.Append(character);
                continue;
            }

            if (IsLoosePhoneticVowel(character))
            {
                continue;
            }

            if (builder.Length == 0 || builder[^1] != character)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsLoosePhoneticVowel(char character)
        => character is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static int ComputeLevenshteinDistance(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right.Length;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left.Length;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column += 1)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row += 1)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column += 1)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static bool WindowLooksLikeBrowser(JsonElement window)
    {
        var title = TryGetJsonStringProperty(window, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return title.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Google Chrome", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Mozilla Firefox", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Brave", StringComparison.OrdinalIgnoreCase);
    }

    private static bool WindowLooksLikeEdge(JsonElement window)
    {
        var title = TryGetJsonStringProperty(window, "title");
        return !string.IsNullOrWhiteSpace(title) &&
               title.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableWindowBounds(JsonElement window)
    {
        if (!TryGetJsonProperty(window, "bounds", out var bounds) ||
            bounds.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var width = TryGetJsonIntProperty(bounds, "width");
        var height = TryGetJsonIntProperty(bounds, "height");
        var left = TryGetJsonIntProperty(bounds, "left");
        var top = TryGetJsonIntProperty(bounds, "top");

        return width is >= 300 &&
               height is >= 100 &&
               left is > -10000 &&
               top is > -10000;
    }

    private static string? TryGetStringArgument(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString()
        };
    }

    private static bool? TryGetBooleanArgument(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool booleanValue => booleanValue,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            _ when bool.TryParse(value.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool TryGetSelectedWindowProperty(string toolOutputText, out JsonElement selectedWindow)
    {
        selectedWindow = default;
        if (string.IsNullOrWhiteSpace(toolOutputText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(toolOutputText);
            if (!TryGetJsonProperty(document.RootElement, "selectedWindow", out var property))
            {
                return false;
            }

            selectedWindow = property.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetWindowProperty(string? toolOutputText, string propertyName, out JsonElement window)
    {
        window = default;
        if (string.IsNullOrWhiteSpace(toolOutputText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(toolOutputText);
            if (!TryGetJsonProperty(document.RootElement, propertyName, out var property))
            {
                if (!LooksLikeWindowPropertyName(propertyName) ||
                    !JsonElementLooksLikeWindow(document.RootElement))
                {
                    return false;
                }

                window = document.RootElement.Clone();
                return true;
            }

            window = property.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetSnapshotTree(JsonElement root, out JsonElement elementTree)
    {
        if (TryGetJsonProperty(root, "elementTree", out elementTree) &&
            elementTree.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        if (TryGetJsonProperty(root, "compactTree", out elementTree) &&
            elementTree.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        elementTree = default;
        return false;
    }

    private static bool TryGetFocusedSnapshotElement(JsonElement root, out JsonElement focusedElement)
    {
        if (TryGetJsonProperty(root, "focusedElement", out focusedElement) &&
            focusedElement.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        if (TryGetJsonProperty(root, "compactTree", out focusedElement) &&
            focusedElement.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        focusedElement = default;
        return false;
    }

    private static bool LooksLikeWindowPropertyName(string propertyName)
        => string.Equals(propertyName, "window", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(propertyName, "selectedWindow", StringComparison.OrdinalIgnoreCase);

    private static bool JsonElementLooksLikeWindow(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(TryGetJsonStringProperty(element, "title")) ||
               !string.IsNullOrWhiteSpace(TryGetJsonStringProperty(element, "handle")) ||
               !string.IsNullOrWhiteSpace(TryGetJsonStringProperty(element, "className"));
    }

    private static string? DescribeWindowElement(JsonElement windowElement)
    {
        var handle = TryGetJsonStringProperty(windowElement, "handle");
        var title = TryGetJsonStringProperty(windowElement, "title");

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(handle))
        {
            return $"{title} ({handle})";
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return handle;
    }

    private static string? TryGetJsonStringProperty(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool? TryGetJsonBooleanProperty(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? TryGetJsonIntProperty(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        property = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (!string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            property = candidate.Value.Clone();
            return true;
        }

        return false;
    }

    private sealed record WindowListMatch(
        string? Handle,
        string? Title,
        string? ClassName,
        bool IsSelected,
        bool HasUsableBounds);

    private sealed record BrowserWindowCandidate(
        string? Handle,
        string? Title,
        bool IsSelected,
        bool HasUsableBounds,
        bool IsEdge);

    private static bool UserRequestLooksLikeWebsiteNavigation(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        return userText.Contains("website", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("url", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("web page", StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(
                   userText,
                   @"\b[\w-]+\.(com|net|org|io|ai|app|tv|co)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool UserRequestLooksLikeBrowserContentSearch(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        if (userText.Contains("taskbar", StringComparison.OrdinalIgnoreCase)
            || userText.Contains("windows search", StringComparison.OrdinalIgnoreCase)
            || userText.Contains("start menu", StringComparison.OrdinalIgnoreCase)
            || userText.Contains("launch", StringComparison.OrdinalIgnoreCase)
            || userText.Contains("open the app", StringComparison.OrdinalIgnoreCase)
            || userText.Contains("application", StringComparison.OrdinalIgnoreCase)
            || userText.Contains("program", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return userText.Contains("search for", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("look for", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("find ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UserExplicitlyRequestsCurrentTabReuse(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        return userText.Contains("same tab", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("current tab", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("this tab", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("reuse the tab", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("reuse current tab", StringComparison.OrdinalIgnoreCase)
               || userText.Contains("replace this tab", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UserRequestLooksLikeActivation(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        var normalized = NormalizeForNameMatching(userText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return Regex.IsMatch(
            normalized,
            @"\b(select|choose|pick|open|click|invoke|press|play|watch)\b",
            RegexOptions.CultureInvariant)
               || normalized.Contains(" go to ", StringComparison.Ordinal);
    }

    private static bool TryExtractRequestedDiscreteSlotTextFromUserText(string userText, out string value)
        => TryExtractRequestedDiscreteSlotTextFromUserText(
            userText,
            out value,
            out _,
            out _);

    private static bool TryExtractRequestedDiscreteSlotTextFromUserText(
        string userText,
        out string value,
        out string? extractionPattern,
        out int? candidateLength)
    {
        value = string.Empty;
        extractionPattern = null;
        candidateLength = null;
        if (string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        foreach (var (patternName, pattern) in new[]
                 {
                     (
                         "explicit_input",
                         @"\b(?:type(?:\s+in)?|enter|use|input)\s+(?:(?:the|my|your)\s+)?(?:(?:profile\s+)?(?:pin|passcode|pass\s+code|otp|verification\s+code|security\s+code|code)\s+)?(?<value>[A-Za-z0-9]{2,12})\b"),
                     (
                         "code_assignment",
                         @"\b(?:profile\s+)?(?:pin|passcode|pass\s+code|otp|verification\s+code|security\s+code|code)\b\s*(?:is|:|=)\s*(?<value>[A-Za-z0-9]{2,12})\b"),
                     (
                         "bare_digits",
                         @"\b(?<value>\d{2,12})\b"),
                 })
        {
            var match = Regex.Match(userText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            var candidateValue = match.Groups["value"].Value;
            if (!TryExtractMultiCharacterDiscreteSlotText(candidateValue, out value))
            {
                continue;
            }

            extractionPattern = patternName;
            candidateLength = value.Length;
            return true;
        }

        return false;
    }

    private static bool UserTextStronglyMatchesNamedChoice(string userText, string elementName)
    {
        if (TextContainsNormalizedName(userText, elementName))
        {
            return true;
        }

        var elementNameTokens = GetNormalizedTokens(elementName)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (elementNameTokens.Length != 1)
        {
            return false;
        }

        var targetToken = elementNameTokens[0];
        foreach (var userToken in GetNormalizedTokens(userText))
        {
            if (string.Equals(userToken, targetToken, StringComparison.Ordinal) ||
                LooksLikeObviousAsrVariant(userToken, targetToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UserRequestExplicitlyAsksForFocusOnly(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        return userText.Contains("focus", StringComparison.OrdinalIgnoreCase)
               && !UserRequestLooksLikeActivation(userText);
    }

    private sealed record NamedActionTargetCandidate(
        string Path,
        bool HasInterestingAction,
        int NameMatchScore,
        int Specificity,
        int PathDepth);

    internal sealed record NamedTargetRewriteEvaluation(
        string? RequestedPath,
        bool RequestedElementResolved,
        IReadOnlyDictionary<string, object?>? RequestedElementSummary,
        string? RequiredAction,
        bool UserRequestedActivation,
        bool SnapshotContainsNamedChoiceSurface,
        string? MatchedPath,
        IReadOnlyDictionary<string, object?>? MatchedElementSummary,
        bool Rewritten,
        string? SkipReason,
        Dictionary<string, object?>? RewrittenArgs);

}

internal static class Display
{
    private const int LabelWidth = 12;

    public static void Banner()
    {
        Console.WriteLine();
        Console.WriteLine("  ╔══════════════════════════════╗");
        Console.WriteLine("  ║         H E R F A C E        ║");
        Console.WriteLine("  ║  AI voice agent — heronwin   ║");
        Console.WriteLine("  ╚══════════════════════════════╝");
        Console.WriteLine();
    }

    public static void Info(string text)
    {
        Console.WriteLine($"i  {text}");
        DebugTrace.WriteEvent("display.info", text);

        if (text.Equals("Listening...", StringComparison.OrdinalIgnoreCase))
        {
            FaceBridge.PublishStatus("listening", "Listening", "Capturing the user request now.");
        }
        else if (text.StartsWith("Waiting for ", StringComparison.OrdinalIgnoreCase))
        {
            FaceBridge.PublishStatus("standby", "Standby", text);
        }
        else if (text.StartsWith("Back to standby", StringComparison.OrdinalIgnoreCase))
        {
            FaceBridge.PublishStatus("standby", "Standby", text);
        }
        else if (text.StartsWith("Shutting down", StringComparison.OrdinalIgnoreCase))
        {
            FaceBridge.PublishStatus("offline", "Shutting down", text);
        }
    }

    public static void Warn(string text)
    {
        Console.WriteLine($"!  {text}");
        DebugTrace.WriteEvent("display.warn", text);
        FaceBridge.PublishStatus("acting", "Warning", text);
    }

    public static void Error(string text)
    {
        Console.Error.WriteLine($"x  {text}");
        DebugTrace.WriteEvent("display.error", text);
        FaceBridge.PublishStatus("error", "Something went wrong", text);
    }

    public static void Separator()
    {
        Console.WriteLine(new string('─', 60));
        DebugTrace.WriteEvent("display.separator", new string('─', 20));
    }

    public static void Prompt(string text)
    {
        Console.Write(text);
        DebugTrace.WriteEvent("display.prompt", text);
    }

    public static void Recording()
    {
        Console.WriteLine("o  Recording... (stop on silence or timeout)");
        DebugTrace.WriteEvent("display.recording", "Recording... (stop on silence or timeout)");
        FaceBridge.PublishStatus("listening", "Listening", "Recording until silence or timeout.");
    }

    public static void Transcribing()
    {
        Console.WriteLine(".. Transcribing speech...");
        DebugTrace.WriteEvent("display.transcribing", "Transcribing speech...");
        FaceBridge.PublishStatus("transcribing", "Transcribing", "Turning speech into text.");
    }

    public static void Transcript(string text)
    {
        Console.WriteLine($"\n{Label("Heard")} {text}");
        DebugTrace.WriteEvent("display.transcript", DebugTrace.Preview(text, 500));
        FaceBridge.PublishStatus("thinking", "Heard you", text, transcript: text);
    }

    public static void UserMessage(string text)
    {
        Console.WriteLine($"\n{Label("You")} {text}");
        DebugTrace.WriteEvent("display.user_message", DebugTrace.Preview(text, 500));
    }

    public static void AssistantMessage(string text)
    {
        Console.WriteLine($"\n{Label("Assistant")} {text}");
        DebugTrace.WriteEvent("display.assistant_message", DebugTrace.Preview(text, 500));
    }

    public static void AssistantReply(string sayText, string logText)
    {
        var normalizedSay = string.IsNullOrWhiteSpace(sayText) ? string.Empty : sayText.Trim();
        var normalizedLog = string.IsNullOrWhiteSpace(logText) ? string.Empty : logText.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedSay))
        {
            Console.WriteLine($"\n{Label("Say")} {normalizedSay}");
        }

        if (!string.IsNullOrWhiteSpace(normalizedLog))
        {
            Console.WriteLine($"\n{Label("Log")} {normalizedLog}");
        }

        DebugTrace.WriteBlock(
            "display.assistant_reply",
            [
                $"say={DebugTrace.Preview(normalizedSay, 400)}",
                $"log={DebugTrace.Preview(normalizedLog, 900)}"
            ]);

        var detail = !string.IsNullOrWhiteSpace(normalizedSay)
            ? normalizedSay
            : string.IsNullOrWhiteSpace(normalizedLog)
                ? "Reply ready."
                : normalizedLog;
        FaceBridge.PublishStatus("speaking", "Speaking", detail, transcript: normalizedSay);
    }

    public static void IntermediateStep(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Console.WriteLine($"\n{Label("Step")} {text.Trim()}");
        DebugTrace.WriteEvent("display.intermediate_step", DebugTrace.Preview(text, 500));
        FaceBridge.PublishStatus("acting", "Acting", text.Trim());
    }

    public static void ContextUsage(int currentTokens, int maxTokens)
    {
        var ratio = maxTokens <= 0 ? 0 : currentTokens / (double)maxTokens * 100;
        Console.WriteLine($"i  Context: ~{currentTokens:N0} / {maxTokens:N0} tokens ({ratio:F1}%)");
        DebugTrace.WriteEvent(
            "display.context_usage",
            $"current={currentTokens}, max={maxTokens}, ratio={ratio:F1}%");
    }

    public static void ContextCompressed(int currentTokens, int maxTokens)
    {
        var ratio = maxTokens <= 0 ? 0 : currentTokens / (double)maxTokens * 100;
        Console.WriteLine($"i  Context compressed: ~{currentTokens:N0} / {maxTokens:N0} tokens ({ratio:F1}%)");
        DebugTrace.WriteEvent(
            "display.context_compressed",
            $"current={currentTokens}, max={maxTokens}, ratio={ratio:F1}%");
    }

    public static void ToolCall(string toolName, string args)
    {
        Console.WriteLine($"\n{Label("Tool call")} {toolName}");
        if (args != "{}")
        {
            Console.WriteLine($"{new string(' ', LabelWidth + 3)}{args}");
        }

        DebugTrace.WriteBlock(
            "display.tool_call",
            [
                $"tool={toolName}",
                $"arguments={DebugTrace.Preview(args, 600)}"
            ]);
        FaceBridge.PublishStatus("acting", "Acting", $"Calling {toolName}.", toolName: toolName);
    }

    public static void ToolResult(string toolName, string result, int imageCount = 0)
    {
        var preview = result.Length > 200 ? $"{result[..200]}..." : result;
        Console.WriteLine($"{Label("Tool result")} ({toolName})");
        Console.WriteLine($"{new string(' ', LabelWidth + 3)}{preview}");
        if (imageCount > 0)
        {
            Console.WriteLine($"{new string(' ', LabelWidth + 3)}[{imageCount} screenshot attachment(s)]");
        }

        DebugTrace.WriteBlock(
            "display.tool_result",
            [
                $"tool={toolName}",
                $"images={imageCount}",
                $"result={DebugTrace.Preview(result, 900)}"
            ]);
        FaceBridge.PublishStatus("acting", "Acting", $"{toolName} finished.", toolName: toolName);
    }

    private static string Label(string text) => $"[{text.PadRight(LabelWidth)}]";
}

internal static class JsonSerializerOptionsCache
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

internal static class AssistantResponseParser
{
    public static bool IsStructuredJson(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawText);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && (document.RootElement.TryGetProperty("say", out _)
                       || document.RootElement.TryGetProperty("log", out _));
        }
        catch
        {
            return false;
        }
    }

    public static AgentReply Parse(string rawText)
    {
        if (!string.IsNullOrWhiteSpace(rawText))
        {
            try
            {
                using var document = JsonDocument.Parse(rawText);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var say = document.RootElement.TryGetProperty("say", out var sayElement)
                        ? sayElement.GetString() ?? string.Empty
                        : string.Empty;
                    var log = document.RootElement.TryGetProperty("log", out var logElement)
                        ? logElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (!string.IsNullOrWhiteSpace(say) || !string.IsNullOrWhiteSpace(log))
                    {
                        return new AgentReply(
                            string.IsNullOrWhiteSpace(log) ? say : log,
                            say,
                            rawText);
                    }
                }
            }
            catch
            {
                // Fall through to plain-text fallback.
            }
        }

        return new AgentReply(rawText, BuildSpeechFallback(rawText), rawText);
    }

    internal static string BuildSpeechFallback(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var normalized = rawText
            .Replace("**", string.Empty)
            .Replace("`", string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ");

        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^\s*[-*•]+\s*", string.Empty);

        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var sentenceEnd = normalized.IndexOfAny(['.', '!', '?']);
        var spoken = sentenceEnd >= 0
            ? normalized[..(sentenceEnd + 1)]
            : normalized;

        return spoken.Length > 180 ? $"{spoken[..177].Trim()}..." : spoken.Trim();
    }
}

internal static class ContextManager
{
    private const int KeepRecentMessages = 8;

    public static int EstimateTokens(
        IReadOnlyList<AgentMessage> history,
        string systemPrompt,
        string? pendingUserText = null)
    {
        var total = EstimateTextTokens(systemPrompt) + 16;
        foreach (var message in history)
        {
            total += message switch
            {
                AgentMessage.User user => EstimateTextTokens(user.Content) + 8,
                AgentMessage.Summary summary => EstimateTextTokens(summary.Content) + 12,
                AgentMessage.VisualContext visual => EstimateTextTokens(visual.Content) + (visual.Images.Count * 512) + 16,
                AgentMessage.Assistant assistant => EstimateTextTokens(assistant.Content ?? string.Empty) + 8,
                AgentMessage.ToolResult toolResult => EstimateTextTokens(toolResult.Content) + 16,
                _ => 8
            };
        }

        if (!string.IsNullOrWhiteSpace(pendingUserText))
        {
            total += EstimateTextTokens(pendingUserText) + 8;
        }

        return total;
    }

    public static async Task EnsureCapacityAsync(
        List<AgentMessage> history,
        string pendingUserText,
        string systemPrompt,
        int maxContextTokens,
        LlmModelProfile modelProfile,
        ILlmClient llmClient,
        CancellationToken cancellationToken)
    {
        var currentTokens = EstimateTokens(history, systemPrompt, pendingUserText);
        Display.ContextUsage(currentTokens, maxContextTokens);

        if (maxContextTokens <= 0
            || currentTokens < maxContextTokens * modelProfile.ContextCompressionTriggerRatio
            || history.Count <= KeepRecentMessages)
        {
            return;
        }

        var splitIndex = Math.Max(1, history.Count - KeepRecentMessages);
        var messagesToCompress = history.Take(splitIndex).ToList();
        DebugTrace.WriteEvent(
            "context.compression_requested",
            $"model={modelProfile.ModelName}, triggerRatio={modelProfile.ContextCompressionTriggerRatio:F2}, messagesToCompress={messagesToCompress.Count}, historyMessages={history.Count}, pendingUserText={DebugTrace.Preview(pendingUserText, 300)}");
        var summaryText = await SummarizeAsync(messagesToCompress, llmClient, cancellationToken);

        history.RemoveRange(0, splitIndex);
        history.Insert(0, new AgentMessage.Summary(summaryText));

        var compressedTokens = EstimateTokens(history, systemPrompt, pendingUserText);
        Display.ContextCompressed(compressedTokens, maxContextTokens);
    }

    private static async Task<string> SummarizeAsync(
        IReadOnlyList<AgentMessage> messages,
        ILlmClient llmClient,
        CancellationToken cancellationToken)
    {
        var transcript = new StringBuilder();
        foreach (var message in messages)
        {
            switch (message)
            {
                case AgentMessage.User user:
                    transcript.AppendLine($"User: {user.Content}");
                    break;
                case AgentMessage.Summary summary:
                    transcript.AppendLine($"Prior summary: {summary.Content}");
                    break;
                case AgentMessage.Assistant assistant:
                    transcript.AppendLine($"Assistant: {assistant.Content}");
                    break;
            }
        }

        var summaryPrompt = """
Summarize the earlier conversation into a compact factual memory for future turns.
Keep only durable context:
- user preferences and instructions
- unresolved tasks or constraints
- important UI/app state that may still matter
- important factual findings from prior turns

Return plain text only, under 250 words, with short lines.
""";

        var result = await llmClient.ChatAsync(
            [new AgentMessage.User($"{summaryPrompt}\n\nConversation:\n{transcript}")],
            [],
            systemPrompt: null,
            cancellationToken: cancellationToken);

        var summaryText = result.Text ?? string.Empty;
        if (AssistantResponseParser.IsStructuredJson(summaryText))
        {
            var parsed = AssistantResponseParser.Parse(summaryText);
            summaryText = string.IsNullOrWhiteSpace(parsed.LogText) ? parsed.SpokenText : parsed.LogText;
        }

        summaryText = summaryText.Trim();
        DebugTrace.WriteEvent("context.summary_created", DebugTrace.Preview(summaryText, 900));
        return string.IsNullOrWhiteSpace(summaryText)
            ? "Earlier conversation was compressed, but no durable context was extracted."
            : summaryText;
    }

    private static int EstimateTextTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (text.Length + 3) / 4);
    }
}
