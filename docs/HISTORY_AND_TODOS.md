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
| `done` | `P0` | Finish the `body` / `cognition` / `execution` cutover. | `process-manager` builds again, `dotnet test src\heronwin.sln` passes with 275 tests, local `MCP_SERVERS` points at `process-manager`/`cognition`/`execution`, and the scripted Netflix smoke flow now exercises the refactored stack end to end through its current log-based checks. |
| `next` | `P1` | Tighten Netflix search/playback scripted validation. | The latest scripted pass still exposed a Netflix search-control mis-target that current unresolved-outcome checks did not fail, so strengthen both the targeting and the scenario/evaluator criteria. |
| `next` | `P1` | Clean up leftover historical `src\herbody` paths and stale local config. | Root docs and local MCP wiring are retargeted; remove only any remaining empty leftovers once the cleanup pass is done. |
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
- 2026-04-18: landed most of the `src/body` / `cognition` / `execution`
  refactor across solution references, `brain`, tests, `.github/agents`, and
  repo docs.
- 2026-04-18: repaired a local ACL problem on generated `obj` and `bin`
  folders so `dotnet build src\heronwin.sln` and
  `dotnet test src\heronwin.sln` pass again; the remaining follow-up is the
  post-reboot `process-manager` build and end-to-end smoke test.
- 2026-04-18: reran `npm run build` in `src/body/process-manager`, switched the
  local `brain/.env` MCP wiring from `eyesandhands` to `process-manager`,
  `cognition`, and `execution`, and added a guardrail that blocks
  `process-manager/start_process` from hijacking website-navigation requests
  into Microsoft Store or other OS-process launches.
- 2026-04-18: added internal Netflix follow-through for named profile
  selection and remaining PIN digits, then reran the scripted Netflix smoke
  flow successfully against the refactored `process-manager` / `cognition` /
  `execution` stack.
- 2026-04-18: the latest scripted pass also showed one follow-up quality gap:
  Netflix search targeting can still hit the browser's `Open in app` control,
  and the current unresolved-outcome checks are not yet strict enough to fail
  that run.

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
