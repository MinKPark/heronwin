# Compact Window Inventory Plan

Last updated: `2026-04-25`
Status: proposed

## Summary

This design proposes a compact model-facing window inventory for startup-phase
decisions such as:

- use the current app surface
- select an already-running window by exact handle
- launch a new app because no suitable window is present

The goal is to remove the dedicated turn-opening `list_windows` LLM/tool round
trip when the runtime does not yet trust the current target surface, while
avoiding the cost and drift risk of passing the raw full desktop window list on
every later LLM call.

This is intentionally different from "always attach `list_windows`." The plan
is:

1. collect or reuse startup inventory only when the runtime lacks a trusted
   current surface
2. inject a compact startup inventory into the first relevant LLM request
3. let the first reply choose `use current`, `activate_window`, or
   `launch_application`
4. return to current-surface evidence after startup is resolved

## Problem

On the April 25, 2026 scripted Netflix reruns, turn `1` still spent its first
LLM attempt only asking for `list_windows`, then spent the second attempt
choosing `activate_window`.

That means the runtime already knows the startup-phase question, but the model
still has to burn one whole response to gather the desktop inventory before it
can answer it.

This startup pattern is generic across apps:

- first determine whether the target app is already usable
- then either stay on the current window, activate an existing one, or launch
  the app
- only after that move deeper into app-specific navigation

The startup skill already expresses that playbook in
`.github/agents/skills/windows/desktop-launch-and-first-look.skill.md`.

## Non-Goals

- pass raw `list_windows` output on every LLM call
- replace current-surface UI evidence with desktop inventory during later
  app-native actions
- add browser-only runtime branches
- remove the raw `list_windows` tool from the MCP surface
- force startup selection fully into runtime without any model decision

## Current Raw Shape

Today `desktop-automation` returns this raw structure from
`list_windows` in
`src/tools/desktop-automation/WindowAutomation.cs`.

Current records:

- `WindowListResult`
- `WindowSummary`
- `WindowBounds`

Representative shape:

```json
{
  "SelectedWindowHandle": null,
  "Windows": [
    {
      "Handle": "0x00250450",
      "Title": "YouTube - Personal - Microsoft Edge",
      "ClassName": "Chrome_WidgetWin_1",
      "ProcessId": 43600,
      "Bounds": {
        "Left": -1928,
        "Top": -8,
        "Width": 1936,
        "Height": 1048
      },
      "IsSelected": false
    },
    {
      "Handle": "0x00060A88",
      "Title": "Netflix - Microsoft Edge",
      "ClassName": "Chrome_WidgetWin_1",
      "ProcessId": 43601,
      "Bounds": {
        "Left": 0,
        "Top": 0,
        "Width": 1936,
        "Height": 1048
      },
      "IsSelected": false
    }
  ]
}
```

That raw shape is useful for debugging and low-level runtime decisions, but it
is heavier than necessary for the model's startup choice. The model usually
needs:

- which window is currently selected, if any
- which visible windows are candidates
- their exact handles
- human-meaningful titles
- a small amount of selection or quality metadata

It rarely needs:

- full raw bounds on every row
- every process id
- every off-target visible window during later app-native phases

## Goals

- Reduce one full scripted startup LLM round trip when no trusted current
  surface exists.
- Keep the design generic across apps and surface types.
- Reuse the same "compact model-facing view, richer runtime/debug view" idea
  used by `llmTree`.
- Preserve raw `list_windows` output for debugging, tests, and fallback logic.
- Keep later app-native attempts focused on current-surface evidence instead of
  repeated whole-desktop inventory.

## Proposed Design

## 1. Add A Compact Window Inventory View

Introduce a model-facing compact projection derived from raw `list_windows`
data.

Recommended first-slice shape:

```json
{
  "selectedWindowHandle": null,
  "windowCount": 6,
  "omittedWindowCount": 0,
  "windows": [
    {
      "handle": "0x000403D6",
      "title": "(89) YouTube - Personal - Microsoft Edge",
      "className": "Chrome_WidgetWin_1",
      "isSelected": false,
      "hasUsableBounds": true,
      "screenHint": "left"
    },
    {
      "handle": "0x000901FC",
      "title": "heronwin - Visual Studio Code",
      "className": "Chrome_WidgetWin_1",
      "isSelected": false,
      "hasUsableBounds": true,
      "screenHint": "center"
    }
  ]
}
```

### Why These Fields

- `handle`
  - the model must be able to choose an exact `activate_window` target
- `title`
  - primary human-meaningful signal for startup decisions
- `className`
  - often useful for distinguishing browser hosts or app frames
- `isSelected`
  - tells the model whether "use current app" is even plausible
- `hasUsableBounds`
  - cheaper than raw geometry while still letting runtime suppress junk rows
- `screenHint`
  - retains coarse location value without paying for full bounds

### Fields To Omit From The Model View By Default

- raw `ProcessId`
- raw `Bounds`
- rows with unusable geometry unless needed for debugging

The raw data can still remain in runtime state for local heuristics and traces.

## 2. Keep Raw And Compact Views Together

Follow the same architectural split used by the UI tree migration:

- raw view remains available to runtime and debugging
- compact view is what `brain` injects into model context by default

For the first slice, derive the compact inventory in `brain` from existing raw
`list_windows` output rather than changing the MCP tool contract immediately.

Reason:

- lower implementation risk
- no connector or tool-surface churn
- easier before/after comparison
- compact schema can still evolve before being promoted to the producer

If the compact view proves stable and broadly useful, a later phase can promote
it into `desktop-automation` so the producer returns both:

- richer raw/debug inventory
- slim model-facing inventory

## 3. Use Compact Inventory Only At Startup Boundaries

Do not attach startup inventory on every LLM request.

Instead, inject it only when all of the following are true:

- the runtime does not currently trust the active target surface
- the turn is at its startup or reacquisition phase
- the runtime has no fresher current-surface evidence that already answers the
  startup question

Skip compact inventory injection when:

- a trusted carry-forward surface already exists
- a fresh post-action current window snapshot is already present
- the turn is already deep inside same-surface app-native work

This keeps token growth controlled and avoids reintroducing desktop-level drift
into later phases.

## 4. Make Startup Inventory Runtime-Owned But Model-Decided

The runtime should own the collection step:

1. if no trusted surface exists, call `list_windows`
2. store raw inventory plus metadata
3. derive compact inventory
4. inject it into the first startup-phase LLM request

The model should still decide among:

- use current app surface
- activate this exact known handle
- launch this app

This keeps the primitive generic and avoids hardcoding app selection policy
into runtime.

## 5. Return To Current-Surface Evidence Immediately After Startup

Once startup resolves to a concrete selected or launched window:

- refresh the current window snapshot
- make that current-surface snapshot the source of truth
- stop passing desktop-wide inventory unless startup certainty breaks again

Expected startup path after this change:

```text
No trusted surface
    |
    v
Runtime collects window inventory
    |
    v
LLM sees compact inventory in first request
    |
    +--> use current app
    +--> activate existing handle
    +--> launch application
    |
    v
Runtime refreshes selected/created current surface
    |
    v
Later LLM steps use only current-surface evidence
```

## Proposed Injection Text

Recommended startup message shape:

```text
Startup window inventory: no trusted current target surface is established yet.
Use this compact inventory to decide whether to stay on the current app,
activate an already-running window by exact handle, or launch the requested
app. Prefer exact windowHandle values when selecting an existing window.
```

Then attach the compact JSON summary in a separate user-context message.

## Data Model And Session State

Extend `DesktopSessionContext` with inventory state parallel to the current
window snapshot state.

Suggested additions:

- `RecentWindowInventoryRaw`
- `RecentWindowInventoryCompact`
- `RecentWindowInventoryMetadata`

Suggested metadata:

- source turn id
- capture timestamp
- whether the inventory came from an explicit startup collection or a fallback
  reacquisition
- window count
- omitted window count

This lets later turns decide whether a still-fresh inventory can be reused for
startup without calling `list_windows` again.

## Ranking And Compaction Rules

The first slice should stay conservative and mostly lossless.

Recommended rules:

- keep only visible windows with non-empty titles
- preserve exact handle and title
- preserve class name
- compute `hasUsableBounds` using the same general rule already used in
  `Conversation.cs`
- replace full bounds with a coarse `screenHint`
- sort selected window first, then usable windows, then title
- cap model-facing rows to a configurable maximum such as `8` or `10`
- include `windowCount` and `omittedWindowCount` so the model knows whether the
  list is partial

Do not do request-specific app ranking in the first slice.

Reason:

- startup compaction should remain generic
- exact handle plus title is already enough for the model to choose
- request-aware ranking can be added later if repeated traces justify it

## Interaction With Existing Carry-Forward Work

This proposal complements, not replaces, cross-turn carry-forward.

Preferred order at turn start:

1. if trusted carry-forward current-surface evidence exists, use it
2. otherwise, if startup inventory is fresh and relevant, inject compact
   startup inventory
3. otherwise, recollect startup inventory with `list_windows`

This keeps current-surface evidence as the highest-priority source of truth,
with compact inventory as the fallback for startup uncertainty.

## Tracing

Add explicit trace events for startup inventory behavior.

Recommended events:

- `agent.turn.startup_inventory_used`
- `agent.turn.startup_inventory_skipped`
- `agent.turn.startup_inventory_refreshed`
- `agent.turn.startup_inventory_reused`
- `agent.turn.startup_inventory_compacted`

Recommended fields:

- `turn`
- `sourceTurn`
- `inventoryAgeMs`
- `windowCount`
- `omittedWindowCount`
- `selectedWindowHandle`
- `reason`

This should make it easy to answer later:

- did the runtime avoid the opening `list_windows` round trip?
- was startup inventory injected from a fresh source or recollected?
- how much prompt weight did the compact inventory add?

## Tests

Add focused coverage for:

- compact inventory derivation from raw `list_windows` JSON
- stable ordering and omission behavior
- startup inventory injection only when no trusted target surface exists
- startup inventory skip when carry-forward current-surface evidence is still
  valid
- exact-handle startup selection after compact injection
- trace fields for used, skipped, refreshed, and reused decisions

Likely test homes:

- `src/head/brain.tests/AgentRunnerDecisionTests.cs`
- `src/head/brain.tests/AgentRunnerContinuationTests.cs`
- trace-report tests if new helper buckets or trace categories need reporting

## Rollout Plan

### Phase A: Brain-Local Compact Projection

- derive compact inventory in `brain` from raw `list_windows`
- inject only at startup boundaries
- keep raw MCP tool output unchanged
- instrument and compare against the current turn `1` baseline

Success target:

- turn `1` startup should collapse from
  `list_windows -> activate_window -> navigate -> confirm`
  toward
  `activate_window or launch -> navigate -> confirm`

### Phase B: Session Reuse

- store compact inventory plus metadata in `DesktopSessionContext`
- reuse fresh startup inventory across nearby scripted turns when needed
- avoid recollecting unchanged inventory unnecessarily

### Phase C: Producer Promotion

- if the compact shape proves stable, consider emitting it directly from
  `desktop-automation` alongside the raw view
- keep the raw view for debug mode and runtime-only heuristics

## Risks

- Risk: compact inventory removes a field the model occasionally needs
  - Mitigation: keep the first slice mostly lossless and retain raw fallback
- Risk: startup inventory leaks too many unrelated windows into later prompts
  - Mitigation: inject only at startup boundaries, not every request
- Risk: request-specific ranking sneaks app policy into runtime
  - Mitigation: keep first-slice ordering generic
- Risk: stale startup inventory causes wrong activation decisions
  - Mitigation: attach freshness metadata and reuse conservatively
- Risk: token savings are smaller than expected
  - Mitigation: trace prompt-token impact and compare startup-attempt counts,
    not just wall clock

## Open Questions

- Should `screenHint` be a coarse label such as `left`, `center`, `right`, or
  should it be omitted entirely in the first slice?
- Do we want the compact inventory cap to be a hard row limit or a soft limit
  that preserves all likely foreground app windows?
- Should startup inventory reuse remain scripted-only at first, or should the
  primitive be available to interactive text mode too once stable?
- After Phase A, does the compact shape deserve promotion into
  `desktop-automation`, or is brain-local projection enough?

## Recommendation

Proceed with Phase A first.

That gives us the main benefit:

- remove the startup `list_windows` LLM round trip

without prematurely changing the MCP contract or pushing whole-desktop
inventory into every prompt.
