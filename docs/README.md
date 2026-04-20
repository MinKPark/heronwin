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

Last updated: 2026-04-19

- Git baseline: `main` at `5dd4a28`, tracking `origin/main`.
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
- Latest verified work in the current refactor pass:
  - `dotnet build src\heronwin.sln` passed with 0 warnings and 0 errors.
  - `dotnet test src\heronwin.sln` passed with 295 total tests.
  - `dotnet test src\head\brain.tests\HeronWin.Brain.Tests.csproj` passed
    with 245 total tests after adding browser-request guardrails, app-first
    website-fallback confirmation, screenshot-gating checks, generic runtime
    continuation coverage, Netflix profile-selection and PIN coverage, and the
    reply-outcome / extra-evidence regression checks for the PIN-prompt retry
    bug.
  - `npm run build` passed in `src\body\process-manager`.
  - `.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml`
    passed end to end after the stale-PIN-continuation and PIN-prompt
    contradiction fixes, but the live smoke still takes roughly seven minutes
    and needs aggressive runtime-performance cleanup.
  - ordinary app launch requests now stay app-first, and the brain asks before
    falling back to a website when a likely web-backed app launch remains
    unconfirmed.
  - post-action screenshot capture now stays behind the refreshed UIAutomation
    tree instead of running by default when the tree already changed.
  - the build break from the previous session turned out to be a repo-local ACL
    issue on generated `obj` and `bin` output folders, not low disk space.
- Current follow-up:
  - cut the live Netflix smoke from roughly seven minutes to under one minute,
    ideally much faster, by removing avoidable repair, retry, evidence, and
    tool-call latency,
  - add a separate scripted smoke if we want explicit app-first launch coverage;
    the current Netflix smoke is now an explicit website-navigation scenario.
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
