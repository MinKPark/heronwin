# History And Todos

This file has two jobs:

- record daily project movement from committed repo history,
- track the active todos from the current working conversation.

## How To Update

- Add a new daily entry from `git log` when a new workday lands.
- Keep the history section based on committed repo history.
- Keep uncommitted work in the working-tree notes section until it is committed.
- Keep the todo list aligned with the latest ongoing conversation.

## Current Todos

| Status | Priority | Item | Next move |
|--------|----------|------|-----------|
| `next` | `P0` | Cut scripted Netflix smoke runtime below one minute. | Use the 2026-05-03 `tars` / `OpenAiCodex` pass (`246.797 s`) as the current post-refactor baseline, then rerun OpenAI API `gpt-5.5` and `gpt-5.4-mini` after the API quota/billing blocker is cleared. |
| `next` | `P0` | Make scripted scenario pass/fail stricter for incomplete final outcomes. | A `gpt-5.4` API run passed current checks even though Turn 5 said playback was not confirmed; update assertions/evaluator so incomplete final replies cannot pass just because required title text appears. |
| `next` | `P1` | Decide whether to add separate scripted coverage for app-first launch. | The current Netflix smoke is now explicitly website-navigation-based; add another smoke if we want deterministic coverage for the app-first fallback-confirmation path. |
| `next` | `P1` | Finish the compact-tree rollout in `cognition`. | Add the opt-in screenshot-vs-compact evaluation harness, then run the documented parity checks, benchmarks, and manual evaluation passes in [Cognition Compact Tree Migration](./designs/cognition-compact-tree-migration.md). |
| `next` | `P1` | Split more assistant-specific runner policy out of the shared library. | Move scenario-only runner/context code toward `tars` and interactive-only voice/text policy toward `cursor` as the next cleanup pass. |
| `next` | `P1` | Broaden the prompt and skill intent vocabulary. | Add a small set of generic intents and cover them with activation tests. |
| `done` | `P2` | Broaden automated tests for built-in process tools. | Completed 2026-05-10: added process-list parsing/formatting coverage, start/stop argument validation coverage, and safe start/list/stop integration tests for test-owned processes. |

## Working-Tree Notes

These notes describe local work that exists in the working tree but is not yet
part of committed repo history.

- End-of-day 2026-05-03 update:
  - implemented the structural pass from
    [Head To Assistants Refactor Plan](./designs/head-to-assistants-refactor-plan.md):
    `src/head` moved to `src/assistants`, the retired `face` UI project was
    removed, `brain` became a shared library, and runnable hosts now live in
    `src/assistants/tars` and `src/assistants/cursor`.
  - added `tars.tests` and `cursor.tests`, assistant-specific prompt profiles,
    assistant-specific `.env.example` files, and the shared
    `.github/agents/shared` prompt/skill layout.
  - updated the solution, launcher, live setup docs, component READMEs, and
    prompt docs for the `src/assistants` layout.
  - verified in this session:
    - `dotnet build src\heronwin.sln` passed.
    - `dotnet test src\heronwin.sln` passed.
    - `dotnet run --project src\assistants\tars -- --help` passed.
    - `dotnet run --project src\assistants\cursor -- --help` passed.
    - cursor trace-report smoke against `.tmp\trace-smoke.jsonl` passed.
    - the stale-reference scan for live docs/source passed with only
      historical-doc references intentionally remaining.
  - latest live measurements:
    - `tars` with `OpenAiCodex / codex-default` passed
      `src\scenarios\netflix-boyfriend-on-demand.yml` in `246.797 s` scenario
      elapsed, with `5` turns, `12` LLM responses, and `16.233 s` average LLM
      attempt latency. The trace report is in
      `.tmp\tars-boyfriend-20260503-161820\trace-report.md`.
    - `tars` with `OpenAiApi / gpt-5.4-mini` selected the OpenAI API provider
      correctly but failed on turn 1 with API `429` quota/billing error. Logs
      are under `.tmp\tars-boyfriend-openaiapi-20260503-180911\`.
    - `tars` with `OpenAiApi / gpt-5.5` also selected the OpenAI API provider
      correctly but failed on turn 1 with the same API `429` quota/billing
      error. Logs are under
      `.tmp\tars-boyfriend-openaiapi-gpt55-20260503-183125\`.
  - blocker:
    - OpenAI API speed comparison cannot be measured until the account quota or
      billing issue behind the `429` response is cleared.
  - first steps for the next session:
    - review the wrap-up doc changes and commit them with the assistant
      refactor work if they look right.
    - after OpenAI API access is healthy, rerun
      `LLM_PROVIDER=openai-api` / `OPENAI_MODEL=gpt-5.5`
      `dotnet run --project src\assistants\tars -- --scenario src\scenarios\netflix-boyfriend-on-demand.yml`.
    - continue Phase 3 of the refactor by moving scenario-only runner/context
      policy and tests toward `tars`, while moving interactive voice/text
      policy and tests toward `cursor`.

- End-of-day 2026-04-25 update:
  - implemented scripted lookahead for no-op next turns and added trace-report
    lookahead metrics.
  - fixed trace-report timing alignment so `assistant.reply.elapsedMs` is not
    treated as a normal event duration.
  - moved direct browser URL+Enter batching into the Edge browser skill.
  - fixed a `JsonDocument` lifetime bug in generic named-target rewriting and
    covered it with
    `EvaluateGenericContainerActionToNamedTarget_UsesExactCaseSnapshotTreeAfterParseScope`.
  - strengthened the generic desktop startup skill to use exact
    `windowHandle` activation when available and to continue into the requested
    destination/action after foregrounding an app.
  - verified in this wrap-up pass:
    - `dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj` passed
      with `267` total tests.
  - latest live measurements:
    - Codex-backed `gpt-5.4-mini` browser-skill rerun passed in `242.735 s`.
    - OpenAI API `gpt-5.4` rerun passed current checks in `98.172 s`, but with
      a Turn 5 quality caveat: the final reply said playback was not confirmed.
    - OpenAI API `gpt-5.4-mini` did not produce a clean pass today; after the
      startup-skill fix it reached Turn 5 once, then failed on `429` TPM limits.
  - saved measurement notes in:
    - [Netflix OpenAI API GPT-5.4 Rerun](./perfbase/2026-04-25-netflix-openai-api-gpt-5.4.md)
    - [Netflix OpenAI API GPT-5.4-Mini Attempts](./perfbase/2026-04-25-netflix-openai-api-gpt-5.4-mini-attempts.md)
  - no `Brain` scenario process was left running after the interrupted mini
    rerun; remaining `dotnet` processes appeared to be VS/MSBuild/test
    infrastructure.
  - first steps for the next session:
    - inspect the `gpt-5.4-mini` Turn 4/5 trace under
      `.tmp/netflix-smoke-runtime/2026-04-25-openai-api-gpt-5.4-mini-aborted-clean-rerun/`.
    - tighten scenario failure detection for incomplete final replies and reply
      contradictions.
    - after the OpenAI API TPM window is clear, rerun one clean
      `LLM_PROVIDER=openai-api` / `OPENAI_MODEL=gpt-5.4-mini` scenario without
      preceding failed attempts.
    - compare `gpt-5.4`, `gpt-5.4-mini`, and Codex-backed runs on both latency
      and final-state quality before choosing the default provider/model.

- Current uncommitted local changes now include the first scripted carry-forward
  implementation pass in:
  - `src/assistants/brain/DesktopSessionContext.cs`
  - `src/assistants/brain/Conversation.cs`
  - `src/assistants/brain/DebugTrace.cs`
  - `src/assistants/brain/ScenarioTesting.cs`
  - `src/assistants/brain/TurnProcessor.cs`
  - `src/assistants/brain.tests/AgentRunnerContinuationTests.cs`
  - `src/assistants/brain.tests/TraceReportTests.cs`
  - the updated repo docs in this folder and under `devdocs/perfbase/`
- Local 2026-04-25 progress:
  - landed the first scripted-only turn-start reuse slice from
    [Scripted Cross-Turn Evidence Reuse Plan](./designs/scripted-cross-turn-evidence-reuse-plan.md):
    `DesktopSessionContext` now carries freshness/provenance metadata for UI
    tree and focus evidence, and `Conversation.RunTurnAsync(...)` can inject
    conservative carry-forward current-screen evidence before the first LLM
    request of later scripted turns.
  - added explicit turn-start trace events:
    `agent.turn.ready_state_used`,
    `agent.turn.ready_state_skipped`,
    `agent.turn.carry_forward_evidence_used`, and
    `agent.turn.carry_forward_evidence_skipped`.
  - added `promptTokenEstimate` to `llm.request` and a new
    `Turn-start helper time` bucket in the repo-native trace report.
  - extended focused tests around scripted carry-forward injection, stale-skip
    fallback, and trace-report helper bucketing.
  - verified in this session:
    - `dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj` passed
      with `252` total tests.
    - `\.\buildandrun.ps1 -TarsOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml`
      passed on 2026-04-25 with scenario elapsed `776.349 s`.
  - saved the fresh rerun artifacts under
    `.tmp/netflix-smoke-runtime/2026-04-25-carry-forward-slice/`, with tracked
    summary in
    [devdocs/perfbase/2026-04-25-netflix-smoke-carry-forward-slice.md](./perfbase/2026-04-25-netflix-smoke-carry-forward-slice.md).
  - the fresh rerun clearly used turn-start carry-forward on scripted turns
    `2` through `5`, and turns `2` through `5` no longer opened with the old
    `list_windows` then `describe_window` discovery pair.
  - caveat: the 2026-04-25 rerun landed directly on Netflix home in turn `1`,
    so turns `2` and `3` became valid no-op checks instead of exercising the
    older profile-picker and PIN path. That means the runtime win is real, but
    the turn-by-turn comparison with the 2026-04-22 profile-flow baseline is
    not perfectly apples to apples.
  - new top hotspot exposed by the rerun:
    turn `1` browser entry and site navigation regressed to `457.760 s`, so
    the next P0 slice should target browser-entry stability or a cleaner
    scenario start state.
- First step for the next session:
  - commit this first-slice carry-forward pass and its measurement docs, then
    capture a more controlled rerun that forces the earlier profile-picker or
    PIN path before deciding the next runtime slice.
  - in parallel, inspect turn `1` browser-entry churn from the 2026-04-25
    trace and decide whether the next fix should stabilize direct Netflix tab
    navigation, open a cleaner browser surface first, or tighten the
    address-bar retry path.

## Daily Repo History

Source shape: `git log --date=short --pretty=format:"%ad %h %s"`

- `2026-04-22` (1 commit): added the scripted cross-turn evidence reuse plan,
  the tracked Netflix smoke baseline summary under `devdocs/perfbase`, and the
  repo-native trace-report implementation and focused tests that make later
  before/after runtime comparisons repeatable.
- `2026-04-21` (1 commit): added the scripted Netflix smoke runtime
  performance plan documentation and made the runtime-cut P0 investigation
  explicit in repo docs.
- `2026-04-19` (20 commits): landed the app-agnostic runtime-and-skills
  migration, generic continuations and discrete-slot entry primitives,
  additional debug instrumentation and argument previews, refreshed plan and
  bug docs, fixed the stale PIN continuation and PIN-prompt contradiction
  issues, and updated the active todo list to make runtime performance the top
  priority.
- `2026-04-18` (18 commits): split the docs, added `process-manager`, finished
  the `tools` / `cognition` / `execution` cutover, normalized tool names and
  renamed `src\herhead` to `src\head`, tightened Netflix/browser guardrails
  and test coverage, moved compact-tree compaction into `cognition` with
  rendered artifacts, `llmTree` projections, and omitted-children tracking,
  removed the obsolete JavaScript LLM runtime, added WPF `face` settings/status
  work, and left the compact-tree evaluation rollout explicitly in progress.
- `2026-04-05` (32 commits): added the WPF `face` companion UI, named-pipe
  state flow, build-and-run orchestration, settings and environment handling,
  FaceBridge tracing, audio improvements, scenario handling, logs cleanup,
  app-skill generation, Netflix skill restructuring, and richer follow-up
  evidence handling.
- `2026-04-04` (16 commits): expanded `brain` action rewriting and navigation
  logic, added scripted mode and YAML scenario support, improved post-action
  screenshots and debug handling, and tightened MCP recovery and narration.
- `2026-04-03` (16 commits): strengthened browser navigation rules with address
  bar shortcuts, fullscreen exit, new-tab handling, and clearer reply
  consistency, while also adding the skill-versus-code policy and moving the
  solution to `net10.0-windows`.
- `2026-04-02` (4 commits): introduced prompt composition with fallback,
  initial grouped skill activation, a browser navigation skill, and refreshed
  environment configuration.
- `2026-03-31` (17 commits): improved UI snapshot structure, image
  optimization, invoke-and-keyboard fallback behavior, artifact cleanup, click
  heuristics, and debug logging across the UI automation path.
- `2026-03-30` (15 commits): pushed the agent toward screenshot-first visual
  context, added post-action descriptions and suggestions, introduced Claude
  client support and LLM temperature control, and improved speech and audio
  handling.
- `2026-03-29` (18 commits): expanded `eyesandhands` with taskbar, screenshot,
  context-menu, full-depth tree, and send-input tools; added ChatGPT web
  integration work; improved docs and solution setup; and brought in the early
  AI voice agent plus `process-manager`.
- `2026-03-28` (4 commits): created the repo, added initial planning, fixed
  early voice-input behavior, and landed the first `eyesandhands` Windows UI
  automation tools.
