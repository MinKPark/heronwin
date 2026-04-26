# Scripted Turn Lookahead Plan

Date: `2026-04-25`

## Goal

Reduce scripted scenario runtime by removing avoidable LLM round trips that only
confirm one turn before immediately starting the next.

The core idea is to let one LLM call bridge:

1. confirmation of the current scripted turn, and
2. decision/action planning for the next scripted turn.

This should preserve logical turn boundaries, scenario assertions, and trace
clarity. It should not become a Netflix-specific shortcut.

## Motivation

The latest corrected Netflix smoke trace still spends most wall-clock time in
LLM calls.

From `.tmp/netflix-smoke-runtime/2026-04-25-trace-alignment-rerun/trace-report.md`:

| Metric | Value |
| --- | ---: |
| Scenario elapsed | `239.572 s` |
| Total LLM time | `191.571 s` |
| LLM responses | `12` |
| Average LLM attempt | `15.964 s` |

Several attempts are confirmation-only or conditional no-op checks:

| Turn | Attempt | LLM s | Role |
| ---: | ---: | ---: | --- |
| 1 | 3 | `18.092` | confirm Netflix home/profile condition |
| 2 | 1 | `15.863` | conditional profile-picker no-op |
| 3 | 1 | `13.899` | conditional PIN no-op |
| 4 | 3 | `16.811` | confirm search results visible |
| 5 | 4 | `17.037` | final playback confirmation |

If the runtime can safely combine confirmation and next-turn decision-making,
each removed LLM call saves roughly `14-18 s`.

## Proposed Behavior

Enable lookahead only in scripted scenario mode.

After a tool action produces fresh evidence and there is a next scenario
command, the next LLM request may include:

- current command
- latest post-action evidence
- next scenario command as lookahead context

The model must answer a structured decision:

- `current_complete`
- `current_needs_recovery`
- `next_complete_noop`
- `next_needs_action`

If the current turn is not complete, the runtime continues recovery under the
current turn. If the current turn is complete and the next turn is a no-op, the
runtime records the next turn as completed without an additional LLM call. If
the current turn is complete and the next turn needs action, the returned tools
are executed under the next logical turn id.

## Non-Goals

- Do not merge scenario semantics.
- Do not weaken per-turn assertions.
- Do not add Netflix-specific runtime shortcuts.
- Do not allow unbounded multi-turn batching.
- Do not use stale evidence to advance turns.

## Invariants

- Logical turns remain separate in logs.
- Scenario assertions still evaluate per logical turn.
- Tool calls for a next command are logged under that next command's turn id.
- The trace must show when an LLM call made a lookahead decision.
- Any ambiguous, malformed, or unsafe decision falls back to the current
  step-by-step behavior.

## Structured Response Contract

The exact contract can evolve, but the runtime needs something equivalent to:

```json
{
  "currentTurnStatus": "complete",
  "currentSay": "Netflix is open now.",
  "currentLog": "Fresh evidence shows Netflix home is visible.",
  "nextTurnStatus": "needs_action",
  "nextTurnReason": "The next command asks to search within Netflix.",
  "toolTargetTurn": 4,
  "toolCalls": [
    {
      "name": "invoke_window_element",
      "arguments": {
        "windowHandle": "0x...",
        "elementPath": "..."
      }
    }
  ]
}
```

Minimum required fields:

- `currentTurnStatus`
- `currentSay`
- `currentLog`
- `nextTurnStatus`, when lookahead is used
- `toolTargetTurn`, when tools are returned for a future turn

## Fallback Rules

Fall back to today's one-turn-at-a-time behavior when:

- scripted mode is off
- no next scenario command exists
- there is no fresh post-action evidence
- current turn status is missing or ambiguous
- current turn is not complete
- returned tools target anything other than the next allowed turn
- a future-turn tool is returned without `toolTargetTurn`
- lookahead depth exceeds the configured cap
- scenario assertions for the completed logical turn fail

Initial max lookahead depth should be `1`.

## Trace Events

Add structured events:

| Event | Purpose |
| --- | --- |
| `agent.lookahead.requested` | Runtime included next command in the LLM request |
| `agent.lookahead.decision` | Parsed model decision and target turn |
| `agent.lookahead.advanced` | Runtime advanced to a later logical turn without a separate LLM call |
| `agent.lookahead.fallback` | Runtime declined lookahead and explains why |
| `agent.lookahead.tool_retargeted` | Tool call from fused response is executed under next logical turn |

Important fields:

- `sourceTurn`
- `targetTurn`
- `currentTurnStatus`
- `nextTurnStatus`
- `reason`
- `toolTargetTurn`
- `toolCallId`
- `requestedTool`
- `executedTool`

## Metrics

Update the trace report or helper analysis to show:

- lookahead requests
- lookahead advances
- lookahead fallbacks by reason
- logical no-op turns completed by lookahead
- future-turn tools executed by lookahead
- estimated LLM calls saved

## Tests To Add

High-value unit tests:

| Test | Purpose |
| --- | --- |
| Lookahead disabled outside scripted mode | Prevent behavior leaking into interactive use |
| Current incomplete blocks advancement | Recovery stays in the same turn |
| Current complete plus next no-op marks next turn complete | Covers conditional skip turns |
| Current complete plus next action retargets tools to next turn | Ensures tools are logged under the next logical turn |
| Ambiguous LLM decision falls back | Protects against malformed output |
| Tool returned without `toolTargetTurn` falls back | Prevents accidental cross-turn execution |
| Fresh evidence required for lookahead | Avoids stale-snapshot advancement |
| Scenario assertions still run per logical turn | Prevents false passes |
| Trace contains source and target turn ids | Keeps timing analysis trustworthy |
| Max lookahead depth respected | Prevents runaway batching |

Integration-style test:

Use a small fake three-command scenario:

1. Turn 1 needs one tool and then succeeds.
2. Turn 2 is conditional and should no-op.
3. Turn 3 needs one tool.

Expected result:

- one fused LLM response can complete Turn 1, skip Turn 2, and start Turn 3
- logs still contain Turn 1, Turn 2, and Turn 3 separately
- Turn 3's tool call is recorded under Turn 3
- scenario assertions still pass per logical turn

## Rollout Plan

1. Add tests and trace contracts first.
2. Implement lookahead for no-op next turns only.
3. Rerun scripted unit tests and trace report tests.
4. Measure the Netflix scenario.
5. Add future-turn tool execution only after the no-op path is stable.
6. Measure again and compare:
   - scenario elapsed
   - LLM responses
   - total LLM time
   - per-turn trace shape

## Risks

| Risk | Mitigation |
| --- | --- |
| False success across turns | Keep assertions per logical turn and require fresh evidence |
| Hard-to-debug traces | Add explicit source/target turn trace events |
| Runaway batching | Cap lookahead depth at `1` initially |
| Hidden app-specific behavior | Keep logic in scripted runtime, not Netflix branches |
| Tool calls logged under wrong turn | Add targeted tests for retargeting |
| Model returns ambiguous status | Strict fallback to existing behavior |

## Recommendation

Start with no-op lookahead. It is the safest and likely removes the Turn 2 and
Turn 3 conditional no-op calls in the Netflix scenario. Once that is stable,
extend to next-turn tool execution so a confirmation call can immediately start
the following command.
