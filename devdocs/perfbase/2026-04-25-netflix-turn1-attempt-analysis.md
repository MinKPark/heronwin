# Netflix Turn 1 Attempt Analysis

Date: `2026-04-25`

This note preserves the detailed turn `1` comparison used to judge later
changes. It focuses on the scripted Netflix smoke command:

- `.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml`

Primary sources:

- baseline trace: `.tmp/netflix-smoke-runtime/2026-04-22-baseline/brain.debug.jsonl`
- baseline report: `.tmp/netflix-smoke-runtime/2026-04-22-baseline/trace-report.md`
- comparable rerun trace: `.tmp/netflix-smoke-runtime/2026-04-25-rerun-2/brain.debug.jsonl`
- comparable rerun report: `.tmp/netflix-smoke-runtime/2026-04-25-rerun-2/trace-report.md`

## Top line

| Run | Turn 1 elapsed s | Turn 1 attempts | Turn 1 LLM s | Turn 1 tool s | Final stop state |
| --- | ---: | ---: | ---: | ---: | --- |
| 2026-04-22 baseline | 148.499 | 5 | 137.662 | 7.189 | Netflix profile picker visible |
| 2026-04-25 rerun 2 | 194.771 | 5 | 183.161 | 7.132 | Netflix profile picker visible |

The action pattern stayed the same across both runs. The main regression was
prompt growth and LLM wait time inside the same five-step turn shape.

## 2026-04-25 rerun 2

| Attempt | LLM s | Prompt tokens | Messages | Model response | Executed tool | Tool s | Landed state |
| --- | ---: | ---: | ---: | --- | --- | ---: | --- |
| 1 | 21.573 | 7,436 | 1 | `list_windows {}` | `list_windows` | 0.045 | Window list returned several browser candidates; no selected target yet |
| 2 | 43.259 | 8,195 | 3 | `activate_window {"windowHandle":"0x001705B6"}` | `activate_window` | 2.265 | Edge tab on Naver Sports became the active browser window |
| 3 | 65.709 | 41,905 | 6 | `set_window_element_text {"windowHandle":"0x001705B6","elementPath":"1/0/0/1/0/0/3/1","text":"https://www.netflix.com/"}` | `set_window_element_text` | 2.645 | Address bar received the Netflix URL, but the page had not navigated yet |
| 4 | 23.612 | 76,276 | 10 | `press_window_key {"windowHandle":"0x001705B6","key":"Enter"}` | `press_window_key` | 2.177 | Window title changed to `Netflix - Personal - Microsoft Edge` |
| 5 | 29.008 | 79,174 | 13 | final answer | none | 0.000 | Fresh post-action snapshot confirmed the Netflix profile picker |

## 2026-04-22 baseline

Prompt-token estimates were not recorded in the baseline trace yet, so that
column is `n/a`.

| Attempt | LLM s | Prompt tokens | Messages | Model response | Executed tool | Tool s | Landed state |
| --- | ---: | ---: | ---: | --- | --- | ---: | --- |
| 1 | 32.804 | n/a | 1 | `list_windows {}` | `list_windows` | 0.078 | Window list returned several browser candidates; no selected target yet |
| 2 | 25.656 | n/a | 3 | `activate_window {"windowHandle":"0x00020072"}` | `activate_window` | 2.290 | Edge tab on YouTube became the active browser window |
| 3 | 30.700 | n/a | 6 | `set_window_element_text {"windowHandle":"0x00020072","elementPath":"1/0/0/1/0/0/3/1","text":"https://www.netflix.com/"}` | `set_window_element_text` | 2.640 | Address bar received the Netflix URL, but the page had not navigated yet |
| 4 | 32.793 | n/a | 10 | `press_window_key {"windowHandle":"0x00020072","key":"Enter"}` | `press_window_key` | 2.181 | Window title changed to `Netflix - Personal - Microsoft Edge` |
| 5 | 15.707 | n/a | 13 | final answer | none | 0.000 | Fresh post-action snapshot confirmed the Netflix profile picker |

## What this shows

- Turn `1` is not tool-bound. The rerun spent `183.161 s` in LLM time and only
  `7.132 s` in executed tool time.
- The current comparable rerun followed the same five-step sequence as the
  baseline:
  - `list_windows`
  - `activate_window`
  - `set_window_element_text`
  - `press_window_key`
  - final confirmation
- The largest prompt jump happened after the active-window snapshot entered the
  turn context:
  - attempt `1`: `7,436`
  - attempt `2`: `8,195`
  - attempt `3`: `41,905`
  - attempt `4`: `76,276`
  - attempt `5`: `79,174`
- The state-transition boundary is clear:
  - attempts `1` through `3` stayed on the same browser-chrome surface
  - attempt `4` triggered the actual navigation transition
  - attempt `5` only confirmed the new surface

## Comparison target for the next slice

For later reruns, the ideal improvement is not just lower total time. The more
specific target is reducing how many full LLM round trips turn `1` needs before
the page transition is submitted and confirmed.
