# Generic Continuations and Discrete Entry Plan

Last updated: 2026-04-19
Status: proposed
Depends on:
- `docs/designs/app-agnostic-runtime-and-skills-plan.md`
- `docs/designs/brain-debuggability-and-rewrite-guardrails.md`

## Summary

This plan covers the remaining heavy migration work after the first
app-boundary cleanup:

- replace Netflix-specific internal continuations with generic continuation
  primitives
- replace Netflix-specific structured PIN entry with a generic discrete-slot
  text-entry primitive
- keep app policy in skills while preserving the current Netflix behavior that
  already works

This is the detailed execution plan for the hardest part of
`app-agnostic-runtime-and-skills`.

## Why This Needs Its Own Plan

The remaining Netflix-shaped code is not one isolated helper. It is spread
across three runtime mechanisms in `src/head/brain/Conversation.cs`:

1. post-reply internal follow-through
2. tool-call interception and rewrite of multi-character text entry
3. surface detection and trace emission

That means the migration has to preserve behavior across the full turn loop,
not just move one function to a new file.

## Current Runtime Hotspots

The remaining app-specific debt is concentrated in:

- `MaybeContinueNetflixProfileSelectionAsync(...)`
- `MaybeContinueNetflixPinEntryAsync(...)`
- `TryBuildNetflixProfileSelectionContinuation(...)`
- `TryBuildRemainingNetflixPinDigits(...)`
- `TryBuildNetflixPinContinuation(...)`
- `ShouldRefreshNetflixPinFocusBeforeContinuation(...)`
- `TryExtractStructuredNetflixPinDigits(...)`
- `ExecuteStructuredNetflixPinEntryAsync(...)`
- `SnapshotLooksLikeNetflixPinFocus(...)`
- `SnapshotLooksLikeNetflixPinWindow(...)`
- `TryExtractNetflixPinInputOrdinal(...)`
- `ElementLooksLikeNetflixPinInput(...)`

Related regression coverage already exists in:

- `src/head/brain.tests/AgentRunnerContinuationTests.cs`
- `src/head/brain.tests/AgentRunnerDecisionTests.cs`

This is good news. The behavior is ugly in runtime shape, but it is already
partially pinned down by tests and traces.

## Behavior We Must Preserve

The migration should not throw away the hard-won reliability that these
branches currently provide.

Required behavior to preserve:

- visibility-only turns do not trigger profile activation or PIN entry
- exact named profile selection is allowed when one exact visible target
  matches the user request
- generic profile-picker controls such as `Manage Profiles`, `Add Profile`, or
  `Done` are not substituted for the requested named profile
- stale PIN tree snapshots lose to fresher evidence that shows Netflix home
- if the focused PIN slot ordinal is known, only the remaining digits are
  entered
- if the PIN surface is visible but the focused ordinal is unknown, runtime
  refreshes focus before deciding what remains
- discrete PIN entry happens one character at a time with per-step focus
  verification
- if the agent tries to send a multi-character string into a discrete-slot PIN
  surface, runtime rewrites that into sequential single-character entry
- internal continuation traces remain readable enough to compare before and
  after runs

## Desired End State

After this migration:

- `Conversation.cs` contains no Netflix-specific continuation or PIN helper
  names
- runtime exposes generic primitives for:
  - post-reply named-choice continuation
  - post-reply discrete-slot text continuation
  - sequential discrete-slot text execution
  - generic preflight refresh for stale surface or focus state
- active skills opt into those primitives through generic metadata or
  affordances, not app-name branches in runtime code
- trace events are generic and reusable across apps
- Netflix-specific wording stays in skill files and scenarios
- Netflix remains the pilot app used to prove the generic primitives before
  other apps adopt them

## Non-Goals

- redesign all tool rewrites in one pass
- invent a universal continuation DSL for every future workflow
- remove generic debuggability and rewrite-guardrail infrastructure
- change snapshot formats from `cognition` or `execution`
- ship a temporary abstraction that still carries Netflix-specific behavior
  under renamed wrappers and then leave it there

## Proposed Runtime Primitives

## 1. Visible Named Choice Continuation

Introduce a generic continuation primitive for the pattern:

- the user explicitly asked to activate a named visible choice
- the assistant draft stopped early
- the latest UI snapshot still exposes one exact visible actionable target

Example shape:

- continuation kind: `select_visible_named_choice`
- required target property: one unique visible target whose exact name matches
  the user request and supports the required action
- generic step: `invoke_window_element`

This primitive should not know what a Netflix profile is. Its job is only:

- decide whether the current turn qualifies for named-choice follow-through
- carry generic trace state
- run the one-step action and refresh evidence

The app-specific meaning of that choice remains in skills.

## 2. Discrete-Slot Text Continuation

Introduce a generic continuation primitive for the pattern:

- the user already provided a complete secret or code value
- the current UI is still waiting on a structured multi-slot text gate
- runtime can determine that some suffix remains to be entered

Example shape:

- continuation kind: `enter_remaining_discrete_text`
- generic output:
  - full value length
  - current focused slot ordinal if known
  - remaining character count
  - whether focus refresh was required

This replaces the current Netflix-specific "remaining PIN digits" builder.

## 3. Sequential Discrete-Slot Text Executor

Rename and generalize `ExecuteStructuredNetflixPinEntryAsync(...)` into a
runtime primitive that is useful for PIN, passcode, OTP, verification-code,
and similar discrete-slot flows.

Expected behavior:

- enter one character at a time using `type_window_text`
- verify focus or slot progression after each character when possible
- stop on first deterministic tool failure
- redact sensitive text in display output and traces
- emit generic trace events instead of `agent.netflix_pin_entry.*`

Expected generic trace family:

- `agent.sequential_text_entry_started`
- `agent.sequential_text_entry_step_completed`
- `agent.sequential_text_entry_focus_verified`
- `agent.sequential_text_entry_aborted`
- `agent.sequential_text_entry_completed`

## 4. Generic Preflight Refresh

The current Netflix PIN flow refreshes focus when the window suggests a PIN
surface but the stored focus snapshot does not expose the active slot ordinal.

Keep that behavior, but make it generic:

- if a continuation candidate depends on focused-slot state and the current
  focus evidence is missing or stale, refresh focus before planning the
  continuation
- if a fresh window snapshot invalidates the old gate entirely, skip the
  continuation cleanly

This preflight stage should remain generic continuation infrastructure rather
than app-specific policy.

## Proposed Skill Contract

The runtime needs a generic way to know whether an active app skill allows a
given primitive to run.

## Initial Contract: Affordance Gating

For the first migration pass, extend the current metadata approach rather than
inventing a heavy new DSL immediately.

Candidate affordances:

- `named_choice_continuation`
- `discrete_slot_text_entry`
- `discrete_slot_text_rewrite`

Meaning:

- the active skill group says this app can legitimately use that generic
  primitive
- runtime still applies generic eligibility checks and evidence rules
- the skill body continues to define the app-specific policy, such as
  visibility-only no-op rules, exact matching expectations, and success
  criteria

## Possible Follow-On Contract

If affordances prove too weak, add a more structured frontmatter block later,
for example `continuation_policies`, but do not start there unless review
concludes it is necessary.

The migration should prefer the smallest contract that keeps runtime generic
and understandable.

## Structural Detection Strategy

The trickiest part is replacing Netflix-specific PIN detection with something
reusable.

Preferred direction:

- use structural evidence first
- use app-neutral wording only if it still describes a cross-app UI pattern

For discrete-slot entry, candidate generic signals include:

- focused element is an editable text slot with an ordinal in its accessible
  name
- current surface exposes multiple editable slots or a repeated single-slot
  pattern
- the requested or intercepted text contains more than one character
- focus progression can be observed after each character

This is better than keying runtime behavior off the string `Netflix` or the
exact Netflix class name.

If pure structure is not strong enough, the next-best fallback is a neutral
cross-app cue family such as `PIN`, `passcode`, `verification code`, or `OTP`,
not product-specific strings.

## Migration Phases

## Phase A. Freeze Current Behavior

Before deleting anything, make sure the important behavior is pinned down with
tests.

Add or refine regression coverage for:

- named-profile continuation starts only for explicit activation requests
- named-profile continuation skips on visibility-only requests
- stale PIN tree is invalidated by fresher home evidence
- focused ordinal determines remaining suffix
- missing ordinal triggers preflight focus refresh
- multi-character `type_window_text` is rewritten into sequential discrete
  entry only when the structured-slot surface is active
- generic trace events preserve equivalent start, skip, step, and complete
  observability

Deliverable:

- the new generic migration can be judged against behavior, not memory

## Phase B. Introduce Generic Executor And Trace Names

First generalize the execution helper and its traces without yet deleting every
Netflix call site.

Expected work:

- rename the structured PIN executor to a generic discrete-slot executor
- move trace event names to a generic family
- keep call sites thin while the new helper lands

This phase should reduce risk before the continuation builders are rewritten.

## Phase C. Replace Tool-Call Interception

Genericize the current `type_window_text` interception path:

- replace `TryExtractStructuredNetflixPinDigits(...)` with a generic
  discrete-slot rewrite detector
- keep the rewrite gated by active skill affordances plus structural evidence
- preserve redaction and per-step verification

Deliverable:

- no Netflix-specific tool-call rewrite logic in production runtime

## Phase D. Replace Post-Reply Continuations

Replace the two Netflix-specific post-reply helpers with generic continuation
candidate builders and runners:

- visible named choice continuation
- discrete-slot text continuation

Expected work:

- make the continuation lifecycle generic
- move app policy to affordances plus skill text
- keep traces comparable with the current debug continuation vocabulary

Deliverable:

- no Netflix-specific post-reply continuation methods in `Conversation.cs`

## Phase E. Remove Netflix-Specific Surface Detectors

Delete the remaining Netflix-named snapshot and element heuristics from
production runtime.

If a detector is still needed, it must either:

- be renamed and generalized to a cross-app UI pattern, or
- move out of runtime and into a more appropriate contract

Deliverable:

- no Netflix-specific helper names left in production `src/head/brain/**/*.cs`

## Phase F. Reclassify Tests

After the code migration:

- generic runtime tests should validate generic continuation and discrete-slot
  behavior
- Netflix-specific tests should remain only where they prove the pilot app
  still works end to end through skills and scenarios

This avoids carrying Netflix-specific helper names forever in low-level unit
tests after the runtime is genericized.

## Implementation Principles

- keep the runtime primitive names anchored to UI patterns, not products
- keep skill metadata small unless traces prove a richer contract is needed
- preserve current guardrails against passive requests becoming implicit
  actions
- preserve stale-evidence invalidation before any automatic continuation
- do not allow "genericization" that merely wraps the current Netflix code in
  a new class name
- prefer one delete checkpoint per phase so the migration does not stall with
  both old and new paths active indefinitely

## Risks And Mitigations

- Risk: named-choice continuation becomes too broad and clicks the wrong thing
  - Mitigation: require explicit activation intent, one unique exact visible
    target, and active-skill affordance gating

- Risk: discrete-slot detection becomes too weak and misses real PIN or OTP
  flows
  - Mitigation: start with structural signals plus affordance gating, and add
    neutral cross-app cue terms only if traces show the structure alone is not
    enough

- Risk: migration keeps old behavior hidden behind renamed wrappers
  - Mitigation: set an explicit deletion checkpoint for Netflix-specific helper
    names in production runtime

- Risk: secrets leak into traces while genericizing the executor
  - Mitigation: keep full-value redaction mandatory in display and trace
    payloads

- Risk: test coverage remains coupled to old helper names and slows cleanup
  - Mitigation: separate generic primitive tests from Netflix pilot-flow tests
    during the migration

## Deliverables

If this plan lands cleanly, the result should be:

- generic continuation primitives in runtime
- generic discrete-slot text-entry support in runtime
- skill-gated use of those primitives
- generic trace event names for sequential discrete entry
- deletion of Netflix-specific continuation and PIN helper names from
  production runtime
- retained Netflix pilot coverage through skills, tests, and scenarios

## Review Questions

- Is affordance gating enough for the first pass, or do we want structured
  `continuation_policies` metadata immediately?
- Should the generic named-choice continuation run for any exact visible named
  target, or only when an active skill opts into that continuation kind?
- For discrete-slot detection, do we want purely structural signals, or should
  runtime allow neutral cross-app cue terms such as `PIN`, `passcode`, `OTP`,
  and `verification code`?
- Is a short-lived adapter phase acceptable while renaming the executor and
  trace events, or do we want a one-shot cutover?
- Do we keep the current `policyName` values temporarily for trace comparison,
  even after the execution helper and event categories become generic?

## Expected Outcome

Once this plan is executed, the Netflix pilot should still behave the same from
the user's perspective, but the underlying runtime should read like reusable UI
automation infrastructure instead of a Netflix exception list.
