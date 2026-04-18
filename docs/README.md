# Heronwin Docs

This folder holds project-level documentation for `heronwin`.

Keep component-specific docs next to their code under `src`, and keep
runtime-loaded prompt and skill files under `.github/agents`.

## Start Here

- [Goal and Design](./GOAL_AND_DESIGN.md)
- [History and Todos](./HISTORY_AND_TODOS.md)
- [Development Guardrails](./DEVELOPMENT_GUARDRAILS.md)

## Current Snapshot

Last updated: 2026-04-18

- Git baseline: `main` at `c43051a`, tracking `origin/main`.
- The active implementation lives under `src`.
- `obsolete/herface-nodejs` is historical reference code and is not part of the
  current runtime path.
- Latest recorded verification in this docs set:
  - `dotnet build src\heronwin.sln` passed with 0 warnings and 0 errors.
  - `dotnet test src\heronwin.sln` passed with 266 total tests.
  - `npm run build` in `src\body\process-manager` passed.
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
dotnet run --project src\herhead\brain
```

Run `face` directly:

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

## Component Docs

- [brain README](../src/herhead/brain/README.md)
- [face README](../src/herhead/face/README.md)
- [body README](../src/body/README.md)
- [process-manager README](../src/body/process-manager/README.md)
- [desktop-automation README](../src/body/desktop-automation/README.md)
- [cognition README](../src/body/cognition/README.md)
- [execution README](../src/body/execution/README.md)
- [agent and skills README](../.github/agents/README.md)
- [skill-vs-code policy](../.github/agents/skill-vs-code-policy.md)
