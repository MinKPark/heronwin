using System.Text.Json;

namespace HeronWin.Brain;

internal sealed record ScriptedLookaheadContext(
    long NextTurnId,
    string NextCommand);

internal sealed record ScriptedLookaheadDecision(
    long SourceTurnId,
    long TargetTurnId,
    string CurrentTurnStatus,
    string NextTurnStatus,
    string NextSay,
    string NextLog,
    string? Reason)
{
    public bool CurrentTurnComplete
        => IsCompleteStatus(CurrentTurnStatus);

    public bool NextTurnCompleteNoOp
        => IsNoOpCompleteStatus(NextTurnStatus);

    private static bool IsCompleteStatus(string status)
        => NormalizeStatus(status) is "complete" or "current_complete";

    private static bool IsNoOpCompleteStatus(string status)
        => NormalizeStatus(status) is "next_complete_noop" or "complete_noop" or "noop" or "no_op" or "complete";

    private static string NormalizeStatus(string status)
        => status.Trim().Replace('-', '_').ToLowerInvariant();
}

internal static class ScriptedLookahead
{
    public static string BuildInstruction(long currentTurnId, string currentCommand, ScriptedLookaheadContext context)
        => $"""
        Scripted lookahead context:
        - Current logical turn: {currentTurnId}
        - Current command: {currentCommand}
        - Next logical turn: {context.NextTurnId}
        - Next command: {context.NextCommand}

        First decide whether the current command is complete from the freshest evidence.
        If the current command is not complete, continue recovering the current command only.
        If the current command is complete, you may also decide whether the next command is already complete as a no-op from the same freshest evidence.
        This no-op lookahead phase may not execute tools for the next command. If the next command needs action, say so in JSON but do not return tool calls for that next command.

        When answering without tools, keep the normal `say` and `log` fields for the current command, and add these optional fields:
        - `currentTurnStatus`: `complete` or `current_needs_recovery`
        - `nextTurnStatus`: `next_complete_noop`, `next_needs_action`, or `unknown`
        - `nextSay`: short spoken result for the next command if it is already complete as a no-op
        - `nextLog`: evidence-grounded log for the next command if it is already complete as a no-op
        - `nextTurnReason`: brief reason for the lookahead decision
        - `toolTargetTurn`: omit or set to null in this no-op lookahead phase
        """;

    public static bool TryParseDecision(
        long sourceTurnId,
        long targetTurnId,
        string rawText,
        out ScriptedLookaheadDecision decision,
        out string skipReason)
    {
        decision = default!;
        skipReason = string.Empty;

        if (string.IsNullOrWhiteSpace(rawText))
        {
            skipReason = "empty_response";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                skipReason = "response_not_json_object";
                return false;
            }

            var root = document.RootElement;
            var currentTurnStatus = GetString(root, "currentTurnStatus");
            if (string.IsNullOrWhiteSpace(currentTurnStatus))
            {
                skipReason = "current_status_missing";
                return false;
            }

            var nextTurnStatus = GetString(root, "nextTurnStatus");
            if (string.IsNullOrWhiteSpace(nextTurnStatus))
            {
                skipReason = "next_status_missing";
                return false;
            }

            if (TryGetInt64(root, "toolTargetTurn", out var toolTargetTurn) &&
                toolTargetTurn != targetTurnId)
            {
                skipReason = "unexpected_tool_target_turn";
                return false;
            }

            decision = new ScriptedLookaheadDecision(
                sourceTurnId,
                targetTurnId,
                currentTurnStatus,
                nextTurnStatus,
                GetString(root, "nextSay") ?? string.Empty,
                GetString(root, "nextLog") ?? string.Empty,
                GetString(root, "nextTurnReason") ?? GetString(root, "reason"));

            return true;
        }
        catch
        {
            skipReason = "response_json_parse_failed";
            return false;
        }
    }

    public static AgentReply BuildNoOpReply(ScriptedLookaheadDecision decision)
    {
        var say = string.IsNullOrWhiteSpace(decision.NextSay)
            ? "No action was needed."
            : decision.NextSay.Trim();
        var log = string.IsNullOrWhiteSpace(decision.NextLog)
            ? decision.Reason ?? "Lookahead determined the next conditional command was already satisfied."
            : decision.NextLog.Trim();

        var raw = JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["say"] = say,
                ["log"] = log,
            },
            JsonSerializerOptionsCache.Default);
        return new AgentReply(log, say, raw);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Null => null,
            _ => property.ToString(),
        };
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(property.GetString(), out value),
            _ => false,
        };
    }
}
