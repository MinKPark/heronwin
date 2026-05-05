# Netflix Scripted Lookahead No-Op Slice

Date: `2026-04-25`

This note records the first live Netflix smoke run after implementing scripted
turn lookahead for next-turn no-op completion only.

Raw artifacts:

- `.tmp/netflix-smoke-runtime/2026-04-25-scripted-lookahead-noop/brain.debug.jsonl`
- `.tmp/netflix-smoke-runtime/2026-04-25-scripted-lookahead-noop/brain.debug.log`
- `.tmp/netflix-smoke-runtime/2026-04-25-scripted-lookahead-noop/trace-report.md`

## Top Line

| Run | Scenario elapsed s | LLM responses | LLM time s | Tool calls | Requested tool s | Lookahead advances |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-04-25 trace alignment rerun | 239.572 | 12 | 191.571 | 9 | 20.290 | 0 |
| 2026-04-25 scripted lookahead no-op | 230.791 | 12 | 193.412 | 8 | 18.113 | 1 |

## Turn Summary

| Turn | Elapsed s | Attempts | LLM s | Tool calls | Tool s | Note |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | 75.274 | 4 | 63.653 | 3 | 7.068 | fused confirmation plus Turn 2 lookahead |
| 2 | 0.000 | 0 | 0.000 | 0 | 0.000 | advanced as no-op from Turn 1 |
| 3 | 16.089 | 1 | 16.065 | 0 | 0.000 | still separate because max lookahead depth is 1 |
| 4 | 61.324 | 3 | 45.582 | 2 | 4.994 | lookahead requested, but next turn needed action |
| 5 | 76.817 | 4 | 68.112 | 3 | 6.051 | normal execution |

## Lookahead Events

| Event | Count |
| --- | ---: |
| `agent.lookahead.requested` | 2 |
| `agent.lookahead.decision` | 2 |
| `agent.lookahead.advanced` | 1 |
| `agent.lookahead.fallback` | 1 |

Fallback reason:

| Reason | Count |
| --- | ---: |
| `next_turn_not_noop_complete` | 1 |

## Read

The scenario passed and the no-op lookahead guardrails behaved correctly:

- Turn 1 confirmed Netflix was already on Min's home screen and advanced Turn 2
  without a separate LLM call.
- Turn 4 confirmed search results were visible but did not advance Turn 5,
  because opening the title and starting playback still needed tool actions.

The top-line LLM response count stayed flat versus the immediately prior
trace-alignment run because this run spent one extra LLM attempt in Turn 1.
That masked the saved Turn 2 no-op call. The useful signal is the trace shape:
Turn 2 is now a real logical turn with `attempts=0`, and the report shows one
estimated LLM call saved.

The next useful performance slice is future-turn action retargeting. That would
let the Turn 4 fused response immediately start Turn 5's first action when the
next command is not a no-op.
