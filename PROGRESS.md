# Heronwin Progress And Status

Last updated: 2026-04-18

This is the top-level progress document for the repository. Keep it current when
the project shape, verification status, or development workflow changes.

## Current Snapshot

- Branch baseline: `main` at `b668009`, tracking `origin/main`.
- Baseline working tree: clean before this document was added.
- Recent work has focused on UI element context handling, environment variable
  expansion in scripted commands, FaceBridge tracing, process cleanup for the
  brain and face runtimes, audio recording behavior, and face UI animation.
- No `TODO`, `FIXME`, `HACK`, or `NotImplemented` markers were found in the
  active source scan across `src`, `README.md`, and `buildandrun.ps1`.

## Verification Snapshot

Verified locally on 2026-04-18:

- `dotnet build src\heronwin.sln`
  - Passed with 0 warnings and 0 errors.
  - Built `eyesandhands`, `Brain`, `Face`, and both test projects.
- `dotnet test src\heronwin.sln`
  - Passed.
  - `HeronWin.HerBody.EyesAndHands.Tests`: 72 passed.
  - `HeronWin.Brain.Tests`: 194 passed.
  - Total: 266 passed, 0 failed, 0 skipped.
- `npm run build` from `src\herbody\process-manager`
  - Passed TypeScript compilation.
  - There is no package test script for `process-manager` yet.

Local tool versions used for this snapshot:

- .NET SDK: `10.0.201`
- Node.js: `v24.14.1`
- npm: `11.11.0`

## Product Shape

`heronwin` is a Windows-local AI agent system with three main runtime pieces:

- `brain`: .NET 10 agent runtime for voice input, scripted commands, LLM calls,
  MCP client integration, debug traces, and scenario execution.
- `face`: WPF companion UI for live state, recent activity, tray controls,
  settings, and named-pipe status updates from `brain`.
- `herbody`: local MCP servers that give the agent machine capabilities.
  Current servers are `eyesandhands` for Windows UI inspection/action and
  `process-manager` for process lifecycle control.

The active repository is the .NET/TypeScript implementation under `src`.
`obsolete/herface-nodejs` is retained as historical/reference code and is not
part of the current run path.

## Repository Map

- `src/herhead/brain`: .NET agent runtime.
- `src/herhead/brain.tests`: xUnit tests for agent runtime behavior.
- `src/herhead/face`: WPF desktop companion app.
- `src/herbody/eyesandhands`: C# MCP server for UI Automation, screenshots,
  menus, taskbar actions, window focus, and typed/key input.
- `src/herbody/eyesandhands.tests`: xUnit tests for UI tooling behavior.
- `src/herbody/process-manager`: TypeScript MCP server for starting, listing,
  and stopping processes.
- `src/scenarios`: YAML scenario files for scripted agent runs.
- `.github/agents`: core prompt and grouped skill files used by the agent.
- `buildandrun.ps1`: local launcher that can build and run brain and face
  together, or run either side alone.

## Operating Process

The default development rule is skill first, code last.

- Use core prompt and skill updates for scenario strategy, action ordering,
  surface-specific playbooks, success criteria, and reporting guidance.
- Use runtime code for deterministic guardrails, recovery, parsing tool output,
  state tracking, evidence refresh, retry behavior, and repeated failure modes
  that cannot be held reliably through prompt guidance alone.
- When runtime code changes, add focused tests for the new invariant or recovery
  behavior.
- Keep skill changes small and targeted to the relevant surface. Current skill
  groups are `windows`, `any-app`, `edge`, `generic-app`, and `netflix`.

Before changing behavior, decide whether the problem is mainly a playbook issue
or a reliability issue. Start with skills for playbook gaps. Promote to code
when the same failure repeats and the fix can be framed as a general runtime
improvement.

## Local Runbook

Build and test the .NET solution:

```powershell
dotnet build src\heronwin.sln
dotnet test src\heronwin.sln
```

Build the TypeScript MCP server:

```powershell
cd src\herbody\process-manager
npm install
npm run build
```

Run brain directly:

```powershell
dotnet run --project src\herhead\brain
```

Run face directly:

```powershell
dotnet run --project src\herhead\face
```

Run both through the repo launcher:

```powershell
.\buildandrun.ps1
```

Useful launcher variants:

```powershell
.\buildandrun.ps1 -BrainOnly
.\buildandrun.ps1 -FaceOnly
.\buildandrun.ps1 -NoBuild
.\buildandrun.ps1 -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

Run scripted brain commands without voice input:

```powershell
dotnet run --project src\herhead\brain -- --command "open netflix"
dotnet run --project src\herhead\brain -- --scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

## Current Scenario Coverage

The tracked scenario is `src/scenarios/netflix-boyfriend-on-demand.yml`.

It covers:

- opening Netflix,
- selecting the `Min` profile when needed,
- entering `NETFLIX_PROFILE_PIN` one digit at a time when needed,
- searching Netflix for `Boyfriend on Demand`,
- opening the visible result,
- playing the first episode,
- rejecting final outcomes that mention `Pursuit of Jade` or `Anaconda`.

Scenario runs use the normal brain agent and MCP tool flow. Scripted mode skips
microphone capture and voice playback, enables debug JSONL traces automatically,
and treats tool errors, contradictions, or unresolved final outcomes as failures
unless the scenario allows them.

## Known Gaps And Next Work

Priority key:

- `P0`: keep as an active guardrail.
- `P1`: do next; improves confidence or agent reliability.
- `P2`: do soon; useful, but not currently blocking the main path.
- `P3`: backlog; revisit only if roadmap or usage changes.

| Priority | Item | Why | Suggested next step |
|----------|------|-----|---------------------|
| `P0` | Keep the runtime/debug workflow centered on tests, normal app execution, and JSONL traces instead of ad hoc PowerShell reflection against built assemblies. | This prevents avoidable Windows Defender friction and keeps debugging repeatable. | Preserve this as a team rule and call it out during debugging work. |
| `P1` | Add dedicated coverage for the WPF `face` app. | `face` is user-visible and handles settings, tray behavior, named-pipe reconnects, and live state; it currently only has build confidence. | Start with focused tests around settings file edits, status message mapping, and view model state transitions before considering heavier UI automation. |
| `P1` | Broaden the prompt/skill intent vocabulary. | Skill activation is central to agent reliability, and richer intent detection should reduce skill-name-specific runtime logic. | Add a small set of new generic intents, cover them with prompt-loader or skill-activation tests, then tune the relevant skill metadata. |
| `P2` | Add tests for `process-manager`. | The server builds, but process start/stop/list behavior has no automated regression coverage. | Add an `npm test` script with unit tests for command validation and process-list parsing before adding integration tests that spawn real processes. |
| `P3` | Browser-backed ChatGPT mode remains out of scope for the .NET brain. | It is explicitly excluded from the current implementation and is not blocking the local voice/MCP agent path. | Revisit only if browser-backed ChatGPT becomes a product requirement; keep current effort on API-backed LLMs and local tooling. |

## Update Checklist

When refreshing this document:

1. Record the current date, branch, and commit.
2. Note whether the working tree was clean before the doc update.
3. Run `dotnet build src\heronwin.sln`.
4. Run `dotnet test src\heronwin.sln`.
5. Run `npm run build` in `src\herbody\process-manager`.
6. Update scenario coverage and known gaps when behavior or tests change.
