# Netflix OpenAI API GPT-5.4 Rerun

Date: `2026-04-25`

This note records the live Netflix smoke rerun using the OpenAI API provider
with `OPENAI_MODEL=gpt-5.4`, compared with the latest Codex-backed run.

Raw artifacts:

- `.tmp/netflix-smoke-runtime/2026-04-25-openai-api-gpt-5.4-rerun-after-json-fix/brain.debug.jsonl`
- `.tmp/netflix-smoke-runtime/2026-04-25-openai-api-gpt-5.4-rerun-after-json-fix/brain.debug.log`
- `.tmp/netflix-smoke-runtime/2026-04-25-openai-api-gpt-5.4-rerun-after-json-fix/trace-report.md`
- `.tmp/netflix-smoke-runtime/2026-04-25-openai-api-gpt-5.4-failed-disposed-jsondocument/trace-report.md`

## Top Line

| Run | Provider / model | Scenario elapsed s | LLM responses | LLM time s | Avg LLM attempt s | Tool time s | Lookahead advances |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-04-25 browser skill URL submit rerun | `OpenAiCodex / gpt-5.4-mini` | 242.735 | 12 | 206.469 | 17.206 | 18.170 | 1 |
| 2026-04-25 OpenAI API GPT-5.4 rerun | `OpenAiApi / gpt-5.4` | 98.172 | 16 | 56.160 | 3.510 | 18.250 | 0 |

## Delta

| Metric | Change |
| --- | ---: |
| Scenario elapsed | `-144.563 s` |
| Scenario elapsed reduction | `59.6%` |
| Scenario speedup | `2.47x` |
| LLM time | `-150.309 s` |
| LLM time reduction | `72.8%` |
| LLM speedup | `3.68x` |
| Average LLM attempt speedup | `4.90x` |
| LLM responses | `+4` |

## Turn Summary

| Turn | Codex elapsed s | Codex LLM s | Codex attempts | API elapsed s | API LLM s | API attempts |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 74.283 | 63.578 | 4 | 24.969 | 14.028 | 4 |
| 2 | 0.000 | 0.000 | 0 | 3.627 | 3.606 | 1 |
| 3 | 15.725 | 15.704 | 1 | 3.094 | 3.076 | 1 |
| 4 | 71.332 | 55.647 | 3 | 24.073 | 8.563 | 3 |
| 5 | 80.162 | 71.540 | 4 | 41.529 | 26.888 | 7 |

## Read

The OpenAI API `gpt-5.4` path was much faster on model latency even though it
used more LLM responses. Tool time stayed essentially flat, so the improvement
is almost entirely from LLM response latency.

There is an important quality caveat: the API run passed the current log-based
scenario checks, but its final Turn 5 reply said the title overlay was open and
playback was not confirmed. The Codex baseline reached the stronger final state:
`It's playing now.`

The first API attempt exposed a `JsonDocument` lifetime bug in the
generic-container named-target rewrite path. That was fixed by cloning the
snapshot tree when it crosses the parse scope, and covered by
`EvaluateGenericContainerActionToNamedTarget_UsesExactCaseSnapshotTreeAfterParseScope`.

Next useful follow-up: tighten the scenario evaluator so a final reply that says
the requested action is incomplete cannot pass only because it contains the
required title text.
