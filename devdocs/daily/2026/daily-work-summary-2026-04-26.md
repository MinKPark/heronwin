# Feature Change Summary - 2026-04-26

Status captured at 2026-04-26 17:42 PDT.

- Current branch: `main`
- Current committed head before this summary doc: `bdb240c`
- Working tree before this summary doc: clean
- Focus: feature and runtime changes made today.

## Feature Sequence Table

| Seq | Commit | Area | Feature change |
| ---: | --- | --- | --- |
| 1 | `e3ab15e` | Runtime architecture | Removed the separate Node.js `process-manager` MCP server and moved process tools into the .NET brain process. |
| 2 | `e97c42b` | Agent behavior | Tightened core prompt rules for deterministic same-surface batching, startup inventory reuse, and fewer unnecessary rediscovery attempts. |
| 3 | `00a3bf4` | Netflix search | Added a dedicated `netflix-search` skill so opening Netflix Search and entering a known query can happen in one bounded attempt. |
| 4 | `e2e2318` | Netflix profile/PIN | Updated the Netflix profile/PIN skill so profile selection and structured PIN entry rules are clearer and more deterministic. |
| 5 | `a01522e` | Trace reporting | Fixed trace report model resolution so Codex runs report `openAiCodexModel` correctly. |
| 6 | `bdb240c` | UI freshness | Added configurable post-action UI settle delay before the snapshot used by the next LLM attempt. |
| 7 | `5391042` | Repo housekeeping | Updated the root README and added the project license. |

## Node.js Removal

The old generated Node.js process-manager package is gone from `src/tools/process-manager`.

What changed:

- Deleted the Node.js package files: `package.json`, `package-lock.json`, `tsconfig.json`, TypeScript source, and package README.
- Added `src/head/brain/BuiltInProcessTools.cs`.
- Exposed these process tools directly from brain:
  - `list_processes`
  - `start_process`
  - `stop_process`
- Updated `McpClientManager` so built-in process tools are listed and callable without launching another MCP server.
- Updated `.env.example` so local MCP wiring only includes the .NET `cognition` and `execution` servers.
- Updated docs to reflect that there is no active JavaScript runtime under `src`.

Why it matters:

- Brain owns the process-management feature now.
- There is no separate Node.js MCP server to build, run, configure, or keep in sync.
- The tool surface remains available to the agent through the same tool-call path.

## Agent Behavior Changes

The core agent guidance now supports bounded batching when a skill has deterministic same-surface instructions.

Key behavior changes:

- Default remains one tool at a time.
- A skill can authorize a short same-surface batch when the action sequence is deterministic.
- Batching must stop at likely UI-transition boundaries such as navigation, modal opening, playback start, page load, or search result updates.
- After a UI-changing boundary, the agent must refresh evidence before deciding the next action.
- Fresh startup or carry-forward window inventory with a concrete `windowHandle` now counts as discovery evidence, so the agent should not repeat `list_windows` just to rediscover the same target.

## Netflix Search Skill

Added `src/agents/shared/skills/netflix/netflix-search.skill.md`.

The skill covers Netflix in-site search entry and result verification.

Main rules:

- If the visible Netflix Search control supports direct value entry, set the query on that exact control.
- If Search must be opened first, invoke Search and type the known query in the same tool-call response.
- Do not spend a separate LLM attempt just to decide whether to type a query that is already known.
- Stop after query entry and wait for fresh Netflix evidence before judging whether results appeared.
- Do not claim search is complete until fresh evidence shows the requested query or matching visible results.

Expected effect:

- The old Search-open step and query-entry step can collapse into one attempt when the surface is clear.

## Netflix Profile And PIN Skill

Updated `src/agents/shared/skills/netflix/netflix-profile-and-pin.skill.md`.

Main rules:

- Only select a profile when the user explicitly asked to select, choose, click, or open a named profile.
- If the profile picker is merely being checked, report the state and stop.
- If the named profile is visible, target that exact profile tile path.
- If the PIN prompt has four separate boxes and the PIN plus starting surface are clear, enter four single-character tool calls in one response.
- Do not send the full PIN as one bulk text value on a four-box PIN prompt.
- Verify after the final digit unless a tool errors or evidence shows focus did not advance.

Expected effect:

- PIN entry can happen in one LLM attempt while still using reliable per-digit tool calls.

## Trace Reporting Change

Trace reports now resolve model names by provider.

What changed:

- OpenAI API runs still use `openAiModel`.
- Codex runs now use `openAiCodexModel`.
- Reports now show provider/model pairs such as `OpenAiCodex / gpt-5.5` correctly.

Why it matters:

- GPT-5.5 Codex runs are no longer mislabeled as the default OpenAI API model.
- Comparisons between API and Codex paths are less confusing.

## Post-Action UI Settle Delay

Added `POST_ACTION_UI_SETTLE_DELAY_MS`, default `1000`.

The new pause happens before the post-action `describe_window` snapshot that feeds the next model attempt.

| Seq | Runtime point | Behavior |
| ---: | --- | --- |
| 1 | Desktop action tool executes | No extra delay before the action. |
| 2 | Final desktop action in the current LLM response completes | Wait `POST_ACTION_UI_SETTLE_DELAY_MS`. |
| 3 | Brain captures `describe_window` | The settled snapshot becomes the newest evidence for the next LLM attempt. |
| 4 | Internal continuations such as structured PIN entry complete | The same settle wait happens before their post-action snapshot. |

Configuration:

- `src/head/brain/.env.example` includes `POST_ACTION_UI_SETTLE_DELAY_MS=1000`.
- Set it to `0` to disable the pause.
- The setting appears in session-start and config trace data.

Why it matters:

- It gives UI Automation a short chance to catch up after the last tool action.
- It targets the exact handoff point where stale UI trees can confuse the next LLM attempt.
- Batched same-surface actions do not pay the delay between each deterministic step; the delay is before the final snapshot.

## Documentation Updates

Docs updated today:

- `README.md`
- `docs/README.md`
- `docs/GET_STARTED.md`
- `devdocs/GOAL_AND_DESIGN.md`
- `devdocs/HISTORY_AND_TODOS.md`
- `docs/get-started-script-mode.md`
- `docs/get-started-voice-mode.md`
- `devdocs/designs/tools-cognition-execution-refactor.md`
- `src/tools/README.md`
- `src/head/brain/README.md`
- `src/head/brain/.env.example`

The docs now describe the .NET-only runtime shape more clearly, including built-in process tools and the new UI settle configuration.

## Tests Added Or Updated

Feature coverage added or updated around:

- Built-in process tools in `McpClientManagerTests`.
- Startup inventory and continuation behavior in `AgentRunnerContinuationTests`.
- Netflix skill activation/loading in `AgentPromptLoaderTests`.
- Netflix search skill composition in `AgentPromptComposerTests`.
- Trace report provider/model resolution in `TraceReportTests`.
- UI settle delay behavior in `AgentRunnerContinuationTests`.
- Turn processor config construction after adding `PostActionUiSettleDelayMs`.

Latest local verification after the UI settle delay change:

- `dotnet test src\head\brain.tests\HeronWin.Brain.Tests.csproj` passed with 270 tests.
- `git diff --check` reported no whitespace errors, only LF-to-CRLF warnings from Git on Windows.

## Follow-Up Feature Work

| Seq | Follow-up | Reason |
| ---: | --- | --- |
| 1 | Keep an eye on current-turn completion vs next-turn lookahead wording. | The next area to simplify is making completion confirmation and lookahead guidance less easy to mix. |
| 2 | Consider whether more app skills should expose deterministic same-surface batches. | Netflix search and PIN now have the pattern; other stable surfaces may benefit too. |
| 3 | Keep process tools built into brain unless a future feature truly needs a separate server. | This preserves the no-Node.js runtime direction. |
