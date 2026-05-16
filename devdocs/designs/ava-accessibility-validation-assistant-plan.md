# AVA Accessibility Validation Assistant Plan

Last updated: 2026-05-16
Status: revised draft for AVA-owned execution review

## Summary

AVA means Accessibility Validation Assistant. AVA should become a new
HeronWin assistant focused on scenario-driven, evidence-backed accessibility
validation for Windows desktop UI and web experiences.

The near-term goal is not to certify legal compliance. The goal is to produce a
repeatable technical validation report that helps developers and reviewers find
accessibility defects, collect evidence, and decide what needs human review.

AVA should not be a passive validator wrapped around a TARS run. AVA should be
its own assistant client: it reads the UX scenario, drives the UI through action
tools, inspects the UI through evidence tools, evaluates accessibility at
configured checkpoints, and writes the report. The scenario remains the source
of truth for what the user is trying to do, but AVA is the actor during an AVA
validation run.

The proposed first version should focus on:

- AVA-owned scenario execution with accessibility validation checkpoints
- a separate AVA client/runner from TARS, sharing only assistant-neutral pieces
- Windows UI Automation snapshots and focus evidence
- semantic UI action, keyboard navigation, and fallback-action evidence
- browser-hosted web experiences as exposed through UI Automation
- keyboard and focus traversal checks
- name, role, value, and state checks
- control pattern and actionability checks
- Markdown and JSON reports with evidence references

## Source Inputs

This plan is based on the shared AVA seed conversation:

- https://chatgpt.com/share/6a08ca62-7dc0-83e8-839e-5e96e7095a85

The shared conversation proposed a framework-neutral agent/skills bundle with
profiles, schemas, rule packs, and validator skills for Windows UIAutomation
plus Web. This plan adapts that idea into the current HeronWin architecture,
where runnable assistants live under `src/assistants`, shared assistant
plumbing lives in `src/assistants/brain`, and desktop tools live under
`src/tools`.

Reference sources checked for the baseline direction:

- Section508.gov Applicability and Conformance Requirements:
  https://www.section508.gov/develop/applicability-conformance/
- ICT Testing Baseline Alignment Framework:
  https://baselinealignment.section508.gov/
- Microsoft UI Automation Tree Overview:
  https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-treeoverview
- Microsoft UI Automation Overview:
  https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview
- DOJ 2026 Title II compliance-date interim final rule PDF:
  https://www.ada.gov/assets/pdfs/2026-ifr.pdf

## Current Repo Fit

HeronWin already has the right host split for AVA:

- `src/assistants/brain`: shared prompt loading, provider clients, MCP
  integration, tracing, YAML parsing, and diagnostics.
- `src/assistants/cursor`: interactive voice/text assistant.
- `src/assistants/tars`: scenario assistant for reproducible YAML runs and a
  useful reference for the scenario file shape.
- `src/tools/cognition`: read-only Windows UI inspection MCP server.
- `src/tools/execution`: Windows UI action MCP server.
- `src/tools/desktop-automation`: shared Windows automation library.

AVA should become a third runnable assistant host with its own active
drive-and-inspect loop. It can reuse the same UX scenario file shape and any
assistant-neutral parser, MCP, trace, or single-turn primitives, but it should
not be implemented as a thin wrapper around TARS and should not ask TARS to run
the UI while AVA only looks at the result.

```text
src/assistants/
  ava/
    Ava.csproj
    Program.cs
    README.md
    .env.example
  ava.tests/
    HeronWin.Ava.Tests.csproj
```

Prompt profile:

```text
.github/agents/
  ava/
    ava.agent.md
    ava.agent.core.md
    skills/
      accessibility/
        accessibility-validation-policy.skill.md
```

Scenarios and sample inputs:

```text
src/scenarios/accessibility/
  validation-configs/
    federal-windows-uia-min.yml
    federal-web-min.yml
  ux/
    checkout-flow.yml
    active-window-smoke.yml
```

Design and generated report examples:

```text
devdocs/designs/
  ava-accessibility-validation-assistant-plan.md

artifacts/ava/
  <run-id>/
    report.md
    report.json
    screenshots/
    uia/
```

## Assistant Boundary

AVA should own accessibility validation policy, scenario-validation shape,
UI-driving strategy, inspection strategy, per-step checkpoint behavior, report
presentation, and rule-pack selection.

`tars` should remain the general functional scenario assistant. During an AVA
run, TARS should not run the UI scenario as a child process, background driver,
or hidden orchestrator. AVA may reuse or extract assistant-neutral pieces from
TARS, but AVA's validation lifecycle must be explicit: inspect the current UI
when needed, execute a scenario step, collect accessibility evidence, run
validators, attach the result to that step, and decide whether to continue.

`brain` should own shared mechanics only:

- prompt catalog loading
- LLM provider and model configuration
- MCP client management
- YAML parser primitives
- debug trace and artifact helpers
- reusable report utilities if they are genuinely assistant-neutral

`tools/cognition`, `tools/execution`, and `tools/desktop-automation` should own
raw UI inspection and action primitives. They should not know about Section 508,
WCAG, AVA scoring, or report policy.

## Client Boundary

AVA and TARS need different assistant client implementations even when they
consume the same UX scenario shape.

- TARS client: optimize for functional scenario completion, scripted prompts,
  log assertions, reproducible traces, and scenario pass/fail.
- AVA client: optimize for active accessibility validation. It must drive and
  inspect the UI in one evidence timeline, choosing actions based on the current
  accessibility surface and recording how discoverable, focusable, invokable,
  stable, and understandable the UI was during execution.
- Shared pieces may include YAML scenario parsing, provider setup, MCP server
  connection management, debug trace writing, report helpers, and a narrow
  assistant-neutral single-turn primitive if it exposes full tool traces.
- AVA-owned pieces must include checkpoint scheduling, target-resolution policy,
  semantic-action versus coordinate-fallback policy, keyboard traversal policy,
  continuation policy, evidence bundling, validator scheduling, and AVA reports.
- A TARS trace may be useful later as a comparison fixture, but it is not the
  primary input for AVA validation. Primary AVA evidence comes from AVA's own
  actions, inspections, and tool traces.

## Standards Scope

Default AVA profile:

- Revised Section 508 minimum technical validation
- WCAG 2.0 Level A and AA where applicable
- UI Automation tree, properties, control types, control patterns, focus, and
  events as the Windows evidence model
- ICT Testing Baseline for Web as a baseline alignment reference, not as a
  claim that AVA fully implements a federal test methodology

Optional future profiles:

- ADA Title II web and mobile app profile using WCAG 2.1 Level AA
- WCAG 2.2 advisory profile
- internal product quality profile for usability concerns beyond conformance
- ACR or OpenACR evidence export profile

The conformance gate must remain separate from advisory scoring. A single
required failure should fail the technical conformance result even if the page
or app scores well on usability.

## Non-Goals

AVA should not initially:

- claim legal certification or final compliance
- replace manual assistive-technology testing
- generate a completed VPAT or ACR without human owner review
- attempt full screen-reader behavioral simulation
- test mobile apps directly
- implement every validator from the shared bundle before the evidence model is
  proven
- make irreversible UI changes while validating

## Proposed CLI

Scenario run:

```powershell
dotnet run --project src/assistants/ava -- --ux-scenario src/scenarios/accessibility/ux/checkout-flow.yml --validation-config src/scenarios/accessibility/validation-configs/federal-windows-uia-min.yml
```

The UX scenario and validation config should be separate first-class inputs:

- `--ux-scenario`: existing TARS scenario shape. This answers "what does the
  user do?" and may remain runnable by `tars` as a functional smoke outside an
  AVA validation run.
- `--validation-config`: AVA validation overlay. This answers "what does AVA
  check, when does it collect evidence, and what fails the run?"

For easy sharing, AVA can also support a small run bundle that references both
files:

```powershell
dotnet run --project src/assistants/ava -- --run src/scenarios/accessibility/checkout-ava-run.yml
```

Example run bundle:

```yaml
name: Checkout Flow AVA Run
uxScenario: ux/checkout-flow.yml
validationConfig: validation-configs/federal-windows-uia-min.yml
```

Trace report reuse:

```powershell
dotnet run --project src/assistants/ava -- --trace-report .\logs\<trace>.jsonl
```

AVA MVP should be UX-scenario-first and validation-config-first. Later
convenience commands can be added as wrappers that generate a one-step UX
scenario internally:

```powershell
dotnet run --project src/assistants/ava -- --active-window
dotnet run --project src/assistants/ava -- --url https://example.gov
dotnet run --project src/assistants/ava -- --report .\artifacts\ava\latest\report.json
```

For the first implementation pass, UX scenario files plus validation config
files are the contract. This keeps AVA reproducible and ensures validation is
tied to user journeys, not only static screens. The UX scenario may remain
format-compatible with TARS for reuse, but AVA must consume and execute it with
the AVA client during validation runs.

AVA should keep an AVA-specific parser/options type, such as `ParseAva`, rather
than overloading `ParseTars`. The parser must accept `--ux-scenario`,
`--validation-config`, `--run`, `--trace-report`, and `--help`, and it should
reject incompatible combinations with tests.

## Scenario-Driven Validation Model

AVA should validate while it executes the scenario, not only after the final
step. Each scenario step should produce three related outcomes:

- functional outcome: did the assistant complete the requested step?
- accessibility outcome: did the selected validators pass for the resulting UI
  state and step context?
- execution accessibility outcome: how hard was the step to complete, and did
  retries, ambiguity, fallback behavior, or failure reveal accessibility
  friction that a human user may also face?

The UX scenario can keep the same minimal file shape used by TARS so scenarios
remain shareable, but the runtime responsibility is different. AVA treats each
item in the scenario's `commands` list as both an action it must drive and a
validation checkpoint opportunity. The validation config is layered over those
commands inside the AVA client at runtime.

Core runtime concepts:

- UX command: a single entry from the UX scenario `commands` list.
- Command index: 1-based position of the UX command, used by validation config
  overrides.
- Generated step id: stable runtime id such as `command-1`, used for artifacts,
  trace events, and report anchors.
- Validation checkpoint: a configured point before and/or after a UX command
  where AVA collects evidence and runs validators.
- AVA command driver: the AVA-owned interaction loop that can inspect, choose a
  target, act through semantic UIA/keyboard/other tools, and record fallbacks.
- Functional result: AVA-observed outcome for the UX command.
- Validation result: AVA outcome for the configured validators at that command.
- Execution accessibility result: AVA's assessment of the execution path,
  including interaction count, retries, focus stability, target ambiguity,
  recovery steps, and failure causes.

The default lifecycle for each command should be:

1. Record the current UX scenario command index, generated step id, command,
   and selected validation config/profile.
2. Resolve validation defaults plus any `commandValidation` override for the
   command index.
3. Optionally collect pre-command evidence when the validation config requests
   it.
4. Build an AVA action plan using AVA's prompt, tool policy, and current UI
   evidence as needed.
5. Drive the UI through `tools/execution`, preferring semantic UIA actions and
   keyboard paths before coordinate or raw-input fallbacks.
6. Record the functional result, assistant reply, tool trace, retries, fallback
   paths, target ambiguity, and any UX assertion impact.
7. Evaluate the command execution path for accessibility friction.
8. Wait for the configured post-command settle point.
9. Collect post-command evidence with `tools/cognition`.
10. Normalize evidence into AVA snapshots.
11. Run the validators selected by the command override or validation defaults.
12. Attach findings, evidence paths, execution accessibility notes, and
   pass/fail status to the command result.
13. Continue, stop, or fail fast based on the validation config policy.

Checkpoint timing should be explicit:

- `before`: validate the state before AVA takes the command action. This is
  useful for baseline screens, starting focus, or checking that a form is
  accessible before input.
- `after`: validate the state after the command completes. This should be the
  default for MVP because it ties findings to the UI state the scenario created.
- `beforeAndAfter`: collect both states and allow validators to compare them.
  This is useful for dynamic state, modal behavior, focus movement, and form
  error announcement.
- `onFailure`: collect evidence when the functional command fails, so the
  report can explain whether the failure was caused by inaccessible UI,
  automation ambiguity, or unrelated application state.

Functional and validation outcomes should remain separate but related:

- If a command succeeds functionally and validators pass, the command passes.
- If a command succeeds functionally but required multiple attempts, ambiguous
  target selection, non-obvious fallback paths, or fragile focus recovery, AVA
  should record an execution accessibility finding. This can be advisory or
  failing based on the validation config.
- If a command succeeds functionally but validators fail, the UX flow can
  continue unless the validation config says to stop. The final AVA report
  should still fail if configured thresholds are exceeded.
- If a command fails functionally, AVA should collect failure evidence and mark
  the failure as an accessibility-relevant signal unless evidence shows the
  failure is outside the tested UI, such as a network outage or missing test
  data. Later command validations should be marked `not-tested` unless the
  scenario can safely continue.
- If evidence is incomplete, validators should return `needs-review` or
  `not-tested`; they should not silently pass.
- UX scenario assertions continue to describe functional success. AVA assertions
  describe validation success.

Execution accessibility should not pretend that an automated assistant is a
perfect proxy for a human. It should use automation friction as a signal for
review. If AVA needed extra discovery, repeated focus changes, coordinate-based
fallbacks, or recovery from a hidden/unnamed control, that is worth reporting
because a keyboard user, screen-reader user, or low-vision user may face the
same friction.

Execution accessibility signals to track:

- number of tool calls needed for the command
- repeated attempts against the same target
- fallback from semantic UIA action to coordinate click or raw keyboarding
- unnamed, duplicate-named, or ambiguously named targets
- focus moved unexpectedly or disappeared after action
- action succeeded only after broad search, visual guessing, or retries
- command failed because the intended control was not reachable, invokable,
  focusable, or discoverable
- command completed but required more intermediate UI states than expected
- command completed with stale evidence, delayed updates, or unclear state
  announcements

AVA can classify these as execution findings:

- `execution-blocker`: the scenario could not continue because the UI was not
  reachable or operable through the available accessibility surface.
- `execution-friction`: the scenario succeeded, but required retries,
  ambiguity resolution, fallback actions, or non-obvious recovery.
- `execution-risk`: the scenario succeeded, but the evidence suggests a human
  may experience difficulty, such as unstable focus or unclear dynamic state.
- `execution-note`: useful observation that does not affect pass/fail.

Continuation policy should be configurable in the validation config:

- `continue`: always attempt the full UX scenario and report all findings.
- `failFast`: stop on the first critical validation failure.
- `stopOnFunctionalFailure`: stop when the UX command cannot be completed.
- `collectOnly`: collect evidence and report findings, but do not fail the run.
- `threshold`: continue until final report evaluation, then fail if thresholds
  such as `maxCriticalFindings` are exceeded.

Runner and client constraints:

- Do not launch `tars`, consume a TARS trace, or call a TARS-hosted scenario run
  as the primary AVA validation path. AVA must create its own action and
  evidence trace.
- Do not call the current `ScriptedConversationRunner.RunAsync` unchanged for
  AVA. It stops after the first failing turn, while AVA needs configurable
  continuation and final-report threshold behavior.
- Prefer extracting a shared turn-execution primitive only if it is
  assistant-neutral and exposes full tool traces. AVA must still own validation
  checkpoints, target-resolution policy, fallback policy, and continuation.
- Disable TARS scripted lookahead for the AVA MVP. If AVA later implements
  lookahead or planning turns, record them as AVA planning evidence and do not
  let them disappear from the evidence timeline.

The per-command report record should include:

- command index and generated step id
- original UX command text
- functional status
- validation status
- execution accessibility status
- checkpoint timing used
- evidence files collected
- validators run
- retries, fallback actions, and ambiguity notes
- findings grouped by severity
- continuation decision

For example, a UX command can say "Submit the visible form without entering
required fields." AVA should execute that command through the AVA scenario
driver, then validate the resulting error state: whether focus moved
predictably, whether required-field errors are exposed through UIA names or
relationships, whether controls still have usable name/role/value/state, and
whether keyboard traversal remains possible.

If that command technically succeeds only after AVA searches several unnamed
buttons, tabs repeatedly to recover focus, or falls back to a coordinate click,
the report should show that as execution friction even if the final UI state is
valid. Success with friction is still useful accessibility evidence; it points
to improvements that may make the scenario easier for humans to complete.

The important rule is that AVA findings belong to the command that produced the
UI state. A final report should make it clear whether a defect was present on
initial load, introduced after navigation, introduced after form submission, or
found during recovery from a failed or high-friction command.

This model matters because many accessibility failures are flow-sensitive:
focus can be lost after navigation, errors may be announced incorrectly after a
form submission, modal dialogs can trap keyboard users, and dynamic content may
not expose updated state. AVA should catch those problems at the moment the
scenario reaches them.

## UX Scenario And Validation Config Shape

AVA should separate the user journey from the accessibility validation overlay.
That split lets product, design, QA, or support teams share UX scenarios while
accessibility specialists share validation configs that can be reused across
many journeys.

The UX scenario should use the same minimal shape as existing TARS scenario
files for format compatibility:

```yaml
name: Checkout Flow UX Smoke
commands:
  - "Navigate the active browser tab directly to https://example.gov/checkout and wait until the checkout page is visible."
  - "Submit the visible form without entering required fields, then wait for validation errors to appear."
assertions:
  requiredCategories:
    - assistant.reply
  forbiddenCategories:
    - agent.reply_contradiction_detected
```

The validation config supplies AVA behavior:

```yaml
name: Federal Windows UIA Minimum Validation
profile: federal-windows-uia-min
validationDefaults:
  checkpointTiming: after
  collect:
    - windowSnapshot
    - focusSnapshot
  validators:
    - uia-tree-integrity
    - name-role-value-state
    - control-pattern-actionability
    - keyboard-focus
continuationPolicy: continue
commandValidation:
  - commandIndex: 1
    checkpointTiming: after
  - commandIndex: 2
    checkpointTiming: beforeAndAfter
    validators:
      - name-role-value-state
      - keyboard-focus
      - form-error
report:
  formats:
    - markdown
    - json
assertions:
  maxCriticalFindings: 0
  requireEvidenceArtifacts: true
```

AVA should treat each UX scenario `commands` entry as a step at runtime. The
runtime can assign generated step ids such as `command-1`, `command-2` for
reporting and artifact paths, but those ids must not be required in the UX
scenario file. Per-command validation overrides should use a 1-based
`commandIndex` in the validation config.

Use `checkpointTiming` rather than boolean `validate.after` flags, so the
schema has one timing vocabulary: `before`, `after`, `beforeAndAfter`, and
`onFailure`. Use `continuationPolicy` for the run-level continuation behavior:
`continue`, `failFast`, `stopOnFunctionalFailure`, `collectOnly`, or
`threshold`.

Use `validation config` rather than `validation scenario` for the AVA-specific
file. The user journey is the scenario; the validation file configures how AVA
observes and evaluates that journey.

## Evidence Model

The first useful AVA evidence bundle should contain:

- run metadata: timestamp, assistant version, profile, target, OS, display info
- UX scenario metadata: scenario path, command index, generated step id,
  command text, functional result
- validation config metadata: config path, profile, selected validators,
  validation result, and continuation policy
- AVA client metadata: driver version, action policy, inspection policy, and
  configured fallback policy
- execution path metadata: tool calls, retries, fallback actions, failed
  targets, ambiguous targets, focus recovery, and elapsed time per command
- window metadata: title, process, bounds, selected target strategy
- UIA compact tree from `describe_window`
- focus tree from `describe_window_focus`
- screenshot metadata and file path
- keyboard traversal log
- tool call trace ids for reproducibility
- validator inputs and normalized snapshots

MVP evidence limits:

- Current compact UIA evidence already exposes path/uiPath, control type, name,
  automation id, class name, enabled/offscreen/focus/focusable/selected state,
  available actions, bounds, and children.
- Current evidence does not yet expose current control value, localized control
  type, raw UIA pattern names, pattern state such as ToggleState or
  ExpandCollapseState, or accessibility relationships.
- Validators that need missing evidence should return `needs-review` or
  `not-tested` until Phase 3 adds that evidence. They must not infer pass from
  absent data.
- Keyboard traversal can start as a bounded sequence using `press_window_key`
  plus `describe_window_focus`, but a dedicated traversal helper should be
  added before keyboard-focus becomes a strict required gate.

Proposed normalized snapshot concepts:

- `AvaRun`
- `AvaUxScenario`
- `AvaValidationConfig`
- `AvaStepResult`
- `AvaExecutionPath`
- `AvaTarget`
- `AvaEvidenceBundle`
- `AvaUiaSnapshot`
- `AvaUiaNode`
- `AvaKeyboardTraversal`
- `AvaFinding`
- `AvaReport`

Each finding should include:

- severity: `critical`, `serious`, `moderate`, `minor`, `review`
- result: `fail`, `pass`, `needs-review`, `not-applicable`, `not-tested`
- rule id
- affected node ids
- user impact
- evidence references
- recommended fix
- confidence

## Validator Set

The shared seed bundle listed these validator skills:

```text
00-scenario-router
01-uia-snapshot-normalizer
02-uia-tree-integrity-validator
03-name-role-value-state-validator
04-control-pattern-actionability-validator
05-keyboard-focus-validator
06-label-relationship-validator
07-form-error-validator
08-dynamic-state-event-validator
09-table-grid-list-tree-validator
10-text-reading-order-validator
11-platform-preferences-validator
12-web-uia-validator
13-web-dom-css-validator
14-screenshot-visual-validator
15-score-and-report-generator
16-human-review-triage
```

Do not implement all of these in the first pass. Use the list as the desired
capability map and start with a smaller executable MVP.

MVP validators:

- `uia-tree-integrity`: tree exists, useful nodes are present, decorative or
  layout-only noise is bounded, focusable items appear in expected views.
- `name-role-value-state`: interactive/content nodes have usable names, roles,
  and available states. Value checks should be `needs-review` until current
  value evidence is available.
- `control-pattern-actionability`: actionable nodes expose appropriate UIA
  actions such as invoke, toggle, expand/collapse, select, set value, range
  value, scroll, or text where expected. Raw pattern-specific checks should wait
  for Phase 3 evidence enrichment.
- `keyboard-focus`: Tab and Shift+Tab traversal reaches actionable controls,
  focus is visible or inferable, and no traps are detected in the bounded run.
- `execution-accessibility`: command execution path is evaluated for retries,
  semantic-action fallback, target ambiguity, focus loss, failed targets, and
  recovery effort as accessibility-relevant friction.
- `report-generator`: converts findings and evidence into Markdown and JSON.

Phase-two validators:

- `label-relationship`
- `form-error`
- `table-grid-list-tree`
- `text-reading-order`
- `web-uia`
- `human-review-triage`

Later validators:

- `dynamic-state-event`
- `platform-preferences`
- `web-dom-css`
- `screenshot-visual`
- ACR/OpenACR export helper

## Reporting

AVA reports should be intentionally boring and auditable:

- Summary
- Scope
- Profile
- Overall result
- Scenario step timeline
- Execution accessibility summary
- Findings by severity
- Findings by rule
- Findings by scenario step
- Success-with-friction observations
- Evidence inventory
- Not tested / needs human review
- Known limitations
- Source standards

The Markdown report is for humans. The JSON report is for tests, dashboards,
and future ACR/OpenACR tooling.

Example result language:

- `Pass`: no blocking failures found for selected rules and collected evidence.
- `Fail`: one or more selected required rules failed.
- `Needs review`: evidence was incomplete, ambiguous, or outside automated
  coverage.
- `Not tested`: selected profile contains requirements AVA did not exercise in
  this run.
- `Success with friction`: the command completed, but AVA observed execution
  friction that should be reviewed for accessibility and usability impact.

Avoid wording such as "certified compliant" or "legally compliant".

## Implementation Phases

Every implementation phase should follow this test-first loop:

1. Review the existing code and test coverage for the area being touched.
2. Run the smallest relevant existing unit-test set before changing behavior,
   so regressions are distinguishable from pre-existing failures.
3. Add or update focused unit tests for the desired behavior before production
   code when the behavior can be expressed without live UI automation.
4. Make the smallest implementation change that satisfies those tests.
5. Re-run the focused unit tests, then the broader affected test project, and
   finally `dotnet test src\heronwin.sln` before closing the phase.
6. Record any coverage gap that cannot be unit-tested, and cover it with a
   fixture, integration test, or manual smoke only when unit tests cannot model
   the behavior.

Prefer fixture-backed unit tests over live desktop automation for parser,
configuration, report, normalizer, and validator behavior. Live Windows UI
checks should verify wiring and evidence reality, not replace deterministic
unit coverage.

### Phase 0 - Review And Scope Lock

- Review this plan.
- Inventory existing tests in `brain.tests`, `tars.tests`, tool tests, and
  prompt-loader tests that AVA can reuse or extend.
- Identify unit-test gaps that must be filled before each implementation slice.
- Lock AVA MVP as scenario-first, scenario-only, and AVA-driven.
- Lock the AVA/TARS client split: AVA must not invoke TARS as the UI driver or
  treat TARS traces as primary validation evidence.
- Confirm the default profile name: proposed `federal-windows-uia-min`.
- Confirm initial report output location under `artifacts/ava`.
- Decide the default step policy for critical findings: continue and report,
  fail fast, or configurable per scenario.
- Lock the artifact names as `UX scenario` and `validation config`.

### Phase 1 - Skeleton Assistant Host

- Add failing unit tests first for AVA assistant-id normalization, prompt
  profile discovery, CLI help parsing, and solution/project inclusion.
- Add `src/assistants/ava`.
- Add `src/assistants/ava.tests`.
- Add AVA to `src/heronwin.sln`.
- Add `.env.example`.
- Add `README.md`.
- Add `buildandrun.ps1` support only if the launcher is expected to route to
  AVA from the start.
- Update both `AppConfig.NormalizeAssistantId` and
  `AgentPromptLoader.NormalizeAssistantId` to accept `ava`.
- Add `.github/agents/ava/ava.agent.md` and `ava.agent.core.md`.
- Add a minimal AVA `*.skill.md` file for accessibility validation policy.
- Update `.github/agents/README.md` when the AVA prompt profile exists.
- Update root setup docs and `docs/README.md` only after the AVA host can run
  `--help`, so live setup docs continue to describe runnable assistants.
- Sketch the AVA client/runner boundary early, even if the first skeleton only
  supports `--help`, so later phases do not accidentally wrap TARS.

Verification:

```powershell
dotnet build src\heronwin.sln
dotnet test src\heronwin.sln
dotnet run --project src\assistants\ava -- --help
```

### Phase 2 - UX Scenario, Validation Config, And Report Contract

- Start with unit tests for the CLI contract, validation-config inheritance,
  continuation policy, no-op report generation, and report serialization.
- Reuse the existing TARS scenario shape for UX scenario input: `name`,
  `commands`, and optional `assertions`.
- Add AVA CLI models and parser tests for `--ux-scenario`,
  `--validation-config`, `--run`, and `--trace-report`.
- Add validation config models with validation defaults and per-step overrides.
- Add UX scenario and validation config loader tests.
- Add step result, validation checkpoint, and report models.
- Add Markdown/JSON serialization.
- Add AVA runner/client contracts for per-command active execution records,
  evidence records, and continuation decisions.
- Add a configurable AVA runner path that can continue after validation
  failures, stop after functional failures, or fail only at final report
  evaluation.
- Explicitly disable inherited TARS lookahead behavior for MVP. Future AVA
  planning turns must be recorded as AVA evidence, not hidden TARS no-op turns.
- Add sample `ux/active-window-smoke.yml`.
- Add sample `validation-configs/federal-windows-uia-min.yml`.
- Make a no-op validation run produce a report with `not-tested` findings
  rather than pretending success.

Verification:

```powershell
dotnet test src\assistants\ava.tests\HeronWin.Ava.Tests.csproj
dotnet run --project src\assistants\ava -- --ux-scenario src\scenarios\accessibility\ux\active-window-smoke.yml --validation-config src\scenarios\accessibility\validation-configs\federal-windows-uia-min.yml
```

### Phase 3 - Active Drive And Evidence Collection MVP

- Start with fixture-backed unit tests for the AVA command driver, mocked
  action/evidence sequencing, evidence bundle writing, debug-trace correlation,
  compact snapshot normalization, and missing-evidence handling.
- Implement an AVA-owned per-command driver that connects to both `cognition`
  and `execution` MCP servers.
- For each command, let AVA collect optional pre-command evidence, drive the UI,
  collect post-command evidence, and collect failure evidence when execution
  fails.
- Reuse `cognition.describe_window`.
- Reuse `cognition.describe_window_focus`.
- Reuse `cognition.capture_window_screenshot`.
- Reuse `execution` actions for semantic UI actions, keyboard interaction, and
  controlled fallback input.
- Add AVA evidence bundle writer keyed by scenario step id.
- Preserve debug trace correlation between AVA action calls, evidence calls, and
  report evidence.
- Record fallback from semantic action to keyboard, coordinate, or raw input as
  execution accessibility evidence.
- Avoid adding standards logic to the MCP tools.

Possible tool enhancement if current evidence is insufficient:

- expose stable node ids, bounding rectangles, focusability, enabled state,
  control type, automation id, localized control type, name, value, patterns,
  and tree path in the compact snapshot.
- expose pattern states and relationships needed for richer validators, such as
  current value, read-only state, toggle state, expand/collapse state,
  selection container relationships, labeled-by/described-by equivalents where
  available, and a stable traversal helper for bounded Tab order capture.

### Phase 4 - MVP Validators

- Write validator fixture tests first, including passing, failing,
  `needs-review`, and `not-tested` cases for each rule.
- Implement deterministic validators over normalized evidence:
  - tree integrity
  - name/role/value/state
  - control-pattern/actionability
  - keyboard focus traversal
  - execution accessibility friction
- Keep validator outputs structured and testable.
- Add fixture-based tests before running against live apps.
- Run validators after each scenario step by default.

### Phase 5 - Web Profile

- Add unit fixtures for browser UIA snapshots and web-profile rule mapping
  before adding live Edge/browser scenarios.
- Use Edge/browser UIA evidence first.
- Add browser-page UX scenarios.
- Add web-focused rule ids that map back to the federal web baseline where the
  evidence supports it.
- Add DOM/CSS collection only after deciding the tool boundary:
  - browser devtools protocol,
  - Playwright helper,
  - or a separate web evidence MCP server.

### Phase 6 - Human Review And ACR Support

- Add unit tests for triage classification, export identifiers, and report
  serialization stability before adding export commands.
- Add triage categories for issues AVA cannot prove.
- Add export-friendly finding ids and evidence summaries.
- Explore OpenACR-compatible JSON/YAML export after reports are stable.

## Test Plan

Unit tests:

- UX scenario parsing
- validation config parsing
- compatibility with existing TARS scenario files
- AVA CLI parsing and invalid argument combinations
- AVA/TARS client boundary: AVA runs do not invoke the TARS scenario runner
- AVA command driver action/evidence sequencing with mocked MCP tools
- assistant id normalization for AVA config and prompt loading
- per-step validation-default inheritance
- continuation policy behavior
- inherited TARS lookahead disabled; future AVA planning turns recorded as AVA
  evidence
- profile selection
- rule-pack selection
- report serialization
- validator fixture cases
- missing-evidence validator cases return `needs-review` or `not-tested`
- prompt loading for assistant id `ava`

Integration tests:

- AVA help command
- no-op scenario report generation with per-step `not-tested` results
- AVA-owned active driver smoke with mocked or fixture-backed action/evidence
  tools
- fixture-backed evidence validation
- existing-shape two-command UX scenario with a validation checkpoint after
  each command
- optional live Windows app smoke when UI automation is available

Manual checks:

- Notepad or Settings active-window smoke
- Edge static page smoke
- keyboard traversal with a known form
- report readability review

## Open Questions For Review

- Which assistant-neutral single-turn primitive, if any, should be extracted so
  AVA and TARS can share transport without sharing client policy?
- What should AVA's first semantic-action policy be before it falls back to
  keyboard, coordinate, or raw input?
- Should critical accessibility findings fail the scenario immediately by
  default, or should AVA continue the full scenario and fail only in the final
  report?
- Is the first default profile `federal-windows-uia-min`, or should the default
  be `federal-web-min` because the seed conversation emphasized Web as well?
- Should reports live under `artifacts/ava` or under the existing logs/debug
  artifact directory?
- Should AVA collect screenshots by default, or only when a visual validator is
  enabled?
- Do we want AVA to run without an LLM for deterministic validators, using an
  LLM only for report wording and human-review triage?

## Recommendation

Build AVA in thin vertical slices:

1. Add the assistant host and prompt profile.
2. Keep the compatible UX scenario shape, but implement an AVA-owned
   drive-and-inspect client.
3. Produce a per-step report from AVA's own action and evidence timeline, even
   before validators are smart.
4. Normalize UIA evidence by scenario step.
5. Add deterministic validators with fixture tests.
6. Add web-specific evidence and rule mapping.

This keeps the project reviewable at each step and avoids turning AVA into a
large standards-shaped prompt bundle before HeronWin has the evidence contracts
to support it.
