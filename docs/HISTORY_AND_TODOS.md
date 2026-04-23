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
| `next` | `P0` | Cut scripted Netflix smoke runtime below one minute. | Start with the first scripted-turn state reuse slice from [Scripted Cross-Turn Evidence Reuse Plan](./designs/scripted-cross-turn-evidence-reuse-plan.md): add conservative turn-start ready-state and carry-forward evidence handling, add the planned trace/logging fields, then rerun the same Netflix smoke and compare the new trace report against the 2026-04-22 baseline. |
| `next` | `P1` | Decide whether to add separate scripted coverage for app-first launch. | The current Netflix smoke is now explicitly website-navigation-based; add another smoke if we want deterministic coverage for the app-first fallback-confirmation path. |
| `next` | `P1` | Finish the compact-tree rollout in `cognition`. | Add the opt-in screenshot-vs-compact evaluation harness, then run the documented parity checks, benchmarks, and manual evaluation passes in [Cognition Compact Tree Migration](./designs/cognition-compact-tree-migration.md). |
| `next` | `P1` | Add dedicated coverage for the WPF `face` app. | Start with settings edits, status mapping, and view-model state transitions. |
| `next` | `P1` | Broaden the prompt and skill intent vocabulary. | Add a small set of generic intents and cover them with activation tests. |
| `soon` | `P2` | Add automated tests for `process-manager`. | Start with command validation and process-list parsing, then add integration tests later. |

## Working-Tree Notes

These notes describe local work that exists in the working tree but is not yet
part of committed repo history.

- The only uncommitted local changes at the end of the 2026-04-22 session are
  the wrap-up doc updates in:
  - `docs/HISTORY_AND_TODOS.md`
  - `docs/README.md`
  - `docs/designs/scripted-cross-turn-evidence-reuse-plan.md`
- Local 2026-04-22 progress:
  - captured a fresh passing scripted Netflix smoke baseline under
    `.tmp/netflix-smoke-runtime/2026-04-22-baseline/`, with tracked summary in
    [docs/perfbase/2026-04-22-netflix-smoke-baseline.md](./perfbase/2026-04-22-netflix-smoke-baseline.md).
  - landed the repo-native trace report workflow via
    `brain.exe --trace-report <path>` and saved the baseline report beside the
    raw artifacts at
    `.tmp/netflix-smoke-runtime/2026-04-22-baseline/trace-report.md`.
  - updated [docs/designs/netflix-smoke-runtime-performance-plan.md](./designs/netflix-smoke-runtime-performance-plan.md)
    with the measured baseline, hotspot ranking, and next-slice ordering.
  - drafted and refined
    [docs/designs/scripted-cross-turn-evidence-reuse-plan.md](./designs/scripted-cross-turn-evidence-reuse-plan.md)
    around the first behavior-changing slice:
    conservative turn-start ready state, carry-forward evidence reuse,
    phase-oriented direction for later work, and logging upgrades needed for
    before/after analysis.
  - completed a code-and-trace cross-check before implementation:
    the repeated turn-start `list_windows` and `describe_window` pattern is
    LLM-driven in the ordinary loop, while brain-owned preflight and
    post-action snapshot paths remain separate helper work.
  - verified in this session:
    - `.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml`
      passed on the first try with scenario elapsed `882.255 s`.
    - `dotnet test src\head\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~TraceReportTests|FullyQualifiedName~ScriptedModeTests"`
      passed after the trace-report implementation work.
  - no behavior-changing optimization has landed yet; the current state is
    planning, reporting, and handoff readiness.
- First step for the next session:
  - review or commit the doc-only handoff updates, then inspect the current
    tests around
    `Conversation.RunTurnAsync`, `DesktopSessionContext`, and `TraceReportTests`,
    then implement the first slice from
    [Scripted Cross-Turn Evidence Reuse Plan](./designs/scripted-cross-turn-evidence-reuse-plan.md):
    turn-start ready-state tracing, carry-forward evidence injection, and the
    helper-timing / prompt-size logging upgrades before rerunning the Netflix
    smoke and generating a fresh comparison report.

## Daily Repo History

Source shape: `git log --date=short --pretty=format:"%ad %h %s"`

- `2026-04-22` (1 commit): added the scripted cross-turn evidence reuse plan,
  the tracked Netflix smoke baseline summary under `docs/perfbase`, and the
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
  the `body` / `cognition` / `execution` cutover, normalized tool names and
  renamed `src\herhead` to `src\head`, tightened Netflix/browser guardrails
  and test coverage, moved compact-tree compaction into `cognition` with
  rendered artifacts, `llmTree` projections, and omitted-children tracking,
  removed the obsolete Node.js LLM runtime, added WPF `face` settings/status
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
