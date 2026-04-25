# Netflix Smoke Carry-Forward Slice

Date: `2026-04-25`

This is the tracked summary for the first behavior-changing scripted turn-start
carry-forward rerun. The raw live artifacts stay under ignored `.tmp/`:

- `.tmp/netflix-smoke-runtime/2026-04-25-carry-forward-slice/brain.debug.jsonl`
- `.tmp/netflix-smoke-runtime/2026-04-25-carry-forward-slice/brain.debug.log`
- `.tmp/netflix-smoke-runtime/2026-04-25-carry-forward-slice/trace-report.md`

## Run metadata

- Command: `.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml`
- Commit: `f96fc1a`
- Scenario: `src/scenarios/netflix-boyfriend-on-demand.yml`
- Provider: `OpenAiCodex`
- Model: `gpt-5.4-mini`
- Passed on first try: `yes`

## Timing

- Session start: `2026-04-25T13:17:14.3991494-07:00`
- Scenario passed at: `2026-04-25T13:30:10.7479813-07:00`
- Scenario elapsed: `776.349 s`

## Comparison vs. 2026-04-22 baseline

- Scenario elapsed: `776.349 s` vs `882.255 s` baseline
  - delta: `-105.906 s`
- Total LLM responses: `19` vs `26`
  - delta: `-7`
- Estimated total LLM wait time from the trace report: `699.131 s` vs
  `824.000 s`
  - delta: `-124.869 s`
- Total tool calls: `14` vs `21`
  - delta: `-7`
- Requested tool time: `28.894 s` vs `32.523 s`
  - delta: `-3.629 s`

## Turn summary

| Turn | Elapsed s | Attempts | Result |
| --- | ---: | ---: | --- |
| 1 | 457.760 | 10 | Netflix home is visible |
| 2 | 14.772 | 1 | No action needed; home already visible |
| 3 | 14.988 | 1 | No action needed; PIN surface absent and home already visible |
| 4 | 108.779 | 3 | Search results show Boyfriend on Demand |
| 5 | 174.200 | 4 | Episode 1 is playing |

## What changed

- `agent.turn.ready_state_used` and `agent.turn.carry_forward_evidence_used`
  fired on turns `2` through `5`.
- Turn-start helper time stayed negligible at `0.016 s` total across `5`
  turn-start decisions.
- Turns `2` through `5` no longer opened with the old
  `list_windows` then `describe_window` discovery pair.
- Turn `4` now starts directly with `click_window_element`.
- Turn `5` now starts directly with `invoke_window_element`.

## Comparison caveat

- This rerun is not perfectly apples to apples with the 2026-04-22 baseline.
- On 2026-04-25, turn `1` ended directly on Netflix home instead of the
  profile-picker screen.
- That means turns `2` and `3` became valid conditional no-op checks instead of
  replaying the earlier profile-selection and PIN-entry flow.
- The carry-forward win is therefore real in trace shape, but the turn-by-turn
  elapsed-time comparison for turns `2` and `3` should not be over-interpreted
  without a more controlled rerun from the earlier start state.

## New top hotspot

- Turn `1` regressed badly to `457.760 s`.
- The trace shows browser-entry churn and repeated address-bar/site-navigation
  retries before Netflix finally loads.
- That makes turn `1` the clearest next P0 target now that later scripted turns
  are no longer paying the old rediscovery cost.
