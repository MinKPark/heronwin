# Netflix MCP Call Instrumentation Rerun

Date: `2026-04-25`

This note records the first successful scripted Netflix smoke run after adding
correlated MCP call lifecycle instrumentation.

Raw artifacts:

- `.tmp/netflix-smoke-runtime/2026-04-25-mcp-call-instrumentation-rerun/brain.debug.jsonl`
- `.tmp/netflix-smoke-runtime/2026-04-25-mcp-call-instrumentation-rerun/brain.debug.log`
- `.tmp/netflix-smoke-runtime/2026-04-25-mcp-call-instrumentation-rerun/trace-report.md`

## Top line

| Run | Scenario elapsed s | LLM responses | LLM time s | Tool calls | Requested tool s |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2026-04-25 compact inventory pass | 315.220 | 14 | 277.501 | 10 | 18.693 |
| 2026-04-25 MCP call instrumentation rerun | 258.062 | 14 | 216.243 | 11 | 20.928 |

## Turn summary

| Turn | Elapsed s | Attempts | LLM s | Tool calls | Tool s |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 64.789 | 3 | 49.319 | 4 | 9.245 |
| 2 | 14.415 | 1 | 14.394 | 0 | 0.000 |
| 3 | 16.931 | 1 | 16.913 | 0 | 0.000 |
| 4 | 94.398 | 5 | 78.123 | 4 | 5.589 |
| 5 | 66.302 | 4 | 57.493 | 3 | 6.094 |

## Turn 1 attempts

| Attempt | LLM s | Tools after response |
| --- | ---: | --- |
| 1 | 14.192 | `activate_window` |
| 2 | 15.928 | `press_window_key`, `set_window_element_text`, `press_window_key` |
| 3 | 19.199 | final confirmation |

## MCP instrumentation check

| Event | Count |
| --- | ---: |
| `mcp.call.start` | 26 |
| `mcp.call.end` | 26 |
| `mcp.call.complete` | 26 |
| `mcp.call.timeout` | 0 |
| `mcp.call.failed` | 0 |
| `mcp.stderr` with active call id | 215 |

Every `mcp.call.start` had a matching `mcp.call.end` by `mcpCallId`.

## MCP call cost by tool

| Tool | Count | Total s | Max s |
| --- | ---: | ---: | ---: |
| `describe_window` | 12 | 11.880 | 1.460 |
| `press_window_key` | 4 | 8.710 | 2.180 |
| `invoke_window_element` | 3 | 8.480 | 2.840 |
| `set_window_element_text` | 1 | 2.620 | 2.620 |
| `focus_window_element` | 1 | 2.350 | 2.350 |
| `activate_window` | 1 | 2.270 | 2.270 |
| `type_window_text` | 1 | 2.180 | 2.180 |
| `capture_window_screenshot` | 1 | 0.380 | 0.380 |
| `list_windows` | 2 | 0.040 | 0.040 |

## Read

The scenario passed and the new instrumentation gives a clean MCP lifecycle:
start, end, complete, and stderr correlation all line up.

This run started from an active YouTube Edge tab but an already-authenticated
Netflix account state, so turns `2` and `3` were valid no-op checks. Turn `1`
is the strongest signal for the latest startup/navigation behavior: the model
used compact startup inventory to select the existing browser window, then
batched address-bar focus, URL entry, and Enter in one attempt.
