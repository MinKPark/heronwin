using System.Text.Json;

namespace HeronWin.HerFace;

internal sealed record HerfaceScenarioAssertions(
    IReadOnlyList<string> RequiredCategories,
    IReadOnlyList<string> ForbiddenCategories,
    IReadOnlyList<string> RequiredFinalText,
    IReadOnlyList<string> ForbiddenFinalText,
    bool AllowToolErrors,
    bool AllowReplyContradictions,
    bool AllowExplicitlyUnresolvedOutcome)
{
    public static HerfaceScenarioAssertions Empty { get; } = new(
        [],
        [],
        [],
        [],
        AllowToolErrors: false,
        AllowReplyContradictions: false,
        AllowExplicitlyUnresolvedOutcome: false);
}

internal sealed record HerfaceScenarioDefinition(
    string Name,
    IReadOnlyList<string> Commands,
    HerfaceScenarioAssertions Assertions);

internal sealed record HerfaceScenarioSuite(
    string Name,
    IReadOnlyList<HerfaceScenarioDefinition> Scenarios);

internal sealed record HerfaceTraceRecord(
    long Sequence,
    string Category,
    JsonElement Data,
    string DataRawText)
{
    public bool TryGetInt64(string propertyName, out long value)
    {
        value = 0;
        if (!TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(property.GetString(), out value),
            _ => false
        };
    }

    public bool TryGetBoolean(string propertyName, out bool value)
    {
        value = false;
        if (!TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;

            case JsonValueKind.False:
                value = false;
                return true;

            case JsonValueKind.String:
                return bool.TryParse(property.GetString(), out value);

            default:
                return false;
        }
    }

    public string? GetString(string propertyName)
    {
        if (!TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.GetRawText();
    }

    private bool TryGetProperty(string propertyName, out JsonElement property)
    {
        property = default;
        if (Data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (Data.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in Data.EnumerateObject())
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
}

internal sealed record HerfaceTurnAssessment(
    bool Passed,
    int ToolCallCount,
    int ToolErrorCount,
    bool HasAssistantReply,
    bool HasReplyContradiction,
    bool HasExplicitlyUnresolvedOutcome,
    string FinalSayText,
    string FinalLogText,
    IReadOnlyList<string> Failures)
{
    public static HerfaceTurnAssessment CreateExecutionFailure(string message)
        => new(
            Passed: false,
            ToolCallCount: 0,
            ToolErrorCount: 0,
            HasAssistantReply: false,
            HasReplyContradiction: false,
            HasExplicitlyUnresolvedOutcome: false,
            FinalSayText: string.Empty,
            FinalLogText: string.Empty,
            Failures: [message]);
}

internal sealed record HerfaceScriptedTurnResult(
    long TurnId,
    string Command,
    AgentReply Reply,
    HerfaceTurnAssessment Assessment);

internal sealed record HerfaceScenarioResult(
    string Name,
    bool Passed,
    IReadOnlyList<HerfaceScriptedTurnResult> Turns,
    IReadOnlyList<string> Failures);

internal static class HerfaceScenarioLoader
{
    public static HerfaceScenarioSuite LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Scenario file was not found.", fullPath);
        }

        return Parse(File.ReadAllText(fullPath), Path.GetFileName(fullPath));
    }

    internal static HerfaceScenarioSuite Parse(string yaml, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new InvalidOperationException("Scenario YAML was empty.");
        }

        var root = HerfaceYamlParser.Parse(yaml);

        if (root is HerfaceYamlSequence rootSequence)
        {
            return new HerfaceScenarioSuite(
                sourceName,
                rootSequence.Items
                    .Select((element, index) => ParseScenario(element, $"Scenario {index + 1}"))
                    .ToArray());
        }

        if (root is not HerfaceYamlMapping rootMapping)
        {
            throw new InvalidOperationException("Scenario YAML must be a mapping or sequence.");
        }

        if (rootMapping.TryGetValue("scenarios", out var scenariosNode) &&
            scenariosNode is HerfaceYamlSequence scenariosSequence)
        {
            var suiteName = GetString(rootMapping, "name") ?? sourceName;
            return new HerfaceScenarioSuite(
                suiteName,
                scenariosSequence.Items
                    .Select((element, index) => ParseScenario(element, $"Scenario {index + 1}"))
                    .ToArray());
        }

        return new HerfaceScenarioSuite(sourceName, [ParseScenario(rootMapping, sourceName)]);
    }

    private static HerfaceScenarioDefinition ParseScenario(HerfaceYamlNode node, string fallbackName)
    {
        if (node is not HerfaceYamlMapping mapping)
        {
            throw new InvalidOperationException("Each scenario entry must be a YAML mapping.");
        }

        var commands = ReadStringArray(mapping, "commands");

        if (commands.Count == 0)
        {
            throw new InvalidOperationException(
                $"Scenario \"{GetString(mapping, "name") ?? fallbackName}\" must provide at least one command.");
        }

        var assertions = mapping.TryGetValue("assertions", out var assertionsNode) &&
                         assertionsNode is HerfaceYamlMapping assertionsMapping
            ? ParseAssertions(assertionsMapping)
            : HerfaceScenarioAssertions.Empty;

        return new HerfaceScenarioDefinition(
            GetString(mapping, "name") ?? fallbackName,
            commands,
            assertions);
    }

    private static HerfaceScenarioAssertions ParseAssertions(HerfaceYamlMapping mapping)
        => new(
            ReadStringArray(mapping, "requiredCategories"),
            ReadStringArray(mapping, "forbiddenCategories"),
            ReadStringArray(mapping, "requiredFinalText"),
            ReadStringArray(mapping, "forbiddenFinalText"),
            GetBoolean(mapping, "allowToolErrors"),
            GetBoolean(mapping, "allowReplyContradictions"),
            GetBoolean(mapping, "allowExplicitlyUnresolvedOutcome"));

    private static IReadOnlyList<string> ReadStringArray(HerfaceYamlMapping mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var node) ||
            node is not HerfaceYamlSequence sequence)
        {
            return [];
        }

        return sequence.Items
            .OfType<HerfaceYamlScalar>()
            .Select(static item => item.Value.Trim())
            .Where(static item => item.Length > 0)
            .ToArray();
    }

    private static bool GetBoolean(HerfaceYamlMapping mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var node) ||
            node is not HerfaceYamlScalar scalar)
        {
            return false;
        }

        return bool.TryParse(scalar.Value, out var parsed) && parsed;
    }

    private static string? GetString(HerfaceYamlMapping mapping, string propertyName)
        => mapping.TryGetValue(propertyName, out var node) && node is HerfaceYamlScalar scalar
            ? scalar.Value
            : null;
}

internal static class HerfaceTraceLogReader
{
    public static IReadOnlyList<HerfaceTraceRecord> ReadAll(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        var records = new List<HerfaceTraceRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var sequence = root.TryGetProperty("sequence", out var sequenceElement) &&
                               sequenceElement.TryGetInt64(out var parsedSequence)
                    ? parsedSequence
                    : 0;
                var category = root.TryGetProperty("category", out var categoryElement)
                    ? categoryElement.GetString() ?? string.Empty
                    : string.Empty;
                var data = root.TryGetProperty("data", out var dataElement)
                    ? dataElement.Clone()
                    : default;
                var rawDataText = data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? string.Empty
                    : data.GetRawText();

                if (!string.IsNullOrWhiteSpace(category))
                {
                    records.Add(new HerfaceTraceRecord(sequence, category, data, rawDataText));
                }
            }
            catch
            {
                // Ignore malformed log lines during scenario evaluation.
            }
        }

        return records;
    }
}

internal static class HerfaceScenarioEvaluator
{
    public static HerfaceTurnAssessment AssessTurn(
        IReadOnlyList<HerfaceTraceRecord> records,
        long turnId,
        HerfaceScenarioAssertions? assertions = null)
    {
        assertions ??= HerfaceScenarioAssertions.Empty;
        var turnRecords = records
            .Where(record => record.TryGetInt64("turn", out var candidateTurnId) && candidateTurnId == turnId)
            .OrderBy(record => record.Sequence)
            .ToArray();
        var failures = new List<string>();

        var toolCompletionRecords = turnRecords
            .Where(record => record.Category == "agent.tool_call_completed")
            .OrderBy(record => record.Sequence)
            .ToArray();
        var toolCallCount = toolCompletionRecords.Length;
        var toolErrorRecords = toolCompletionRecords
            .Where(record => record.TryGetBoolean("isError", out var isError) && isError)
            .ToArray();
        var toolErrorCount = toolErrorRecords.Length;
        var replyContradictionRecords = turnRecords
            .Where(record => record.Category == "agent.reply_contradiction_detected")
            .OrderBy(record => record.Sequence)
            .ToArray();
        var hasReplyContradiction = replyContradictionRecords.Length > 0;
        var assistantReply = turnRecords.LastOrDefault(record => record.Category == "assistant.reply");
        var hasAssistantReply = assistantReply is not null;
        var finalSayText = assistantReply?.GetString("sayText") ?? string.Empty;
        var finalLogText = assistantReply?.GetString("logText") ?? string.Empty;
        var combinedFinalText = $"{finalSayText}\n{finalLogText}".Trim();
        var hasExplicitlyUnresolvedOutcome = AgentRunner.HasExplicitlyUnresolvedOutcome(combinedFinalText);
        var hasRecoveredReplyContradiction = hasReplyContradiction
            && hasAssistantReply
            && string.IsNullOrWhiteSpace(
                AgentRunner.GetReplyOutcomeContradictionRule(
                    new AgentReply(finalLogText, finalSayText, RawText: string.Empty)));
        var hasRecoveredToolErrors = toolErrorCount > 0
            && hasAssistantReply
            && !hasReplyContradiction
            && !hasExplicitlyUnresolvedOutcome
            && toolCompletionRecords.Any(
                record => record.Sequence > toolErrorRecords[^1].Sequence
                          && (!record.TryGetBoolean("isError", out var isError) || !isError));

        if (!hasAssistantReply)
        {
            failures.Add("No assistant.reply event was recorded for this turn.");
        }

        if (toolErrorCount > 0 && !assertions.AllowToolErrors && !hasRecoveredToolErrors)
        {
            failures.Add($"The turn recorded {toolErrorCount} unrecovered tool error event(s).");
        }

        if (hasReplyContradiction && !assertions.AllowReplyContradictions && !hasRecoveredReplyContradiction)
        {
            failures.Add("The turn recorded a reply contradiction between say/log outcomes.");
        }

        if (hasExplicitlyUnresolvedOutcome && !assertions.AllowExplicitlyUnresolvedOutcome)
        {
            failures.Add("The final logged reply still says the request is not complete or not confirmed.");
        }

        return new HerfaceTurnAssessment(
            Passed: failures.Count == 0,
            ToolCallCount: toolCallCount,
            ToolErrorCount: toolErrorCount,
            HasAssistantReply: hasAssistantReply,
            HasReplyContradiction: hasReplyContradiction,
            HasExplicitlyUnresolvedOutcome: hasExplicitlyUnresolvedOutcome,
            FinalSayText: finalSayText,
            FinalLogText: finalLogText,
            Failures: failures);
    }

    public static HerfaceScenarioResult EvaluateScenario(
        IReadOnlyList<HerfaceTraceRecord> records,
        HerfaceScenarioDefinition scenario,
        IReadOnlyList<HerfaceScriptedTurnResult> turns)
    {
        var failures = new List<string>();

        foreach (var turn in turns.Where(turn => !turn.Assessment.Passed))
        {
            failures.Add(
                $"Turn {turn.TurnId} ({turn.Command}) failed: {string.Join("; ", turn.Assessment.Failures)}");
        }

        if (turns.Count == 0)
        {
            failures.Add("The scenario did not execute any turns.");
            return new HerfaceScenarioResult(scenario.Name, false, turns, failures);
        }

        var scenarioTurnIds = turns.Select(turn => turn.TurnId).ToHashSet();
        var scenarioRecords = records
            .Where(record => record.TryGetInt64("turn", out var turnId) && scenarioTurnIds.Contains(turnId))
            .ToArray();
        var scenarioCategories = scenarioRecords
            .Select(record => record.Category)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var finalTurn = turns[^1];
        var finalReplyText = $"{finalTurn.Assessment.FinalSayText}\n{finalTurn.Assessment.FinalLogText}";

        foreach (var category in scenario.Assertions.RequiredCategories)
        {
            if (!scenarioCategories.Contains(category))
            {
                failures.Add($"Required category \"{category}\" was not present in the scenario log.");
            }
        }

        foreach (var category in scenario.Assertions.ForbiddenCategories)
        {
            if (scenarioCategories.Contains(category))
            {
                failures.Add($"Forbidden category \"{category}\" was present in the scenario log.");
            }
        }

        foreach (var requiredText in scenario.Assertions.RequiredFinalText)
        {
            if (!ContainsInvariant(finalReplyText, requiredText))
            {
                failures.Add($"Final reply did not contain required text \"{requiredText}\".");
            }
        }

        foreach (var forbiddenText in scenario.Assertions.ForbiddenFinalText)
        {
            if (ContainsInvariant(finalReplyText, forbiddenText))
            {
                failures.Add($"Final reply contained forbidden text \"{forbiddenText}\".");
            }
        }

        return new HerfaceScenarioResult(
            scenario.Name,
            failures.Count == 0,
            turns,
            failures);
    }

    private static bool ContainsInvariant(string haystack, string needle)
        => !string.IsNullOrWhiteSpace(needle)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

internal static class ScriptedConversationRunner
{
    public static async Task<int> RunAsync(
        HerfaceConsoleOptions options,
        AppConfig config,
        ILlmClient llmClient,
        McpClientManager mcpManager,
        CancellationToken cancellationToken)
    {
        var suite = string.IsNullOrWhiteSpace(options.ScenarioFilePath)
            ? new HerfaceScenarioSuite(
                "ad hoc scripted run",
                [new HerfaceScenarioDefinition("Ad hoc scripted commands", options.Commands, HerfaceScenarioAssertions.Empty)])
            : HerfaceScenarioLoader.LoadFromFile(options.ScenarioFilePath);

        var jsonLogPath = DebugTrace.JsonLogFilePath;
        if (string.IsNullOrWhiteSpace(jsonLogPath))
        {
            Display.Error("Scripted mode requires an active debug JSONL trace, but none was configured.");
            return 1;
        }

        Display.Info("Scripted mode is active. Microphone capture and speech playback are bypassed.");
        Display.Info(
            $"Loaded {suite.Scenarios.Count} scenario(s) from {(options.ScenarioFilePath is null ? "inline commands" : options.ScenarioFilePath)}.");

        var overallPassed = true;
        long nextTurnId = 0;

        foreach (var scenario in suite.Scenarios)
        {
            Display.Separator();
            Display.Info($"Scenario: {scenario.Name}");
            var history = new List<AgentMessage>();
            var turns = new List<HerfaceScriptedTurnResult>();
            var stopScenario = false;

            foreach (var command in scenario.Commands)
            {
                var turnId = Interlocked.Increment(ref nextTurnId);
                try
                {
                    DebugTrace.WriteStructuredEvent(
                        "agent.turn.scripted_begin",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["scenario"] = scenario.Name,
                            ["command"] = command,
                        });

                    var processedTurn = await HerfaceTurnProcessor.ProcessAsync(
                        turnId,
                        command,
                        history,
                        config,
                        llmClient,
                        mcpManager,
                        cancellationToken,
                        turnSource: "scripted");
                    if (processedTurn.UpdatedConfig is not null)
                    {
                        config = processedTurn.UpdatedConfig;
                    }
                    var logRecords = HerfaceTraceLogReader.ReadAll(jsonLogPath);
                    var assessment = HerfaceScenarioEvaluator.AssessTurn(logRecords, turnId, scenario.Assertions);
                    var turnResult = new HerfaceScriptedTurnResult(turnId, command, processedTurn.Reply, assessment);
                    turns.Add(turnResult);
                    ReportTurn(turnResult);

                    if (!assessment.Passed)
                    {
                        stopScenario = true;
                        overallPassed = false;
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    DebugTrace.WriteStructuredEvent(
                        "agent.turn.scripted_exception",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = turnId,
                            ["scenario"] = scenario.Name,
                            ["command"] = command,
                            ["error"] = DebugTrace.Preview(ex.ToString(), 800),
                        });

                    var failedTurn = new HerfaceScriptedTurnResult(
                        turnId,
                        command,
                        new AgentReply(string.Empty, string.Empty, string.Empty),
                        HerfaceTurnAssessment.CreateExecutionFailure(ex.Message));
                    turns.Add(failedTurn);
                    ReportTurn(failedTurn);
                    stopScenario = true;
                    overallPassed = false;
                    break;
                }
            }

            var scenarioRecords = HerfaceTraceLogReader.ReadAll(jsonLogPath);
            var scenarioResult = HerfaceScenarioEvaluator.EvaluateScenario(scenarioRecords, scenario, turns);
            ReportScenario(scenarioResult);

            if (!scenarioResult.Passed)
            {
                overallPassed = false;
            }

            if (stopScenario)
            {
                Display.Warn($"Stopped scenario \"{scenario.Name}\" after the first failing command.");
            }
        }

        Display.Separator();
        if (overallPassed)
        {
            Display.Info("All scripted scenarios passed their log-based checks.");
            return 0;
        }

        Display.Error("One or more scripted scenarios failed their log-based checks.");
        return 1;
    }

    private static void ReportTurn(HerfaceScriptedTurnResult turn)
    {
        if (turn.Assessment.Passed)
        {
            Display.Info(
                $"Turn {turn.TurnId} passed log checks: toolCalls={turn.Assessment.ToolCallCount}, say={DebugTrace.Preview(turn.Assessment.FinalSayText, 180)}");
            return;
        }

        Display.Warn(
            $"Turn {turn.TurnId} failed log checks: {string.Join(" | ", turn.Assessment.Failures)}");
    }

    private static void ReportScenario(HerfaceScenarioResult scenario)
    {
        if (scenario.Passed)
        {
            Display.Info($"Scenario passed: {scenario.Name}");
            return;
        }

        Display.Error($"Scenario failed: {scenario.Name}");
        foreach (var failure in scenario.Failures)
        {
            Display.Warn(failure);
        }
    }
}
