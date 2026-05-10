# Feature Change Summary - 2026-05-04

Status captured at 2026-05-04 21:35 PDT.

- Current branch: `main`
- Current committed head before this summary doc: `2650d03`
- Working tree before this summary doc: clean
- Focus: repository restructuring — renaming `src/body` to `src/tools`, and reorganizing developer documentation under `devdocs/`.

## Feature Sequence Table

| Seq | Commit | Area | Feature change |
| ---: | --- | --- | --- |
| 1 | `6237486` | Source layout | Renamed `src/body/*` to `src/tools/*` (desktop-automation, execution, cognition, and their tests). |
| 2 | `7a66698` | Naming conventions | Replaced remaining `body`/`herbody` references with `tools` across project files, env vars, namespaces, and docs. |
| 3 | `4e57cb7` | Doc layout | Moved dev-facing docs (`DEVELOPMENT_GUARDRAILS.md`, `GOAL_AND_DESIGN.md`, `HISTORY_AND_TODOS.md`, `bugs/`, `designs/`, `perfbase/`, prior daily summary) from `docs/` to new `devdocs/` folder. |
| 4 | `2650d03` | Doc structure | Updated docs to reflect the new `devdocs/` organization, added `devdocs/README.md`, and added `docs/APP_SKILLS.md`. |

## Source Layout: `src/body` → `src/tools`

Renamed the entire body subtree to `tools`:

- `src/body/desktop-automation` → `src/tools/desktop-automation`
- `src/body/desktop-automation.tests` → `src/tools/desktop-automation.tests`
- `src/body/execution` → `src/tools/execution`
- `src/body/cognition` → `src/tools/cognition`
- `src/body/README.md` → `src/tools/README.md`

The first commit performed pure file moves with no content edits. The second commit then updated every reference that still pointed at the old name.

What changed in the follow-up rename pass:

- `heronwin.sln` project paths now point under `src/tools/`.
- Assistant project files (`Cursor.csproj`, `Tars.csproj`) and their `.env.example` entries point at the new MCP server paths.
- Namespaces moved from `HeronWin.Body.*` to `HeronWin.Tools.*` in source and tests.
- The desktop-automation tests project is now `HeronWin.Tools.DesktopAutomation.Tests.csproj`.
- The debugging environment variable changed from `BODY_WINDOWS_DEBUG` to `TOOLS_WINDOWS_DEBUG`.
- `buildandrun.ps1` and `McpClientManager` use the new paths.
- Validation fixture files (`.validate-stdin.txt`, `.validate-stdout.txt`, `.validate-stderr.txt`) under `desktop-automation` were updated to match.
- Documentation (`README.md`, `docs/GOAL_AND_DESIGN.md`, `docs/HISTORY_AND_TODOS.md`, design docs, prior daily summary) was rewritten to use the new names.

Why it matters:

- The directory name now matches how the codebase already talks about these MCP servers ("tools").
- All entry points — solution, project references, env vars, namespaces, docs — are consistent.

## Doc Layout: `docs/` → `devdocs/`

Developer-facing docs were split out from end-user setup docs.

Moved from `docs/` to `devdocs/`:

- `DEVELOPMENT_GUARDRAILS.md`
- `GOAL_AND_DESIGN.md`
- `HISTORY_AND_TODOS.md`
- `daily-work-summary-2026-04-26.md`
- The `bugs/` folder (including `README.md` and the two 2026-04-19 Netflix bug notes)
- The `designs/` folder (all design docs and SVG flow diagrams)
- The `perfbase/` folder (Netflix smoke baselines and reruns)

The fourth commit then made the link surface match the new layout:

- Added `devdocs/README.md` as the dev-doc index, listing core, design, bug, and perfbase docs.
- Updated `README.md` and `docs/README.md` to point at `devdocs/` for design and history content while keeping live setup docs under `docs/`.
- Adjusted internal cross-links in moved design docs (`app-agnostic-runtime-and-skills-plan.md`, `brain-debuggability-and-rewrite-guardrails.md`, `generic-continuations-and-discrete-entry-plan.md`, `head-to-assistants-refactor-plan.md`, `netflix-smoke-runtime-performance-plan.md`, `scripted-cross-turn-evidence-reuse-plan.md`) so relative paths still resolve.
- Updated the prior daily summary's cross-links to match the new locations.

## New Docs

- `devdocs/README.md` — index for the developer doc tree (core, designs, bugs, perfbase).
- `docs/APP_SKILLS.md` — new end-user / setup-side document about app skills (added under the live `docs/` tree).

## Documentation Updates

Touched docs today:

- `README.md`
- `docs/README.md`
- `docs/APP_SKILLS.md` (new)
- `devdocs/README.md` (new)
- `devdocs/HISTORY_AND_TODOS.md`
- `devdocs/daily-work-summary-2026-04-26.md`
- `devdocs/designs/app-agnostic-runtime-and-skills-plan.md`
- `devdocs/designs/brain-debuggability-and-rewrite-guardrails.md`
- `devdocs/designs/compact-window-inventory-plan.md`
- `devdocs/designs/generic-continuations-and-discrete-entry-plan.md`
- `devdocs/designs/head-to-assistants-refactor-plan.md`
- `devdocs/designs/netflix-smoke-runtime-performance-plan.md`
- `devdocs/designs/scripted-cross-turn-evidence-reuse-plan.md`
- `devdocs/designs/tools-cognition-execution-refactor.md`
- `src/tools/README.md`

The new structure draws a clearer line between end-user setup material in `docs/` and developer / design / history material in `devdocs/`.

## Tests Added Or Updated

No new test cases were added today. The desktop-automation test project was renamed and its namespaces updated to match the `HeronWin.Tools.*` move; behavior under test is unchanged.

## Follow-Up Feature Work

| Seq | Follow-up | Reason |
| ---: | --- | --- |
| 1 | Completed 2026-05-10: swept tracked and hidden repo surfaces for old `body` / `herbody` paths, namespaces, and debug environment variables. Removed stale `.validate-*.txt` desktop-automation artifacts and ignored future validation output. | Remaining old-name mentions in this summary and the tools refactor design are historical migration notes. |
| 2 | Confirm `devdocs/README.md` stays in sync as new design or perfbase docs land. | The new index needs to be updated alongside future doc additions. |
| 3 | Decide whether `daily-work-summary-*.md` files should live under a dedicated `devdocs/daily/` subfolder once a few more accumulate. | The flat layout works for two summaries but will get noisy over time. |
