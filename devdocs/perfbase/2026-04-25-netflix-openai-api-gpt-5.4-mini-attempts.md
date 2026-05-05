# Netflix OpenAI API GPT-5.4-Mini Attempts

Date: `2026-04-25`

This note records the attempted Netflix smoke runs using the OpenAI API
provider with `OPENAI_MODEL=gpt-5.4-mini`.

Raw artifacts:

- `.tmp/netflix-smoke-runtime/2026-04-25-openai-api-gpt-5.4-mini-failed-turn1-startup/`
- `.tmp/netflix-smoke-runtime/2026-04-25-openai-api-gpt-5.4-mini-failed-rate-limit-after-startup-skill/`
- `.tmp/netflix-smoke-runtime/2026-04-25-openai-api-gpt-5.4-mini-aborted-clean-rerun/`

## Attempts

| Attempt | Result | Elapsed s | Turns reached | LLM responses | LLM time s | Read |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| Before startup-skill fix | Failed | 11.769 | 1 | 4 | 5.212 | Tried invalid fuzzy `activate_window` args, recovered to Edge, then stopped before URL navigation. |
| After startup-skill fix | Failed | 88.724 | 2 | 7 | 62.712 | Startup guidance worked and Netflix home was reached, then the run failed on OpenAI API `429` TPM rate limits in Turn 2. |
| Clean rerun after cooldown | Failed | 115.420 | 5 | 12 | 89.163 | Reached Turn 5, then failed on another `429` TPM limit and had a reply contradiction in the scenario log. |

## Read

The model latency for successful individual attempts was very low, often around
`1-3 s`, but the run was not stable enough to produce a completed scenario under
the current OpenAI API `gpt-5.4-mini` rate limits.

The startup-skill update did improve behavior: after the patch, mini selected
the concrete browser `windowHandle` instead of the invalid fuzzy activation
shape. The remaining blocker is measurement reliability under `gpt-5.4-mini`
TPM limits, plus the need to inspect the Turn 4/5 contradiction in the clean
rerun before treating mini as behaviorally comparable to `gpt-5.4`.

Next useful measurement step: wait for the rate-limit window to fully reset, run
a single mini scenario with no preceding failed attempts, and consider lowering
context size or improving API retry/backoff if `429` repeats.
