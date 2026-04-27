# Heronwin Docs

This folder holds project-level documentation for `heronwin`.

Keep component-specific docs next to their code under `src`, and keep
runtime-loaded prompt and skill files under `.github/agents`.

## Start Here

- [Get Started](./GET_STARTED.md)
  - [Script Mode](./get-started-script-mode.md)
  - [Voice Mode](./get-started-voice-mode.md)
- [Goal and Design](./GOAL_AND_DESIGN.md)
- [History and Todos](./HISTORY_AND_TODOS.md)
- [Development Guardrails](./DEVELOPMENT_GUARDRAILS.md)
- [Bug Docs](./bugs/README.md)

## Current Snapshot

Last updated: 2026-04-26

- Git baseline: `main` at `bdb240c`.
- Latest session summary, 2026-04-26:
  - See [Feature Change Summary - 2026-04-26](./daily-work-summary-2026-04-26.md)
    for today's Node.js removal, Netflix skill changes, UI settle delay work,
    verification, and next steps.
- Previous session wrap-up, 2026-04-25:
  - implemented scripted no-op lookahead and trace-report lookahead summaries;
    the best Codex-backed Netflix rerun after browser skill URL batching passed
    in `242.735 s` with `12` LLM responses and `206.469 s` total LLM time.
  - fixed trace-report slow-event alignment so `assistant.reply.elapsedMs`
    is not mixed into event-duration tables.
  - strengthened the Edge browser skill to batch address-bar URL replacement
    and `Enter` submission in one tool-call response.
  - fixed a `JsonDocument` lifetime bug in generic named-target rewriting that
    was exposed by the OpenAI API path.
  - measured `OpenAI API / gpt-5.4`: passed current scenario checks in
    `98.172 s`, with `16` LLM responses and `56.160 s` total LLM time. Caveat:
    the final Turn 5 reply said playback was not confirmed, so scenario
    validation needs tightening before treating it as behaviorally equivalent.
  - tried `OpenAI API / gpt-5.4-mini`: no clean pass yet. The startup-skill fix
    made mini use concrete `windowHandle` activation, but the later attempts
    failed on OpenAI API `429` TPM limits, including one run that reached Turn 5.
  - latest focused/full verification: `dotnet test src\head\brain.tests\HeronWin.Brain.Tests.csproj`
    passed on 2026-04-25 with `267` total tests.
  - current measurement notes:
    [OpenAI API GPT-5.4](./perfbase/2026-04-25-netflix-openai-api-gpt-5.4.md)
    and
    [OpenAI API GPT-5.4-Mini Attempts](./perfbase/2026-04-25-netflix-openai-api-gpt-5.4-mini-attempts.md).
- The active implementation lives under `src`.
- The `body` refactor is landed:
  - `src/body` is the active tree.
  - `cognition` and `execution` are in the solution and referenced by `brain`.
  - prompts, skills, tests, and repo docs have been retargeted to the new
    server and tool names.
  - the local untracked `brain/.env` MCP server wiring now points at
    `cognition` and `execution`; process listing/start/stop tools are built
    into `brain`.
  - the empty historical `src\herbody` directory is gone.
- There is no active JavaScript runtime or package under `src`.
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

## Run And Verify

Build and test the .NET solution:

```powershell
dotnet build src\heronwin.sln
dotnet test src\heronwin.sln
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
- [desktop-automation README](../src/body/desktop-automation/README.md)
- [cognition README](../src/body/cognition/README.md)
- [execution README](../src/body/execution/README.md)
- [agent and skills README](../.github/agents/README.md)
- [skill-vs-code policy](../.github/agents/skill-vs-code-policy.md)
