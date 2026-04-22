# Scripted Netflix Smoke Runtime Performance Plan

Last updated: 2026-04-21
Status: proposed
Depends on:
- `docs/HISTORY_AND_TODOS.md`
- `src/scenarios/netflix-boyfriend-on-demand.yml`

## Summary

The latest documented scripted Netflix smoke passes, but the current runtime
figure in repo docs is only historical context until we capture a fresh
baseline in this workspace. The P0 goal is to bring the same scenario under one
minute without weakening the scenario contract or adding Netflix-specific
runtime branches.

This plan starts with trace-based measurement instead of guesswork. We already
have most of the timing hooks we need in the debug JSONL trace. The missing
piece is a repeatable report that turns that trace into a per-turn and
per-latency-bucket summary, so we can remove the biggest avoidable costs first
and keep them from quietly returning later.

A fresh baseline capture is required in the current workspace. There is no
current `brain.debug.jsonl` under `src/head/brain/bin/**/logs`.

## Deferred Data Sections

The following parts of this plan should be updated only after baseline data
exists:

- Summary:
  replace the historical runtime wording with the measured baseline runtime and
  a short statement of the top confirmed latency buckets.
- Baseline findings:
  add the actual measured totals after the first trace-report run instead of
  predicting them now.
- Section 4:
  choose the first optimization slice only from measured report output.
- Section 6 numeric guardrails:
  set warning thresholds or budgets only after we have baseline and
  post-optimization comparison data.

Until then, this document should stay focused on workflow, questions, and
decision rules rather than expected outcomes.

## Goals

- Cut `src/scenarios/netflix-boyfriend-on-demand.yml` below 60 seconds on a
  fresh passing rerun.
- Produce a repeatable breakdown of where time is spent by turn, LLM attempt,
  tool execution, evidence refresh, and helper logic.
- Confirm or add focused automated coverage around any runtime path we optimize
  before behavior changes land.
- Remove the biggest avoidable repair loops, retry loops, redundant tool calls,
  and duplicate evidence refreshes.
- Leave behind guardrails so future runtime changes do not drift back into
  multi-minute scripted runs.

## Non-Goals

- Rewriting the scenario to make it easier.
- Adding Netflix-specific runtime shortcuts.
- Turning one noisy wall-clock number into a flaky CI gate immediately.
- Folding unrelated P1 work into this pass unless the baseline proves it is the
  main culprit.

## Constraints

- Keep runtime and prompts app agnostic where possible.
- Keep app-specific behavior in skills and scenario wording.
- Use real scripted runs as the source of truth.
- Do not change runtime behavior for an optimization slice until the targeted
  code path has adequate automated coverage, adding tests first when needed.
- Keep raw live traces under ignored `.tmp/`, not in git.
- Prefer repo-native reporting code over ad hoc shell parsing.

## What We Can Already Measure

| Question | Existing trace source | Notes |
| --- | --- | --- |
| How long did a turn take? | `assistant.reply` | Already includes `elapsedMs` and total attempt count. |
| How many LLM attempts happened? | `llm.request`, `llm.response`, `assistant.reply` | Response latency can be computed from matching request and response timestamps. |
| How long did each requested tool take? | `agent.tool_call_requested`, `agent.tool_call_completed` | `agent.tool_call_completed` already includes `elapsedMs`, tool name, and error state. |
| How much time came from extra repair calls? | `agent.reply_repair_requested`, `agent.reply_repair_completed`, `llm.*` | Lets us separate ordinary model time from repair-only model time. |
| How much time came from extra evidence refresh? | `agent.additional_desktop_evidence_*` | Includes the explicit 1-second wait path. |
| How much time came from automatic post-action refresh? | `agent.desktop_followup_snapshot`, `agent.desktop_followup_focus_snapshot` | Good candidate for duplicate work analysis. |
| How much time came from browser helpers? | `agent.browser_*`, `agent.activate_window_preflight_*` | Useful for spotting stacked preflight steps before one intended action. |
| How often did runtime finish an action internally? | `agent.internal_continuation_*` | Tells us whether the model is stopping short and forcing follow-through. |
| What did the underlying MCP layer cost? | `mcp.call.*` | Useful for zooming in after high-level hotspots are identified. |

## Plan

### 1. Capture A Clean Baseline

Run one fresh passing baseline with:

```powershell
.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

Then copy the generated JSONL trace from the normal `brain` logs directory into
an ignored folder such as:

```text
.tmp/netflix-smoke-runtime/2026-04-21-baseline/brain.debug.jsonl
```

Alongside that trace, record:

- commit SHA
- scenario path
- provider in use
- wall-clock start and end time
- whether the run passed on the first try

This gives us a stable baseline before any behavior changes.

### 2. Add A Repeatable Trace Report

Land a small repo-native reporting path that reads a JSONL trace and emits a
Markdown or JSON summary. Reuse `BrainTraceLogReader` instead of inventing a
second parser.

Recommended output:

- scenario total wall-clock time
- one row per turn with command, turn elapsed, attempt count, tool call count,
  and obvious retry signals
- grouped totals for:
  - LLM time
  - reply repair time
  - requested tool time
  - browser preflight/helper time
  - automatic post-action snapshot time
  - automatic focus snapshot time
  - extra evidence refresh time
  - internal continuation time
- top slow events by category
- top slow executed tools
- counts for repairs, extra evidence passes, internal continuations, blocked
  tool calls, and follow-up snapshots

Recommended implementation home:

- reporting logic near `src/head/brain/ScenarioTesting.cs`
- optional CLI switch on `brain`, such as `--trace-report <path>`
- tests in `src/head/brain.tests`

This should be the first code change in the P0 work so every later fix has a
before/after summary.

### Baseline Findings

This section is intentionally deferred until the first fresh passing baseline
and trace-report output exist.

When baseline data is available, capture:

| Field | Value |
| --- | --- |
| Baseline run date | `TBD after baseline` |
| Commit SHA | `TBD after baseline` |
| Provider | `TBD after baseline` |
| Scenario wall-clock runtime | `TBD after baseline` |
| Slowest turn | `TBD after baseline` |
| Total LLM time | `TBD after baseline` |
| Total tool time | `TBD after baseline` |
| Total extra evidence time | `TBD after baseline` |
| Total post-action refresh time | `TBD after baseline` |
| Total internal continuation time | `TBD after baseline` |

### 3. Rank Culprits Before Fixing

For the first passing baseline, classify runtime cost into three buckets:

- Necessary work
- Reliability work that may be valid but too frequent
- Pure overhead

The initial report should answer these questions:

- Which turn is the slowest?
- How much time is model wait versus tool execution versus helper logic?
- How many extra LLM calls came from reply repair?
- How many extra UI refreshes came from post-action snapshots or confidence
  retries?
- Which executed tools dominate elapsed time?
- Which browser preflights or internal continuations happen more than once?

Only after those questions are answered should behavior changes begin.

### 4. Choose The First Optimization Slice From Baseline Data

This section is intentionally deferred until we have a fresh passing baseline
trace and the first generated trace report.

We should not predict the culprit in advance. Once the baseline exists, update
this section with the actual top latency buckets and pick the first fix slice
from measured evidence only.

When we revisit this section, capture:

| Rank | Measured culprit | Evidence from the report | Planned fix slice | Success metric |
| --- | --- | --- | --- | --- |
| 1 | `TBD after baseline` | `TBD after baseline` | `TBD after baseline` | `TBD after baseline` |
| 2 | `TBD after baseline` | `TBD after baseline` | `TBD after baseline` | `TBD after baseline` |
| 3 | `TBD after baseline` | `TBD after baseline` | `TBD after baseline` | `TBD after baseline` |

Rules for filling this in:

- rank by measured wall-clock impact, not intuition
- cite the concrete trace categories or report totals that support the ranking
- choose one primary optimization slice at a time
- define the expected before-and-after metric before making the change
- keep unselected hypotheses out of the active fix slice unless later data
  promotes them

### 5. Lock Down Coverage Before Changing Behavior

After choosing the first optimization slice, identify the exact code paths that
may change and verify their current automated coverage before editing behavior.

For each slice:

1. list the production files and methods likely to change
2. list the current tests that exercise those paths
3. decide whether the existing tests are strong enough to catch functional
   regression
4. add or strengthen tests first if the current coverage is too indirect,
   shallow, or missing
5. only then implement the optimization itself

Minimum expectation before changing runtime behavior:

- the touched path has focused unit or scenario-oriented coverage
- the tests assert the behavior we are preserving, not just that the code runs
- any new gating or suppression logic has both positive and negative-path tests
- if the optimization changes trace-based decision logic, tests should verify
  the relevant emitted categories or outcomes where practical

Likely test homes:

- `src/head/brain.tests`
- targeted scripted-mode or trace-evaluation tests near
  `ScenarioTesting.cs` behavior
- focused continuation, repair, or guardrail tests covering
  `Conversation.cs` logic

### 6. Rerun After Each Slice

After each focused fix:

1. rerun the same scenario
2. generate the same trace report
3. compare before and after
4. keep the change only if it reduces the target bucket without weakening
   correctness

This avoids batching several speculative changes together and losing the causal
signal.

### 7. Add Guardrails After We Hit The Target

Once the scenario is under one minute, leave behind cheap regression signals.

Recommended guardrails:

- keep the trace-report helper as a normal local workflow
- extend scripted evaluation with count-based budgets that are less flaky than
  wall-clock budgets, but defer the exact numbers until we have baseline and
  improved-run comparison data:
  - max LLM attempts per turn
  - max reply repairs per scenario
  - max additional desktop evidence passes per scenario
  - max internal continuations per scenario
  - max follow-up snapshots or focus snapshots on action-heavy turns
- add unit tests for any new gating logic that suppresses redundant refreshes
  or preflights
- document the perf workflow in `docs/README.md`

Wall-clock should stay a local benchmark until we understand variance well
enough to decide whether a hard automated threshold is safe.

## Likely Code Touchpoints

- `src/head/brain/Conversation.cs`
  - reply repair rules
  - post-action refresh logic
  - extra evidence refresh logic
  - browser helper and preflight logic
  - internal continuation logic
- `src/head/brain/McpClientManager.cs`
  - lower-level tool latency review if MCP time dominates
- `src/head/brain/ScenarioTesting.cs`
  - trace reading and scripted reporting
- `src/head/brain.tests`
  - focused regression coverage for any optimized runtime path
- `.github/agents/skills/netflix/*.skill.md`
  - only if the model is stopping short or choosing inefficient action paths
- `docs/README.md`
  - final perf workflow documentation after the report helper exists

## Exit Criteria

This P0 is done when all of the following are true:

- the scripted Netflix smoke completes in under 60 seconds on a fresh passing
  rerun
- we have a saved before/after summary showing which buckets got smaller
- the scenario still passes without relaxing its existing correctness
  assertions
- every optimization slice either confirmed adequate pre-existing coverage or
  added focused automated tests before behavior changes landed
- the biggest removed costs are explained by code or skill changes, not luck
- the repo contains a repeatable way to summarize future scripted-run latency

## Risks And Mitigations

- Risk: we make the smoke faster but less trustworthy.
  - Mitigation: keep existing scenario assertions intact and compare before/after
    trace summaries.
- Risk: we chase model latency when the real problem is helper churn.
  - Mitigation: baseline measurement comes before behavior changes.
- Risk: a hard wall-clock gate flakes because real website timing varies.
  - Mitigation: start with trace-derived count budgets and keep wall-clock as a
    local benchmark first.
- Risk: fixes slide back into app-specific runtime behavior.
  - Mitigation: prefer skill wording first and keep runtime changes framed in
    generic UI terms.

## Review Questions

- Should the trace reporter live as a `brain` CLI switch, or do we want it as a
  test/helper-only entry point first?
- Do we want warning-only perf budgets at first, or do we want count-based
  scripted assertions as soon as the first optimization pass lands?
- Once the scenario is reliably under one minute, do we stop there for P0, or
  do we immediately keep pushing toward a lower local benchmark?
