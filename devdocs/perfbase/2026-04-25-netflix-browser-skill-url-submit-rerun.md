# Netflix Browser Skill URL Submit Rerun

Date: `2026-04-25`

This note records the first live Netflix smoke rerun after strengthening the
Edge browser skill to batch address-bar URL replacement and `Enter`
submission in one tool-call response.

Raw artifacts:

- `.tmp/netflix-smoke-runtime/2026-04-25-browser-skill-url-submit-rerun/brain.debug.jsonl`
- `.tmp/netflix-smoke-runtime/2026-04-25-browser-skill-url-submit-rerun/brain.debug.log`
- `.tmp/netflix-smoke-runtime/2026-04-25-browser-skill-url-submit-rerun/trace-report.md`

## Top Line

| Run | Scenario elapsed s | LLM responses | LLM time s | Turn 1 attempts | Turn 1 LLM s | Turn 1 tools | Lookahead advances |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-04-25 scripted lookahead no-op | 230.791 | 12 | 193.412 | 4 | 63.653 | 3 | 1 |
| 2026-04-25 browser skill URL submit rerun | 242.735 | 12 | 206.469 | 4 | 63.578 | 4 | 1 |

## Turn 1 Attempt Breakdown

| Attempt | LLM s | Tools after response | Read |
| ---: | ---: | --- | --- |
| 1 | 14.000 | `activate_window` | model tried `{"titleContains":"Microsoft Edge"}`; tool errored because that is not a valid activation shape |
| 2 | 13.996 | `activate_window` | model recovered by activating the concrete Edge window handle |
| 3 | 15.371 | `set_window_element_text`, `press_window_key` | browser skill improvement worked: URL replacement and `Enter` were batched in one LLM response |
| 4 | 20.212 | none | final confirmation plus Turn 2 lookahead no-op decision |

## Read

The browser skill change worked for the specific issue we targeted: Turn 1 no
longer paid a separate LLM attempt only to decide whether to press `Enter`
after setting the browser address bar.

However, the overall Turn 1 attempt count did not drop because a new first
attempt used an invalid `activate_window` argument shape:

```json
{"titleContains":"Microsoft Edge"}
```

That failed quickly at the tool layer, then the next attempt activated the
known Edge window by handle. Net effect:

- saved one URL-submit decision attempt,
- lost one attempt to invalid window activation,
- total LLM responses stayed at `12`,
- scenario still passed.

The next skill-level improvement should be in the launch/startup skill:
when compact window inventory provides a concrete browser window handle, prefer
`activate_window` with `windowHandle` over fuzzy title arguments.
