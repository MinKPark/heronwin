# Goal And Design

## Goal

`heronwin` is a Windows-local AI agent project. The goal is to make the agent
usable through a voice/text-driven desktop experience while keeping its machine
capabilities local, explicit, and testable.

In practical terms, the project is aiming for:

- a usable Windows companion experience instead of a console-only agent,
- local MCP tool servers for concrete machine actions,
- a runtime that can be exercised both through voice and through scripted
  scenario runs,
- behavior that can be tuned through prompts and skills before adding more
  runtime complexity.

## Current Design

The current design has three main runtime pieces plus supporting assets.

### `brain`

`brain` is the .NET 10 agent runtime under `src/head/brain`.

It handles:

- microphone and voice-oriented flow,
- scripted commands and YAML scenario execution,
- LLM integration,
- stdio MCP client integration,
- trace and artifact generation,
- prompt composition and skill activation.

### `face`

`face` is the WPF companion app under `src/head/face`.

It is responsible for:

- always-on-top desktop presence,
- live agent state display,
- recent activity display,
- settings editing for the selected local `brain` `.env`,
- named-pipe connectivity to `brain`.

### `body`

`body` holds local MCP servers and shared desktop automation code under `src/body`.

Current components:

- `cognition`: stateless Windows UI inspection.
- `execution`: stateless Windows UI interaction.
- `desktop-automation`: shared Windows automation library used by `cognition`
  and `execution`.
- `process-manager`: process listing, start, and stop operations.

### Prompt And Skill Layer

The agent behavior layer lives under `.github/agents`.

That layer provides:

- a core agent prompt,
- grouped skills such as `windows`, `any-app`, `edge`, `generic-app`, and
  `netflix`,
- the main skill-versus-code policy used to decide whether a behavior fix
  belongs in prompts/skills or in runtime code.

## Runtime Shape

The normal runtime flow is:

1. `brain` receives a voice turn or scripted command.
2. `brain` composes the active prompt and skill set.
3. `brain` calls local MCP servers such as `cognition`, `execution`, or
   `process-manager`.
4. `brain` records traces and artifacts for verification and debugging.
5. `face` receives status updates from `brain` over a named pipe and reflects
   them in the desktop UI.

## Verification Shape

The current design intentionally supports more than one verification path:

- xUnit coverage for deterministic runtime behavior,
- scripted scenario files under `src/scenarios`,
- normal app execution for end-to-end checks,
- JSONL traces and debug artifacts for post-run evidence.

## Current Scope

The active repository implementation is the .NET and TypeScript code under
`src`.

Current areas of emphasis:

- Windows desktop automation through `cognition` and `execution`,
- reliable tool-use behavior through skills plus runtime guardrails,
- scenario-driven development for flows such as Netflix playback,
- face/brain coordination through local process and pipe orchestration.

## Current Non-Goals

These are currently outside the main path:

- browser-backed ChatGPT mode in the .NET `brain`,
- reintroducing a separate obsolete Node.js agent runtime,
- moving runtime-consumed prompt files out of `.github/agents`.
