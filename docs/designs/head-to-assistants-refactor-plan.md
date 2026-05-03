# Head To Assistants Refactor Plan

Last updated: 2026-05-03
Status: draft for review

## Summary

This plan covers the refactor of the current `src/head` tree into an
`src/assistants` tree and the split of the current `brain` executable into
assistant-oriented projects:

- `brain`: shared code used by assistant hosts
- `tars`: scenario-based assistant for running user scenarios
- `cursor`: voice/text interactive assistant

The goal is to make each assistant own its control loop and context policy,
while keeping provider, tool, prompt, and configuration plumbing shared.

## Opinion On The Latest Direction

I like the move to keep conversation runners and context management inside each
assistant. Those two areas are likely to diverge quickly:

- `tars` will care about deterministic scenario phases, assertions, trace
  evidence, and reproducibility.
- `cursor` will care about interactive continuity, interruption, voice/text
  ergonomics, and live user feedback.

Duplicating runner/context code at the assistant layer is acceptable here. It
keeps the assistant personalities and execution policies explicit, and we can
extract a shared abstraction later only after the duplication shows a stable
shape.

Dropping ad-hoc `--command` support is also a good simplification. If scenario
files are the contract, even a one-step run can be represented as a one-command
scenario. That gives every scripted run the same trace and assertion model.

## Current State

The current `src/head` tree contains:

```text
src/head/
  brain/        .NET 10 executable with shared runtime, scripted mode,
                text mode, voice mode, LLM clients, MCP client, process tools,
                debug tracing, and scenario testing helpers
  brain.tests/  xUnit tests for the current brain assembly
```

Important current couplings:

- `Brain.csproj` is an executable and owns both scripted and interactive entry
  points through `Program.cs`.
- Tests access internal runtime types through
  `InternalsVisibleTo("HeronWin.Brain.Tests")`.
- `buildandrun.ps1`, README files, get-started docs, and historical design docs
  reference `src/head/brain`.
- `DotEnvLoader` searches several legacy brain paths, including
  `src/head/brain/.env`.
- `Brain.csproj` references the `body/cognition` and `body/execution` projects
  with `ReferenceOutputAssembly="false"` so the MCP server binaries are built
  with the runtime.

## Desired Structure

```text
src/assistants/
  brain/
    Brain.csproj
    README.md

  tars/
    Tars.csproj
    Program.cs
    .env.example
    README.md

  cursor/
    Cursor.csproj
    Program.cs
    .env.example
    README.md

  brain.tests/
    HeronWin.Brain.Tests.csproj

  tars.tests/
    HeronWin.Tars.Tests.csproj

  cursor.tests/
    HeronWin.Cursor.Tests.csproj
```

The top-level solution folder should be renamed from `head` to `assistants`.
The physical folder should move from `src/head` to `src/assistants`.

## Component Diagram

![Assistants component diagram](./head-to-assistants-refactor-plan-components.svg)

## Project Roles

### brain

`brain` becomes a shared library rather than a launched assistant. It should
hold the runtime pieces both `tars` and `cursor` need:

- agent prompt loading and composition
- shared prompt, message, reply, tool-call, and trace models
- LLM provider catalog and client implementations
- MCP client manager and built-in process tools
- app skill generation primitives
- debug tracing and artifact cleanup
- shared config parsing and `.env` loading infrastructure
- desktop session primitives that assistants can use or wrap
- low-level YAML parsing utilities if they are needed by shared prompt/config
  code

`brain` should not own assistant-specific conversation runners or context
management after the split. Keep the namespace as `HeronWin.Brain` during the
structural pass to reduce churn. A later cleanup can rename namespaces to
`HeronWin.Assistants.Brain` if we want the source names to fully match the new
folder names.

### tars

`tars` owns scenario execution. It supports only scenario files:

- `--scenario`

It should not support:

- `--command`
- repeated `--command`
- `--commands-file`

One-step scripted work should be represented as a one-command scenario. This
keeps all non-interactive runs on the same scenario, trace, and assertion
contract.

`tars` owns:

- scenario CLI parsing and help text
- scenario loading and validation
- scenario turn loop
- scenario-specific conversation runner
- scenario-specific context manager
- log-based assertions and scenario result reporting
- scripted lookahead policy if it remains scenario-only

Recommended first command shape:

```powershell
dotnet run --project src/assistants/tars -- --scenario src/scenarios/netflix-boyfriend-on-demand.yml
```

### cursor

`cursor` owns interactive voice/text assistance:

- provider-selected default interactive mode
- text input loop
- voice input loop
- wake word handling
- transcription and speech playback
- `/reset`, `/exit`, `/mode:voice`, and `/mode:text`
- interactive conversation runner
- interactive context manager

Recommended first command shape:

```powershell
dotnet run --project src/assistants/cursor
```

## Proposed File Ownership

Initial split:

| Current file group | New owner |
| --- | --- |
| `Program.cs` scripted branch | `tars/Program.cs` |
| `Program.cs` text/voice loop | `cursor/Program.cs` |
| `ConsoleMode.cs` | `tars`, renamed around scenario-only CLI |
| `ScenarioTesting.cs` | `tars` |
| `ScriptedLookahead.cs` | `tars` unless cursor needs the same policy later |
| `Audio.cs`, `SpeechGate.cs`, `VoiceLanguagePreferences.cs` | `cursor` |
| `InteractiveModeCommands.cs` | `cursor` |
| current retired UI project | delete from the repo |
| current named-pipe status bridge | delete unless a replacement status sink is added later |
| `Conversation.cs` runner logic | duplicate into `tars` and `cursor`, then trim per assistant |
| `ContextManager` logic | duplicate into `tars` and `cursor`, then trim per assistant |
| shared conversation models from `Conversation.cs` | `brain` |
| `TurnProcessor.cs` orchestration | duplicate into `tars` and `cursor`, then trim per assistant |
| `AppConfig.cs`, `Llm*`, `OpenAiCodexCliClient.cs` | `brain` |
| `McpClientManager.cs`, `BuiltInProcessTools.cs` | `brain` |
| `AgentPrompts.cs`, `AppSkillGeneration.cs`, shared `YamlConfiguration.cs` pieces | `brain` |
| `DebugTrace.cs`, `ArtifactCleanup.cs`, `HttpClientFactory.cs` | `brain` |
| `DisplayTopology.cs`, `DesktopSessionContext.cs` | `brain` |

The key rule: assistant policy stays in assistant projects, shared mechanics
stay in `brain`.

## Environment File Plan

Use assistant-specific `.env.example` files:

- `src/assistants/tars/.env.example`
- `src/assistants/cursor/.env.example`

`tars` and `cursor` should each call the shared `.env` loader with an assistant
kind or default project path. Search order should be:

1. current directory `.env`
2. the launched assistant folder `.env`
3. `src/assistants/.env`
4. legacy `src/head/brain/.env` fallback for migration

`BRAIN_ENV_DIR` should either be renamed to a neutral name such as
`HERONWIN_ENV_DIR` or kept temporarily with a compatibility alias. If renamed,
the loader should set both variables during the migration so relative MCP paths
continue to resolve.

## Solution And Build Updates

Update `src/heronwin.sln`:

- rename solution folder `head` to `assistants`
- remove the current retired UI project from the solution and delete its
  project directory from the repo
- point moved `HeronWin.Brain.Tests` project at `assistants\brain.tests`
- change `Brain.csproj` to a library
- add `Tars.csproj` and `Cursor.csproj`
- add `HeronWin.Tars.Tests.csproj` and `HeronWin.Cursor.Tests.csproj` if tests
  are split during the same pass

Build references:

- `tars` references `brain`
- `cursor` references `brain`
- `tars` and `cursor` keep build-order-only references to `body/cognition` and
  `body/execution` if their runs need those binaries built

## Launcher Updates

Update `buildandrun.ps1` around the new assistant names:

- default launch: run `cursor`
- `-CursorOnly`: run only the interactive assistant
- `-TarsOnly`: run only the scenario assistant
- `-Scenario`: route to `tars`
- `-TarsArgs`: pass extra args to `tars`
- `-CursorArgs`: pass extra args to `cursor`

Remove old UI launch behavior entirely. Keep `-BrainOnly` and `-BrainArgs` as
temporary compatibility aliases with a warning only if compatibility matters
for existing local workflows. If kept, `-BrainOnly -Scenario` should route to
`tars`; plain `-BrainOnly` should route to `cursor`.

## Documentation Updates

Documentation updates are part of the refactor, not a follow-up. The code
change is not complete until the current user-facing docs describe
`src/assistants`, `tars`, `cursor`, and the deleted retired UI project.

### Live Docs To Update

Must update:

- `README.md`
- `docs/GET_STARTED.md`
- `docs/get-started-script-mode.md`
- `docs/get-started-voice-mode.md`
- `docs/get-started-openaiconfig.md`
- `docs/GOAL_AND_DESIGN.md`
- `docs/README.md`
- `src/body/README.md`
- `src/assistants/brain/README.md`
- `src/assistants/tars/README.md`
- `src/assistants/cursor/README.md`

Expected content changes:

- replace active `src/head/...` paths with `src/assistants/...`
- replace script-mode commands from `brain --command` / `brain
  --commands-file` to `tars --scenario`
- document that one-step scripted work should be represented as a one-step
  scenario file
- replace interactive launch docs with `cursor`
- remove retired UI startup/settings instructions
- document `brain` as a shared library, not a runnable assistant
- update launcher examples from `-BrainOnly` to `-CursorOnly` and `-TarsOnly`
- update `.env` examples and path guidance for `tars`, `cursor`, and optional
  shared `src/assistants/.env`

### New Or Moved READMEs

Create or update:

- `src/assistants/brain/README.md`: shared library responsibilities and
  non-goals
- `src/assistants/tars/README.md`: scenario file contract, CLI, trace output,
  and assertion behavior
- `src/assistants/cursor/README.md`: interactive voice/text flow, provider
  defaults, and local commands

Remove or move:

- `src/head/brain/README.md` after the folder move
- the current retired UI project README, because that project is deleted from
  the repo

### Historical Docs Policy

Historical docs under `docs/designs`, `docs/bugs`, `docs/perfbase`, and daily
summaries can keep old paths where they describe old runs. Add a short note in
the docs index explaining that historical docs may reference `src/head/brain`
from before the refactor.

Historical docs should only be edited when they are linked from live setup
instructions or when their current wording claims to describe the active
architecture.

### Documentation Verification

After implementation, run stale-reference searches and review each hit:

```powershell
rg -n "src[/\\]head|head[/\\]brain|dotnet run --project src[/\\]head|BrainOnly|--command|--commands-file|brain \.env|retired UI|settings window" README.md docs src/body src/assistants buildandrun.ps1
```

Expected result:

- no stale references in live setup docs, READMEs, launcher docs, or active
  source comments
- allowed stale references only in historical docs that explicitly describe
  pre-refactor behavior
- any allowed stale reference should be clear from nearby context

## Test Plan

After each phase:

```powershell
dotnet build src/heronwin.sln
dotnet test src/heronwin.sln
```

Focused checks after the split:

```powershell
dotnet test src/assistants/brain.tests/HeronWin.Brain.Tests.csproj
dotnet test src/assistants/tars.tests/HeronWin.Tars.Tests.csproj
dotnet test src/assistants/cursor.tests/HeronWin.Cursor.Tests.csproj
dotnet run --project src/assistants/tars -- --help
dotnet run --project src/assistants/cursor -- --help
```

Behavior smoke checks:

```powershell
dotnet run --project src/assistants/tars -- --scenario src/scenarios/netflix-boyfriend-on-demand.yml
.\buildandrun.ps1 -CursorOnly
.\buildandrun.ps1 -TarsOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

## Migration Phases

### Phase 1: Rename The Container And Delete Retired UI Project

- Move `src/head` to `src/assistants`.
- Delete the current retired UI project directory from the repo.
- Remove that project from the solution.
- Update solution paths and solution folder names.
- Update `buildandrun.ps1` paths without splitting behavior yet.
- Update `.env` discovery to include `src/assistants/brain/.env` and keep
  legacy `src/head/brain/.env` fallback.
- Update live docs from `src/head` to `src/assistants`.
- Verify build and tests.

### Phase 2: Create Brain Library And Thin Hosts

- Change `Brain.csproj` from executable to library.
- Add `Tars.csproj` and `Cursor.csproj`.
- Move current scripted/scenario entry flow into `tars/Program.cs`.
- Move current text/voice entry flow into `cursor/Program.cs`.
- Remove `--command` and `--commands-file`; keep only `--scenario` for `tars`.
- Expose the smallest practical public/friend API from `brain` for the hosts.
- Keep namespaces stable unless a compile boundary requires a small rename.
- Verify build, tests, `tars --help`, and `cursor --help`.

### Phase 3: Move Assistant-Specific Runners

- Duplicate the current conversation runner into `tars` and `cursor`.
- Move or duplicate context management into `tars` and `cursor`.
- Move scenario-only logic to `tars`.
- Move voice/text-only logic to `cursor`.
- Keep shared models, providers, tools, prompt loading, debug tracing, and
  config loading in `brain`.
- Split tests by ownership where useful.
- Verify full solution tests.

### Phase 4: Polish Names And Compatibility

- Rename user-facing text from `brain` to `tars`, `cursor`, or shared `brain`
  depending on context.
- Decide whether to keep temporary compatibility aliases for old commands.
- Decide whether to rename namespaces from `HeronWin.Brain` to
  `HeronWin.Assistants.Brain`.
- Remove or deprecate legacy path fallbacks after at least one stable pass.

## Risks And Mitigations

- Duplicating runner/context code can drift. Mitigate by allowing drift where
  it reflects real assistant policy, and keep shared mechanics in `brain`.
- Large `Program.cs` split could hide behavior changes. Mitigate by first
  creating thin host programs and preserving the current control flow.
- Internal type access will break after the executable becomes a library.
  Mitigate with a small public host API or temporary `InternalsVisibleTo` for
  `Tars`, `Cursor`, and split test assemblies.
- `.env` lookup can silently pick the wrong file. Mitigate by logging the env
  file path in session startup and keeping legacy fallback during migration.
- Historical docs contain many old `src/head/brain` references. Mitigate by
  updating live docs only and documenting that historical records keep old
  paths.

## Open Questions

- Is `tars` the intended final name for the scenario assistant, replacing the
  earlier `auto` name?
- Should `brain` remain the namespace `HeronWin.Brain` for now, or should the
  implementation pass also rename it to `HeronWin.Assistants.Brain`?
- Should `tars` and `cursor` each have separate `.env` files, or should they
  share `src/assistants/.env` by default?
- Should trace files continue to be named `brain.debug.*`, or should they move
  to `tars.debug.*` and `cursor.debug.*`?
