# Cognition Compact Tree Evaluation Rollout Plan

Last updated: 2026-05-31
Status: in progress

## Summary

The compact-tree runtime migration is complete. `cognition` now returns compact
snapshots from `describe_window` and `describe_window_focus`; `brain`, `cursor`,
and `tars` use the `llmTree` projection for model-facing UI context; and AVA
collects UIA tree evidence, rendered compact-tree images, and real screenshots.

The remaining work is no longer a migration task. It is an evaluation rollout:
prove, document, and keep checking that compact-tree output preserves the
recognizable screen state, key text, and actionable controls well enough for
assistant reasoning and accessibility validation.

## Cross-Check Findings

- Current runtime tool names are `describe_window` and `describe_window_focus`.
  The retired `describe_window_compact` and `describe_window_focus_compact`
  names were found in shared agent skills and compatibility prompt docs, and
  have been cleaned from live `src/agents` prompt sources.
- `cursor` and `tars` share the `brain` turn processor, which extracts
  `llmTree` from compact snapshot responses before sending UI context to the
  model.
- AVA now has the strongest evaluation starting point: it collects
  `describe_window`, `describe_window_focus`, and `capture_window_screenshot`
  evidence, and it passes `includeImage=true` for compact snapshot tools.
- The repo has compact rendering, offline artifact rendering support, and an
  AVA-owned opt-in compact-tree evaluation entry point. Manual evaluation runs
  and benchmark result notes are still pending.
- I found no live `UiSnapshotCompactor` implementation to remove; references to
  removing it are historical.
- I found no documented parity run, benchmark fixture, or manual evaluation
  result proving the compact render against real screenshots.

## Implemented Entry Point

AVA now supports an explicit compact-tree evaluation mode:

```powershell
dotnet run --project src/assistants/ava -- --evaluate-compact-tree --window-handle 0x00123456
```

The command collects:

- `describe_window` with `includeImage=true`
- `describe_window_focus` with `includeImage=true`
- `capture_window_screenshot`

It writes `compact-tree-evaluation.json`, `verdict.json`, raw tool outputs, and
copied image artifacts under `artifacts/ava/compact-tree-evaluation/<run-id>`
unless `--output-dir` is provided.

Add `--vision-verdict` to ask the configured AVA evaluator LLM to compare the
real screenshot with the compact window render:

```powershell
dotnet run --project src/assistants/ava -- --evaluate-compact-tree --window-handle 0x00123456 --vision-verdict
```

## Goals

- Add an opt-in compact-tree evaluation flow that compares:
  - a real screenshot from `capture_window_screenshot`
  - a rendered compact-tree image from `describe_window` or
    `describe_window_focus` with `includeImage=true`
- Produce structured verdicts for recognition-level fidelity:
  `samePrimaryScreen`, `sameRecognizableTaskOrState`, `sameKeyText`,
  `sameKeyActionableControls`, `missingCriticalElements`,
  `hallucinatedElements`, `overallMatch`, and `confidence`.
- Store evaluation inputs, outputs, and verdicts as auditable artifacts.
- Run and document parity checks, benchmark measurements, and manual review
  passes.
- Clean stale `*_compact` tool references from live prompt and skill docs.

## Non-Goals

- Do not reintroduce separate `*_compact` MCP tools.
- Do not run screenshot-vs-compact evaluation during normal assistant turns.
- Do not make compact renders pixel-faithful screenshots.
- Do not block AVA accessibility validation on an LLM vision verdict.

## Proposed Owner

Implement the first evaluation harness in or next to AVA. AVA already owns
evidence bundles, rendered compact-tree collection, real screenshot collection,
Markdown/JSON reporting, and role-specific LLM configuration. Keep reusable
snapshot parsing and artifact helpers in shared code only when more than one
assistant needs them.

## Implementation Plan

### 1. Clean Live Tool References

- Status: done.
- Replaced live prompt and skill references to `describe_window_compact` with
  `describe_window`.
- Replaced live prompt and skill references to `describe_window_focus_compact`
  with `describe_window_focus`.
- Kept historical design docs unchanged except for clear status/supersession
  notes.
- Existing activation behavior continues to key off current tool names.

### 2. Add Opt-In Evaluation Entry Point

- Status: done for explicit window-handle evaluation.
- Added `ava --evaluate-compact-tree --window-handle <handle>`.
- Requires an existing window handle or a validation/scenario flow that resolves
  one deterministically.
- Collects:
  - `describe_window` with `includeImage=true`
  - `describe_window_focus` with `includeImage=true`
  - `capture_window_screenshot`
- Uses debug-trace mode for evaluation runs, so raw UIA evidence remains
  available for investigation while normal assistant turns stay compact.

### 3. Persist Evaluation Artifacts

- Status: done for the AVA entry point.
- Stores the compact JSON, compact render PNG, real screenshot PNG, and verdict
  JSON under a stable artifact directory.
- Includes source stats such as `sourceNodeCount`, `keptNodeCount`,
  `omittedNodeCount`, and `algorithmVersion`.
- Includes tool call ids when available so verdicts can be traced back to MCP
  evidence.

### 4. Add Vision Verdict Rubric

- Status: done as optional `--vision-verdict`.
- Treats the real screenshot as the source of truth.
- Asks a vision-capable model to compare the screenshot with the compact render.
- Requires structured JSON with:
  - `samePrimaryScreen`
  - `sameRecognizableTaskOrState`
  - `sameKeyText`
  - `sameKeyActionableControls`
  - `missingCriticalElements`
  - `hallucinatedElements`
  - `overallMatch`
  - `confidence`
  - `notes`
- Keeps the verdict diagnostic. It should inform rollout decisions, not replace
  deterministic evidence or AVA findings.

### 5. Add Benchmarks And Parity Checks

- Status: pending.
- Use saved large snapshots and representative trace JSONL files.
- Measure compact build/render time, output byte size, retained node counts,
  and omitted node counts.
- Compare current compact output against expected high-value UI nodes for:
  - browser chrome-heavy windows
  - deep web content pages
  - focused-control subtrees
  - sparse UIA trees that require screenshot fallback

### 6. Document Manual Evaluation Results

- Status: pending.
- Add a dated result note under `devdocs/perfbase` or a compact-tree-specific
  evaluation folder.
- For each manual pass, record:
  - app/window surface
  - real screenshot path
  - compact render path
  - verdict summary
  - missed or hallucinated elements
  - follow-up tuning needed

## Acceptance Criteria

- Live prompt and skill docs use only current compact snapshot tool names.
- An opt-in evaluation run can produce compact JSON, compact render, real
  screenshot, and verdict artifacts for a selected window.
- Pending: at least three manual evaluation passes are documented.
- Pending: a non-gating benchmark captures compact output size and timing for saved
  representative snapshots.
- The active todo list points at this plan rather than the completed migration
  plan.

## Verification

- Focused prompt/skill activation tests after tool-name cleanup.
- AVA evidence/report tests after evaluation artifact wiring.
- Compact snapshot builder and artifact-renderer tests after any renderer or
  schema changes.
- Manual evaluation with a browser-heavy window, a deep content page, and a
  focused-control subtree.
