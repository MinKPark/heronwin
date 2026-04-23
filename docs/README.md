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

Last updated: 2026-04-22

- Git baseline: `main` at `be8509f`, tracking `origin/main`.
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
  - `dotnet test src\head\brain.tests\HeronWin.Brain.Tests.csproj` passed
    with 245 total tests after the Netflix/browser/runtime guardrail work from
    that pass.
  - `npm run build` passed in `src\body\process-manager`.
- Current follow-up:
  - implement the first behavior-changing slice from
    [Scripted Cross-Turn Evidence Reuse Plan](./designs/scripted-cross-turn-evidence-reuse-plan.md):
    conservative turn-start ready state, carry-forward evidence reuse, and the
    logging upgrades needed to tell whether discovery cost truly went away,
  - rerun the same Netflix smoke and compare the new trace report against the
    2026-04-22 baseline before taking the next slice,
  - add a separate scripted smoke if we want explicit app-first launch coverage;
    the current Netflix smoke is now an explicit website-navigation scenario.
- Working-tree status at wrap-up: only the doc-only handoff updates in
  `docs/HISTORY_AND_TODOS.md`, `docs/README.md`, and
  `docs/designs/scripted-cross-turn-evidence-reuse-plan.md` are left
  uncommitted.
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
