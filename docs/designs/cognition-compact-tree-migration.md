# Cognition Compact Tree Migration

Last updated: 2026-04-18

## Summary

This design moves UI snapshot compaction from `brain` into the `cognition`
MCP server, improves compaction speed by operating on typed snapshot data
instead of parsing serialized JSON, and adds an opt-in evaluation flow that
checks whether a rendered compact-tree image still matches the real visible
screen.

The plan keeps the existing raw inspection tools for debugging and manual
inspection, then adds new compact inspection tools that are intended to become
the model-facing path used by `brain`.

## Goals

- Move model-facing UI tree compaction from `brain` to `cognition`.
- Improve compaction speed enough that `brain` no longer needs to parse and
  summarize large `describe_window*` JSON payloads locally.
- Improve compaction accuracy by validating compacted output against the real
  window screenshot with a vision-capable LLM in an opt-in evaluation flow.
- Keep the raw inspection path available for debugging, parity checks, and
  manual investigation.

## Non-Goals

- Replace the raw `describe_window` or `describe_window_focus` tools.
- Run screenshot-vs-compact evaluation on every normal tool call.
- Build a pixel-faithful screenshot recreation from the compacted tree.
- Move LLM inference into `cognition`.

## Current State

- `brain` currently uses `UiSnapshotCompactor` to turn large
  `describe_window` and `describe_window_focus` payloads into prose summaries
  before sending them back to the model.
- `cognition` currently returns the raw structured UIA tree produced by
  `desktop-automation`.
- `desktop-automation` snapshots already include useful fields for compaction
  and rendering, including `uiPath`, `name`, `controlType`, `className`,
  `availableActions`, and `bounds`.
- `brain` already supports vision inputs and scripted scenario execution, which
  makes it the right place for opt-in evaluation against screenshots.

## Decisions

### Tool surface

- Keep raw tools unchanged:
  - `describe_window`
  - `describe_window_focus`
- Add new compact tools in `cognition`:
  - `describe_window_compact`
  - `describe_window_focus_compact`

### Compact response shape

- Compact tools return structured JSON, not prose-only summaries.
- Each compact response includes:
  - `window`
  - `sourceStats`
  - `compactTree`
  - optional rendered image metadata when image rendering is requested
- `sourceStats` includes:
  - `sourceNodeCount`
  - `keptNodeCount`
  - `omittedNodeCount`
  - `algorithmVersion`
  - `budgetHintChars`

### Parameters

- Add `budgetHintChars` to the new compact tools.
  - `brain` supplies this from the active `LlmModelProfile`.
  - when omitted, cognition uses a server default aligned with current standard
    budgets.
- Add `includeImage` to the new compact tools.
  - default `false`
  - when `true`, the tool emits a rendered compact-tree image plus image path
    metadata in the response

### Migration shape

- `brain` switches normal model-facing UI context to the compact tools.
- `brain` keeps a temporary fallback to the local compactor during migration.
- After parity and evaluation are good enough, delete the local
  `UiSnapshotCompactor` and its remaining direct callers.

### Accuracy validation

- Use the real window screenshot as the source of truth.
- Run screenshot-vs-compact validation only in an opt-in evaluation flow.
- The evaluation lives in `brain` or a `brain`-adjacent harness because
  `cognition` does not own an LLM client.

## Implementation Plan

### 1. Move the compaction engine to cognition

- Create a typed compact-tree pipeline in shared desktop automation code so the
  logic can run directly on typed snapshots rather than reparsing JSON text.
- Capture the data needed for compaction in one pass:
  - `path`
  - `uiPath`
  - `name`
  - `controlType`
  - `className`
  - `automationId`
  - `availableActions`
  - `bounds`
  - `isOffscreen`
  - `hasKeyboardFocus`
  - `isKeyboardFocusable`
  - `isSelected`
  - parent/child relationships
- Preserve the existing heuristics as the starting point:
  - focus and selection are high priority
  - actionable named content outranks filler
  - browser chrome is retained when useful but de-prioritized relative to page
    content
  - brand/logo and caption controls are de-prioritized

### 2. Improve speed

- Replace `JsonDocument`-driven property lookups with typed node evaluation.
- Precompute node features once instead of repeatedly reading the same fields.
- Replace repeated full child sorting with a bounded priority-selection
  approach.
- Track estimated output size while building the compact tree so the algorithm
  stops growing once the response is near the requested budget.
- Keep ancestor chains automatically so deep retained nodes remain
  understandable without extra backtracking passes.
- Serialize compact responses without indentation to reduce payload size and
  avoid wasted budget.

### 3. Define the compact-tree output

- `compactTree` remains tree-shaped so retained nodes preserve layout context.
- Retained nodes include:
  - stable identity fields
  - important state flags
  - actions
  - bounds
  - kept children
- The compact tree may include context ancestors and a limited number of nearby
  siblings to preserve recognition quality around retained targets.
- The compact response should be usable both by `brain` and by offline
  render/evaluation tooling without requiring the original raw tree.

### 4. Render the compact tree to an image

- Render a synthetic UI map, not a plain indented text dump.
- Use a bounds-based wireframe approach:
  - canvas sized from the window bounds
  - each retained node drawn as a labeled rectangle based on its bounds
  - depth-based color treatment for quick structure recognition
  - explicit highlight treatment for focused and selected nodes
- For nodes without usable bounds, render them in a fallback outline lane on
  the side of the canvas so they are still visible to the reviewer and to the
  vision model.
- Include concise labels derived from `name` and `controlType`.
- Save the rendered image as a local PNG and surface it through `imagePath` so
  existing image extraction in `brain` continues to work.

### 5. Switch brain to the new compact tools

- Update normal model-facing UI-context paths in `brain` to call:
  - `describe_window_compact`
  - `describe_window_focus_compact`
- Pass `budgetHintChars` from the active `LlmModelProfile`.
- Cache the compact JSON result in the existing UI/focus context slots.
- Keep raw describe tools available for:
  - explicit debugging
  - parity comparison
  - tool-result fallback during migration
- Remove the local prose compactor only after the new compact tools have
  covered the current behavior and the evaluation flow shows acceptable
  recognition parity.

## Accuracy Evaluation Plan

### Evaluation flow

- Add an opt-in `brain` evaluation command or harness flow that:
  1. captures the real window screenshot
  2. calls the matching compact tool with `includeImage=true`
  3. sends both images to the configured vision-capable LLM
  4. stores the verdict and artifacts in the normal debug/log area

### Evaluation rubric

- Ask the LLM for a structured verdict covering:
  - `samePrimaryScreen`
  - `sameRecognizableTaskOrState`
  - `sameKeyText`
  - `sameKeyActionableControls`
  - `missingCriticalElements`
  - `hallucinatedElements`
  - `overallMatch`
  - `confidence`
- Treat the real screenshot as the source of truth.
- The compact render only needs to preserve recognition-level fidelity, not
  styling or pixels.

### Why use a rendered image

- A bounds-based image gives the vision model a closer match to visible layout
  than a prose summary or a raw text tree.
- This lets evaluation focus on whether the compacted output still conveys the
  right screen, state, and actionable elements.

## Verification

- Port current `UiSnapshotCompactor` test coverage to the new typed compaction
  engine.
- Add compact-tool contract tests for:
  - `budgetHintChars`
  - `includeImage`
  - image metadata fields
  - compact JSON shape
- Add renderer tests to verify:
  - nonblank output
  - expected canvas sizing
  - bounded nodes are drawn inside the canvas
  - missing-bounds fallback rendering works
- Add brain integration tests that verify:
  - model-facing UI context uses the compact cognition tools
  - fallback to raw/local behavior still works during migration
  - image paths are picked up as attachments when returned
- Add non-gating benchmark fixtures using saved large snapshots to compare:
  - old brain compaction time
  - new cognition compact-tool time
  - output size
  - kept-node counts
- Run manual evaluation passes on at least:
  - a browser chrome-heavy window
  - a deep content page
  - a focused-control subtree

## Rollout

1. Add the typed compaction engine and compact response model.
2. Add `describe_window_compact` and `describe_window_focus_compact`.
3. Add compact-tree image rendering behind `includeImage`.
4. Switch `brain` to use the compact tools for normal model-facing UI context.
5. Add the opt-in screenshot-vs-compact evaluation harness.
6. Run parity checks, benchmarks, and manual evals.
7. Remove the old local `UiSnapshotCompactor` once the new path is proven.

## Assumptions

- Compact tools are the new model-facing path, while raw tools remain available
  for debugging.
- `brain` remains responsible for model selection and for any LLM-based
  evaluation.
- The rendered compact-tree image is a semantic recognition artifact, not a
  targetable UI surface and not a screenshot substitute for ordinary user
  interaction.
- The opt-in evaluation flow is diagnostic and should not become a required
  runtime dependency for normal cognition calls.
