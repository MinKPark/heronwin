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
| `done` | `P0` | Make the debugging workflow an explicit standing guardrail. | Captured in [Development Guardrails](./DEVELOPMENT_GUARDRAILS.md). |
| `done` | `n/a` | Create a top-level `docs/` folder and split the project docs into focused files. | Keep the index and cross-links current. |
| `next` | `P0` | refactor herbody code | rename the folder to "body" and split eyesandhands to "cognition" and "execution" MCP servers |
| `next` | `P1` | Add dedicated coverage for the WPF `face` app. | Start with settings edits, status mapping, and view-model state transitions. |
| `next` | `P1` | Broaden the prompt and skill intent vocabulary. | Add a small set of generic intents and cover them with activation tests. |
| `soon` | `P2` | Add automated tests for `process-manager`. | Start with command validation and process-list parsing, then add integration tests later. |
| `backlog` | `P3` | Revisit browser-backed ChatGPT mode only if it becomes a product requirement. | Keep current effort on API-backed LLMs and local tooling. |

## Working-Tree Notes

These notes describe local work that exists in the working tree but is not yet
part of committed repo history.

- 2026-04-18: split the top-level project docs into `docs/README.md`,
  `docs/GOAL_AND_DESIGN.md`, `docs/HISTORY_AND_TODOS.md`, and
  `docs/DEVELOPMENT_GUARDRAILS.md`.
- 2026-04-18: moved the standing debugging rule into a dedicated guardrail
  document and updated the root README links.

## Daily Repo History

Source shape: `git log --date=short --pretty=format:"%ad %h %s"`

- `2026-04-18` (1 commit): added top-level progress and status documentation to
  track project state.
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
