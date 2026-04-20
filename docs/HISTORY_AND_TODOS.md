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
| `next` | `P0` | Cut scripted Netflix smoke runtime below one minute. | The current end-to-end Netflix smoke now passes, but it still takes about seven minutes to run. Profile and remove avoidable repair loops, extra evidence refreshes, redundant tool calls, and other per-turn latency so the same scenario completes in under one minute, ideally much faster. |
| `next` | `P1` | Decide whether to add separate scripted coverage for app-first launch. | The current Netflix smoke is now explicitly website-navigation-based; add another smoke if we want deterministic coverage for the app-first fallback-confirmation path. |
| `next` | `P1` | Finish the compact-tree rollout in `cognition`. | Add the opt-in screenshot-vs-compact evaluation harness, then run the documented parity checks, benchmarks, and manual evaluation passes in [Cognition Compact Tree Migration](./designs/cognition-compact-tree-migration.md). |
| `next` | `P1` | Add dedicated coverage for the WPF `face` app. | Start with settings edits, status mapping, and view-model state transitions. |
| `next` | `P1` | Broaden the prompt and skill intent vocabulary. | Add a small set of generic intents and cover them with activation tests. |
| `soon` | `P2` | Add automated tests for `process-manager`. | Start with command validation and process-list parsing, then add integration tests later. |

## Working-Tree Notes

These notes describe local work that exists in the working tree but is not yet
part of committed repo history.

- Local working tree now includes:
  - the verified fix for the Netflix PIN-prompt contradiction / retry churn,
  - the updated bug doc for that fix,
  - the refreshed active todo list that promotes scenario runtime to `P0`.

## Daily Repo History

Source shape: `git log --date=short --pretty=format:"%ad %h %s"`

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
