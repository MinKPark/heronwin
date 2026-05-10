# Feature Change Summary - 2026-05-10

Status captured at 2026-05-10 13:43 PDT.

- Current branch: `main`
- Current committed head before this summary doc: `4b8994b`
- Working tree before this summary doc: clean
- Focus: repository cleanup, daily-summary structure, and built-in process-tool test hardening.

## Feature Sequence Table

| Seq | Commit | Area | Feature change |
| ---: | --- | --- | --- |
| 1 | `2762e52` | Repo cleanup | Removed stale desktop-automation validation artifacts, ignored future `.validate-*.txt` output, and added a clearer remaining-work split to `devdocs/GOAL_AND_DESIGN.md`. |
| 2 | `cfcdbf2` | Devdoc structure | Moved daily summaries under `devdocs/daily/2026/`, added a daily-summary index, and refreshed the developer-doc index. |
| 3 | `4b8994b` | Process tools | Added parsing, formatting, argument validation, and safe start/list/stop coverage for the built-in process tools. |

## Repository Cleanup

Removed stale generated validation files from `src/tools/desktop-automation/`:

- `.validate-stderr.txt`
- `.validate-stdin.txt`
- `.validate-stdout.txt`

Added `.validate-*.txt` to `.gitignore` so future local validation output stays out of tracked source.

The cleanup also updated the 2026-05-04 daily summary to mark the old `body` / `herbody` path sweep as complete. Remaining old-name mentions are historical notes, not live paths.

## Daily Summary Layout

Daily wrap-up notes now live under a year folder:

- `devdocs/daily/2026/daily-work-summary-2026-04-26.md`
- `devdocs/daily/2026/daily-work-summary-2026-05-04.md`

Added `devdocs/daily/README.md` as the daily-summary index and refreshed `devdocs/README.md` so new summaries have a stable place in the developer docs.

## Built-In Process Tools Coverage

The built-in process tools remain owned by `brain`; no separate process-manager service was reintroduced.

Production changes landed in `src/assistants/brain/BuiltInProcessTools.cs`:

- added normalized process-list parsing for Windows `tasklist /FO CSV /NH` output
- added parsing for Unix-style `ps aux` output
- added normalized process-list text formatting
- added internal start/stop argument records and validation helpers
- made `list_processes` return normalized process rows when parsing succeeds
- kept `start_process` and `stop_process` returning concise text tool results

Automated coverage landed in `src/assistants/brain.tests/BuiltInProcessToolsTests.cs`:

- start-process command and argument validation
- stop-process PID and force-flag validation
- Windows task-list CSV parsing
- `ps aux` parsing
- formatted process-list output
- unknown-PID stop error handling
- safe start/list/stop integration coverage for a test-owned PowerShell sleep process
- `cwd` integration coverage for `start_process`

The active plan was updated at `devdocs/designs/built-in-process-tools-test-plan.md`, and the P2 process-tools item in `devdocs/HISTORY_AND_TODOS.md` is now marked done.

## Verification

Recorded in `devdocs/designs/built-in-process-tools-test-plan.md` as verified earlier on 2026-05-10:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~BuiltInProcessToolsTests|FullyQualifiedName~McpClientManagerTests"
dotnet test src\heronwin.sln
```

Wrap-up pass verification:

- `git status --short` was clean before this summary doc was added.
- `git log --date=short --pretty=format:"%ad %h %s" --since="2026-05-10 00:00"` confirmed the three 2026-05-10 commits listed above.

Builds and tests were not rerun during this wrap-up pass because the wrap-up changes are documentation-only.

## Next Session

First steps:

1. Review and commit the wrap-up docs if they look right.
2. Resume the active P0 work by tightening scripted scenario pass/fail behavior so incomplete final outcomes cannot pass on required-title text alone.
3. Continue the Netflix smoke runtime work after the OpenAI API quota/billing blocker is cleared enough to rerun `OpenAiApi` comparisons.

Open process-tools follow-up is optional: the plan still notes a possible future structured artifact for `list_processes`, but the P2 coverage pass itself is complete.
