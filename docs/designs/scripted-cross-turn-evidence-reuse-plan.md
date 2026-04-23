# Scripted Cross-Turn Evidence Reuse Plan

Last updated: 2026-04-22
Status: proposed
Depends on:
- `docs/designs/netflix-smoke-runtime-performance-plan.md`
- `docs/perfbase/2026-04-22-netflix-smoke-baseline.md`
- `.tmp/netflix-smoke-runtime/2026-04-22-baseline/trace-report.md`
- `src/head/brain/Conversation.cs`
- `src/head/brain/DesktopSessionContext.cs`
- `src/head/brain/TurnProcessor.cs`

## Summary

The first Netflix smoke trace report shows a repeatable pattern at the start of
scripted turns `2` through `5`: the runtime pays one full LLM response to ask
for `list_windows` and another full LLM response to ask for `describe_window`
before the turn reaches the first user-progressing action.

That repeated reacquisition cost is large in model time and tiny in tool time:

- first-two-attempt LLM time across turns `2` through `5`: `240.287 s`
- matching `list_windows` tool time across turns `2` through `5`: `0.009 s`
- matching `describe_window` tool time across turns `2` through `5`: `1.553 s`

The waste is therefore not the tool execution itself. The waste is that the
runtime is not reusing evidence it already has strongly enough to let the next
scripted turn start from the current screen.

This plan still starts with one narrow first slice:

- reuse fresh cross-turn desktop evidence to avoid repeated
  `list_windows` + `describe_window` loops at the start of contiguous scripted
  turns

But the discussion behind this slice surfaced a broader runtime gap:

- the runtime does not yet own the scripted turn envelope strongly enough

The target direction for this slice is therefore:

- before the first LLM attempt of a scripted turn, brain should ensure the
  target surface or window is foregrounded and provide fresh current UI
  evidence
- the LLM should start from "the target surface is ready" rather than spending
  the opening attempts rediscovering it
- within a stable surface, the LLM may later be allowed to return a bounded
  list of actions instead of a single action
- when a planned action is likely to change the visible layout materially, the
  turn should be split into smaller execution phases that brain runs one at a
  time

This is intentionally separate from:

- fully general same-surface continuation optimization
- model/provider changes

## Current Status

- Status in the 2026-04-22 wrap-up pass:
  - fresh passing Netflix baseline captured and saved
  - repo-native trace report landed and verified with focused tests
  - first-slice design aligned against code, trace data, and explicit
    assumptions
  - no behavior-changing runtime implementation has started yet
- Exact first step for the next session:
  - inspect existing coverage around turn-start state in
    `Conversation.RunTurnAsync`, add the planned ready-state and carry-forward
    logging fields, then implement conservative scripted turn-start reuse and
    rerun the same scenario with a fresh trace report for comparison

## Goals

- Let the next scripted turn start from a runtime-owned ready state:
  target surface or window foregrounded and current-screen evidence available.
- Reuse fresh carry-forward UI evidence when the previous turn already ended
  with a trustworthy current-screen snapshot.
- Remove at least one avoidable LLM/tool discovery pair from turns `2` through
  `5` in the Netflix smoke baseline pattern.
- Keep the change app agnostic and framed in generic desktop/browser evidence
  terms.
- Preserve correctness by falling back to discovery whenever carry-forward
  evidence is missing, stale, ambiguous, or contradicted.
- Leave behind trace events and tests that explain when reuse happened and when
  it was skipped.
- Define how later slices should split a scripted turn into smaller phases when
  the UI layout is likely to change.

## Non-Goals

- Suppressing all `list_windows` or `describe_window` calls.
- Adding Netflix-specific shortcuts.
- Building the full phased multi-action executor in the first patch.
- Turning carry-forward evidence into a blind assumption with no fallback.

## Current Runtime State

The code already carries useful desktop state across turns:

- `DesktopSessionContext` persists:
  - `CurrentWindowHandle`
  - `CurrentWindowTitle`
  - `RecentListWindowsOutput`
  - `RecentWindowContext`
  - `RecentUiTreeContext`
  - `RecentFocusContext`
  - `CurrentUiElementContext`
  - `CurrentFocusElementContext`
- `Conversation.cs` restores those fields into local variables at the start of
  `AgentRunner.RunTurnAsync`.
- `RememberRecentWindowSnapshot(...)` and `RememberRecentFocusSnapshot(...)`
  keep that state updated after successful tools and post-action snapshots.
- `PrepareToolArgumentsForDesktopSession(...)` already reuses
  `CurrentWindowHandle` for many tool calls.

The main gap is not storage. The main gap is how the next turn starts:

- `TurnProcessor.cs` persists only the original user text and the final
  assistant raw reply into cross-turn `history`.
- Turn-local tool results and fresh post-action evidence are not added to the
  next turn's persisted history.
- `AgentRunner.RunTurnAsync(...)` starts the next turn with `history` plus the
  new user command, but it does not inject an explicit carry-forward current UI
  evidence message before the first LLM request.

That means the runtime knows the likely current window and UI tree, but the
model still begins the next scripted turn by rediscovering them.

There is also a second structural gap:

- the runtime does not currently make "the target surface or window is
  foregrounded and the current UI surface is known" a strong precondition of
  the first scripted LLM attempt

In practice this leaves the model to spend its own first attempts asking for
window selection and UI description, even though brain already tracks the
current target window and already knows how to capture authoritative snapshots.

## Baseline Evidence

From `.tmp/netflix-smoke-runtime/2026-04-22-baseline/trace-report.md`:

| Turn | Attempt 1 | Attempt 2 | First user-progressing action | First-two-attempt LLM time |
| --- | --- | --- | --- | ---: |
| 2 | `list_windows` after `36.492 s` | `describe_window` after `23.054 s` | attempt `3`: `invoke_window_element` | `59.546 s` |
| 3 | `list_windows` after `31.050 s` | `describe_window` after `33.596 s` | attempt `3`: `set_window_element_text` | `64.646 s` |
| 4 | `list_windows` after `38.673 s` | `describe_window` after `15.382 s` | attempt `3`: `click_window_element` | `54.055 s` |
| 5 | `list_windows` after `40.199 s` | `describe_window` after `21.841 s` | attempt `3`: `invoke_window_element` | `62.040 s` |

Total first-two-attempt LLM time across these turns: `240.287 s`

The prior turn had already ended with fresh evidence in each of these cases,
but that evidence was not surfaced strongly enough to prevent rediscovery.

## Hypothesis

If the runtime owns scripted turn start more aggressively, the model can often
skip one or both of these opening discovery attempts:

- `list_windows`
- `describe_window`

That means the first LLM attempt of a scripted turn should begin after brain
has already done the safest runtime-owned setup work:

- make sure the target surface or window is foregrounded when current state
  supports doing so
- capture or reuse current-screen evidence conservatively
- hand the model the current UI tree as the initial surface for planning

This should be a lower-risk first slice than hard-blocking or rewriting those
tool calls, because it preserves the model's ability to ask for fresh discovery
when it truly needs it.

## Design Direction

The desired long-term flow for scripted turns is:

1. Brain establishes turn-start readiness.
2. Brain provides fresh initial UI evidence with the scripted turn prompt.
3. The LLM decides whether the turn should execute as one stable phase or as
   multiple smaller phases.
4. Brain executes one phase at a time.
5. Brain gathers fresh post-phase evidence and asks the LLM to confirm expected
   state transition, continue, or repair.

### Runtime-Owned Turn Start

Before the first scripted LLM attempt, brain should:

- prefer the known target window and bring it to the foreground when that can
  be done safely
- capture or reuse the initial actionable UI tree for that foregrounded surface
- include that current-screen evidence directly in the first model request

The model should then be able to assume the intended surface is already the
active surface it is working against.

### Phase Planning

The first scripted LLM response should not only choose actions. It should also
classify whether the requested work should stay in one stable phase or be split
into multiple smaller phases.

Split the turn when the next action is likely to produce a materially different
UI layout or surface, such as:

- submitting a search or navigating to a new page
- opening a details page or modal
- switching windows, tabs, or apps
- entering playback or another mode with a different control layout
- triggering a focus-mode change that invalidates the current element tree

Keep the work in one phase when the surface is still effectively the same and
the next actions are deterministic continuations on that surface.

### Multi-Action Within A Stable Phase

Within a stable phase, the LLM may later be allowed to return a bounded list of
actions rather than only one action. Brain already supports executing multiple
tool calls returned in one response, so the missing piece is the turn contract,
not the basic loop mechanics.

This is an implementation direction, not a current assumption. The baseline
shows that even scenario steps written as "continue until" still resolved into
one-tool-per-attempt behavior, so batched phase execution should be treated as a
separate behavior change to validate rather than something already latent in the
current prompt contract.

This is especially relevant for:

- deterministic text slot completion
- obvious visible choice activation
- short navigation chains on the same validated surface

### Post-Phase Confirmation And Fallback

After a phase completes, brain should gather fresh UI evidence and ask the LLM
to evaluate the actual state transition:

- expected next surface reached and ready for the next phase or next turn
- still on the same surface and safe to continue
- mismatch or ambiguity that requires retry or repair

When mismatch or ambiguity appears, runtime should degrade to a more careful
action-by-action path instead of continuing to batch actions optimistically.

## Recommended First Slice

Recommendation: start with runtime-owned turn-start readiness plus prompt-side
carry-forward evidence injection, not tool suppression.

Why this first:

- it is generic
- it builds on state we already store
- it gives the model a better starting surface without taking away tools
- it is easier to instrument and test than hard-blocking tool calls
- it is the prerequisite for any later phase-based multi-action execution model

### 1. Establish Turn-Start Readiness Before The First LLM Attempt

Before the first LLM request of a new scripted turn, the runtime should decide
whether it can safely treat the current target surface and current UI evidence
as the starting surface.

For the first slice this means:

- if a target window is already known and still safe to reuse, ensure it is the
  active foreground surface before the first model request
- if fresh trustworthy UI evidence is already available from the prior turn,
  reuse it instead of forcing turn-start rediscovery
- if either assumption is weak or contradicted, fall back to normal discovery

This should be traced explicitly so later reruns show whether the runtime began
the turn in ready state or had to reacquire it.

### 2. Inject Carry-Forward Evidence At Scripted Turn Start

Before the first LLM request of a new scripted turn, if the runtime has
trustworthy cross-turn desktop evidence, add one or more explicit messages that
say in effect:

- the previous turn ended with a fresh current-screen snapshot
- this is the current source of truth unless contradicted
- use this evidence before deciding whether fresh discovery is necessary

Recommended evidence to inject:

- compact current UI tree from the actionable window context
- current window summary
- focused element context if it exists and is still relevant

Prefer compact evidence over raw full snapshot text when possible, to avoid
adding more prompt weight than necessary.

The first response contract should evolve toward:

- here is the current surface
- decide whether this work should stay in one stable phase or split into
  multiple smaller phases
- return the first phase's actions only

The first patch does not need to fully implement phase execution, but the
prompt and trace language should avoid painting us into a single-action-only
corner. The first patch also should not assume that batched actions will land
cleanly without their own targeted validation.

### 3. Add Freshness And Provenance To Desktop Session State

The current session object stores the evidence itself, but not enough metadata
to make a conservative reuse decision.

Add metadata such as:

- source turn id
- source tool or source phase
- capture timestamp
- whether the evidence came from an explicit post-action snapshot

This metadata should support a helper that answers:

- is the stored evidence fresh enough to offer cross-turn reuse?
- if not, why not?

### 4. Make Reuse Conservative

Carry-forward evidence should be offered only when all of the following are
true:

- we are in scripted mode for the initial slice
- current target window identity is known or can be safely reactivated
- current window information is known
- the stored window snapshot contains a usable element tree
- the prior evidence came from a trustworthy source such as
  `describe_window` or a successful post-action snapshot
- the evidence is fresh enough according to a conservative policy

Reuse should be skipped when any of the following are true:

- no stored snapshot exists
- the stored snapshot has no element tree
- the prior capture failed or was ambiguous
- the evidence is older than the chosen freshness window
- the current turn explicitly requests broader discovery that should override
  carry-forward assumptions

The exact freshness window does not need to be finalized in this doc, but the
first implementation should choose a conservative value and trace the decision.

### 5. Instrument The Decision

Add explicit trace categories so the reuse behavior can be audited in later
reports.

Recommended events:

- `agent.turn.ready_state_used`
- `agent.turn.ready_state_skipped`
- `agent.turn.carry_forward_evidence_used`
- `agent.turn.carry_forward_evidence_skipped`
- `agent.turn.carry_forward_evidence_invalidated`
- `agent.turn.phase_split_planned`
- `agent.turn.phase_split_skipped`

Recommended fields:

- `turn`
- `sourceTurn`
- `window`
- `evidenceAgeMs`
- `sourceKind`
- `reason`

### 6. Improve Logging For Follow-Up Analysis

The first slice should leave behind trace data that makes the next rerun easy
to interpret. The goal is not more noise. The goal is to answer, from one
report, whether turn-start reuse actually happened, whether helper work moved
elsewhere, and whether prompt growth still dominates later attempts.

Highest-value logging upgrades for this slice:

- Add explicit turn-start ready-state events:
  - `agent.turn.ready_state_used`
  - `agent.turn.ready_state_skipped`
- Add explicit evidence provenance events:
  - `agent.turn.carry_forward_evidence_used`
  - `agent.turn.carry_forward_evidence_invalidated`
- Add `elapsedMs` to automatic preflight and helper events that currently only
  log count or result shape.
- Add a trigger or source field to requested and completed tool calls so later
  reports can distinguish LLM-requested work from runtime-owned helper work.
- Add estimated prompt size to `llm.request` so later reports can correlate
  latency growth with context growth.
- Add provider response metadata to `llm.response` when it is available, such
  as finish reason or token usage.

Recommended fields for ready-state and carry-forward events:

- `turn`
- `sourceTurn`
- `windowHandle`
- `windowTitle`
- `evidenceAgeMs`
- `snapshotSource`
- `hadUiTree`
- `hadFocusContext`
- `activationAttempted`
- `activationSucceeded`
- `skipReason`

Recommended fields for tool execution trace enrichment:

- `triggerKind` with values such as:
  - `llm`
  - `turn_ready_state`
  - `browser_preflight`
  - `post_action_followup`
  - `internal_continuation`
  - `repair`
- `phase` with values such as:
  - `turn_start`
  - `main_phase`
  - `post_phase_confirmation`
- `usedStoredWindowHandle`

Recommended preflight and helper timing upgrades:

- `agent.browser_window_preflight_list_windows`
- `agent.browser_window_preflight_activate_window`
- `agent.activate_window_preflight_list_windows`
- any new turn-start ready-state activation event added by this slice

These should record `elapsedMs` consistently so the trace report can separate:

- LLM-chosen discovery
- runtime-owned turn-start setup
- browser-specific helper work
- post-action follow-up work

Recommended prompt and model metadata upgrades:

- add an estimated total prompt token count to `llm.request`
- preserve `messageCount` and `systemPromptChars`
- add response-side usage or finish metadata to `llm.response` when the
  provider exposes it

Later phase-based slices should add:

- `agent.turn.phase_planned`
- `agent.turn.phase_started`
- `agent.turn.phase_completed`
- `agent.turn.phase_repair_requested`

with fields such as:

- `phaseIndex`
- `expectedSurface`
- `actualSurface`
- `plannedToolCount`
- `executedToolCount`
- `stopReason`

These phase events are not required for the first patch, but the first patch
should avoid choosing event names or field shapes that would block them later.

## Deliberately Deferred For Later

This plan does not recommend the following in the first slice:

- hard-blocking initial `list_windows` or `describe_window`
- silently rewriting redundant discovery tool calls
- applying the same behavior to interactive voice or text turns
- full multi-phase execution with post-phase repair loops
- generalized batching for all same-surface action sequences
- packaging the final pattern as a reusable skill before the runtime contract
  has stabilized

Those can be considered later if the prompt-side reuse slice lands cleanly but
the model still asks for redundant discovery anyway.

## Likely Code Touchpoints

- `src/head/brain/DesktopSessionContext.cs`
  - add freshness, provenance, and ready-state metadata
- `src/head/brain/Conversation.cs`
  - establish turn-start readiness before the first scripted LLM attempt
  - build and inject carry-forward evidence at turn start
  - trace whether ready state and reuse were used or skipped
  - add the freshness and invalidation decision helpers
- `src/head/brain/DebugTrace.cs`
  - enrich `llm.request` and `llm.response`
  - support the new ready-state, provenance, and trigger fields cleanly
- `src/head/brain/LlmClients.cs`
  - surface provider-side response metadata when available
- `src/head/brain/OpenAiCodexCliClient.cs`
  - surface CLI-side response metadata if any stable signal is available
- `src/head/brain/ScenarioTesting.cs`
  - extend the trace report to bucket turn-start setup and helper work
- `src/head/brain/TurnProcessor.cs`
  - thread enough context so the reuse logic can remain scripted-only for the
    first slice, if needed
- `src/head/brain.tests`
  - focused tests for ready-state eligibility, injection, and fallback

## Test Plan

Before changing behavior, add tests that cover at least:

1. turn-start ready state is used when a fresh scripted target window and UI
   snapshot already exist
2. carry-forward evidence is offered when fresh scripted desktop evidence exists
3. carry-forward evidence is skipped when no stored snapshot exists
4. carry-forward evidence is skipped when the stored snapshot lacks an element
   tree
5. carry-forward evidence is skipped when freshness metadata says it is stale
6. interactive mode behavior is unchanged by the scripted-only first slice
7. trace events record the ready-state and reuse use or skip reason
8. the first-slice prompt contract remains compatible with later
   phase-splitting work
9. preflight and helper events that should be timed now emit `elapsedMs`
10. `llm.request` records the new prompt-size estimate field
11. trace-report output can distinguish LLM discovery cost from runtime-owned
    turn-start setup cost

Good initial test homes:

- `src/head/brain.tests/ScriptedModeTests.cs`
- `src/head/brain.tests/AgentRunnerDecisionTests.cs`
- `src/head/brain.tests/TraceReportTests.cs`
- targeted trace-report or conversation-state tests as needed

## Success Metrics

This slice is successful when all of the following are true on a fresh passing
rerun:

- turns that already ended on a trustworthy target surface can begin with a
  runtime-owned ready state instead of defaulting to discovery
- total turn-start discovery cost drops materially instead of merely moving the
  same work into automatic runtime preflight
- turns `2` through `5` no longer all begin with the same
  `list_windows` then `describe_window` pattern, or equivalent automatic
  rediscovery cost
- total LLM time drops materially from the baseline
- first-two-attempt LLM time for turns `2` through `5` drops by at least
  `120.000 s`
- the scenario still passes without weakening existing assertions
- the trace clearly records when carry-forward reuse was used or skipped
- the trace report can show whether discovery time actually went away rather
  than merely shifting into helper buckets

## Risks And Mitigations

- Risk: we foreground the wrong target window or trust stale evidence.
  - Mitigation: conservative freshness rules, explicit invalidation, and easy
    fallback to normal discovery.
- Risk: injecting more evidence text makes later model calls slower.
  - Mitigation: prefer compact UI context over raw full snapshots and trace the
    prompt-side effect through before/after reruns.
- Risk: the model still asks for discovery even after receiving carry-forward
  evidence.
  - Mitigation: treat this first slice as prompt-first. If redundancy remains,
    evaluate explicit tool suppression as a later slice with better evidence.
- Risk: the future multi-action path batches across a UI transition boundary and
  overshoots the correct surface.
  - Mitigation: only batch within stable phases, split on likely layout change,
    and degrade to stepwise repair on mismatch.
- Risk: this helps but still does not move enough wall-clock time for the P0
  target.
  - Mitigation: keep the scope narrow, measure the gain, then decide whether
    same-surface continuation or model latency work should be next.

## Review Questions

- Should the first slice stay scripted-only, or do we want the carry-forward
  evidence model for all contiguous desktop turns?
- Do we want to inject only compact UI tree evidence first, or include focused
  element evidence at the same time?
- Should the future planner surface call these smaller execution units
  `phases` consistently in trace and prompt language?
- Do we want the first LLM reply to classify phase splitting and return the
  first phase's actions in one response, or do we want a separate planner step?
- After the design settles, do we want to capture the runtime-owned scripted
  turn pattern as a reusable Codex skill for future investigations and reviews?
- If the first slice lands but the model still asks for redundant discovery, do
  we prefer prompt tightening first or explicit tool-call suppression next?
