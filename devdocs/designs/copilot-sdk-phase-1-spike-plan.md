# Copilot SDK Phase 1 Spike Plan

Status: review draft.

Parent proposal: [GitHub Copilot SDK Investigation And Proposal](./github-copilot-sdk-investigation-and-proposal.md).

## Purpose

Phase 1 should answer one concrete integration question:

Can AVA keep owning validation orchestration and user/scenario input while the GitHub Copilot SDK and bundled Copilot CLI own the inner agent/tool loop?

The spike should prove or disprove that boundary with one local, reviewable sidecar project. It should not change production assistant behavior.

The Netflix Boyfriend On Demand scenario is the Phase 1 promotion gate. The SDK path should not be considered ready for Phase 2 unless that scenario can complete successfully with speed and stability comparable to a fresh same-machine control run through the current HeronWin path.

Desired ownership boundary:

```text
AVA owns: scenario -> prompt -> validation -> report
Copilot owns: prompt -> tool loop -> assistant result
HeronWin tools own: Windows/browser observation and action
```

## Non-Goals

- Do not add a production `copilot-sdk` provider route.
- Do not replace `ILlmClient`, `BrainTurnProcessor`, `AvaBrainCommandDriver`, or AVA reporting.
- Do not convert HeronWin `.skill.md` files into SDK `SKILL.md` directories.
- Do not enable UI action tools by default.
- Do not use remote or cloud Copilot sessions for desktop automation.

## Proposed Artifacts

| Path | Purpose |
| --- | --- |
| `src/assistants/copilot-sdk-smoke/CopilotSdkSmoke.csproj` | New isolated executable project targeting `net10.0-windows`, with `GitHub.Copilot.SDK` and minimal helper dependencies. |
| `src/assistants/copilot-sdk-smoke/Program.cs` | Entrypoint that parses options, resolves prompt input, creates the SDK session, subscribes to events, sends one prompt, and writes a summary. |
| `src/assistants/copilot-sdk-smoke/CopilotSmokeOptions.cs` | Local option parser for the spike only. Keep this separate from `BrainConsoleMode` until the shape is proven. |
| `src/assistants/copilot-sdk-smoke/CopilotSmokePromptLoader.cs` | Loads either `--command` text or one command from `--ux-scenario` / `--run`. Prefer a tiny local YAML reader over friend-assembly access to `BrainScenarioLoader` for Phase 1 isolation. |
| `src/assistants/copilot-sdk-smoke/CopilotSmokeMcpConfig.cs` | Reads `MCP_SERVERS` from the environment or `src/assistants/ava/.env`, resolves relative paths, and filters to read-only `cognition` unless action tools are explicitly enabled. |
| `src/assistants/copilot-sdk-smoke/CopilotSmokeScenarioRunner.cs` | Optional after single-turn success. Runs all commands in an AVA bundle, records per-step SDK results, and emits comparable scenario metrics. |
| `src/assistants/copilot-sdk-smoke/CopilotSmokeEventLog.cs` | Writes JSONL records with timestamps, run IDs, SDK event names, tool names, prompt provenance, and error details. |
| `src/assistants/copilot-sdk-smoke/README.md` | Documents setup, required auth, sample commands, safety defaults, and known SDK behavior from the spike. |
| `src/heronwin.sln` | Add the sidecar project only after it builds locally; keep production assistant projects unchanged. |

## CLI Contract

```powershell
dotnet run --project src/assistants/copilot-sdk-smoke -- --help

dotnet run --project src/assistants/copilot-sdk-smoke -- `
  --command "Inspect the active window and summarize the visible accessibility tree." `
  --cognition-only

dotnet run --project src/assistants/copilot-sdk-smoke -- `
  --ux-scenario src/scenarios/accessibility/ux/browser-page-smoke.yml `
  --step 1 `
  --cognition-only

dotnet run --project src/assistants/copilot-sdk-smoke -- `
  --run src/scenarios/accessibility/browser-page-smoke.bundle.yml `
  --step 1 `
  --cognition-only

dotnet run --project src/assistants/copilot-sdk-smoke -- `
  --run src/scenarios/accessibility/netflix-boyfriend-on-demand.bundle.yml `
  --all-steps `
  --allow-execution `
  --timeout-seconds 1800
```

Initial options:

| Option | Behavior |
| --- | --- |
| `--command <text>` | Sends literal text as the one SDK user prompt. Mutually exclusive with scenario flags. |
| `--ux-scenario <path>` | Loads a UX scenario YAML and selects one command. |
| `--run <bundle.yml>` | Loads an AVA bundle, resolves its `uxScenario`, and selects one command. Validation config is recorded as provenance but not enforced by the spike. |
| `--step <number>` | One-based scenario command index. Defaults to `1`. |
| `--all-steps` | Runs every command in the selected scenario sequentially. Required for the Netflix Boyfriend On Demand promotion gate. Mutually exclusive with `--step`. |
| `--cognition-only` | Registers or allows read-only cognition tools only. This should be the default even if the flag is omitted. |
| `--allow-execution` | Opt-in gate for action tools. Phase 1 should document the design but not require this path to pass. |
| `--repeat <number>` | Optional stability loop for live checks. Defaults to `1`; use `3` for promotion-gate stability sampling when time allows. |
| `--log-dir <path>` | Overrides the default `logs/copilot-sdk-smoke/<run-id>` directory. |
| `--dry-run` | Resolves config and prompt provenance, writes planned records, and exits before creating an SDK session. |
| `--timeout-seconds <n>` | Caps the SDK run so a hung CLI process does not block the spike indefinitely. |

## Prompt Provenance

Every run should write explicit records before sending anything to the SDK:

```json
{"category":"prompt.source","source":"cli-args","argvPreview":"--ux-scenario ... --step 1"}
{"category":"prompt.source","source":"scenario-file","path":"src/scenarios/accessibility/ux/browser-page-smoke.yml","scenarioName":"Browser page smoke","step":1}
{"category":"prompt.source","source":"scenario-command","sha256":"...","textPreview":"Open or select a representative browser page..."}
{"category":"prompt.source","source":"system-prompt","sourceDescription":"ava sidecar smoke prompt","sha256":"..."}
{"category":"sdk.prompt.send","sessionId":"...","turn":1,"promptSource":"scenario-command","sha256":"..."}
```

## Event Log

Write JSONL under `logs/copilot-sdk-smoke/<run-id>` by default.

| Category | Required data |
| --- | --- |
| `smoke.start` | Run ID, process ID, cwd, SDK package version if available, selected mode, log directory. |
| `mcp.config.loaded` | Source path, server names, resolved commands, filtered/allowed server list. |
| `sdk.session.create` | Session options that are safe to log, auth mode if knowable, MCP server names, tool filter mode. |
| `sdk.event` | Raw SDK event name/type, event ID if available, parent/turn ID if available, safe metadata. |
| `sdk.assistant.delta` | Text delta length and preview. |
| `sdk.tool.call` | Tool name, call ID, sanitized argument keys, permission decision. |
| `sdk.tool.result` | Tool name, call ID, success/error, text length, image count if exposed. |
| `sdk.session.idle` | Turn ID, elapsed time, final text length, tool call count. |
| `scenario.step.start` | Scenario name, step number, command checksum, whether execution tools are allowed. |
| `scenario.step.complete` | Scenario name, step number, elapsed time, assistant text preview, tool call count, tool error count, status. |
| `scenario.summary` | Scenario name, elapsed time, step count, success count, failure count, total tool calls, total tool errors, retry/repair counts if observable. |
| `smoke.summary` | Exit status, final assistant text preview, event count, tool call count, open questions discovered. |
| `smoke.error` | Exception type, message, SDK/CLI stderr preview if available. |

## Safety Defaults

- Register or allow `cognition` only by default.
- Treat `execution` as unavailable unless `--allow-execution` is passed.
- Log the complete discovered tool list before any prompt is sent.
- If the SDK exposes first-party shell, filesystem, or repo tools by default, record them and keep the prompt read-only.
- If the SDK supports permission callbacks in the selected `.NET` API, deny action tools by default and log the permission request.
- Redact environment values whose keys include `KEY`, `TOKEN`, `SECRET`, `PASSWORD`, or `PIN`.
- Never write SDK event content into `artifacts/ava` during the spike; use `logs/copilot-sdk-smoke` to avoid confusing real AVA reports.

## Implementation Order

1. SDK API confirmation

   - Verify the exact `.NET` package name, version, session creation API, event subscription API, MCP configuration API, and permission callback API.
   - Record any divergence from the public docs in the parent proposal before coding deeper.

2. Sidecar scaffold

   - Add `src/assistants/copilot-sdk-smoke`.
   - Target `net10.0-windows` to match the assistant projects.
   - Add `GitHub.Copilot.SDK`.
   - Add project references only if needed; avoid `InternalsVisibleTo` changes in Phase 1 unless isolation blocks progress.
   - Add `--help`, `--dry-run`, and JSONL log creation first.

3. Prompt input loading

   - Implement `--command` literal input.
   - Implement `--ux-scenario` command extraction for the simple AVA YAML shape used by `src/scenarios/accessibility/ux/browser-page-smoke.yml`.
   - Implement `--run` bundle resolution for the simple AVA bundle shape used by `src/scenarios/accessibility/browser-page-smoke.bundle.yml`.
   - Write prompt provenance records for all three routes.

4. Read-only MCP registration

   - Load `MCP_SERVERS` using the same JSON shape as `.env`.
   - Resolve relative commands from the `.env` directory.
   - Filter to the `cognition` server for the default path.
   - Confirm the sidecar can start or register the cognition MCP server through the SDK.

5. SDK bundled CLI smoke

   - Create one SDK client/session in bundled CLI mode.
   - Attach event handlers before sending the prompt.
   - Send one prompt and wait for idle/completion or timeout.
   - Write final assistant text and event summary to the console and JSONL.

6. AVA-shaped result mapping

   - Build a small in-memory summary equivalent to the AVA command-driver fields: final text, tool call count, tool error count, status, and window handle if discoverable.
   - Do not write an AVA report yet. Instead, document whether the observed SDK events contain enough data to populate `AvaCommandExecutionResult` later.

7. Full-scenario runner

   - Add `--all-steps` only after one-step read-only and one-step action-gated flows work.
   - Run scenario commands sequentially in one SDK session if session state carries correctly.
   - If one long session proves unstable, record that and test one SDK session per step as a fallback.
   - Emit `scenario.step.*` and `scenario.summary` records.
   - Keep validation config enforcement out of the spike unless result mapping is already clear.

8. Optional action-tool probe

   - Only after read-only success, test `--allow-execution` with a harmless action or skip if permission behavior is unclear.
   - Record whether SDK permissions/hooks can block or approve action tools at the right boundary.
   - Keep this optional and separate from Phase 1 pass/fail.

9. Netflix Boyfriend On Demand promotion gate

   - Run a fresh current-path control before the SDK run:
     `dotnet run --project src/assistants/ava -- --run src/scenarios/accessibility/netflix-boyfriend-on-demand.bundle.yml`.
   - Run the SDK sidecar against the same AVA bundle with `--all-steps --allow-execution`.
   - Compare elapsed time, step success, tool call count, tool error count, retries/repairs if observable, and final state.
   - Preserve summary artifacts under ignored `.tmp/` or `logs/`, and add only a concise Markdown summary under `devdocs/perfbase` if the run becomes a useful baseline.

10. Review notes and follow-up decision

   - Update the parent proposal with observed SDK event names, missing data, auth friction, and whether the SDK can be adapter-shaped for AVA.
   - Recommend one of: continue with Copilot-owned tool loop, investigate provider-like usage, or stop integration.

## Test Plan

The test plan has two layers:

- Automated and dry-run checks that should run in CI or locally without Copilot auth.
- Live SDK checks that require local Copilot SDK authentication and a usable desktop session.

### Test Evidence To Collect

Every test run should record:

- Command executed.
- Exit code.
- Log directory path.
- JSONL categories present.
- Final console summary.
- Any SDK/CLI auth, timeout, or permission error.

For live checks, also record:

- Whether the bundled CLI was used or an external CLI path was required.
- Whether MCP tools were visible to the SDK.
- Whether any tool call happened.
- Whether assistant text and idle/completion events were observable.
- Whether the data is enough to populate an AVA-style command result.

For Boyfriend On Demand comparisons, also record:

- Control command, SDK command, timestamps, elapsed seconds, and run IDs.
- Scenario result: all five steps passed or the exact failed step.
- Final supported state: Episode 1 of Boyfriend on Demand is playing, or the closest observed stop state.
- Total SDK/LLM responses if observable.
- Total tool calls, tool errors, permission denials, retries/repairs, and timeout events.
- Whether any forbidden title confusion appeared, especially `Pursuit of Jade` or `Anaconda`.
- Whether profile/PIN handling respected the turn contract: stop at the PIN prompt on the profile-selection step, then enter the PIN only on the explicit PIN-entry step.

### Current Baseline Context

Use a fresh same-machine control run for pass/fail decisions. Historical notes only provide orientation:

- `devdocs/perfbase/2026-04-22-netflix-smoke-baseline.md` records an older OpenAiCodex run that passed on the first try in `882.255 s` scenario elapsed, with `5` turns, `26` LLM responses, `21` tool calls, and `0` tool errors.
- `devdocs/designs/head-to-assistants-refactor-plan.md` records a later `tars` / `OpenAiCodex` pass in `246.797 s`, with `5` turns, `12` LLM responses, and `16.233 s` average LLM attempt latency.
- `docs/ava/sample/result/report.redacted.md` shows the AVA bundle shape for the same scenario: `5` steps, all execution steps passed, and per-step tool calls of `4`, `4`, `6`, `4`, and `5`.

### Automated And Dry-Run Checks

| ID | Check | Command | Expected result |
| --- | --- | --- | --- |
| T1 | Build sidecar | `dotnet build src/assistants/copilot-sdk-smoke` | Project builds without modifying production assistants. |
| T2 | Help text | `dotnet run --project src/assistants/copilot-sdk-smoke -- --help` | Prints supported options and exits `0`; no SDK session is created. |
| T3 | Dry-run literal prompt | `dotnet run --project src/assistants/copilot-sdk-smoke -- --command "Inspect the active window." --dry-run` | JSONL contains `smoke.start`, `prompt.source`, and `smoke.summary`; no `sdk.session.create`. |
| T4 | Dry-run UX scenario prompt | `dotnet run --project src/assistants/copilot-sdk-smoke -- --ux-scenario src/scenarios/accessibility/ux/browser-page-smoke.yml --step 1 --dry-run` | JSONL shows scenario path, scenario name, step 1 command, and prompt checksum. |
| T5 | Dry-run bundle prompt | `dotnet run --project src/assistants/copilot-sdk-smoke -- --run src/scenarios/accessibility/browser-page-smoke.bundle.yml --step 1 --dry-run` | JSONL shows bundle path, resolved UX scenario path, validation config path, step 1 command, and prompt checksum. |
| T6 | Dry-run Boyfriend bundle, one step | `dotnet run --project src/assistants/copilot-sdk-smoke -- --run src/scenarios/accessibility/netflix-boyfriend-on-demand.bundle.yml --step 1 --dry-run` | JSONL shows Netflix bundle path, resolved UX scenario path, validation config path, step 1 command, and prompt checksum. |
| T7 | Dry-run Boyfriend bundle, all steps | `dotnet run --project src/assistants/copilot-sdk-smoke -- --run src/scenarios/accessibility/netflix-boyfriend-on-demand.bundle.yml --all-steps --allow-execution --dry-run` | JSONL includes five `scenario.step.start` planned records and a planned `scenario.summary`; no SDK session is created. |
| T8 | Invalid mutually exclusive prompt sources | `dotnet run --project src/assistants/copilot-sdk-smoke -- --command "x" --ux-scenario src/scenarios/accessibility/ux/browser-page-smoke.yml --dry-run` | Exits non-zero with a clear validation error; JSONL includes `smoke.error` if log creation happened. |
| T9 | Invalid step index | `dotnet run --project src/assistants/copilot-sdk-smoke -- --ux-scenario src/scenarios/accessibility/ux/browser-page-smoke.yml --step 99 --dry-run` | Exits non-zero with a clear range error. |
| T10 | Invalid step/all-steps mix | `dotnet run --project src/assistants/copilot-sdk-smoke -- --run src/scenarios/accessibility/netflix-boyfriend-on-demand.bundle.yml --step 1 --all-steps --dry-run` | Exits non-zero with a clear validation error. |
| T11 | MCP config dry-run | `dotnet run --project src/assistants/copilot-sdk-smoke -- --command "Inspect the active window." --cognition-only --dry-run` | JSONL includes `mcp.config.loaded`; allowed servers include `cognition` and exclude `execution`. |
| T12 | Secret redaction | Run dry-run with a fake sensitive env var such as `$env:COPILOT_TEST_TOKEN='abc123'` | JSONL and console output do not contain the sensitive value. |
| T13 | No production drift | `git diff -- src/assistants/cursor src/assistants/tars src/assistants/ava src/assistants/brain` | No production runtime edits unless explicitly approved after the spike. |

### Live SDK Checks

These checks are manual/local because they need Copilot SDK auth and a live Windows desktop.

| ID | Check | Command | Expected result |
| --- | --- | --- | --- |
| L1 | SDK session creation | `dotnet run --project src/assistants/copilot-sdk-smoke -- --command "Reply with a one sentence readiness check." --cognition-only --timeout-seconds 60` | Session starts in bundled CLI mode, assistant responds, JSONL includes `sdk.session.create`, `sdk.event`, and `smoke.summary`. |
| L2 | Read-only cognition tool visibility | `dotnet run --project src/assistants/copilot-sdk-smoke -- --command "Inspect the active window using available read-only tools and summarize it." --cognition-only --timeout-seconds 120` | SDK can see or call at least one cognition tool, or logs clearly explain why no tool was called. No execution tools are allowed. |
| L3 | AVA scenario command as prompt | `dotnet run --project src/assistants/copilot-sdk-smoke -- --ux-scenario src/scenarios/accessibility/ux/browser-page-smoke.yml --step 1 --cognition-only --timeout-seconds 120` | Scenario command becomes the SDK user prompt; assistant text and SDK events can be mapped to an AVA-style command result. |
| L4 | Bundle input as prompt | `dotnet run --project src/assistants/copilot-sdk-smoke -- --run src/scenarios/accessibility/browser-page-smoke.bundle.yml --step 1 --cognition-only --timeout-seconds 120` | Bundle resolves to the UX scenario command; validation config path is logged as provenance but not enforced. |
| L5 | Timeout handling | Run any live command with `--timeout-seconds 1` | Process exits cleanly, child SDK/CLI work is cancelled or abandoned safely, and JSONL includes timeout detail. |
| L6 | Permission gate observation | Run read-only command and inspect discovered tools/events | If action-capable tools appear, permission handling denies them by default or the run fails closed. |
| L7 | Current-path Boyfriend control | `dotnet run --project src/assistants/ava -- --run src/scenarios/accessibility/netflix-boyfriend-on-demand.bundle.yml` | Current AVA path completes the five-step bundle and writes a report. Use this run as the same-machine baseline for SDK comparison. |
| L8 | SDK Boyfriend full run | `dotnet run --project src/assistants/copilot-sdk-smoke -- --run src/scenarios/accessibility/netflix-boyfriend-on-demand.bundle.yml --all-steps --allow-execution --timeout-seconds 1800` | SDK path completes all five scenario commands, reaches Boyfriend on Demand playback, and writes per-step metrics. |
| L9 | SDK Boyfriend stability sample | Repeat L8 three times when time allows, or at minimum rerun once after a failure | Success rate is comparable to the control path; failures are deterministic enough to diagnose rather than random tool-loop drift. |

### Boyfriend On Demand Comparison Gate

Use L7 and L8 as the required promotion gate once the single-step live checks pass.

Functional success requires:

- All five commands from `src/scenarios/netflix-boyfriend-on-demand.yml` complete.
- Final assistant text or observed state confirms `Boyfriend on Demand`.
- Episode 1 playback is reached, or the run clearly reaches the same final state accepted by the current AVA/TARS path.
- No `agent.reply_contradiction_detected`-equivalent event is observed if the SDK exposes comparable events.
- No forbidden title confusion appears: `Pursuit of Jade` and `Anaconda` must not be claimed as the target.
- PIN behavior follows the scenario contract: the profile-selection step may stop at the PIN prompt, and the PIN is entered only during the explicit PIN-entry step.

Speed is comparable if:

- SDK scenario elapsed time is within `1.25x` the fresh current-path control median, or the absolute difference is explained by SDK auth/model/tool-loop overhead and accepted in review.
- SDK tool-call count is within `1.5x` the control count unless fewer/larger SDK tool calls explain the difference.
- SDK timeout budget is not reached. The default promotion timeout is `1800 s`.
- No single step regresses by more than `2x` the corresponding control step without a written explanation.

Stability is comparable if:

- The control path and SDK path both pass at least once on the same machine and account state.
- If `--repeat 3` is run, SDK passes at least `2` of `3` attempts and does not fail the same step with unrelated causes each time.
- Tool errors are `0`, or no higher than the control run and not responsible for task failure.
- Permission denials are expected and read-only/action boundaries remain intact.
- Generated logs are sufficient to identify which step, tool, or SDK event caused any failure.

### Optional Action-Tool Probe

Only run this after L1 through L4 pass and the permission boundary is understood.

| ID | Check | Command | Expected result |
| --- | --- | --- | --- |
| A1 | Explicit execution opt-in | `dotnet run --project src/assistants/copilot-sdk-smoke -- --command "<harmless approved action>" --allow-execution --timeout-seconds 120` | Execution tools are available only because `--allow-execution` was passed; permission decisions are logged. |
| A2 | No implicit execution | Repeat the same prompt without `--allow-execution` | Execution tool calls are denied or unavailable; no UI action occurs. |

### Pass Criteria

Phase 1 passes if:

- T1 through T13 pass.
- L1 and L3 pass.
- L7 and L8 pass, or L8 is explicitly deferred with an accepted reason and Phase 1 is marked inconclusive rather than passing.
- At least one live run records enough SDK events to identify assistant text, completion/idle state, and whether tools were called.
- Default behavior is read-only and does not expose `execution` without explicit opt-in.
- The spike records a clear recommendation for Phase 2 tool ownership.

Phase 1 is inconclusive if:

- SDK auth or package availability blocks all live checks, but dry-run and build checks pass.
- The SDK runs but hides event/tool details needed to map results into AVA.
- MCP registration works only through an API shape that would require larger production changes.
- Single-step checks pass, but Boyfriend On Demand cannot be completed within the available test window.

Phase 1 fails if:

- The sidecar cannot create a local SDK session in bundled CLI mode after auth/package setup is corrected.
- Read-only MCP registration cannot be made visible to the SDK.
- The SDK exposes action tools by default and cannot be gated or denied.
- The event model cannot produce enough data to distinguish assistant text, tool calls, tool results, and completion.
- Boyfriend On Demand repeatedly fails in the SDK path while the same-machine control path passes.

## Review Questions Before Coding

1. Should `src/assistants/copilot-sdk-smoke` be added to `src/heronwin.sln` immediately, or kept as a standalone project until the SDK package shape is known?
2. Should the spike use a copied minimal YAML reader for isolation, or add friend-assembly access to reuse `BrainScenarioLoader` and `AppConfig`?
3. Is `cognition`-only enough for the first review, or should the optional `--allow-execution` probe be part of the initial PR?
4. Should event logs include full prompt/tool result text locally, or only previews plus checksums by default?
5. Which auth route should be documented as the default for the first live run: signed-in GitHub user, environment token, or BYOK?
