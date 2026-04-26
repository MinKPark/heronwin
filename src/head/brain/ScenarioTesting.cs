using System.Text.Json;

using System.Globalization;
using System.Text;

namespace HeronWin.Brain;

internal sealed record BrainScenarioAssertions(
    IReadOnlyList<string> RequiredCategories,
    IReadOnlyList<string> ForbiddenCategories,
    IReadOnlyList<string> RequiredFinalText,
    IReadOnlyList<string> ForbiddenFinalText,
    bool AllowToolErrors,
    bool AllowReplyContradictions,
    bool AllowExplicitlyUnresolvedOutcome)
{
    public static BrainScenarioAssertions Empty { get; } = new(
        [],
        [],
        [],
        [],
        AllowToolErrors: false,
        AllowReplyContradictions: false,
        AllowExplicitlyUnresolvedOutcome: false);
}

internal sealed record BrainScenarioDefinition(
    string Name,
    IReadOnlyList<string> Commands,
    BrainScenarioAssertions Assertions);

internal sealed record BrainScenarioSuite(
    string Name,
    IReadOnlyList<BrainScenarioDefinition> Scenarios);

internal sealed record BrainTraceRecord(
    long Sequence,
    string Category,
    JsonElement Data,
    string DataRawText,
    DateTimeOffset? Timestamp = null)
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

internal sealed record BrainTurnAssessment(
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
    public static BrainTurnAssessment CreateExecutionFailure(string message)
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

internal sealed record BrainScriptedTurnResult(
    long TurnId,
    string Command,
    AgentReply Reply,
    BrainTurnAssessment Assessment);

internal sealed record BrainScenarioResult(
    string Name,
    bool Passed,
    IReadOnlyList<BrainScriptedTurnResult> Turns,
    IReadOnlyList<string> Failures);

internal static class BrainScenarioLoader
{
    public static BrainScenarioSuite LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Scenario file was not found.", fullPath);
        }

        return Parse(File.ReadAllText(fullPath), Path.GetFileName(fullPath));
    }

    internal static BrainScenarioSuite Parse(string yaml, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new InvalidOperationException("Scenario YAML was empty.");
        }

        var root = BrainYamlParser.Parse(yaml);

        if (root is BrainYamlSequence rootSequence)
        {
            return new BrainScenarioSuite(
                sourceName,
                rootSequence.Items
                    .Select((element, index) => ParseScenario(element, $"Scenario {index + 1}"))
                    .ToArray());
        }

        if (root is not BrainYamlMapping rootMapping)
        {
            throw new InvalidOperationException("Scenario YAML must be a mapping or sequence.");
        }

        if (rootMapping.TryGetValue("scenarios", out var scenariosNode) &&
            scenariosNode is BrainYamlSequence scenariosSequence)
        {
            var suiteName = GetString(rootMapping, "name") ?? sourceName;
            return new BrainScenarioSuite(
                suiteName,
                scenariosSequence.Items
                    .Select((element, index) => ParseScenario(element, $"Scenario {index + 1}"))
                    .ToArray());
        }

        return new BrainScenarioSuite(sourceName, [ParseScenario(rootMapping, sourceName)]);
    }

    private static BrainScenarioDefinition ParseScenario(BrainYamlNode node, string fallbackName)
    {
        if (node is not BrainYamlMapping mapping)
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
                         assertionsNode is BrainYamlMapping assertionsMapping
            ? ParseAssertions(assertionsMapping)
            : BrainScenarioAssertions.Empty;

        return new BrainScenarioDefinition(
            GetString(mapping, "name") ?? fallbackName,
            commands,
            assertions);
    }

    private static BrainScenarioAssertions ParseAssertions(BrainYamlMapping mapping)
        => new(
            ReadStringArray(mapping, "requiredCategories"),
            ReadStringArray(mapping, "forbiddenCategories"),
            ReadStringArray(mapping, "requiredFinalText"),
            ReadStringArray(mapping, "forbiddenFinalText"),
            GetBoolean(mapping, "allowToolErrors"),
            GetBoolean(mapping, "allowReplyContradictions"),
            GetBoolean(mapping, "allowExplicitlyUnresolvedOutcome"));

    private static IReadOnlyList<string> ReadStringArray(BrainYamlMapping mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var node) ||
            node is not BrainYamlSequence sequence)
        {
            return [];
        }

        return sequence.Items
            .OfType<BrainYamlScalar>()
            .Select(static item => ExpandEnvironmentPlaceholders(item.Value.Trim()))
            .Where(static item => item.Length > 0)
            .ToArray();
    }

    private static string ExpandEnvironmentPlaceholders(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}",
            static match =>
            {
                var variableName = match.Groups["name"].Value;
                var replacement = Environment.GetEnvironmentVariable(variableName);
                return string.IsNullOrWhiteSpace(replacement)
                    ? match.Value
                    : replacement;
            });
    }

    private static bool GetBoolean(BrainYamlMapping mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var node) ||
            node is not BrainYamlScalar scalar)
        {
            return false;
        }

        return bool.TryParse(scalar.Value, out var parsed) && parsed;
    }

    private static string? GetString(BrainYamlMapping mapping, string propertyName)
        => mapping.TryGetValue(propertyName, out var node) && node is BrainYamlScalar scalar
            ? scalar.Value
            : null;
}

internal static class BrainTraceLogReader
{
    public static IReadOnlyList<BrainTraceRecord> ReadAll(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        var records = new List<BrainTraceRecord>();
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
                var timestamp = root.TryGetProperty("timestamp", out var timestampElement) &&
                                timestampElement.ValueKind == JsonValueKind.String &&
                                DateTimeOffset.TryParse(
                                    timestampElement.GetString(),
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind,
                                    out var parsedTimestamp)
                    ? parsedTimestamp
                    : (DateTimeOffset?)null;
                var data = root.TryGetProperty("data", out var dataElement)
                    ? dataElement.Clone()
                    : default;
                var rawDataText = data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? string.Empty
                    : data.GetRawText();

                if (!string.IsNullOrWhiteSpace(category))
                {
                    records.Add(new BrainTraceRecord(sequence, category, data, rawDataText, timestamp));
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

internal sealed record BrainTraceAttemptReport(
    long TurnId,
    int Attempt,
    double ElapsedMs,
    IReadOnlyList<string> ExecutedTools,
    string ResponsePreview);

internal sealed record BrainTraceTurnReport(
    long TurnId,
    string ScenarioName,
    string Command,
    double TurnElapsedMs,
    int AttemptCount,
    double LlmTimeMs,
    double AverageLlmAttemptMs,
    int ToolCallCount,
    double ToolTimeMs,
    int PostActionSnapshotCount,
    double PostActionSnapshotTimeMs,
    int FocusSnapshotCount,
    double FocusSnapshotTimeMs,
    int AdditionalEvidenceCount,
    double AdditionalEvidenceTimeMs,
    int ReplyRepairCount,
    double ReplyRepairTimeMs,
    int InternalContinuationConsideredCount,
    int InternalContinuationExecutedCount,
    IReadOnlyList<string> ExecutedTools,
    IReadOnlyList<BrainTraceAttemptReport> Attempts);

internal sealed record BrainTraceBucketSummary(
    string Name,
    int Count,
    double ElapsedMs);

internal sealed record BrainTraceToolSummary(
    string Tool,
    int Count,
    double TotalElapsedMs,
    double MaxElapsedMs);

internal sealed record BrainTraceSlowEvent(
    string Category,
    long? TurnId,
    double ElapsedMs,
    string Detail);

internal sealed record BrainTraceLookaheadFallbackSummary(
    string Reason,
    int Count);

internal sealed record BrainTraceLookaheadSummary(
    int RequestedCount,
    int DecisionCount,
    int AdvancedCount,
    int FallbackCount,
    IReadOnlyList<BrainTraceLookaheadFallbackSummary> FallbacksByReason)
{
    public int EstimatedLlmCallsSaved => AdvancedCount;

    public bool HasEvents
        => RequestedCount > 0 ||
           DecisionCount > 0 ||
           AdvancedCount > 0 ||
           FallbackCount > 0;
}

internal sealed record BrainTraceReport(
    string SourcePath,
    string? Provider,
    string? Model,
    string? ScenarioName,
    DateTimeOffset? SessionStart,
    DateTimeOffset? ScenarioCompletedAt,
    double ScenarioElapsedMs,
    int TurnCount,
    int TotalLlmAttemptCount,
    double TotalLlmTimeMs,
    double AverageLlmAttemptMs,
    int BlockedToolCallCount,
    BrainTraceLookaheadSummary Lookahead,
    IReadOnlyList<BrainTraceTurnReport> Turns,
    IReadOnlyList<BrainTraceBucketSummary> Buckets,
    IReadOnlyList<BrainTraceToolSummary> SlowTools,
    IReadOnlyList<BrainTraceSlowEvent> SlowEvents)
{
    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Brain Trace Report");
        builder.AppendLine();
        builder.AppendLine($"- Trace: `{SourcePath}`");
        if (!string.IsNullOrWhiteSpace(ScenarioName))
        {
            builder.AppendLine($"- Scenario: `{ScenarioName}`");
        }

        if (!string.IsNullOrWhiteSpace(Provider) || !string.IsNullOrWhiteSpace(Model))
        {
            builder.AppendLine($"- Provider / model: `{Provider ?? "(unknown)"} / {Model ?? "(unknown)"}`");
        }

        if (SessionStart is { } sessionStart)
        {
            builder.AppendLine($"- Session start: `{sessionStart:O}`");
        }

        if (ScenarioCompletedAt is { } scenarioCompletedAt)
        {
            builder.AppendLine($"- Scenario completed: `{scenarioCompletedAt:O}`");
        }

        builder.AppendLine($"- Scenario elapsed: `{FormatSeconds(ScenarioElapsedMs)}`");
        builder.AppendLine($"- Turns: `{TurnCount}`");
        builder.AppendLine($"- Total LLM responses: `{TotalLlmAttemptCount}`");
        builder.AppendLine($"- Average LLM attempt: `{FormatSeconds(AverageLlmAttemptMs)}`");
        builder.AppendLine();

        builder.AppendLine("## Turn Summary");
        builder.AppendLine();
        builder.AppendLine("| Turn | Elapsed s | Attempts | LLM s | Avg attempt s | Tool calls | Tool s | Retry signals | Command |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |");
        foreach (var turn in Turns)
        {
            var retrySignals =
                $"repairs={turn.ReplyRepairCount}, extra={turn.AdditionalEvidenceCount}, followup={turn.PostActionSnapshotCount}, continuations={turn.InternalContinuationConsideredCount}/{turn.InternalContinuationExecutedCount}";
            builder.AppendLine(
                $"| {turn.TurnId} | {FormatSecondsValue(turn.TurnElapsedMs)} | {turn.AttemptCount} | {FormatSecondsValue(turn.LlmTimeMs)} | {FormatSecondsValue(turn.AverageLlmAttemptMs)} | {turn.ToolCallCount} | {FormatSecondsValue(turn.ToolTimeMs)} | {EscapeMarkdownCell(retrySignals)} | {EscapeMarkdownCell(DebugTrace.Preview(turn.Command, 120))} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Latency Buckets");
        builder.AppendLine();
        builder.AppendLine("| Bucket | Count | Total s |");
        builder.AppendLine("| --- | ---: | ---: |");
        foreach (var bucket in Buckets)
        {
            builder.AppendLine(
                $"| {EscapeMarkdownCell(bucket.Name)} | {bucket.Count} | {FormatSecondsValue(bucket.ElapsedMs)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Counts");
        builder.AppendLine();
        builder.AppendLine($"- Blocked tool calls: `{BlockedToolCallCount}`");
        builder.AppendLine();

        if (Lookahead.HasEvents)
        {
            builder.AppendLine("## Lookahead");
            builder.AppendLine();
            builder.AppendLine($"- Requests: `{Lookahead.RequestedCount}`");
            builder.AppendLine($"- Decisions parsed: `{Lookahead.DecisionCount}`");
            builder.AppendLine($"- No-op turns advanced: `{Lookahead.AdvancedCount}`");
            builder.AppendLine($"- Fallbacks: `{Lookahead.FallbackCount}`");
            builder.AppendLine($"- Estimated LLM calls saved: `{Lookahead.EstimatedLlmCallsSaved}`");
            if (Lookahead.FallbacksByReason.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("| Fallback reason | Count |");
                builder.AppendLine("| --- | ---: |");
                foreach (var fallback in Lookahead.FallbacksByReason)
                {
                    builder.AppendLine(
                        $"| {EscapeMarkdownCell(fallback.Reason)} | {fallback.Count} |");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Top Slow Executed Tools");
        builder.AppendLine();
        builder.AppendLine("| Tool | Count | Total s | Max s |");
        builder.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var tool in SlowTools)
        {
            builder.AppendLine(
                $"| {EscapeMarkdownCell(tool.Tool)} | {tool.Count} | {FormatSecondsValue(tool.TotalElapsedMs)} | {FormatSecondsValue(tool.MaxElapsedMs)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Top Slow Events");
        builder.AppendLine();
        builder.AppendLine("| Category | Turn | Elapsed s | Detail |");
        builder.AppendLine("| --- | ---: | ---: | --- |");
        foreach (var slowEvent in SlowEvents)
        {
            builder.AppendLine(
                $"| {EscapeMarkdownCell(slowEvent.Category)} | {(slowEvent.TurnId?.ToString(CultureInfo.InvariantCulture) ?? "-")} | {FormatSecondsValue(slowEvent.ElapsedMs)} | {EscapeMarkdownCell(DebugTrace.Preview(slowEvent.Detail, 120))} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Attempt Breakdown");
        builder.AppendLine();
        foreach (var turn in Turns)
        {
            builder.AppendLine($"### Turn {turn.TurnId}");
            builder.AppendLine();
            builder.AppendLine("| Attempt | LLM s | Tools after response |");
            builder.AppendLine("| --- | ---: | --- |");
            foreach (var attempt in turn.Attempts)
            {
                var tools = attempt.ExecutedTools.Count == 0
                    ? "(none)"
                    : string.Join(", ", attempt.ExecutedTools);
                builder.AppendLine(
                    $"| {attempt.Attempt} | {FormatSecondsValue(attempt.ElapsedMs)} | {EscapeMarkdownCell(tools)} |");
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string EscapeMarkdownCell(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string FormatSeconds(double milliseconds)
        => $"{FormatSecondsValue(milliseconds)} s";

    private static string FormatSecondsValue(double milliseconds)
        => (milliseconds / 1000d).ToString("0.000", CultureInfo.InvariantCulture);
}

internal static class BrainTraceReporter
{
    public static string GenerateMarkdown(string path)
        => Generate(path).ToMarkdown();

    public static BrainTraceReport Generate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Trace file was not found.", fullPath);
        }

        var records = BrainTraceLogReader.ReadAll(fullPath)
            .OrderBy(record => record.Sequence)
            .ToArray();
        if (records.Length == 0)
        {
            throw new InvalidOperationException("Trace file did not contain any readable records.");
        }

        var sessionStartRecord = records.FirstOrDefault(static record => record.Category == "session.start");
        var provider = sessionStartRecord?.GetString("llmProvider");
        var model = sessionStartRecord?.GetString("openAiModel");
        var scriptedTurns = records
            .Where(static record => record.Category == "agent.turn.scripted_begin")
            .Select(
                static record => new
                {
                    TurnId = record.TryGetInt64("turn", out var turnId) ? turnId : 0,
                    ScenarioName = record.GetString("scenario") ?? string.Empty,
                    Command = record.GetString("command") ?? string.Empty,
                })
            .Where(static item => item.TurnId > 0)
            .OrderBy(static item => item.TurnId)
            .ToArray();
        var llmAttempts = BuildAttemptReports(records);
        var turns = scriptedTurns
            .Select(turn => BuildTurnReport(records, llmAttempts, turn.TurnId, turn.ScenarioName, turn.Command))
            .ToArray();
        var scenarioName = scriptedTurns
            .Select(static turn => turn.ScenarioName)
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name));
        var scenarioCompletedAt = records
            .Where(
                static record => record.Category == "display.info" &&
                                 record.Timestamp is not null &&
                                 (record.GetString("message")?.StartsWith("Scenario passed:", StringComparison.Ordinal) == true ||
                                  record.GetString("message")?.StartsWith("Scenario failed:", StringComparison.Ordinal) == true))
            .Select(static record => record.Timestamp)
            .OfType<DateTimeOffset>()
            .LastOrDefault();
        if (scenarioCompletedAt == default)
        {
            scenarioCompletedAt = records.LastOrDefault(static record => record.Timestamp is not null)?.Timestamp ?? default;
        }

        var sessionStart = sessionStartRecord?.Timestamp
            ?? records.FirstOrDefault(static record => record.Timestamp is not null)?.Timestamp;
        var scenarioElapsedMs = sessionStart is { } start && scenarioCompletedAt != default
            ? Math.Max(0d, (scenarioCompletedAt - start).TotalMilliseconds)
            : 0d;
        var slowTools = records
            .Where(static record => record.Category == "agent.tool_call_completed")
            .Select(
                static record => new
                {
                    Tool = record.GetString("executedTool") ?? record.GetString("tool") ?? "(unknown)",
                    ElapsedMs = GetElapsedMs(record),
                })
            .GroupBy(static item => item.Tool, StringComparer.Ordinal)
            .Select(
                static group => new BrainTraceToolSummary(
                    group.Key,
                    group.Count(),
                    group.Sum(static item => item.ElapsedMs),
                    group.Max(static item => item.ElapsedMs)))
            .OrderByDescending(static item => item.TotalElapsedMs)
            .ThenBy(static item => item.Tool, StringComparer.Ordinal)
            .Take(10)
            .ToArray();
        var slowEvents = records
            .Select(record => (Record: record, ElapsedMs: GetElapsedMs(record)))
            .Where(static item => item.ElapsedMs > 0d)
            .OrderByDescending(static item => item.ElapsedMs)
            .ThenBy(item => item.Record.Sequence)
            .Take(12)
            .Select(
                static item => new BrainTraceSlowEvent(
                    item.Record.Category,
                    item.Record.TryGetInt64("turn", out var turnId) ? turnId : null,
                    item.ElapsedMs,
                    SummarizeEvent(item.Record)))
            .ToArray();

        return new BrainTraceReport(
            fullPath,
            provider,
            model,
            scenarioName,
            sessionStart,
            scenarioCompletedAt == default ? null : scenarioCompletedAt,
            scenarioElapsedMs,
            turns.Length,
            llmAttempts.Length,
            llmAttempts.Sum(static attempt => attempt.ElapsedMs),
            llmAttempts.Length == 0 ? 0d : llmAttempts.Average(static attempt => attempt.ElapsedMs),
            records.Count(static record => record.Category == "agent.tool_call_blocked"),
            BuildLookaheadSummary(records),
            turns,
            BuildBuckets(records, llmAttempts),
            slowTools,
            slowEvents);
    }

    private static BrainTraceLookaheadSummary BuildLookaheadSummary(IReadOnlyList<BrainTraceRecord> records)
    {
        var fallbackReasons = records
            .Where(static record => record.Category == "agent.lookahead.fallback")
            .Select(static record => record.GetString("reason") ?? "(unknown)")
            .GroupBy(static reason => reason, StringComparer.Ordinal)
            .Select(static group => new BrainTraceLookaheadFallbackSummary(group.Key, group.Count()))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Reason, StringComparer.Ordinal)
            .ToArray();

        return new BrainTraceLookaheadSummary(
            RequestedCount: records.Count(static record => record.Category == "agent.lookahead.requested"),
            DecisionCount: records.Count(static record => record.Category == "agent.lookahead.decision"),
            AdvancedCount: records.Count(static record => record.Category == "agent.lookahead.advanced"),
            FallbackCount: records.Count(static record => record.Category == "agent.lookahead.fallback"),
            FallbacksByReason: fallbackReasons);
    }

    private static BrainTraceAttemptReport[] BuildAttemptReports(IReadOnlyList<BrainTraceRecord> records)
    {
        var requests = new Dictionary<(long TurnId, int Attempt), BrainTraceRecord>();
        var attempts = new List<(long TurnId, int Attempt, long RequestSequence, long ResponseSequence, double ElapsedMs, string ResponsePreview)>();

        foreach (var record in records)
        {
            if (record.Category == "llm.request" &&
                record.TryGetInt64("turn", out var turnId) &&
                record.TryGetInt64("attempt", out var attemptId))
            {
                requests[(turnId, (int)attemptId)] = record;
                continue;
            }

            if (record.Category == "llm.response" &&
                record.TryGetInt64("turn", out var responseTurnId) &&
                record.TryGetInt64("attempt", out var responseAttemptId) &&
                requests.TryGetValue((responseTurnId, (int)responseAttemptId), out var request))
            {
                var elapsedMs = request.Timestamp is { } startedAt && record.Timestamp is { } completedAt
                    ? Math.Max(0d, (completedAt - startedAt).TotalMilliseconds)
                    : 0d;
                attempts.Add(
                    (
                        responseTurnId,
                        (int)responseAttemptId,
                        request.Sequence,
                        record.Sequence,
                        elapsedMs,
                        record.GetString("textPreview") ?? string.Empty));
            }
        }

        var toolRecords = records
            .Where(static record => record.Category == "agent.tool_call_completed")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        var grouped = attempts
            .OrderBy(static attempt => attempt.TurnId)
            .ThenBy(static attempt => attempt.Attempt)
            .GroupBy(static attempt => attempt.TurnId)
            .ToArray();
        var reports = new List<BrainTraceAttemptReport>();

        foreach (var turnGroup in grouped)
        {
            var turnAttempts = turnGroup.OrderBy(static attempt => attempt.Attempt).ToArray();
            for (var index = 0; index < turnAttempts.Length; index += 1)
            {
                var current = turnAttempts[index];
                var nextRequestSequence = index + 1 < turnAttempts.Length
                    ? turnAttempts[index + 1].RequestSequence
                    : long.MaxValue;
                var executedTools = toolRecords
                    .Where(
                        record => record.TryGetInt64("turn", out var toolTurnId) &&
                                  toolTurnId == current.TurnId &&
                                  record.Sequence > current.ResponseSequence &&
                                  record.Sequence < nextRequestSequence)
                    .Select(record => record.GetString("executedTool") ?? record.GetString("tool") ?? "(unknown)")
                    .ToArray();
                reports.Add(
                    new BrainTraceAttemptReport(
                        current.TurnId,
                        current.Attempt,
                        current.ElapsedMs,
                        executedTools,
                        current.ResponsePreview));
            }
        }

        return reports.ToArray();
    }

    private static BrainTraceTurnReport BuildTurnReport(
        IReadOnlyList<BrainTraceRecord> records,
        IReadOnlyList<BrainTraceAttemptReport> llmAttempts,
        long turnId,
        string scenarioName,
        string command)
    {
        var turnRecords = records
            .Where(record => record.TryGetInt64("turn", out var candidateTurnId) && candidateTurnId == turnId)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        var assistantReply = turnRecords.LastOrDefault(static record => record.Category == "assistant.reply");
        var turnElapsedMs = assistantReply is not null &&
                            assistantReply.TryGetInt64("elapsedMs", out var elapsedMs)
            ? elapsedMs
            : 0d;
        var attemptReports = llmAttempts
            .Where(attempt => attempt.TurnId == turnId)
            .OrderBy(static attempt => attempt.Attempt)
            .ToArray();
        var toolRecords = turnRecords
            .Where(static record => record.Category == "agent.tool_call_completed")
            .ToArray();
        var postSnapshots = turnRecords
            .Where(static record => record.Category == "agent.desktop_followup_snapshot")
            .ToArray();
        var focusSnapshots = turnRecords
            .Where(static record => record.Category == "agent.desktop_followup_focus_snapshot")
            .ToArray();
        var additionalEvidence = turnRecords
            .Where(static record => record.Category == "agent.additional_desktop_evidence_completed")
            .ToArray();
        var replyRepairs = turnRecords
            .Where(static record => record.Category == "agent.reply_repair_completed")
            .ToArray();

        return new BrainTraceTurnReport(
            turnId,
            scenarioName,
            command,
            turnElapsedMs,
            attemptReports.Length == 0 && assistantReply is not null && assistantReply.TryGetInt64("attempts", out var attemptsFromReply)
                ? (int)attemptsFromReply
                : attemptReports.Length,
            attemptReports.Sum(static attempt => attempt.ElapsedMs),
            attemptReports.Length == 0 ? 0d : attemptReports.Average(static attempt => attempt.ElapsedMs),
            toolRecords.Length,
            toolRecords.Sum(GetElapsedMs),
            postSnapshots.Length,
            postSnapshots.Sum(GetElapsedMs),
            focusSnapshots.Length,
            focusSnapshots.Sum(GetElapsedMs),
            additionalEvidence.Length,
            additionalEvidence.Sum(GetElapsedMs),
            replyRepairs.Length,
            replyRepairs.Sum(GetElapsedMs),
            turnRecords.Count(static record => record.Category == "agent.internal_continuation_considered"),
            turnRecords.Count(static record => record.Category == "agent.internal_continuation_completed"),
            toolRecords
                .Select(record => record.GetString("executedTool") ?? record.GetString("tool") ?? "(unknown)")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            attemptReports);
    }

    private static BrainTraceBucketSummary[] BuildBuckets(
        IReadOnlyList<BrainTraceRecord> records,
        IReadOnlyList<BrainTraceAttemptReport> llmAttempts)
    {
        return
        [
            new BrainTraceBucketSummary("LLM time", llmAttempts.Count, llmAttempts.Sum(static attempt => attempt.ElapsedMs)),
            BuildBucket(records, "Reply repair time", static record => record.Category == "agent.reply_repair_completed"),
            BuildBucket(records, "Requested tool time", static record => record.Category == "agent.tool_call_completed"),
            BuildBucket(
                records,
                "Turn-start helper time",
                static record => record.Category.StartsWith("agent.turn.ready_state_", StringComparison.Ordinal)),
            BuildBucket(
                records,
                "Browser helper time",
                static record => record.Category.StartsWith("agent.browser_", StringComparison.Ordinal) ||
                                 record.Category.StartsWith("agent.activate_window_preflight_", StringComparison.Ordinal)),
            BuildBucket(records, "Automatic post-action snapshot time", static record => record.Category == "agent.desktop_followup_snapshot"),
            BuildBucket(records, "Automatic focus snapshot time", static record => record.Category == "agent.desktop_followup_focus_snapshot"),
            BuildBucket(records, "Extra evidence refresh time", static record => record.Category == "agent.additional_desktop_evidence_completed"),
            BuildBucket(records, "Internal continuation time", static record => record.Category == "agent.internal_continuation_completed"),
        ];
    }

    private static BrainTraceBucketSummary BuildBucket(
        IReadOnlyList<BrainTraceRecord> records,
        string name,
        Func<BrainTraceRecord, bool> predicate)
    {
        var matching = records.Where(predicate).ToArray();
        return new BrainTraceBucketSummary(name, matching.Length, matching.Sum(GetElapsedMs));
    }

    private static double GetElapsedMs(BrainTraceRecord record)
        => record.TryGetInt64("elapsedMs", out var elapsedMs)
            ? elapsedMs
            : 0d;

    private static string SummarizeEvent(BrainTraceRecord record)
    {
        return record.Category switch
        {
            "agent.tool_call_completed" => record.GetString("executedTool") ?? record.GetString("tool") ?? string.Empty,
            "assistant.reply" => record.GetString("sayPreview") ?? record.GetString("sayText") ?? string.Empty,
            "llm.response" => record.GetString("textPreview") ?? string.Empty,
            "agent.desktop_followup_snapshot" => record.GetString("tool") ?? record.GetString("snapshotWindow") ?? string.Empty,
            "agent.internal_continuation_completed" => record.GetString("policyName") ?? string.Empty,
            "mcp.call.complete" => record.GetString("tool") ?? string.Empty,
            _ => record.DataRawText,
        };
    }
}

internal static class BrainScenarioEvaluator
{
    public static BrainTurnAssessment AssessTurn(
        IReadOnlyList<BrainTraceRecord> records,
        long turnId,
        BrainScenarioAssertions? assertions = null)
    {
        assertions ??= BrainScenarioAssertions.Empty;
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

        return new BrainTurnAssessment(
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

    public static BrainScenarioResult EvaluateScenario(
        IReadOnlyList<BrainTraceRecord> records,
        BrainScenarioDefinition scenario,
        IReadOnlyList<BrainScriptedTurnResult> turns)
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
            return new BrainScenarioResult(scenario.Name, false, turns, failures);
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

        return new BrainScenarioResult(
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
        BrainConsoleOptions options,
        AppConfig config,
        ILlmClient llmClient,
        McpClientManager mcpManager,
        CancellationToken cancellationToken)
    {
        var suite = string.IsNullOrWhiteSpace(options.ScenarioFilePath)
            ? new BrainScenarioSuite(
                "ad hoc scripted run",
                [new BrainScenarioDefinition("Ad hoc scripted commands", options.Commands, BrainScenarioAssertions.Empty)])
            : BrainScenarioLoader.LoadFromFile(options.ScenarioFilePath);

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
            var desktopSession = new DesktopSessionContext();
            var turns = new List<BrainScriptedTurnResult>();
            var stopScenario = false;

            for (var commandIndex = 0; commandIndex < scenario.Commands.Count; commandIndex++)
            {
                var command = scenario.Commands[commandIndex];
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

                    var scriptedLookahead = commandIndex + 1 < scenario.Commands.Count
                        ? new ScriptedLookaheadContext(nextTurnId + 1, scenario.Commands[commandIndex + 1])
                        : null;
                    var processedTurn = await BrainTurnProcessor.ProcessAsync(
                        turnId,
                        command,
                        history,
                        desktopSession,
                        config,
                        llmClient,
                        mcpManager,
                        cancellationToken,
                        turnSource: "scripted",
                        scriptedLookahead: scriptedLookahead);
                    if (processedTurn.UpdatedConfig is not null)
                    {
                        config = processedTurn.UpdatedConfig;
                    }
                    var logRecords = BrainTraceLogReader.ReadAll(jsonLogPath);
                    var assessment = BrainScenarioEvaluator.AssessTurn(logRecords, turnId, scenario.Assertions);
                    var turnResult = new BrainScriptedTurnResult(turnId, command, processedTurn.Reply, assessment);
                    turns.Add(turnResult);
                    ReportTurn(turnResult);

                    if (!assessment.Passed)
                    {
                        stopScenario = true;
                        overallPassed = false;
                        break;
                    }

                    if (scriptedLookahead is not null &&
                        processedTurn.Reply.LookaheadDecision is { } lookaheadDecision &&
                        TryAdvanceNoOpLookaheadTurn(
                            scenario,
                            commandIndex + 1,
                            scriptedLookahead.NextTurnId,
                            turnId,
                            lookaheadDecision,
                            history,
                            jsonLogPath,
                            turns,
                            out var lookaheadTurnPassed))
                    {
                        commandIndex++;
                        nextTurnId = Math.Max(nextTurnId, scriptedLookahead.NextTurnId);
                        if (!lookaheadTurnPassed)
                        {
                            stopScenario = true;
                            overallPassed = false;
                            break;
                        }
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

                    var failedTurn = new BrainScriptedTurnResult(
                        turnId,
                        command,
                        new AgentReply(string.Empty, string.Empty, string.Empty),
                        BrainTurnAssessment.CreateExecutionFailure(ex.Message));
                    turns.Add(failedTurn);
                    ReportTurn(failedTurn);
                    stopScenario = true;
                    overallPassed = false;
                    break;
                }
            }

            var scenarioRecords = BrainTraceLogReader.ReadAll(jsonLogPath);
            var scenarioResult = BrainScenarioEvaluator.EvaluateScenario(scenarioRecords, scenario, turns);
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

    internal static bool TryAdvanceNoOpLookaheadTurn(
        BrainScenarioDefinition scenario,
        int targetCommandIndex,
        long targetTurnId,
        long sourceTurnId,
        ScriptedLookaheadDecision decision,
        List<AgentMessage> history,
        string jsonLogPath,
        List<BrainScriptedTurnResult> turns,
        out bool passed)
    {
        passed = false;
        if (targetCommandIndex < 0 ||
            targetCommandIndex >= scenario.Commands.Count)
        {
            WriteLookaheadFallback(sourceTurnId, targetTurnId, "target_command_index_out_of_range");
            return false;
        }

        if (decision.TargetTurnId != targetTurnId)
        {
            WriteLookaheadFallback(sourceTurnId, targetTurnId, "target_turn_mismatch");
            return false;
        }

        if (!decision.CurrentTurnComplete)
        {
            WriteLookaheadFallback(sourceTurnId, targetTurnId, "current_turn_not_complete");
            return false;
        }

        if (!decision.NextTurnCompleteNoOp)
        {
            WriteLookaheadFallback(sourceTurnId, targetTurnId, "next_turn_not_noop_complete");
            return false;
        }

        var command = scenario.Commands[targetCommandIndex];
        var reply = ScriptedLookahead.BuildNoOpReply(decision);
        DebugTrace.WriteStructuredEvent(
            "agent.turn.scripted_begin",
            new Dictionary<string, object?>
            {
                ["turn"] = targetTurnId,
                ["scenario"] = scenario.Name,
                ["command"] = command,
                ["lookaheadSourceTurn"] = sourceTurnId,
            });
        DebugTrace.WriteStructuredEvent(
            "agent.lookahead.advanced",
            new Dictionary<string, object?>
            {
                ["sourceTurn"] = sourceTurnId,
                ["targetTurn"] = targetTurnId,
                ["currentTurnStatus"] = decision.CurrentTurnStatus,
                ["nextTurnStatus"] = decision.NextTurnStatus,
                ["reason"] = decision.Reason,
                ["mode"] = "next_noop_only",
            });
        DebugTrace.WriteStructuredEvent(
            "agent.turn.processing_start",
            new Dictionary<string, object?>
            {
                ["turn"] = targetTurnId,
                ["source"] = "scripted-lookahead",
                ["historyMessages"] = history.Count,
                ["queuedText"] = DebugTrace.Preview(command, 500),
            });
        Display.UserMessage(command);
        Display.AssistantReply(reply.SpokenText, reply.LogText);
        DebugTrace.WriteStructuredEvent(
            "assistant.reply",
            new Dictionary<string, object?>
            {
                ["turn"] = targetTurnId,
                ["elapsedMs"] = 0,
                ["attempts"] = 0,
                ["usedAnyTools"] = false,
                ["performedDesktopAction"] = false,
                ["performedConfidenceEvidenceRetry"] = false,
                ["lookaheadSourceTurn"] = sourceTurnId,
                ["sayText"] = reply.SpokenText,
                ["logText"] = reply.LogText,
                ["sayPreview"] = DebugTrace.Preview(reply.SpokenText, 400),
                ["logPreview"] = DebugTrace.Preview(reply.LogText, 900),
                ["rawPreview"] = DebugTrace.Preview(reply.RawText, 1200),
            });
        history.Add(new AgentMessage.User(command));
        history.Add(new AgentMessage.Assistant(reply.RawText));
        DebugTrace.WriteEvent(
            "agent.turn.complete",
            $"turn={targetTurnId}, source=scripted-lookahead, spoken={DebugTrace.Preview(reply.SpokenText, 300)}, log={DebugTrace.Preview(reply.LogText, 600)}");

        var logRecords = BrainTraceLogReader.ReadAll(jsonLogPath);
        var assessment = BrainScenarioEvaluator.AssessTurn(logRecords, targetTurnId, scenario.Assertions);
        var turnResult = new BrainScriptedTurnResult(targetTurnId, command, reply, assessment);
        turns.Add(turnResult);
        ReportTurn(turnResult);
        passed = assessment.Passed;
        return true;
    }

    private static void WriteLookaheadFallback(long sourceTurnId, long targetTurnId, string reason)
    {
        DebugTrace.WriteStructuredEvent(
            "agent.lookahead.fallback",
            new Dictionary<string, object?>
            {
                ["sourceTurn"] = sourceTurnId,
                ["targetTurn"] = targetTurnId,
                ["reason"] = reason,
            });
    }

    private static void ReportTurn(BrainScriptedTurnResult turn)
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

    private static void ReportScenario(BrainScenarioResult scenario)
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
