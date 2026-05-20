# Agents And Skills

HeronWin agents are prompt profiles for a Windows-local automation system. Skills are smaller Markdown playbooks that the runtime loads only when they apply to the current turn.

Use these docs when changing assistant behavior, adding app-specific guidance, or reviewing generated skill drafts.

## Assistant Roles

The assistants share prompt composition, LLM routing, tracing, and local MCP tools, but each assistant has a distinct job:

- `cursor`: interactive text and voice control of Windows apps and browsers.
- `tars`: repeatable YAML scenario automation with log-based assertions.
- `ava`: scenario-backed accessibility validation with saved UI evidence and Markdown/JSON reports.

Put shared app and browser behavior in shared skills. Put role-specific behavior in the assistant profile or assistant-specific skills.

## Start Here

- [How Agents And Skills Work](./how-agents-and-skills-work.md)
- [Create App Skills](./create-app-skills.md)
- [App Skill Template](./templates/app-surface-and-state.skill.md)

## Source Layout

Runtime-loaded prompts and skills live under `src/agents`:

```text
src/agents/
  shared/
    heronwin.core.md
    skills/
  cursor/
  tars/
  ava/
  her.agent.md
  her.agent.core.md
  skill-vs-code-policy.md
```

Shared app and site skills live under `src/agents/shared/skills/<group>/*.skill.md`.

Assistant-specific skills live under `src/agents/<assistant>/skills` only when the guidance is truly specific to that assistant's role.

## Quick Verification

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~AgentPrompt"
dotnet test src\heronwin.sln
```
