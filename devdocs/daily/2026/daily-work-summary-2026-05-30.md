# Feature Change Summary - 2026-05-30

Status captured after commit `285bf52`.

- Current branch: `dev/minkpark/remote`
- Current committed head before this summary doc: `285bf52`
- Working tree before this summary doc: clean
- Focus: closing the compact-tree runtime migration, adding an AVA-owned
  compact-tree evaluation entry point, and leaving the remaining rollout work
  as manual parity/benchmark evaluation.

## Feature Sequence Table

| Seq | Commit | Area | Feature change |
| ---: | --- | --- | --- |
| 1 | `285bf52` | AVA / cognition | Implemented compact-tree evaluation mode with optional vision verdict support. |

## Compact-Tree Evaluation

The old `cognition` compact-tree migration plan is now marked done, and the
active follow-up lives in
`devdocs/designs/cognition-compact-tree-evaluation-rollout-plan.md`.

The implemented AVA command is:

```powershell
dotnet run --project src/assistants/ava -- --evaluate-compact-tree --window-handle 0x00123456
```

It collects:

- `describe_window` with `includeImage=true`
- `describe_window_focus` with `includeImage=true`
- `capture_window_screenshot`

It writes raw tool outputs, copied PNG artifacts, `compact-tree-evaluation.json`,
and `verdict.json` under `artifacts/ava/compact-tree-evaluation/<run-id>` unless
`--output-dir` is provided.

The optional vision verdict path is:

```powershell
dotnet run --project src/assistants/ava -- --evaluate-compact-tree --window-handle 0x00123456 --vision-verdict
```

That route asks the configured AVA evaluator LLM to compare the real screenshot
against the compact window render using the planned structured rubric.

## Prompt And Skill Cleanup

Live agent prompt and skill sources under `src/agents` no longer reference the
retired `describe_window_compact` or `describe_window_focus_compact` tool names.
They now use the current `describe_window` and `describe_window_focus` tools.

Historical design references remain in historical docs, with supersession notes
where needed.

## Verification

Verified during the implementation pass:

```powershell
dotnet test src\assistants\ava.tests\HeronWin.Ava.Tests.csproj
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~AgentPromptComposerTests|FullyQualifiedName~AssistantIdNormalizationTests|FullyQualifiedName~ProviderModeTests"
dotnet test src\heronwin.sln
dotnet run --project src\assistants\ava -- --help
git diff --check
```

Results:

- AVA tests passed with 71 tests.
- Focused brain tests passed with 40 tests.
- Full solution tests passed with 481 tests total across brain,
  desktop-automation, AVA, TARS, and cursor test projects.
- `ava --help` printed the new compact-tree evaluation options.
- `git diff --check` reported only expected CRLF normalization warnings.

Note: an early focused brain test run hit a transient `VBCSCompiler` file-handle
lock on `HeronWin.Brain.dll`; `dotnet build-server shutdown` cleared it, and
subsequent focused and full-solution test runs passed.

## Remaining Work

The compact-tree evaluation entry point is implemented, but the rollout is not
done until the artifacts are exercised on real windows.

Next session:

1. Open or select representative windows and run `ava --evaluate-compact-tree`
   for a browser chrome-heavy window, a deep content page, and a focused-control
   subtree.
2. Run at least one `--vision-verdict` pass with a configured evaluator model.
3. Add dated parity/manual evaluation notes under `devdocs/perfbase` or a
   compact-tree-specific evaluation folder.
4. Add a non-gating benchmark note for compact output size, source/kept/omitted
   node counts, and elapsed time using saved representative snapshots or trace
   artifacts.
5. Once those notes are captured, update
   `devdocs/designs/cognition-compact-tree-evaluation-rollout-plan.md` and the
   active P1 todo.
