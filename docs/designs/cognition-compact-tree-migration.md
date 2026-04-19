# Cognition Compact Tree Migration

Last updated: 2026-04-18
Status: in progress

## Summary

This design moves UI snapshot compaction from `brain` into the `cognition`
MCP server, improves compaction speed by operating on typed snapshot data
instead of parsing serialized JSON, and adds an opt-in evaluation flow that
checks whether a rendered compact-tree image still matches the real visible
screen.

The plan keeps the existing raw inspection tools for debugging and manual
inspection, then adds new compact inspection tools that are intended to become
the model-facing path used by `brain`. The compact response now carries two
tree-shaped views over the same retained nodes: a richer `compactTree` for
runtime/debugging and a slim `llmTree` projection that `brain` can pass to the
LLM.

## Remaining Work

The runtime migration is landed, but the rollout is still open until these
follow-ups are done:

- add the opt-in screenshot-vs-compact evaluation harness
- run and document parity checks, benchmarks, and manual evaluation passes

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
- `describe_window_focus*` snapshots intentionally have different `path` and
  `uiPath` values: `path` is rooted at the focused subtree while `uiPath`
  remains rooted at the actual window, which makes `uiPath` the safer
  model-facing identifier for later execution.

## Current Runtime Decision Flow

The compact-vs-raw decision is currently made inside `brain`, not inside
`cognition`.

Today the normal post-action path is still raw-first:

```text
Desktop action tool runs
    |
    v
Take raw post-action snapshot
    brain -> describe_window
    |
    v
RefreshCompactWindowContextAsync(rawSnapshot)
    |
    +--> immediately seed currentUiElementContext with rawSnapshot
    |
    +--> compact tool available?
         |
         +--> no
         |     -> keep raw full tree as model-facing context
         |
         +--> yes
               -> call describe_window_compact
                    |
                    +--> success
                    |     -> RememberCompactWindowContext(compactJson)
                    |     -> extract llmTree
                    |     -> overwrite currentUiElementContext with compact
                    |        model-facing context
                    |
                    +--> failure
                          -> leave raw full tree in currentUiElementContext
```

The focus path follows the same pattern:

```text
Take raw post-action focus snapshot
    brain -> describe_window_focus
    |
    v
RefreshCompactFocusContextAsync(rawFocusSnapshot)
    |
    +--> seed currentFocusElementContext with raw focus snapshot
    +--> try describe_window_focus_compact
         |
         +--> success -> store llmTree-derived compact focus context
         +--> failure -> keep raw focus snapshot
```

This means the current runtime behavior is:

- raw first
- compact second
- compact only wins if the compact tool succeeds

## Current Decision Points

The important runtime decisions currently look like this:

1. Tool availability
   - `brain` checks whether compact tools are present before trying to use
     them.
   - This is a runtime capability check, not a `cognition`-owned routing
     decision.

2. Current-window targeting
   - `brain` decides whether a tool should inherit the current selected window.
   - `describe_window_compact` and `describe_window_focus_compact` are treated
     as current-window tools, so `brain` injects `windowHandle` from the active
     desktop session when needed.

3. Stored model-facing context
   - `brain` keeps `currentUiElementContext` and `currentFocusElementContext`
     as the reusable model-facing UI context.
   - Those slots may hold either:
     - raw full-tree context, or
     - compact `llmTree`-derived context
   - The stored value depends on whether the compact refresh succeeded.

4. Tool-result routing back to the model
   - For compact tools, `brain` extracts and returns `llmTree`.
   - For many desktop-action and raw-window tool results, `brain` prefers the
     stored UI context instead of the tool's raw text.
   - Because the stored UI context is seeded with raw data before compact
     refresh succeeds, later turns can still receive the raw full tree.

## Why The Raw Full Tree Can Still Reach The Model

The key gap is that `RefreshCompactWindowContextAsync(...)` and
`RefreshCompactFocusContextAsync(...)` currently seed the stored model-facing
context with the raw fallback snapshot before they attempt the compact tool.

So the effective behavior is:

```text
raw snapshot arrives
    -> store raw snapshot as current model context
    -> try compact tool
         -> success: overwrite with llmTree-based context
         -> failure: raw snapshot remains active
```

That is why a failed compact call can still leave a huge `describe_window`
payload as the model-facing context for the next LLM turn.

## Target Runtime Decision Flow

The intended migration end state is compact-first, not raw-first:

```text
Desktop action tool runs
    |
    v
Take compact post-action snapshot first
    brain -> describe_window_compact
    |
    +--> success
    |     -> RememberCompactWindowContext(compactJson)
    |     -> extract llmTree
    |     -> currentUiElementContext = compact model-facing context
    |
    +--> failure
          -> optionally take raw describe_window
          -> use raw output only as fallback/debug context
```

And for focused-element follow-up:

```text
Take compact focus snapshot first
    brain -> describe_window_focus_compact
    |
    +--> success
    |     -> currentFocusElementContext = llmTree-based compact context
    |
    +--> failure
          -> optionally fall back to raw describe_window_focus
```

The target behavior should therefore be:

- compact first
- raw only when compact is unavailable or fails
- `llmTree` as the default model-facing UI context
- raw trees reserved for debugging, parity checks, and explicit fallback

## Current Gap Versus Design Intent

The design intent of this migration is that `brain` should switch ordinary
model-facing UI context to compact tools and pass `llmTree` to the model.

That intent is not fully realized yet because the runtime still does all of
the following in the normal post-action path:

- calls raw `describe_window`
- stores that raw full-tree snapshot in the reusable UI context slot
- only then tries `describe_window_compact`
- leaves the raw tree in place if the compact call fails

So the migration is partially landed in code and tool surface, but the
post-action routing order in `brain` is still backwards for the compact-first
goal.

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
  - `llmTree`
  - optional rendered image metadata when image rendering is requested
- `sourceStats` includes:
  - `sourceNodeCount`
  - `keptNodeCount`
  - `omittedNodeCount`
  - `algorithmVersion`

### Parameters

- Add `includeImage` to the new compact tools.
  - default `false`
  - when `true`, the tool emits a rendered compact-tree image plus image path
    metadata in the response

### Tree roles

- `compactTree` is the retained runtime/debug tree.
  - keep rich fields needed for rendering, debugging, and action-time
    validation
  - preserve both `path` and `uiPath`
- `llmTree` is a projection of `compactTree`, not a second independent
  compaction pass.
  - use `uiPath` as the stable model-facing identifier
  - omit fields that mostly matter for diagnostics rather than model reasoning
  - stay tree-shaped so the model still sees containment and nearby context
- The two trees must refer to the same retained node set so model choices can
  be resolved against the richer compact response without ambiguity.

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
- Keep ancestor chains automatically so deep retained nodes remain
  understandable without extra backtracking passes.
- Serialize compact responses without indentation to reduce payload size and
  avoid wasted bytes.

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
- `llmTree` is derived from the retained `compactTree` nodes and should default
  to:
  - `uiPath`
  - `controlType`
  - `name`
  - `availableActions`
  - `children`
- `llmTree` may also include small, high-signal extras when present:
  - compact state flags such as focused, selected, focusable, or offscreen
  - `automationId` only when `name` is empty or unusually weak
  - omission metadata such as `omittedChildren` when it helps the model reason
    about truncated local context
- `llmTree` should not include `path` by default because `path` is relative in
  focus snapshots and can point at a different root than the later execution
  tools expect.

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
- Cache the compact JSON result in the existing UI/focus context slots.
- Send `llmTree` to the model for ordinary UI reasoning, while keeping
  `compactTree` available for validation, rendering, and debug shadow work.
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
  - presence and shape of `llmTree`
  - `includeImage`
  - image metadata fields
  - compact JSON shape
- Add projection tests to verify:
  - `llmTree` uses `uiPath`
  - focus snapshots do not leak relative `path` values into model-facing nodes
  - `llmTree` stays aligned with the retained `compactTree` node set
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
3. Add `llmTree` as a projection of the retained compact tree.
4. Add compact-tree image rendering behind `includeImage`.
5. Switch `brain` to use the compact tools for normal model-facing UI context.
6. Add the opt-in screenshot-vs-compact evaluation harness.
7. Run parity checks, benchmarks, and manual evals.
8. Remove the old local `UiSnapshotCompactor` once the new path is proven.

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
