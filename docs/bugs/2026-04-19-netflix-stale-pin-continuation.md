# Netflix Stale PIN Continuation

Last updated: 2026-04-19
Status: proposed

## Summary

During the rerun of the Netflix Boyfriend On Demand scenario under Codex, the
scenario passed, but the new continuation trace revealed a runtime bug:
`brain` started an internal PIN-entry continuation after the model had
already entered the PIN and the UI had already advanced to Netflix Home.

This is a good example of the kind of issue that can stay hidden during a
"green" scenario run unless we record and review the decision trace.

## Bug Report

Observed behavior:

- the model entered PIN digits successfully
- the Netflix profile lock disappeared
- Netflix Home became visible
- after that, `brain` still started `netflix_pin_entry` internal
  continuation work using stale preconditions

Expected behavior:

- once the target surface is no longer present, the internal continuation
  should not start
- runtime continuation code should refresh evidence before acting
- if refreshed evidence shows the goal is already achieved, the continuation
  should be skipped or aborted with a clear trace reason

## Impact

- It wastes actions after success, which can create flaky follow-on behavior.
- It increases the chance of duplicate or contradictory tool calls.
- It makes "passed" scenarios less trustworthy because hidden stale actions
  may still be happening underneath the pass condition.
- The underlying issue is app agnostic: any internal continuation could act on
  stale state if it does not re-check fresh evidence before starting.

## Evidence

Source run:

- scenario: `src/scenarios/netflix-boyfriend-on-demand.yml`
- provider banner: `LLM: ChatGPT / Codex sign-in`
- result: scenario passed

Relevant logs:

- `src/head/brain/bin/Debug/net10.0-windows/logs/brain.debug.jsonl`
- `src/head/brain/bin/Debug/net10.0-windows/logs/brain.debug.log`

Relevant trace pattern from the run:

- the model typed PIN digits `3`, `5`, `7`, `9`
- the visible window had already advanced to Netflix Home
- `agent.internal_continuation_started` still fired for
  `policyName = "netflix_pin_entry"`
- `agent.internal_continuation_step_completed` then recorded PIN-entry work
  even though the target lock surface was already gone

Related negative evidence:

- the old `Home` rewrite loop did not recur
- named-target rewrite evaluation behaved correctly
- `agent.reply_contradiction_detected` stayed at `0`

## Reproduction

1. Clear runtime logs under `src/head/brain/bin/Debug/net10.0-windows/logs`.
2. Run:

```powershell
$env:NETFLIX_PROFILE_PIN='3579'
dotnet run --project src/head/brain -- --scenario src/scenarios/netflix-boyfriend-on-demand.yml
```

3. Inspect the JSONL log for:
   - `agent.internal_continuation_considered`
   - `agent.internal_continuation_started`
   - `agent.internal_continuation_step_completed`
   - `agent.internal_continuation_completed`
4. Confirm whether `netflix_pin_entry` starts after Netflix Home is already
   visible.

## Diagnosis

Current diagnosis:

- the continuation eligibility decision is being made from stale or no-longer-
  valid UI evidence
- the runtime layer does not sufficiently re-check current state immediately
  before starting the internal continuation

Likely root cause:

- the continuation candidate is formed from an earlier actionable UI context
- after the model's explicit tool actions succeed, the continuation still uses
  that earlier context instead of requiring a fresh precondition check

Architecture boundary:

- the stale-state guard belongs in app-agnostic runtime code
- Netflix-specific selection/PIN policy remains in Netflix skill files

## Fix Plan

### 1. Add a fresh precondition check before continuation start

Before any internal continuation starts, the runtime should refresh its
evidence and confirm that the target surface still exists.

Planned runtime behavior:

- re-resolve current actionable UI context after model-driven actions finish
- require the continuation precondition to still be true
- if the surface is gone or the goal is already satisfied, do not start the
  continuation

### 2. Add explicit stale-state skip or abort reasons

The trace should tell us exactly why continuation did not proceed.

Add or use clear reasons such as:

- `precondition_no_longer_true`
- `target_surface_gone`
- `goal_already_satisfied`

These reasons should be generic and reusable by future continuations in other
apps.

### 3. Prevent duplicate follow-through after successful model action

If the model already completed the same outcome in the same turn, the runtime
should suppress continuation rather than repeating the work.

This should be phrased generically in code:

- compare intended continuation goal against refreshed current state
- avoid appending more steps once the goal has already been met

### 4. Add focused regression tests

Add tests around the runtime continuation gate so we can verify that:

- a continuation does not start when refreshed evidence shows success already
- a continuation does not start when the target surface is gone
- the skip or abort trace reason is recorded clearly

Keep the test emphasis app agnostic when possible. Add Netflix-specific tests
only where the scenario wording or Netflix policy itself matters.

### 5. Rerun the scenario and review the trace

After the code change:

1. clear logs
2. rerun the Netflix scenario
3. verify that the PIN continuation no longer starts after Home is visible
4. confirm the trace now explains the decision cleanly

## Verification Plan

- `dotnet test src/heronwin.sln`
- rerun `src/scenarios/netflix-boyfriend-on-demand.yml`
- inspect the fresh JSONL trace for continuation gating behavior
- confirm no duplicate PIN-entry action occurs after successful unlock

## Status

Current state:

- bug reproduced in trace
- diagnosis is strong but still based on runtime log interpretation
- fix not yet implemented

## Follow-Up

If this fix works well, we should treat "fresh precondition required before
internal continuation starts" as a standard runtime rule for all future
continuation helpers, not only Netflix PIN entry.
