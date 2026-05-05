# Netflix Phase Prompt Rerun

Date: `2026-04-25`

This note tracks the first rerun after adding generic phase framing and bounded
same-surface multi-action guidance to the shared agent prompt plus the startup
and Edge skills.

Raw artifacts:

- `.tmp/netflix-smoke-runtime/2026-04-25-phase-prompt-rerun/brain.debug.jsonl`
- `.tmp/netflix-smoke-runtime/2026-04-25-phase-prompt-rerun/brain.debug.log`
- `.tmp/netflix-smoke-runtime/2026-04-25-phase-prompt-rerun/trace-report.md`

## Top line

| Run | Scenario elapsed s | LLM responses | LLM time s | Tool calls | Requested tool s |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2026-04-22 baseline | 882.255 | 26 | 822.765 | 21 | 32.523 |
| 2026-04-25 rerun 2 | 635.759 | 18 | 579.740 | 13 | 31.157 |
| 2026-04-25 phase prompt rerun | 680.153 | 18 | 624.751 | 14 | 31.112 |

## Turn summary

| Turn | Elapsed s | Attempts | LLM s | Tool calls | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| 1 | 141.188 | 4 | 129.850 | 4 | Netflix profile picker visible |
| 2 | 89.310 | 3 | 84.527 | 1 | Min PIN prompt visible |
| 3 | 243.533 | 5 | 228.886 | 5 | Netflix home visible |
| 4 | 89.032 | 3 | 73.522 | 2 | Search results visible |
| 5 | 116.210 | 3 | 107.966 | 2 | Episode 1 playing |

## Main signal

- Turn `1` improved versus the comparable rerun:
  - `194.771 s` down to `141.188 s`
  - `5` attempts down to `4`
  - `183.161 s` LLM time down to `129.850 s`
- The improvement came from a stable-surface two-tool response:
  - attempt `3` returned `set_window_element_text` and `press_window_key`
  - that collapsed URL entry plus submission into one LLM round trip
- The whole scenario still regressed versus rerun `2` because later turns got
  worse:
  - turn `2`: `77.958 s` up to `89.310 s`
  - turn `3`: `125.395 s` up to `243.533 s`

## Attempt breakdown of turn 1

| Attempt | LLM s | Tools after response |
| --- | ---: | --- |
| 1 | 16.571 | `list_windows` |
| 2 | 32.646 | `activate_window` |
| 3 | 64.121 | `set_window_element_text`, `press_window_key` |
| 4 | 16.511 | final confirmation |

## Read

- The generic phase and bounded multi-action prompt change helped the exact
  place we wanted first: turn `1` browser entry.
- The same prompt change was too permissive for later turns. Turn `3`
  especially became expensive again, and the trace shows a bundled
  `set_window_element_text` plus `describe_window_focus` attempt that did not
  pay off.
- The next refinement should keep the turn `1` benefit while narrowing what can
  be batched:
  - allow short execution-only chains on the same validated surface
  - stop before appending cognitive refresh tools such as focus/window
    inspection into the same batch
  - keep runtime-owned post-transition verification as the handoff point
