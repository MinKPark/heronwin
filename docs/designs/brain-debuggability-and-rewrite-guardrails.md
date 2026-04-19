# Brain Debuggability and Rewrite Guardrails

Last updated: 2026-04-19
Status: proposed

## Summary

This plan addresses the Netflix/Codex failure where `brain` saw the Netflix
profile picker, an internal follow-through path went stale, and a later
named-target rewrite reinterpreted the action as Edge browser chrome
(`Home`) because the original turn text mentioned "Netflix home".

The goal is to improve two things together:

- debuggability, so future failures can be explained from the trace without
  manual log archaeology
- rewrite guardrails, so passive observation text does not become an
  actionable click target

## Problem Statement

Observed failure pattern from the aborted scenario run:

1. Turn 1 navigated Edge to `https://www.netflix.com/`.
2. The profile picker became visible.
3. The assistant draft stopped after confirming that the profile picker was
   on screen.
4. `brain` internally followed through and tried to activate the requested
   Netflix profile.
5. The active page changed away from the Netflix picker.
6. A later `click_window_element` targeting the old Netflix path was
   rewritten by `exact_named_visible_target_preferred`.
7. Because the original turn text said "either the Netflix profile selection
   screen or Netflix home is visible", the rewrite found `Home` in Edge
   chrome and clicked that instead.
8. The run looped between entering the Netflix URL and returning to the Edge
   home/new-tab surface.

The current logs were sufficient to reconstruct the sequence, but not the
decision state. The trace did not directly answer:

- why profile auto-follow-through triggered
- what the requested stale element was at rewrite time
- what named candidates were considered
- why `Home` won over "do not rewrite"

## Goals

- Make named-target rewrites explainable from a single structured trace event.
- Make Netflix profile auto-follow-through explainable from explicit
  start/skip/complete trace events.
- Prevent passive "wait until visible" language from turning into an action
  target rewrite.
- Prevent turns that merely mention "profile selection screen" from
  triggering profile-selection auto-follow-through.
- Add focused regression tests for the exact failure mode we saw.

## Non-Goals

- Redesign all action-rewrite logic in one pass.
- Remove Netflix auto-follow-through entirely.
- Change MCP snapshot formats or the compact-tree contract.
- Add always-on screenshot capture beyond the current debug-mode behavior.

## Proposed Changes

## 1. Add Named-Target Rewrite Decision Tracing

Add a new JSONL event for action-tool rewrite evaluation:

- category: `agent.named_target_rewrite_evaluated`

Recommended fields:

- `turn`
- `toolCallId`
- `tool`
- `userTextPreview`
- `requestedPath`
- `requestedElementResolved`
- `requestedElementSummary`
  - `path`
  - `name`
  - `controlType`
  - `className`
  - `automationId`
  - `availableActions`
- `requiredAction`
- `userRequestedActivation`
- `snapshotContainsProfilePicker`
- `matchedPath`
- `matchedElementSummary`
- `rewritten`
- `skipReason`

Why this matters:

- the exact stale-path-to-Home failure would have been visible immediately
- we would know whether the requested element still existed at evaluation time
- we would know whether rewrite was skipped because the prompt lacked action
  intent, because no named match existed, or because the requested element was
  already specific enough

## 2. Add Netflix Auto-Follow-Through Tracing

Add dedicated events around profile auto-follow-through:

- `agent.netflix_profile_auto_follow_through_skipped`
- `agent.netflix_profile_auto_follow_through_started`
- `agent.netflix_profile_auto_follow_through_completed`

Recommended fields:

- `turn`
- `userTextPreview`
- `assistantReplyPreview`
- `profilePickerVisible`
- `matchedProfilePath`
- `matchedProfileSummary`
- `skipReason`
- `preActionWindow`
- `postActionWindow`

Why this matters:

- future traces will clearly show whether `brain` decided to continue on the
  user's behalf or not
- if it did continue, the exact matched profile target will be recorded
- if it did not continue, the skip reason will be explicit instead of implicit

## 3. Tighten Generic Named-Target Rewrite Guardrails

Current issue:

- `TryRewriteGenericContainerActionToNamedTarget(...)` can reinterpret a stale
  action path by scanning the current UI tree for any named element mentioned
  in the turn text
- this is too permissive for passive prompts such as "wait until either the
  profile selection screen or Netflix home is visible"

Planned guardrail:

- only allow this generic named-target rewrite when the user text expresses
  activation intent

Examples that should allow rewrite:

- "Select Min."
- "Open Manage Profiles."
- "Click Search."

Examples that should not allow rewrite:

- "Wait until Netflix home is visible."
- "Confirm that the profile selection screen is open."
- "Tell me whether Home is visible."

Expected outcome:

- the stale Netflix profile path will not be rewritten into Edge `Home`
  during a passive visibility/navigation turn
- explicit corrective rewrites for real selection turns still work

## 4. Tighten Netflix Profile-Selection Intent Detection

Current issue:

- the system should only auto-follow through profile selection when the user
  explicitly asked to select a profile
- turns that merely mention the phrase "profile selection screen" should not
  count

Planned guardrail:

- make `UserRequestLooksLikeProfileSelection(...)` require explicit
  profile-selection intent, not just the presence of the word `profile`

Examples that should count:

- "Select the profile named Min."
- "Choose Min's profile."
- "Click the Min profile."

Examples that should not count:

- "Wait until the profile selection screen is visible."
- "Tell me whether the profile screen is showing."
- "Navigate to Netflix and stop when profile selection is visible."

Expected outcome:

- turn 1 of the scenario will stop at the correct wait condition
- turn 2 will own the actual profile-selection action

## 5. Add Focused Regression Tests

Add or update tests in `src/head/brain.tests/AgentRunnerDecisionTests.cs` for:

- passive visibility prompt does not trigger Netflix profile-target matching
- passive visibility prompt does not rewrite a stale click into `Home`
- explicit "Select Min" still resolves the correct profile target
- existing named-target rewrite behavior for explicit user intent still passes

If practical, also add a small trace-oriented test that validates the new
decision payload shape at the helper level.

## Planned Implementation Order

1. land named-target rewrite evaluation tracing
2. land Netflix profile auto-follow-through tracing
3. tighten generic named-target rewrite guardrails
4. tighten profile-selection intent detection
5. add focused regression tests
6. rerun the scripted Netflix scenario in debug mode
7. confirm the new trace makes the decision path obvious

## Risks and Mitigations

- Risk: rewrite guardrails become too strict and remove useful corrections
  - Mitigation: limit the new block to passive, non-activation prompts and
    keep the existing explicit-intent tests green

- Risk: profile-selection intent detection misses useful phrasing variants
  - Mitigation: start conservative, then expand from real traces rather than
    broad guesses

- Risk: trace volume gets noisy
  - Mitigation: keep the full detail in JSONL, use previews instead of full
    payload dumps, and reserve candidate-level expansion for follow-up if
    needed

## Review Questions

- Is "activation intent required" the right top-level rule for generic
  named-target rewrite, or do we want an even narrower rule?
- Do we want candidate lists in the new rewrite event now, or only requested
  and chosen element summaries in the first pass?
- Should Netflix profile auto-follow-through stay enabled by default after the
  new intent guard lands?

## Expected Outcome

If this plan is approved and implemented, the next time a similar issue
happens we should be able to answer all of the following directly from the
trace:

- what the model asked to click
- whether that target still existed
- whether rewrite was even eligible
- what rewrite chose instead
- why rewrite chose it
- whether `brain` decided to continue a Netflix profile step internally

