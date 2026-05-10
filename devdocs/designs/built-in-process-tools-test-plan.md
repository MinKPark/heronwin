# Built-In Process Tools Test Plan

Last updated: 2026-05-10
Status: implemented

## Summary

This plan covers the P2 follow-up to broaden automated tests for the built-in
process tools owned by `brain`:

- `list_processes`
- `start_process`
- `stop_process`

The current tests verify that the tools are exposed through `McpClientManager`
and that invalid `start_process` arguments return a tool error. The next pass
should add focused unit coverage for argument parsing and output handling, plus
small integration coverage for starting, listing, and stopping a process that
the test itself owns.

The goal is to make these tools safer to change without reintroducing a
separate process-manager service or relying on brittle assumptions about the
developer machine's live process table.

## Implementation Status - 2026-05-10

Implemented the first broad coverage pass:

- added normalized process-list parsing and formatting helpers for Windows
  `tasklist /FO CSV /NH` output and Unix-style `ps aux` output
- added internal start/stop argument records and parsing helpers
- added focused unit tests for process-list parsing, start-process argument
  validation, and stop-process argument validation
- added safe integration coverage for starting, listing, and stopping a
  test-owned PowerShell sleep process
- added safe integration coverage that verifies `start_process` honors `cwd`

The implementation keeps process tools built into `brain`; no separate
process-manager service was reintroduced.

Verified on 2026-05-10:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~BuiltInProcessToolsTests|FullyQualifiedName~McpClientManagerTests"
dotnet test src\heronwin.sln
```

## Current State

Production code lives in:

- `src/assistants/brain/BuiltInProcessTools.cs`
- `src/assistants/brain/McpClientManager.cs`

Existing automated coverage lives mostly in:

- `src/assistants/brain.tests/McpClientManagerTests.cs`

Current coverage:

- `ListAllToolsAsync_IncludesBuiltInProcessTools_WhenNoOverrideIsUsed`
- `CallToolAsync_ReturnsToolError_ForInvalidBuiltInStartProcessArguments`

Main gaps:

- `list_processes` output is returned as raw platform command output, so there
  is no focused test seam for parsing or normalizing process-list data.
- `start_process` argument parsing is only lightly covered through one invalid
  call.
- `stop_process` has no focused automated coverage.
- There is no integration test proving a process started by the tool can be
  observed and stopped safely by the same tool surface.

## Goals

- Add deterministic tests for process-list parsing or normalization.
- Add focused tests for valid and invalid `start_process` argument shapes.
- Add focused tests for valid and invalid `stop_process` argument shapes.
- Add safe integration coverage for start/list/stop using a test-owned process.
- Keep test failures actionable: argument validation, process start failure,
  process stop failure, and list parsing should fail in separate tests.
- Preserve the current .NET-only direction where process tools remain built
  into `brain`.

## Non-Goals

- Do not add a new process-manager MCP server.
- Do not test by stopping arbitrary existing processes.
- Do not depend on a specific user app, browser, shell profile, or machine
  process table.
- Do not make wall-clock-sensitive tests that wait on long sleeps or UI
  windows.
- Do not change agent policy for when `start_process` should be blocked for
  browser-navigation requests; that belongs to the existing conversation-level
  guardrail tests.

## Recommended Design

### 1. Introduce Small Test Seams

Keep `BuiltInProcessTools` internal, but split the hard-to-test pieces into
small internal helpers:

- process-list command output parsing or normalization
- argument extraction for `start_process`
- argument extraction for `stop_process`
- process runner abstraction for tests that should not launch a real command

The seam should stay narrow. The production behavior can still call
`Process.Start`, `Process.GetProcessById`, `tasklist`, and `ps`; tests should be
able to exercise validation and parsing without always touching the OS.

Likely shape:

- `BuiltInProcessToolArguments`
- `BuiltInProcessListParser`
- `IProcessToolHost` or a small delegate-based host for start/list/stop

Only add these if they make tests clear. If a smaller internal static helper is
enough, prefer that.

### 2. Normalize List Output Before Returning It

`list_processes` currently returns raw `tasklist /FO CSV /NH` output on Windows
and raw `ps aux` output elsewhere. For testability and tool consistency, add a
normalized internal representation such as:

```csharp
internal sealed record ProcessSummary(
    int? Pid,
    string Name,
    string? SessionName,
    string? User,
    string? RawLine);
```

The public tool result can remain text for now, but parsing into a structured
intermediate form gives the tests a stable target. It also leaves room to return
JSON later if the model-facing contract changes.

### 3. Keep Integration Tests Test-Owned

For safe start/stop integration coverage, launch a process that is:

- created by the test
- long-lived enough to list and stop
- easy to identify by PID
- harmless if it exits early
- cleaned up in `finally` or `IAsyncLifetime.DisposeAsync`

Preferred Windows command:

```powershell
powershell -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"
```

Because this repository targets `net10.0-windows`, Windows-only integration
tests are acceptable. If future cross-platform support matters, use
`dotnet exec` with a tiny helper or platform-specific command selection.

### 4. Separate Unit And Integration Coverage

Use focused unit tests for parsing and argument validation. Use a smaller number
of integration tests for actual OS process behavior.

Suggested test files:

- `src/assistants/brain.tests/BuiltInProcessToolsTests.cs`
- keep manager exposure tests in `McpClientManagerTests.cs`

## Test Matrix

### Tool Exposure

Keep or extend existing manager-level tests:

| Test | Purpose |
| --- | --- |
| `ListAllToolsAsync_IncludesBuiltInProcessTools_WhenNoOverrideIsUsed` | Built-ins are advertised through the same MCP tool list |
| `CallToolAsync_RoutesBuiltInProcessToolWithoutExternalServer` | Built-ins can be called without configured MCP servers |

### Argument Parsing

Add tests around raw dictionary and `JsonElement` inputs:

| Test | Purpose |
| --- | --- |
| `StartProcess_RequiresNonEmptyCommand` | Missing, null, empty, and whitespace commands fail clearly |
| `StartProcess_AcceptsStringArrayArguments` | Native `string[]` input is accepted |
| `StartProcess_AcceptsJsonArrayArguments` | MCP JSON array input is accepted |
| `StartProcess_RejectsNonStringArguments` | Mixed or non-string `args` fail before process launch |
| `StartProcess_RejectsMissingWorkingDirectory` | Invalid `cwd` returns a tool error |
| `StopProcess_RequiresPositivePid` | Missing, zero, negative, fractional, and too-large PID values fail clearly |
| `StopProcess_AcceptsJsonNumberPid` | MCP JSON number input is accepted |
| `StopProcess_AcceptsJsonBooleanForce` | MCP JSON boolean input is accepted |
| `StopProcess_RejectsNonBooleanForce` | Invalid `force` values fail clearly |

### Process List Parsing

Add parser tests with saved representative command output:

| Test | Purpose |
| --- | --- |
| `ParseWindowsTaskListCsv_HandlesQuotedNamesAndMemoryFields` | CSV parsing handles commas, quotes, and standard tasklist columns |
| `ParseWindowsTaskListCsv_SkipsMalformedRowsWithoutThrowing` | One bad row does not break the whole list |
| `ParsePsAux_HandlesWhitespaceAndCommandWithSpaces` | Unix-style parser preserves command text after fixed columns |
| `FormatProcessList_IncludesNameAndPid` | Tool-facing text contains the fields the agent needs |

Even if the runtime only uses Windows today, `ps aux` parsing can be covered if
the code keeps the non-Windows branch.

### Start Integration

Add one or two safe integration tests:

| Test | Purpose |
| --- | --- |
| `StartProcess_StartsSleepProcessAndReturnsPid` | A valid command starts and returns a PID |
| `StartProcess_UsesProvidedWorkingDirectory` | `cwd` is honored for a harmless command |

These tests should avoid relying on visible windows. Prefer redirected output
and no shell profile.

### Stop Integration

Add safe stop coverage:

| Test | Purpose |
| --- | --- |
| `StopProcess_StopsTestOwnedProcess` | A process started by the test can be stopped by PID |
| `StopProcess_ForceStopsTestOwnedProcessTree_WhenRequested` | `force=true` uses tree kill behavior where supported |
| `StopProcess_ReturnsErrorForUnknownPid` | Unknown PID failures are surfaced as tool errors |

The process tree test is useful but can be deferred if it proves flaky. The
single-process stop test is the minimum integration guardrail.

## Implementation Plan

### Phase 1: Add Unit-Testable Parsing And Validation

1. Extract process-list parsing or normalization helpers from
   `BuiltInProcessTools`.
2. Extract argument parsing into small internal methods or records.
3. Add tests for Windows `tasklist` sample output.
4. Add tests for `ps aux` sample output if the non-Windows branch remains.
5. Add argument validation tests for `start_process` and `stop_process`.

Exit criteria:

- Parser and validation tests pass without launching real processes.
- Existing `McpClientManagerTests` still pass.

### Phase 2: Add Safe Start/Stop Integration Tests

1. Add a helper that starts a test-owned sleep process through the tool.
2. Parse the returned PID from the tool result.
3. Verify the process exists by PID.
4. Stop it through `stop_process`.
5. Verify it exits within a short timeout.
6. Always clean up the PID in `finally`.

Exit criteria:

- Integration tests only touch processes they started.
- Tests finish quickly when the command succeeds.
- Tests clean up even when an assertion fails.

### Phase 3: Tighten Tool Result Assertions

1. Standardize success messages enough for tests to assert useful text.
2. Standardize error messages enough to distinguish validation failures from OS
   failures.
3. Keep user-facing text concise and avoid leaking excessive command output.

Exit criteria:

- Tests assert stable message prefixes or structured fields.
- Error output remains useful when a live process call fails.

### Phase 4: Optional Contract Improvement

Consider returning a structured artifact from `list_processes`, while preserving
the current text result:

```json
{
  "processes": [
    { "pid": 1234, "name": "notepad.exe" }
  ]
}
```

This is optional and should only be done if it fits the existing
`ToolCallOutcome` pattern cleanly. The P2 test work does not require changing
the model-facing contract.

## Verification

Run focused tests first:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~BuiltInProcessToolsTests|FullyQualifiedName~McpClientManagerTests"
```

Then run the full brain test project:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj
```

Before merging a behavior change, run the full solution if the process tools,
manager routing, or shared test helpers changed:

```powershell
dotnet test src\heronwin.sln
```

## Risks And Mitigations

| Risk | Mitigation |
| --- | --- |
| Integration tests leave a process running | Track the PID and stop it in `finally`; use a short-lived sleep command |
| Tests become machine-specific | Use OS-provided commands only; do not depend on app installs or process names |
| Process-list parsing is brittle | Parse saved fixtures for the supported command shape and tolerate malformed rows |
| Stop tests kill the wrong process | Only stop PIDs returned from a process the test created |
| Tests are flaky under load | Use short polling timeouts and assert eventual exit rather than fixed sleeps |

## Open Questions

- Should `list_processes` keep returning raw text, normalized text, or both text
  and structured artifacts?
- Do we want to mark OS-touching start/stop tests with a trait so they can be
  excluded from ultra-fast local runs?
- Should `start_process` gain an option for short foreground commands whose
  output should be captured synchronously, or should it remain a detached launch
  tool only?
