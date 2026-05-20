# Agent Prompts And Skills

HeronWin now uses assistant prompt profiles layered over shared desktop skills.

Maintainer docs live under [docs/agentsandskills](../../docs/agentsandskills/README.md).

## Layout

- `shared/heronwin.core.md`: shared response contract, evidence rules, tool policy, and desktop automation guardrails.
- `shared/skills/**/*.skill.md`: assistant-agnostic app, browser, Windows, and site playbooks.
- `ava/ava.agent.md` and `ava/ava.agent.core.md`: accessibility validation assistant identity and validation policy.
- `ava/skills/**/*.skill.md`: accessibility validation guidance.
- `tars/tars.agent.md` and `tars/tars.agent.core.md`: scenario assistant identity and deterministic run policy.
- `tars/skills/**/*.skill.md`: scenario assertions, reproducibility, and scripted-run guidance.
- `cursor/cursor.agent.md` and `cursor/cursor.agent.core.md`: interactive voice/text assistant identity and live collaboration policy.
- `cursor/skills/**/*.skill.md`: interactive mode, reset, voice, and clarification guidance.
- `her.agent.md` and `her.agent.core.md`: compatibility fallbacks for older launches and explicit overrides.

## Composition

The runtime composes prompts in this order:

1. Shared core prompt.
2. Assistant core prompt.
3. Shared skills selected by activation metadata.
4. Assistant-specific skills selected by activation metadata.
5. Generated app skills, when the runtime persists a new skill group.

Use `AVA_AGENT_DEFINITION_PATH`, `TARS_AGENT_DEFINITION_PATH`, or `CURSOR_AGENT_DEFINITION_PATH` for assistant-specific overrides. `AGENT_DEFINITION_PATH` remains as a shared compatibility fallback.

## Policy

- App and site skills stay under `shared/skills` unless they are truly about scenario execution or interactive voice/text behavior.
- Accessibility validation guidance belongs under `ava/skills`.
- Scenario-only guidance belongs under `tars/skills`.
- Live collaboration, spoken pacing, reset, and mode-switch guidance belongs under `cursor/skills`.
- See `skill-vs-code-policy.md` for when to change prompts or skills instead of runtime code.
