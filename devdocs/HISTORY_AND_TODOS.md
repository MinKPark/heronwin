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
| `next` | `P0` | Make scripted scenario pass/fail stricter for incomplete final outcomes. | `gpt-5.4` API and `gpt-5.3-codex-spark` CLI runs passed current checks even though Turn 5 said playback was not confirmed; update assertions/evaluator so incomplete final replies cannot pass just because required title text appears. |
| `next` | `P1` | Decide whether to add separate scripted coverage for app-first launch. | The current Netflix smoke is now explicitly website-navigation-based; add another smoke if we want deterministic coverage for the app-first fallback-confirmation path. |
| `next` | `P1` | Finish the compact-tree rollout in `cognition`. | Add the opt-in screenshot-vs-compact evaluation harness, then run the documented parity checks, benchmarks, and manual evaluation passes in [Cognition Compact Tree Migration](./designs/cognition-compact-tree-migration.md). |
| `next` | `P1` | Split more assistant-specific runner policy out of the shared library. | Move scenario-only runner/context code toward `tars` and interactive-only voice/text policy toward `cursor` as the next cleanup pass. |
| `next` | `P1` | Broaden the prompt and skill intent vocabulary. | Add a small set of generic intents and cover them with activation tests. |
| `done` | `P0` | Support Codex Spark. | Completed 2026-05-16: implemented the CLI-first path from [Codex Spark Support Plan](./designs/codex-spark-support-plan.md), including Spark alias normalization, model-profile handling, Spark-safe image omission, config/docs updates, trace-report coverage, and focused tests. |
| `done` | `P2` | Broaden automated tests for built-in process tools. | Completed 2026-05-10: added process-list parsing/formatting coverage, start/stop argument validation coverage, and safe start/list/stop integration tests for test-owned processes. |

## Working-Tree Notes

These notes describe local work that exists in the working tree but is not yet
part of committed repo history.

- Start-of-day 2026-05-16 update:
  - before this task-tracking edit, `git status --short --branch` was clean at
    commit `05cfa9d` on `main` / `origin/main`.
  - this pass adds the P0 Codex Spark support task for today's implementation
    work.
  - no builds or tests were rerun for this documentation-only task update.
  - implemented the P0 Codex Spark support slice:
    - added Spark aliases for `spark`, `codex-spark`, and
      `gpt-5.3-codex-spark`.
    - added Spark-specific Codex model profile behavior.
    - kept the existing CLI route and passed Spark as
      `codex exec --model gpt-5.3-codex-spark`.
    - omitted `--image` inputs for Spark while preserving image inputs for
      other Codex models.
    - updated config examples, OpenAI setup docs, and the design plan.
  - verification:
    - `dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~LlmSupportTests|FullyQualifiedName~TraceReportTests|FullyQualifiedName~ProviderModeTests"`
    - `dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj`
    - `dotnet test src\heronwin.sln`
  - live Spark scenario measurement on 2026-05-15 PDT:
    - `OPENAI_CODEX_MODEL=gpt-5.3-codex-spark`
    - first attempt failed in `8.772 s` because the local untracked Tars
      `.env` still points MCP server commands at retired `../../body/...`
      paths.
    - reran with absolute `src/tools/...` MCP executable paths.
    - process exit code `0`; harness result passed.
    - wall-clock command time `179.669 s`; trace scenario elapsed `177.948 s`.
    - trace report: 5 turns, 14 LLM responses, average LLM attempt `6.526 s`,
      18 tool calls, requested tool time `35.471 s`.
    - Spark bridge telemetry sent no CLI images and omitted 4 screenshot
      attachments as text-only context.
    - caveat: the final turn's own reply still said playback was not
      confirmed, so this run strengthens the stricter scenario-evaluator P0
      rather than proving a clean end-to-end playback success.
    - rerun after the MCP path fix exited `1` with wall-clock command time
      `196.292 s` and trace scenario elapsed `194.688 s`.
    - rerun trace report: 5 turns, 16 LLM responses, average LLM attempt
      `6.357 s`, 19 tool calls, requested tool time `40.541 s`.
    - rerun Spark telemetry sent no CLI images and omitted 7 screenshot
      attachments as text-only context.
    - rerun reached the `Boyfriend on Demand` title surface and attempted
      playback, but final playback was not confirmed; the final Codex CLI call
      exited `1` with a ChatGPT plugin-sync `403` warning in stderr.

## Daily Repo History

Source shape: `git log --date=short --pretty=format:"%ad %h %s"`

- `2026-05-10` (3 commits): swept stale validation artifacts and old-name
  references, moved daily summaries under `devdocs/daily/2026/`, added the
  daily-summary index, and completed the P2 built-in process-tools coverage
  pass with parsing, argument-validation, and safe start/list/stop tests.
- `2026-05-04` (5 commits): renamed the source/tooling surface from `body` to
  `tools`, split developer documentation into `devdocs/`, added daily-summary
  documentation for the restructure, and refreshed setup and project docs for
  the new layout.
- `2026-05-03` (5 commits): executed the head-to-assistants refactor plan by
  moving `src/head` into `src/assistants`, deleting the retired `face` UI,
  turning `brain` into the shared library, and adding the runnable `tars` and
  `cursor` assistant hosts with tests and configuration.
- `2026-04-26` (9 commits): removed the separate process-manager service,
  added built-in process tools, tightened prompt guidance for deterministic
  same-surface actions, added Netflix search/profile/PIN skill updates,
  improved trace model resolution, documented OpenAI configuration, and added
  the post-action UI settle delay.
- `2026-04-25` (10 commits): landed scripted turn-start evidence carry-forward,
  compact window inventory, MCP call instrumentation, scripted lookahead for
  no-op next turns, trace-report fixes, browser/startup skill tightening, and
  tracked Netflix smoke rerun notes.
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
