# Netflix Compact Window Inventory Pass

Date: `2026-04-25`

This note records the first successful scripted Netflix smoke run after adding
runtime-owned compact startup window inventory.

Raw artifacts:

- `.tmp/netflix-smoke-runtime/2026-04-25-compact-window-inventory-pass/brain.debug.jsonl`
- `.tmp/netflix-smoke-runtime/2026-04-25-compact-window-inventory-pass/brain.debug.log`
- `.tmp/netflix-smoke-runtime/2026-04-25-compact-window-inventory-pass/trace-report.md`

## Top line

| Run | Scenario elapsed s | LLM responses | LLM time s | Tool calls | Requested tool s |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2026-04-22 baseline | 882.255 | 26 | 822.765 | 21 | 32.523 |
| 2026-04-25 rerun 2 | 635.759 | 18 | 579.740 | 13 | 31.157 |
| 2026-04-25 phase prompt rerun | 680.153 | 18 | 624.751 | 14 | 31.112 |
| 2026-04-25 compact inventory pass | 315.220 | 14 | 277.501 | 10 | 18.693 |

## Turn 1 comparison

| Run | Turn 1 elapsed s | Attempts | LLM s | Tool calls | Opening shape |
| --- | ---: | ---: | ---: | ---: | --- |
| 2026-04-25 rerun 2 | 194.771 | 5 | 183.161 | 4 | `list_windows` -> `activate_window` -> `set_window_element_text` -> `press_window_key` -> final |
| 2026-04-25 phase prompt rerun | 141.188 | 4 | 129.850 | 4 | `list_windows` -> `activate_window` -> batched URL entry + Enter -> final |
| 2026-04-25 compact inventory pass | 126.298 | 3 | 114.611 | 3 | injected compact inventory -> `activate_window` -> batched URL entry + Enter -> final |

## Turn 1 attempts

| Attempt | LLM s | Tools after response |
| --- | ---: | --- |
| 1 | 47.941 | `activate_window` |
| 2 | 50.434 | `set_window_element_text`, `press_window_key` |
| 3 | 16.236 | final confirmation |

## Startup inventory signal

- Runtime collected `list_windows` before the first LLM request.
- Trace emitted `agent.turn.startup_inventory_refreshed` for turn `1`.
- Trace emitted `agent.turn.startup_inventory_used` for turn `1`.
- The first LLM request had `messageCount=3` and `promptTokenEstimate=8288`.
- The first LLM response selected an existing Edge window directly with `activate_window`.

## Read

The compact startup inventory did what it was meant to do: it removed the
model-requested `list_windows` attempt from turn `1`. Compared with the phase
prompt rerun, turn `1` improved by `14.890 s`, removed one LLM attempt, and
reduced turn `1` LLM time by `15.239 s`.

This run started from an already-authenticated Netflix state and reached Netflix
home, so turns `2` and `3` became valid no-op checks. Treat the overall
`315.220 s` scenario elapsed as a useful smoke result, but use the turn `1`
attempt shape as the strongest apples-to-apples signal for the compact
inventory change.
