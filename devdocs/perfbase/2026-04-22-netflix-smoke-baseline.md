# Netflix Smoke Baseline

Date: `2026-04-22`

This is the tracked summary for the fresh scripted Netflix smoke baseline run.
The raw live artifacts stay under ignored `.tmp/`:

- `.tmp/netflix-smoke-runtime/2026-04-22-baseline/brain.debug.jsonl`
- `.tmp/netflix-smoke-runtime/2026-04-22-baseline/brain.debug.log`
- `.tmp/netflix-smoke-runtime/2026-04-22-baseline/README.md`

## Run metadata

- Command: `.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml`
- Commit: `2d994a83b32bb23c54f205c3d2b29a0202b6105b`
- Scenario: `src/scenarios/netflix-boyfriend-on-demand.yml`
- Provider: `OpenAiCodex`
- Model: `gpt-5.4-mini`
- Passed on first try: `yes`

## Timing

- Outer command start: `2026-04-22T21:40:18.4596273-07:00`
- Outer command end: `2026-04-22T21:55:07.2451639-07:00`
- Outer elapsed: `888.786 s`
- Scenario session start: `2026-04-22T21:40:24.7181143-07:00`
- Scenario passed at: `2026-04-22T21:55:06.9730828-07:00`
- Scenario elapsed: `882.255 s`

## Turn summary

| Turn | Elapsed s | Attempts | Result |
| --- | ---: | ---: | --- |
| 1 | 148.499 | 5 | Netflix profile screen is visible |
| 2 | 114.895 | 4 | Min PIN prompt is visible |
| 3 | 241.045 | 7 | Min opened and Netflix home is visible |
| 4 | 165.011 | 5 | Search results show Boyfriend on Demand |
| 5 | 208.425 | 5 | Episode 1 is playing |

## Quick totals from the trace

- Total LLM responses: `26`
- Estimated total LLM wait time: `824.000 s`
- Total tool calls: `21`
- Total tool time: `32.523 s`
- Post-action snapshots: `12`
- Reply repairs: `0`
- Additional evidence passes: `0`
- Internal continuations considered: `10`
- Internal continuations executed: `0`

## Notes

- This run is the current fresh baseline for the workspace.
- The raw trace remains untracked by design so later runs do not create large
  git churn under `docs/`.
