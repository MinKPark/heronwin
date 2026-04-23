# Scripted Netflix Smoke Runtime Performance Plan

Last updated: 2026-04-22
Status: in progress
Depends on:
- `docs/HISTORY_AND_TODOS.md`
- `src/scenarios/netflix-boyfriend-on-demand.yml`

## Summary

The scripted Netflix smoke now has a fresh workspace baseline from
2026-04-22: `882.255 s` (`14m 42.255s`) for
`src/scenarios/netflix-boyfriend-on-demand.yml` on commit
`2d994a83b32bb23c54f205c3d2b29a0202b6105b`. The P0 goal is still to bring the
same scenario under one minute without weakening the scenario contract or
adding Netflix-specific runtime branches.

The reporter-backed baseline readout makes the dominant constraint clear:
ordinary LLM wait, not repair churn, is the main runtime cost. The saved
baseline shows `822.765 s` of LLM time across `26` responses, or about
`93.3%` of total runtime. Requested tools took `32.523 s`, automatic
post-action snapshots took `10.093 s`, reply repairs were `0`, and additional
evidence passes were `0`.

The main finding from the first trace report is that the runtime is paying a
full LLM response for most individual action steps. Turns `2` through `5` each
begin by reacquiring state with `list_windows` and then `describe_window`,
while turn `3` spends four more LLM responses entering the four PIN digits and
turn `5` spends two more LLM responses on two sequential invoke actions. The
next optimization slices therefore need to reduce generic scripted-turn LLM
round trips before smaller tool or snapshot improvements can matter.

A repeatable repo-native trace report now exists via
`brain.exe --trace-report <path>`. The fresh baseline artifacts are saved under
ignored `.tmp/`, and the tracked summary lives under `docs/perfbase/`.

## Remaining Deferred Sections

The 2026-04-22 baseline and first readout are now recorded in this document.
The main sections still intentionally deferred are:

- Section 6 numeric guardrails:
  set warning thresholds or budgets only after we have baseline and
  post-optimization comparison data.
- Any hard claim that the `< 60 s` target is achievable on the current provider
  and model without reducing ordinary LLM latency or LLM round trips first.

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
.tmp/netflix-smoke-runtime/<date>-baseline/brain.debug.jsonl
```

Alongside that trace, record:

- commit SHA
- scenario path
- provider in use
- wall-clock start and end time
- whether the run passed on the first try

This gives us a stable baseline before any behavior changes.

Completed baseline capture:

- raw artifacts:
  `.tmp/netflix-smoke-runtime/2026-04-22-baseline/brain.debug.jsonl`
- raw metadata:
  `.tmp/netflix-smoke-runtime/2026-04-22-baseline/README.md`
- tracked summary:
  `docs/perfbase/2026-04-22-netflix-smoke-baseline.md`

### 2. Use The Repeatable Trace Report

A repo-native reporting path now reads a JSONL trace and emits a Markdown
summary. It reuses `BrainTraceLogReader` instead of inventing a second parser.

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
- CLI switch on `brain`: `--trace-report <path>`
- tests in `src/head/brain.tests`

Completed implementation:

- runtime entry point:
  `src/head/brain/Program.cs`
- CLI parsing:
  `src/head/brain/ConsoleMode.cs`
- reporting logic:
  `src/head/brain/ScenarioTesting.cs`
- focused tests:
  `src/head/brain.tests/TraceReportTests.cs`

Saved baseline report:

- `.tmp/netflix-smoke-runtime/2026-04-22-baseline/trace-report.md`

This report is now the before/after source of truth for every later runtime
slice.

### Baseline Findings

| Field | Value |
| --- | --- |
| Baseline run date | `2026-04-22` |
| Commit SHA | `2d994a83b32bb23c54f205c3d2b29a0202b6105b` |
| Provider / model | `OpenAiCodex / gpt-5.4-mini` |
| Scenario wall-clock runtime | `882.255 s` |
| Trace report artifact | `.tmp/netflix-smoke-runtime/2026-04-22-baseline/trace-report.md` |
| Slowest turn | `Turn 3 at 241.045 s` |
| Total LLM time | `822.765 s` across `26` responses |
| Average LLM attempt time | `31.645 s` |
| Total tool time | `32.523 s` across `21` tool calls |
| Total extra evidence time | `0.000 s` across `0` extra evidence passes |
| Total post-action refresh time | `10.093 s` across `12` follow-up snapshots |
| Reply repairs | `0` |
| Internal continuation activity | `10` considered, `0` executed |

First readout from the baseline:

- LLM wait is the dominant bucket at about `93.3%` of total runtime.
- The scenario averaged `5.2` LLM responses per turn.
- Turns `2` through `5` each spent their first LLM response on `list_windows`
  and their second on `describe_window`, for about `240.287 s` of LLM time
  before the first user-progressing action on those turns.
- Turn `3` is expensive because it pays four additional LLM responses to enter
  four PIN digits through `set_window_element_text`.
- Turn `5` is expensive because it pays two additional LLM responses to perform
  two sequential `invoke_window_element` actions after state reacquisition.
- Turn `5` is the most expensive model turn by average attempt latency:
  `39.954 s` per response over `5` responses.
- The slowest tools were `set_window_element_text` at `13.105 s` total and
  `invoke_window_element` at `8.496 s` total, but tool time is still a distant
  secondary bucket compared with ordinary model wait.
- Message count grows materially within the hot turns:
  turn `3` grows from `5` to `25` messages and turn `5` grows from `9` to
  `21`, which likely contributes to the late-attempt slowdown.

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

Fresh baseline answers from 2026-04-22:

- The slowest turn was turn `3` at `241.045 s`, followed by turn `5` at
  `208.425 s`.
- Model wait dominated the runtime: `822.765 s` LLM time versus `32.523 s`
  tool time and `10.093 s` post-action snapshot time.
- There were no extra LLM calls from reply repair: reply repairs were `0`.
- There were no extra evidence passes: `0`.
- Turns `2` through `5` all spent their first LLM response on `list_windows`
  and their second on `describe_window`, even though the previous turn had just
  ended with fresh Netflix evidence.
- Turn `3` then spent four more LLM responses on four `set_window_element_text`
  calls for the PIN digits.
- Turn `5` then spent two more LLM responses on two `invoke_window_element`
  calls to open the title and start episode 1.
- Post-action snapshots happened `12` times, so they are worth auditing, but
  they are not the primary cause of the multi-minute runtime.
- Internal continuations were considered `10` times and executed `0` times,
  which suggests some runtime logic is still checking follow-through paths even
  when they do not end up acting.

### 4. Choose The First Optimization Slice From Baseline Data

| Rank | Measured culprit | Evidence from the report | Planned fix slice | Success metric |
| --- | --- | --- | --- | --- |
| 1 | Repeated scripted-turn state reacquisition | Turns `2` through `5` each begin with LLM -> `list_windows` and LLM -> `describe_window`; those reacquisition attempts alone cost about `240.287 s` of LLM time before the first user-progressing action on those turns | Reuse fresh carried-forward window and UI evidence at scripted turn boundaries when the active window and surface are still valid, and only reacquire state when current evidence is stale, missing, or contradicted | On the next rerun, remove at least one initial LLM/tool pair from turns `2` through `5` and cut total LLM time by at least `120.000 s` without weakening scenario assertions |
| 2 | Deterministic same-surface continuation still paying full LLM round trips | Turn `3` pays four extra LLM responses for four `set_window_element_text` PIN digits; turn `5` pays two extra LLM responses for two `invoke_window_element` actions after the relevant surface is already visible | Add generic within-turn follow-through for deterministic slot entry and obvious next-step activation on the same validated surface, instead of sending each step back through a full model loop | Reduce turn `3` from `7` responses to `4` or fewer and turn `5` from `5` responses to `3` or fewer on a fresh passing rerun |
| 3 | Automatic post-action snapshot churn | `12` follow-up snapshots costing `10.093 s` total; every state-changing tool still refreshes evidence even when the tool result already contains usable confirmation data | Audit whether scripted mode can reuse the triggering tool payload or skip some follow-up snapshots when the result already provides fresh enough confirmation | Reduce follow-up snapshots from `12` to `8` or fewer and cut snapshot time below `7.000 s` without introducing stale-evidence false positives |

Rules for filling this in:

- rank by measured wall-clock impact, not intuition
- cite the concrete trace categories or report totals that support the ranking
- choose one primary optimization slice at a time
- define the expected before-and-after metric before making the change
- keep unselected hypotheses out of the active fix slice unless later data
  promotes them

Immediate next steps from the baseline:

1. Use `brain.exe --trace-report` as the default before/after workflow for each
   rerun and save the generated Markdown beside the raw `.tmp` artifacts.
2. Start the first behavior-changing slice with scripted-turn state reuse, since
   repeated `list_windows` plus `describe_window` is the clearest generic
   source of avoidable LLM round trips.
3. Follow that with deterministic same-surface continuation for flows like PIN
   entry and obvious next-step invokes once the report confirms the first slice
   landed cleanly.
4. Reassess after those two slices whether the remaining gap to `< 60 s` is
   still dominated by ordinary model latency, in which case prompt weight or
   provider/model choices may need their own P0 discussion.

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
