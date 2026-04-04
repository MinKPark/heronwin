using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace HeronWin.HerFace;

internal sealed record ToolDefinition(string Name, string Description, JsonElement Parameters);

internal sealed record ToolCallRequest(string Id, string Name, string Arguments);

internal sealed record ToolImage(string MimeType, string Base64Data, string Detail = "low");

internal sealed record ToolCallOutcome(string Text, IReadOnlyList<ToolImage> Images, bool IsError = false);

internal sealed record AgentReply(string LogText, string SpokenText, string RawText);

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
    public static async Task<AgentReply> RunTurnAsync(
        long turnId,
        string userText,
        List<AgentMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        ComposedAgentPrompt composedPrompt,
        ILlmClient llmClient,
        McpClientManager mcpManager,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task>? intermediateStepNarrator = null)
    {
        var turnStopwatch = Stopwatch.StartNew();
        var messages = history.ToList();
        var availableToolNames = tools
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        var runtimeToolPolicy = BuildRuntimeToolPolicy(tools);
        if (!string.IsNullOrWhiteSpace(runtimeToolPolicy))
        {
            messages.Add(new AgentMessage.Summary(runtimeToolPolicy));
        }

        var ordinalActionSummary = BuildOrdinalActionReferenceSummary(userText, history);
        if (!string.IsNullOrWhiteSpace(ordinalActionSummary))
        {
            messages.Add(new AgentMessage.Summary(ordinalActionSummary));
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
            });
        Display.UserMessage(userText);
        var usedAnyTools = false;
        var performedDesktopAction = false;
        var performedConfidenceEvidenceRetry = false;
        var llmAttempt = 0;
        string? recentListWindowsOutput = null;
        string? recentWindowContext = null;
        string? recentFocusContext = null;

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

                if (performedDesktopAction
                    && !performedConfidenceEvidenceRetry
                    && NeedsAdditionalDesktopEvidence(parsedReply))
                {
                    performedConfidenceEvidenceRetry = true;
                    DebugTrace.WriteStructuredEvent(
                        "agent.additional_desktop_evidence_requested",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["reason"] = "assistant_uncertain",
                            ["sayPreview"] = DebugTrace.Preview(parsedReply.SpokenText, 300),
                            ["logPreview"] = DebugTrace.Preview(parsedReply.LogText, 500),
                        });
                    messages.Add(new AgentMessage.Assistant(responseText));
                    messages.AddRange(await CollectAdditionalConfidenceEvidenceAsync(
                        turnId,
                        mcpManager,
                        llmClient.ModelProfile,
                        cancellationToken));
                    continue;
                }

                Display.AssistantReply(parsedReply.SpokenText, parsedReply.LogText);
                DebugTrace.WriteStructuredEvent(
                    "assistant.reply",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["elapsedMs"] = (int)Math.Round(turnStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                        ["attempts"] = llmAttempt,
                        ["usedAnyTools"] = usedAnyTools,
                        ["performedDesktopAction"] = performedDesktopAction,
                        ["performedConfidenceEvidenceRetry"] = performedConfidenceEvidenceRetry,
                        ["sayText"] = parsedReply.SpokenText,
                        ["logText"] = parsedReply.LogText,
                        ["sayPreview"] = DebugTrace.Preview(parsedReply.SpokenText, 400),
                        ["logPreview"] = DebugTrace.Preview(parsedReply.LogText, 900),
                        ["rawPreview"] = DebugTrace.Preview(responseText, 1200),
                    });

                messages.Add(new AgentMessage.Assistant(responseText));
                return parsedReply with { RawText = responseText };
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
                        ["argumentsPreview"] = DebugTrace.Preview(toolCall.Arguments, 600),
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
                var intermediateStep = BuildToolStepNarration(toolCall.Name, parsedArgsDictionary);
                if (!string.IsNullOrWhiteSpace(intermediateStep))
                {
                    Display.IntermediateStep(intermediateStep);
                    DebugTrace.WriteEvent(
                        "agent.tool_step_narration",
                        $"turn={turnId}, toolCallId={toolCall.Id}, tool={toolCall.Name}, step={DebugTrace.Preview(intermediateStep, 240)}");

                    if (intermediateStepNarrator is not null)
                    {
                        try
                        {
                            await intermediateStepNarrator(intermediateStep, cancellationToken);
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

                var executableArgs = CloneArguments(parsedArgsDictionary);
                var effectiveArgumentsText = toolCall.Arguments;
                var executableToolName = toolCall.Name;
                string? toolRewriteNote = null;
                var attemptedBrowserFullscreenExit = false;
                var attemptedBrowserWindowPreflight = false;

                async Task MaybeExitBrowserFullscreenAsync(string reason)
                {
                    if (attemptedBrowserFullscreenExit ||
                        !ShouldExitBrowserFullscreenBeforeBrowserShortcut(recentWindowContext))
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
                        var exitResult = await mcpManager.CallToolAsync("send_input_to_window", exitArgs, cancellationToken);
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

                        if (!exitResult.IsError &&
                            DescribePrimaryWindowFromToolOutput(exitResult.Text) is not null)
                        {
                            recentWindowContext = exitResult.Text;
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
                            var listResult = await mcpManager.CallToolAsync(
                                "list_windows",
                                new Dictionary<string, object?>(),
                                cancellationToken);
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
                                recentListWindowsOutput = listResult.Text;
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

                    if (availableToolNames.Contains("select_window") &&
                        TryBuildBrowserSelectionArguments(recentListWindowsOutput, out var browserSelectionArgs))
                    {
                        try
                        {
                            var selectionResult = await mcpManager.CallToolAsync(
                                "select_window",
                                browserSelectionArgs,
                                cancellationToken);
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_window_preflight_select_window",
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
                                recentWindowContext = selectionResult.Text;
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugTrace.WriteStructuredEvent(
                                "agent.browser_window_preflight_select_window_failed",
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

                    if (!availableToolNames.Contains("launch_app_via_taskbar_search"))
                    {
                        return;
                    }

                    try
                    {
                        var launchArgs = new Dictionary<string, object?>
                        {
                            ["appName"] = "Microsoft Edge",
                        };
                        var launchResult = await mcpManager.CallToolAsync(
                            "launch_app_via_taskbar_search",
                            launchArgs,
                            cancellationToken);
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
                            recentWindowContext = launchResult.Text;
                        }

                        var followUpSelectionArgs = TryBuildLaunchFollowUpSelectionArguments(launchResult.Text);
                        if (followUpSelectionArgs is not null &&
                            availableToolNames.Contains("select_window"))
                        {
                            var followUpResult = await mcpManager.CallToolAsync(
                                "select_window",
                                followUpSelectionArgs,
                                cancellationToken);
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
                                recentWindowContext = followUpResult.Text;
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

                if (toolCall.Name == "select_window" &&
                    TryRewriteSelectWindowArguments(executableArgs, recentListWindowsOutput, out var rewrittenSelectArgs))
                {
                    executableArgs = rewrittenSelectArgs;
                    effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_arguments_rewritten",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["reason"] = "recent_list_windows_handle_preferred",
                            ["originalArgumentsPreview"] = DebugTrace.Preview(toolCall.Arguments, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.Preview(effectiveArgumentsText, 600),
                        });
                }

                if (TryRewriteBrowserSearchControlAction(
                        userText,
                        toolCall.Name,
                        executableArgs,
                        recentWindowContext,
                        out var rewrittenBrowserSearchArgs))
                {
                    executableArgs = rewrittenBrowserSearchArgs;
                    effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
                    DebugTrace.WriteStructuredEvent(
                        "agent.tool_call_arguments_rewritten",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["toolCallId"] = toolCall.Id,
                            ["tool"] = toolCall.Name,
                            ["reason"] = "browser_site_search_control_preferred",
                            ["originalArgumentsPreview"] = DebugTrace.Preview(toolCall.Arguments, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.Preview(effectiveArgumentsText, 600),
                        });
                }

                if (TryRewriteBrowserAddressBarActionToShortcut(
                        toolCall.Name,
                        executableArgs,
                        recentWindowContext,
                        out var rewrittenBrowserAddressBarArgs))
                {
                    await MaybeExitBrowserFullscreenAsync("browser_address_bar_shortcut_rewrite");
                    executableToolName = "send_input_to_window";
                    executableArgs = rewrittenBrowserAddressBarArgs;
                    effectiveArgumentsText = JsonSerializer.Serialize(executableArgs, JsonSerializerOptionsCache.Default);
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
                            ["originalArgumentsPreview"] = DebugTrace.Preview(toolCall.Arguments, 600),
                            ["rewrittenArgumentsPreview"] = DebugTrace.Preview(effectiveArgumentsText, 600),
                        });
                }

                await MaybeEnsureBrowserWindowBeforeBrowserNavigationAsync("browser_navigation_preflight");

                if (executableToolName == "send_input_to_window" &&
                    LooksLikeUrl(TryGetStringArgument(executableArgs, "text") ?? string.Empty))
                {
                    await MaybeExitBrowserFullscreenAsync("browser_url_entry");
                }

                if (executableToolName == "send_input_to_window" &&
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
                        var newTabResult = await mcpManager.CallToolAsync("send_input_to_window", newTabArgs, cancellationToken);
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
                            recentWindowContext = newTabResult.Text;
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

                if (executableToolName == "send_input_to_window" &&
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
                        var primeResult = await mcpManager.CallToolAsync("send_input_to_window", primeArgs, cancellationToken);
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
                            recentWindowContext = primeResult.Text;
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
                            ["executedTool"] = executableToolName,
                            ["elapsedMs"] = 0,
                            ["isError"] = true,
                            ["images"] = 0,
                            ["resultPreview"] = DebugTrace.Preview(blockedMessage, 900),
                        });
                    messages.Add(new AgentMessage.ToolResult(toolCall.Id, toolCall.Name, blockedMessage));
                    continue;
                }

                Display.ToolCall(executableToolName, effectiveArgumentsText);

                var toolCallStopwatch = Stopwatch.StartNew();
                ToolCallOutcome toolOutput;
                try
                {
                    toolOutput = await mcpManager.CallToolAsync(executableToolName, executableArgs, cancellationToken);
                }
                catch (Exception ex)
                {
                    toolOutput = new ToolCallOutcome($"Error: {ex.Message}", [], true);
                }

                Display.ToolResult(executableToolName, toolOutput.Text, toolOutput.Images.Count);
                DebugTrace.WriteStructuredEvent(
                    "agent.tool_call_completed",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = turnId,
                        ["toolCallId"] = toolCall.Id,
                        ["tool"] = toolCall.Name,
                        ["executedTool"] = executableToolName,
                        ["elapsedMs"] = (int)Math.Round(toolCallStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                        ["isError"] = toolOutput.IsError,
                        ["images"] = toolOutput.Images.Count,
                        ["resultPreview"] = DebugTrace.Preview(toolOutput.Text, 900),
                    });
                var toolResultContext = UiSnapshotCompactor.CompactToolTextForContext(
                    toolCall.Name,
                    toolOutput.Text,
                    llmClient.ModelProfile);
                messages.Add(new AgentMessage.ToolResult(toolCall.Id, toolCall.Name, toolResultContext, toolOutput.Images));

                if (!toolOutput.IsError)
                {
                    if (toolCall.Name == "list_windows")
                    {
                        recentListWindowsOutput = toolOutput.Text;
                    }

                    if (DescribePrimaryWindowFromToolOutput(toolOutput.Text) is not null)
                    {
                        recentWindowContext = toolOutput.Text;
                    }

                    if (toolCall.Name is "focus_selected_window_element" or "describe_selected_window_focus")
                    {
                        recentFocusContext = toolOutput.Text;
                    }
                }

                if (toolOutput.Images.Count > 0)
                {
                    followUpEvidence.Add(new AgentMessage.VisualContext(
                        $"Supplemental screenshot output from tool \"{toolCall.Name}\". Treat these images as the source of truth for what is visibly on screen before answering.",
                        toolOutput.Images));
                }

                if (IsDesktopActionTool(toolCall.Name))
                {
                    performedDesktopAction = true;
                    DebugTrace.WriteStructuredEvent(
                        "agent.desktop_action_tool_detected",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["tool"] = toolCall.Name,
                            ["toolIsError"] = toolOutput.IsError,
                            ["reportedWindow"] = DescribePrimaryWindowFromToolOutput(toolOutput.Text),
                        });
                    try
                    {
                        if (toolCall.Name == "launch_app_via_taskbar_search")
                        {
                            var appName = TryGetStringArgument(parsedArgsDictionary, "appName");
                            if (!string.IsNullOrWhiteSpace(appName))
                            {
                                var followUpSelectionArgs = TryBuildLaunchFollowUpSelectionArguments(toolOutput.Text);
                                if (followUpSelectionArgs is not null)
                                {
                                    try
                                    {
                                        var followUpSelectionStopwatch = Stopwatch.StartNew();
                                        var selectResult = await mcpManager.CallToolAsync(
                                            "select_window",
                                            followUpSelectionArgs,
                                            cancellationToken);
                                        Display.ToolResult("select_window", selectResult.Text, selectResult.Images.Count);
                                        followUpEvidence.Add(new AgentMessage.User(
                                            $"Internal window re-selection after launching \"{appName}\":\n{selectResult.Text}"));
                                        if (selectResult.Images.Count > 0)
                                        {
                                            followUpEvidence.Add(new AgentMessage.VisualContext(
                                                "Internal screenshot evidence after re-selecting the launched app window. Treat the screenshot as authoritative for the current visible screen.",
                                                selectResult.Images));
                                        }
                                        DebugTrace.WriteStructuredEvent(
                                            "agent.desktop_followup_select_window",
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
                                            "agent.desktop_followup_select_window_failed",
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
                                        "agent.desktop_followup_select_window_skipped",
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
                        var postActionSnapshot = await mcpManager.CallToolAsync(
                            "describe_selected_window",
                            new Dictionary<string, object?> { ["maxDepth"] = 4 },
                            cancellationToken);
                        Display.ToolResult("describe_selected_window", postActionSnapshot.Text, postActionSnapshot.Images.Count);
                        recentWindowContext = postActionSnapshot.Text;
                        var compactPostActionSnapshot = UiSnapshotCompactor.CompactToolTextForContext(
                            "describe_selected_window",
                            postActionSnapshot.Text,
                            llmClient.ModelProfile);
                        followUpEvidence.Add(new AgentMessage.User(
                            $"Post-action visible UI snapshot after tool \"{toolCall.Name}\":\n{compactPostActionSnapshot}\nUse this UI Automation tree first. If it is too sparse or ambiguous to describe the visible screen confidently, call capture_selected_window_screenshot before answering."));
                        if (postActionSnapshot.Images.Count > 0)
                        {
                            followUpEvidence.Add(new AgentMessage.VisualContext(
                                "Post-action visual evidence for the current selected window. Use it only if you actually have image content available.",
                                postActionSnapshot.Images));
                        }
                        DebugTrace.WriteStructuredEvent(
                            "agent.desktop_followup_snapshot",
                            new Dictionary<string, object?>
                            {
                                ["turn"] = turnId,
                                ["tool"] = toolCall.Name,
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
                                ["tool"] = toolCall.Name,
                                ["actionReportedWindow"] = DescribePrimaryWindowFromToolOutput(toolOutput.Text),
                                ["snapshotWindow"] = DescribePrimaryWindowFromToolOutput(postActionSnapshot.Text),
                                ["sourceOfTruth"] = postActionSnapshot.Images.Count > 0 ? "uia_plus_screenshot" : "uia_tree",
                            });

                        if (ShouldCollectFocusSnapshotAfterAction(toolCall.Name, parsedArgsDictionary))
                        {
                            var focusSnapshotStopwatch = Stopwatch.StartNew();
                            var focusSnapshot = await mcpManager.CallToolAsync(
                                "describe_selected_window_focus",
                                new Dictionary<string, object?> { ["maxDepth"] = 3 },
                                cancellationToken);
                            Display.ToolResult("describe_selected_window_focus", focusSnapshot.Text, focusSnapshot.Images.Count);
                            recentFocusContext = focusSnapshot.Text;
                            var compactFocusSnapshot = UiSnapshotCompactor.CompactToolTextForContext(
                                "describe_selected_window_focus",
                                focusSnapshot.Text,
                                llmClient.ModelProfile);
                            followUpEvidence.Add(new AgentMessage.User(
                                $"Post-action focused element snapshot after tool \"{toolCall.Name}\":\n{compactFocusSnapshot}\nTreat this focused subtree as fresher evidence than any older focus assumptions before sending more navigation keys."));
                            DebugTrace.WriteStructuredEvent(
                                "agent.desktop_followup_focus_snapshot",
                                new Dictionary<string, object?>
                                {
                                    ["turn"] = turnId,
                                    ["tool"] = toolCall.Name,
                                    ["elapsedMs"] = (int)Math.Round(focusSnapshotStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                                    ["images"] = focusSnapshot.Images.Count,
                                    ["resultPreview"] = DebugTrace.Preview(focusSnapshot.Text, 700),
                                });
                        }

                        if (!string.IsNullOrWhiteSpace(toolRewriteNote))
                        {
                            followUpEvidence.Add(new AgentMessage.User(toolRewriteNote));
                        }

                        var toolSpecificGuidance = BuildToolSpecificGuidance(toolCall.Name, toolOutput.Text, parsedArgsDictionary);
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
            }

            messages.AddRange(followUpEvidence);
        }
    }

    private static bool IsDesktopActionTool(string toolName)
        => toolName is "launch_app_via_taskbar_search"
            or "select_taskbar_app"
            or "select_window"
            or "focus_selected_window_element"
            or "invoke_selected_window_element"
            or "invoke_main_menu_item"
            or "invoke_context_menu_item"
            or "send_input_to_window";

    internal static string? BuildRuntimeToolPolicy(IReadOnlyList<ToolDefinition> tools)
    {
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var parts = new List<string>();

        if (toolNames.Contains("select_window"))
        {
            parts.Add(
                "When recent tool evidence already provides a stable target identifier such as `windowHandle`, prefer reusing that exact identifier over a broader text match.");
        }

        if (toolNames.Contains("send_input_to_window"))
        {
            parts.Add(
                "Treat `send_input_to_window` as explicit keyboard or text input that still requires follow-up verification; key presses and text entry alone do not confirm that the intended visible UI result occurred.");
        }

        if (toolNames.Contains("describe_selected_window") || toolNames.Contains("capture_selected_window_screenshot"))
        {
            parts.Add(
                "For conditional instructions such as \"if profile selection is visible\" or \"if a passcode is required\", inspect the current UI first. If the condition is present and the user named a target or action, perform that action instead of stopping just because the target is visible. If the condition is absent, treat that conditional step as a successful no-op instead of forcing a click, and phrase the reply as \"condition not present, so no action was needed\" rather than as an incomplete or failed outcome.");
        }

        if (toolNames.Contains("launch_app_via_taskbar_search"))
        {
            parts.Add(
                "Use `launch_app_via_taskbar_search` only for launching Windows apps. When a browser window is already selected and the user wants to search for content such as a show, movie, article, or page, keep the interaction inside the browser or website instead of using Windows Search.");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
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
        if (toolName != "send_input_to_window")
        {
            return false;
        }

        var key = TryGetStringArgument(args, "key");
        return key is not null && key.Trim() is "Tab" or "Left" or "Right" or "Up" or "Down";
    }

    internal static string? BuildToolStepNarration(
        string toolName,
        IReadOnlyDictionary<string, object?> args)
    {
        return toolName switch
        {
            "list_windows" => "I'm checking what's already open.",
            "select_window" => BuildSelectWindowNarration(args),
            "describe_selected_window" => "I'm checking the current window.",
            "describe_selected_window_focus" => "I'm checking where focus landed.",
            "capture_selected_window_screenshot" => "I'm taking a quick screenshot.",
            "list_taskbar_elements" => "I'm checking the taskbar.",
            "select_taskbar_app" => BuildTaskbarSelectionNarration(args),
            "launch_app_via_taskbar_search" => BuildTaskbarSearchLaunchNarration(args),
            "list_main_menu_items" => "I'm checking the app menu.",
            "invoke_main_menu_item" => "I'm choosing that menu item.",
            "list_context_menu_items" => "I'm checking the context menu.",
            "invoke_context_menu_item" => "I'm choosing that context menu item.",
            "focus_selected_window_element" => BuildElementFocusNarration(args),
            "invoke_selected_window_element" => BuildElementInvokeNarration(args),
            "send_input_to_window" => BuildSendInputNarration(args),
            _ => null
        };
    }

    internal static string? BuildToolSpecificGuidance(
        string toolName,
        string toolOutputText,
        IReadOnlyDictionary<string, object?> args)
    {
        if (IsLaunchAttemptWithoutSelectedWindow(toolName, toolOutputText))
        {
            return toolName switch
            {
                "select_taskbar_app" =>
                    "The taskbar app activation did not surface a launched or selected app window. Do not imply that the app opened successfully, and do not treat the unchanged current window as the requested app just because it is still visible. Use fresh evidence before deciding what happened, and if another materially different launch route is available, prefer that over repeating the same route.",
                "launch_app_via_taskbar_search" =>
                    "The taskbar search launch did not surface a launched app window. Do not imply that the app opened successfully, do not assume a same-title app window exists just because Search shows a matching result, and do not treat the unchanged current window as the requested app just because it is still visible. Use fresh evidence before deciding what happened, and if another materially different launch route is available, prefer that next.",
                _ => null
            };
        }

        if (toolName != "send_input_to_window")
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

    private static bool IsLaunchAttemptWithoutSelectedWindow(string toolName, string toolOutputText)
    {
        if (toolName is not "select_taskbar_app" and not "launch_app_via_taskbar_search")
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
            ? $"I'm switching to {title}."
            : "I'm switching to the right window.";
    }

    private static string BuildTaskbarSelectionNarration(IReadOnlyDictionary<string, object?> args)
    {
        var title = TryGetShortSpeechLabel(TryGetStringArgument(args, "titleContains"));
        return title is not null
            ? $"I'm opening {title} from the taskbar."
            : "I'm opening it from the taskbar.";
    }

    private static string BuildTaskbarSearchLaunchNarration(IReadOnlyDictionary<string, object?> args)
    {
        var appName = TryGetShortSpeechLabel(TryGetStringArgument(args, "appName"));
        return appName is not null
            ? $"I'm launching {appName} from Search."
            : "I'm launching it from Search.";
    }

    private static string BuildElementFocusNarration(IReadOnlyDictionary<string, object?> args)
    {
        var elementPath = TryGetStringArgument(args, "elementPath");
        return string.Equals(elementPath, "root", StringComparison.OrdinalIgnoreCase)
            ? "I'm refocusing the current window."
            : "I'm focusing that control.";
    }

    private static string BuildElementInvokeNarration(IReadOnlyDictionary<string, object?> args)
    {
        var elementPath = TryGetStringArgument(args, "elementPath");
        return string.Equals(elementPath, "root", StringComparison.OrdinalIgnoreCase)
            ? "I'm activating the current page."
            : "I'm activating that control.";
    }

    private static string BuildSendInputNarration(IReadOnlyDictionary<string, object?> args)
    {
        var text = TryGetStringArgument(args, "text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return LooksLikeUrl(text)
                ? "I'm typing the URL."
                : "I'm typing the requested text.";
        }

        var key = TryGetStringArgument(args, "key");
        if (string.IsNullOrWhiteSpace(key))
        {
            return "I'm sending the next input.";
        }

        var modifiers = GetModifierNames(args);
        if (modifiers.Count > 0)
        {
            var shortcutParts = modifiers
                .Select(NormalizeModifierLabel)
                .Append(NormalizeKeyLabel(key));
            return $"I'm pressing {string.Join(" plus ", shortcutParts)}.";
        }

        return key.Trim() switch
        {
            "Tab" => "I'm moving to the next field.",
            "Left" => "I'm moving left.",
            "Right" => "I'm moving right.",
            "Up" => "I'm moving up.",
            "Down" => "I'm moving down.",
            _ => $"I'm pressing {NormalizeKeyLabel(key)}."
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

    private static string? GetReplyOutcomeContradictionRule(AgentReply reply)
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
            || combined.Contains("uncertain", StringComparison.Ordinal)
            || combined.Contains("ambigu", StringComparison.Ordinal)
            || combined.Contains("too sparse", StringComparison.Ordinal)
            || combined.Contains("not exposed", StringComparison.Ordinal)
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

        var combined = text.ToLowerInvariant();
        if (LooksLikeConditionalNoOpOutcome(combined))
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
            || combined.Contains("not open", StringComparison.Ordinal)
            || combined.Contains("not opened", StringComparison.Ordinal)
            || combined.Contains("not loaded", StringComparison.Ordinal)
            || combined.Contains("not yet", StringComparison.Ordinal)
            || combined.Contains("still showing", StringComparison.Ordinal)
            || combined.Contains("still on", StringComparison.Ordinal);
    }

    private static bool LooksLikeConditionalNoOpOutcome(string combinedLowerText)
    {
        return combinedLowerText.Contains("condition was absent", StringComparison.Ordinal)
               || combinedLowerText.Contains("condition is absent", StringComparison.Ordinal)
               || combinedLowerText.Contains("requested condition was absent", StringComparison.Ordinal)
               || combinedLowerText.Contains("conditional no-op", StringComparison.Ordinal)
               || combinedLowerText.Contains("no action was needed", StringComparison.Ordinal)
               || combinedLowerText.Contains("no action needed", StringComparison.Ordinal)
               || (combinedLowerText.Contains("no profile selection prompt", StringComparison.Ordinal)
                   && combinedLowerText.Contains("did not click anything", StringComparison.Ordinal))
               || (combinedLowerText.Contains("no profile selection screen", StringComparison.Ordinal)
                   && combinedLowerText.Contains("did not click anything", StringComparison.Ordinal))
               || (combinedLowerText.Contains("not a profile selection screen", StringComparison.Ordinal)
                   && combinedLowerText.Contains("did not click anything", StringComparison.Ordinal))
               || (combinedLowerText.Contains("did not see a profile-selection screen", StringComparison.Ordinal)
                   && combinedLowerText.Contains("no profile to choose", StringComparison.Ordinal))
               || (combinedLowerText.Contains("not a profile picker", StringComparison.Ordinal)
                   && combinedLowerText.Contains("did not click anything", StringComparison.Ordinal))
               || (combinedLowerText.Contains("no passcode prompt", StringComparison.Ordinal)
                   && combinedLowerText.Contains("not needed", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<AgentMessage>> CollectAdditionalConfidenceEvidenceAsync(
        long turnId,
        McpClientManager mcpManager,
        LlmModelProfile modelProfile,
        CancellationToken cancellationToken)
    {
        var evidenceStopwatch = Stopwatch.StartNew();
        Display.Info("The first pass was uncertain; waiting 1 second and collecting another UI snapshot plus screenshot.");
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
                "Your first draft sounded uncertain about the current visible state. Wait 1 more second, then use the new UI Automation snapshot and screenshot below before answering. Prefer the newest screenshot as the source of truth for what is visibly on screen if UI Automation is still sparse.")
        };

        await Task.Delay(1000, cancellationToken);

        try
        {
            var snapshotStopwatch = Stopwatch.StartNew();
            var retrySnapshot = await mcpManager.CallToolAsync(
                "describe_selected_window",
                new Dictionary<string, object?> { ["maxDepth"] = 4 },
                cancellationToken);
            Display.ToolResult("describe_selected_window", retrySnapshot.Text, retrySnapshot.Images.Count);
            var compactRetrySnapshot = UiSnapshotCompactor.CompactToolTextForContext(
                "describe_selected_window",
                retrySnapshot.Text,
                modelProfile);
            extraEvidence.Add(new AgentMessage.User(
                $"Second-pass visible UI snapshot after waiting 1 more second:\n{compactRetrySnapshot}"));
            if (retrySnapshot.Images.Count > 0)
            {
                extraEvidence.Add(new AgentMessage.VisualContext(
                    "Second-pass visual evidence returned with the UI snapshot. Use it if actual image content is present.",
                    retrySnapshot.Images));
            }
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

        try
        {
            var screenshotStopwatch = Stopwatch.StartNew();
            var screenshot = await mcpManager.CallToolAsync(
                "capture_selected_window_screenshot",
                new Dictionary<string, object?>(),
                cancellationToken);
            Display.ToolResult("capture_selected_window_screenshot", screenshot.Text, screenshot.Images.Count);
            extraEvidence.Add(new AgentMessage.User(
                $"Second-pass screenshot capture after waiting 1 more second:\n{screenshot.Text}"));
            if (screenshot.Images.Count > 0)
            {
                extraEvidence.Add(new AgentMessage.VisualContext(
                    "Second-pass screenshot evidence after waiting 1 more second. Treat this as the source of truth for the visible screen.",
                    screenshot.Images));
            }
            DebugTrace.WriteStructuredEvent(
                "agent.additional_desktop_evidence_screenshot",
                new Dictionary<string, object?>
                {
                    ["turn"] = turnId,
                    ["elapsedMs"] = (int)Math.Round(screenshotStopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                    ["window"] = DescribePrimaryWindowFromToolOutput(screenshot.Text),
                    ["images"] = screenshot.Images.Count,
                    ["resultPreview"] = DebugTrace.Preview(screenshot.Text, 700),
                });
        }
        catch (Exception ex)
        {
            extraEvidence.Add(new AgentMessage.User(
                $"Second-pass screenshot capture after waiting 1 more second was unavailable: {ex.Message}"));
            DebugTrace.WriteStructuredEvent(
                "agent.additional_desktop_evidence_screenshot_failed",
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
            ? "Rewrite your previous answer as strict JSON only: {\"say\":\"...\",\"log\":\"...\"}. Use the post-action UI Automation tree first. If the current evidence is too sparse or ambiguous to describe the visible screen confidently, do not guess. `say` and `log` must not contradict each other. If the evidence shows the request is incomplete or failed, `say` must also say that clearly. In say, include the action outcome, the current visible screen state if it is supported by evidence, and 2 or 3 likely next actions. In log, include the fuller evidence-based description and briefly note any uncertainty."
            : "Rewrite your previous answer as strict JSON only: {\"say\":\"...\",\"log\":\"...\"}. Keep say short and spoken-friendly. Put fuller detail in log.";

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

    internal static string? DescribePrimaryWindowFromToolOutput(string toolOutputText)
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
        if (toolName != "send_input_to_window" ||
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
        if (toolName is not "invoke_selected_window_element" and not "focus_selected_window_element")
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
        if (toolName is not "invoke_selected_window_element" and not "focus_selected_window_element")
        {
            return false;
        }

        if (!SnapshotLooksLikeBrowserWindow(recentWindowContext) ||
            !UserRequestLooksLikeBrowserContentSearch(userText))
        {
            return false;
        }

        var elementPath = TryGetStringArgument(args, "elementPath") ?? TryGetStringArgument(args, "uiPath");
        if (string.IsNullOrWhiteSpace(elementPath) ||
            TryFindElementByPath(recentWindowContext, elementPath, out _))
        {
            return false;
        }

        var requiredAction = toolName == "focus_selected_window_element" ? "focus" : "invoke";
        if (!TryFindUniqueElementPathByNameAndAction(recentWindowContext, "Search", requiredAction, out var repairedPath))
        {
            return false;
        }

        rewrittenArgs = CloneArguments(args);
        rewrittenArgs["elementPath"] = repairedPath;
        rewrittenArgs.Remove("uiPath");
        return true;
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
            return TryGetJsonProperty(document.RootElement, "elementTree", out var elementTree) &&
                   ContainsMatchingElement(elementTree, ElementLooksLikeFullscreenBrowserContent);
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, object?> CloneArguments(IReadOnlyDictionary<string, object?> args)
        => args.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

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
                    TryGetJsonBooleanProperty(window, "isSelected") == true,
                    HasUsableWindowBounds(window)))
                .Where(match => !string.IsNullOrWhiteSpace(match.Handle) &&
                                !string.IsNullOrWhiteSpace(match.Title) &&
                                match.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
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

    internal static bool ShouldBlockTaskbarSearchForBrowserContentQuery(
        string userText,
        string toolName,
        string? recentWindowContext)
    {
        return toolName == "launch_app_via_taskbar_search"
               && SnapshotLooksLikeBrowserWindow(recentWindowContext)
               && UserRequestLooksLikeBrowserContentSearch(userText);
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
            if (TryGetJsonProperty(document.RootElement, "focusedElement", out var focusedElement) &&
                ElementLooksLikeBrowserAddressBar(focusedElement))
            {
                return true;
            }

            return TryGetJsonProperty(document.RootElement, "elementTree", out var elementTree) &&
                   ContainsMatchingElement(elementTree, ElementLooksLikeBrowserAddressBar);
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
            return TryGetJsonProperty(document.RootElement, "elementTree", out var elementTree) &&
                   ContainsMatchingElement(elementTree, ElementLooksLikeBrowserAddressBar);
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
            return TryGetJsonProperty(document.RootElement, "elementTree", out var elementTree) &&
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
            if (!TryGetJsonProperty(document.RootElement, "elementTree", out var elementTree))
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

    private static bool TryGetWindowProperty(string toolOutputText, string propertyName, out JsonElement window)
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
    }

    public static void Warn(string text)
    {
        Console.WriteLine($"!  {text}");
        DebugTrace.WriteEvent("display.warn", text);
    }

    public static void Error(string text)
    {
        Console.Error.WriteLine($"x  {text}");
        DebugTrace.WriteEvent("display.error", text);
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
    }

    public static void Transcribing()
    {
        Console.WriteLine(".. Transcribing speech...");
        DebugTrace.WriteEvent("display.transcribing", "Transcribing speech...");
    }

    public static void Transcript(string text)
    {
        Console.WriteLine($"\n{Label("Heard")} {text}");
        DebugTrace.WriteEvent("display.transcript", DebugTrace.Preview(text, 500));
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
    }

    public static void IntermediateStep(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Console.WriteLine($"\n{Label("Step")} {text.Trim()}");
        DebugTrace.WriteEvent("display.intermediate_step", DebugTrace.Preview(text, 500));
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
