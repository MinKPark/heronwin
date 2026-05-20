# How Agents And Skills Work

HeronWin builds the assistant system prompt at runtime from an agent profile plus selected skills. The profile gives the assistant its workflow role and shared operating contract. Skills add focused guidance for the current request without making every turn carry every app playbook.

The goal is local, explicit, testable Windows automation: observe UI state, choose actions through local tools, record traces, and replay or validate workflows from YAML when repeatability matters.

## Assistant Roles

All assistants use the same local inspection and execution foundation, but they optimize for different workflows:

| Assistant | Role | Use It For |
| --- | --- | --- |
| `cursor` | Interactive control assistant | Live text or voice sessions that inspect and control Windows apps or browsers. |
| `tars` | Scenario automation assistant | Repeatable YAML workflows, smoke tests, app playbooks, regression checks, and log-based assertions. |
| `ava` | Accessibility validation assistant | Scenario-backed validation runs that collect UI evidence and write Markdown/JSON accessibility reports. |

This role split is important for prompt and skill placement. A Netflix playback rule belongs in shared app skills because all assistants can use it. A voice reset rule belongs under `cursor`. A scenario assertion rule belongs under `tars`. Accessibility validation policy belongs under `ava`.

## Main Pieces

`src/agents/shared/heronwin.core.md` is the shared core prompt. It owns cross-assistant response contracts, evidence rules, tool policy, and desktop automation guardrails.

`src/agents/<assistant>/<assistant>.agent.md` is the assistant's fallback definition. It gives the assistant identity and mode framing. Current assistant profiles are `cursor`, `tars`, and `ava`.

`src/agents/<assistant>/<assistant>.agent.core.md` is the assistant-specific core prompt. It layers mode-specific policy on top of the shared core prompt.

`src/agents/shared/skills/**/*.skill.md` contains shared Windows, browser, generic app, and app/site skills.

`src/agents/<assistant>/skills/**/*.skill.md` contains skills that only make sense for one assistant role, such as `tars` scenario assertions, `cursor` voice/reset behavior, or `ava` validation guidance.

`src/agents/her.agent.md` and `src/agents/her.agent.core.md` are compatibility fallbacks for older launches and explicit overrides.

## Default Loading

`AgentPromptLoader.Load(assistantId)` resolves files from the current working directory.

For the default profile path, it looks for:

1. `src/agents/<assistant>/<assistant>.agent.md`
2. `src/agents/her.agent.md`
3. `her.agent.md`

For split prompt profiles, it loads existing core prompts in this order:

1. `src/agents/shared/heronwin.core.md`
2. `src/agents/<assistant>/<assistant>.agent.core.md`

For skills, it loads existing skill directories in this order:

1. `src/agents/shared/skills`
2. `src/agents/<assistant>/skills`
3. `src/agents/skills`

The last path is a compatibility location. New shared app skills should use `src/agents/shared/skills`.

## Overrides

An assistant-specific environment variable wins first:

- `AVA_AGENT_DEFINITION_PATH`
- `TARS_AGENT_DEFINITION_PATH`
- `CURSOR_AGENT_DEFINITION_PATH`

If no assistant-specific override is set, `AGENT_DEFINITION_PATH` is used as a shared compatibility override.

When an override is set, the loader reads that fallback definition and then looks for a compatible core prompt beside it, at repo root, or at `src/agents/her.agent.core.md`.

## Prompt Composition

When split prompts are available, `AgentPromptComposer` builds the system prompt in this shape:

1. Shared core prompt.
2. Assistant core prompt.
3. Active shared skills.
4. Active assistant-specific skills.

If no split core prompt is available, the composer falls back to the single fallback definition.

Each active skill is grouped by its metadata `group`, ordered by `priority`, and rendered as a dedicated prompt section. Inactive skills are not included in that turn's system prompt.

## Skill Activation

Every `.skill.md` file can include YAML frontmatter. New skills should use structured activation metadata:

```yaml
---
id: spotify-search
group: spotify
priority: 410
activation:
  when_any_keywords:
    - spotify
  when_any_intents:
    - search_or_enumeration_request
---
```

Activation text includes the current user request plus recent user messages, summaries, and assistant `say`/`log` text. Keywords are normalized, so plain phrases are enough.

Supported request intents include:

- `launch_request`
- `browser_request`
- `instruction_lookup_request`
- `direct_browser_navigation_request`
- `search_or_enumeration_request`
- `action_request`

Tool names in activation can include or omit MCP prefixes. For example, `cognition/describe_window` and `describe_window` both match `describe_window`.

## Generated Skills

When the user asks to open an unknown app, the runtime may offer to generate a dedicated skill group first. If the user approves, the assistant should use official vendor documentation and return a `skill_generation` JSON payload.

The runtime persists generated app skills under `src/agents/shared/skills/<group>/`, reloads the prompt catalog, then continues the app launch using the new skills.

Generated skills are drafts. Review them before relying on them for repeatable workflows.

## Where Behavior Belongs

Use `src/agents/shared/heronwin.core.md` for cross-assistant rules that should apply to every turn.

Use shared skills for app, website, Windows, browser, or generic app guidance that only matters in certain contexts.

Use assistant-specific core prompts or skills for behavior tied to one host role, such as `cursor` live conversation, `tars` deterministic scenario runs, or `ava` accessibility validation.

Use runtime code when the change is deterministic recovery, tool-output interpretation, safety enforcement, evidence refresh, or a reusable invariant. See [Skill Versus Code Policy](../../src/agents/skill-vs-code-policy.md).
