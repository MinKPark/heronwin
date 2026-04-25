# Heronwin Docs

This folder holds project-level documentation for `heronwin`.

Keep component-specific docs next to their code under `src`, and keep
runtime-loaded prompt and skill files under `.github/agents`.

## Start Here

- [Goal and Design](./GOAL_AND_DESIGN.md)
- [History and Todos](./HISTORY_AND_TODOS.md)
- [Development Guardrails](./DEVELOPMENT_GUARDRAILS.md)
- [Bug Docs](./bugs/README.md)

## Current Snapshot

Last updated: 2026-04-25

- Git baseline: `main` at `f96fc1a`.
- The active implementation lives under `src`.
- The `body` refactor is landed:
  - `src/body` is the active tree.
  - `cognition` and `execution` are in the solution and referenced by `brain`.
  - prompts, skills, tests, and repo docs have been retargeted to the new
    server and tool names.
  - the local untracked `brain/.env` MCP server wiring now points at `process-manager`,
    `cognition`, and `execution`.
  - the empty historical `src\herbody` directory is gone.
- the removed obsolete Node.js runtime is no longer part of the repository.
- Latest verified work in the current scripted-runtime performance pass:
  - the first scripted turn-start carry-forward slice is now implemented in
    `brain`: conservative scripted-only carry-forward evidence injection,
    desktop-session freshness/provenance metadata, turn-start trace events, and
    `promptTokenEstimate` on `llm.request`.
  - `dotnet test src\head\brain.tests\HeronWin.Brain.Tests.csproj` passed on
    2026-04-25 with `252` total tests after the carry-forward slice and trace
    updates.
  - `.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml`
    passed on 2026-04-25 in `776.349 s`.
    Raw artifacts are under
    `.tmp/netflix-smoke-runtime/2026-04-25-carry-forward-slice/`, and the
    tracked summary is
    [docs/perfbase/2026-04-25-netflix-smoke-carry-forward-slice.md](./perfbase/2026-04-25-netflix-smoke-carry-forward-slice.md).
  - the fresh rerun shows `agent.turn.ready_state_used` and
    `agent.turn.carry_forward_evidence_used` on turns `2` through `5`, and the
    old turn-start `list_windows` then `describe_window` discovery pair is gone
    from turns `2` through `5`.
  - `.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml`
    passed on 2026-04-22 and established the fresh baseline at `882.255 s`.
    Raw artifacts are under `.tmp/netflix-smoke-runtime/2026-04-22-baseline/`
    and the tracked summary is
    [docs/perfbase/2026-04-22-netflix-smoke-baseline.md](./perfbase/2026-04-22-netflix-smoke-baseline.md).
  - `dotnet test src\head\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~TraceReportTests|FullyQualifiedName~ScriptedModeTests"`
    passed after the trace-report implementation work.
  - `brain.exe --trace-report <path>` now provides a repo-native Markdown
    summary for saved JSONL traces; the first saved report is
    `.tmp/netflix-smoke-runtime/2026-04-22-baseline/trace-report.md`.
- Earlier broad repo-wide verification still comes from the 2026-04-19
  snapshot:
  - `dotnet build src\heronwin.sln` passed with 0 warnings and 0 errors.
  - `dotnet test src\heronwin.sln` passed with 295 total tests.
  - `npm run build` passed in `src\body\process-manager`.
- Current follow-up:
  - keep the first scripted carry-forward slice as the new runtime base,
  - capture a more controlled apples-to-apples rerun that exercises the older
    profile-picker or PIN path before judging the next slice,
  - investigate turn `1` browser-entry churn from the 2026-04-25 rerun, which
    became the new top hotspot even though later scripted turns stopped doing
    redundant discovery,
  - add a separate scripted smoke if we want explicit app-first launch coverage;
    the current Netflix smoke is now an explicit website-navigation scenario.
- Working-tree status in the current session:
  - first-slice carry-forward code, focused tests, and updated perf/handoff
    docs are uncommitted local changes.
- Local tool versions used for the snapshot:
  - .NET SDK `10.0.201`
  - Node.js `v24.14.1`
  - npm `11.11.0`

## Run And Verify

Build and test the .NET solution:

```powershell
dotnet build src\heronwin.sln
dotnet test src\heronwin.sln
```

Build the TypeScript MCP server:

```powershell
cd src\body\process-manager
npm install
npm run build
```

Run `brain` directly:

```powershell
dotnet run --project src\head\brain
```

Keep a local `src\head\brain\.env` next to the project and start from
`src\head\brain\.env.example` when you need to recreate it.

Run `face` directly:

```powershell
dotnet run --project src\head\face
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

## Component Docs

- [brain README](../src/head/brain/README.md)
- [face README](../src/head/face/README.md)
- [body README](../src/body/README.md)
- [process-manager README](../src/body/process-manager/README.md)
- [desktop-automation README](../src/body/desktop-automation/README.md)
- [cognition README](../src/body/cognition/README.md)
- [execution README](../src/body/execution/README.md)
- [agent and skills README](../.github/agents/README.md)
- [skill-vs-code policy](../.github/agents/skill-vs-code-policy.md)
