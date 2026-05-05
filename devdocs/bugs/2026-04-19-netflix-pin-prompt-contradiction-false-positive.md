# Netflix PIN Prompt Contradiction False Positive

Last updated: 2026-04-19
Status: verified

## Summary

The latest Netflix Boyfriend On Demand rerun confirmed that the earlier stale
PIN continuation bug is fixed, but it exposed a separate reliability issue in
reply-outcome contradiction detection.

On turn 2, the runtime correctly selected the `Min` profile and stopped at the
profile PIN prompt. That satisfies the scripted command, which says to continue
until either Min opens or Min's PIN prompt is visible.

However, turn 2 attempt 7 still emitted
`agent.reply_contradiction_detected` with
`rule = "log_unresolved_but_say_resolved"` because the log text said
`Netflix home has not opened yet` even though the same log also explicitly said
`the requested condition is satisfied because Min's profile PIN prompt is visible.`

This bug is now fixed in app-agnostic runtime code.

The fix did two things:

- taught unresolved-outcome detection to treat `requested condition is
  satisfied` style language as resolved when it describes an allowed alternate
  stop condition
- stopped additional-evidence collection from firing on confidence language
  such as `Uncertainty is low ...`

After the fix, the same Netflix scenario passed. Turn 2 now ends on attempt 4
with the PIN prompt reply accepted directly, with no
`agent.reply_contradiction_detected` event and no extra evidence retry.

## Bug Report

Observed behavior:

- turn 2 user text asked the agent to select `Min` and continue until either:
  - Min opens, or
  - Min's profile PIN prompt is visible
- the runtime reached the PIN prompt, which is a valid terminal condition for
  that turn
- attempt 7 emitted `agent.reply_contradiction_detected`
- the contradiction rule was `log_unresolved_but_say_resolved`
- attempt 8 produced a corrected final assistant reply and turn 2 passed
- the overall scenario still failed because the contradiction event had already
  been logged

Expected behavior:

- if one branch of an explicit `A or B` stop condition is satisfied, the turn
  should be treated as resolved
- a log sentence like `home has not opened yet, but the requested condition is
  satisfied because the PIN prompt is visible` should not be classified as an
  unresolved outcome
- the contradiction detector should distinguish between:
  - a genuinely incomplete outcome, and
  - a completed alternate stop condition

## Impact

- It produces a false-positive runtime reliability signal during a valid turn.
- It causes scenario failure even when the user-visible behavior is correct.
- It makes contradiction traces noisier and less trustworthy for future
  debugging.
- The issue is app agnostic: any workflow that stops on one of several allowed
  terminal conditions can hit the same heuristic failure.

## Evidence

Source run:

- scenario: `src/scenarios/netflix-boyfriend-on-demand.yml`
- command:

```powershell
.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

- provider banner: `LLM: ChatGPT / Codex sign-in`
- result: scenario failed only because the forbidden contradiction category was
  present

Relevant logs:

- `src/head/brain/bin/Debug/net10.0-windows/logs/brain.debug.jsonl`
- `src/head/brain/bin/Debug/net10.0-windows/logs/brain.debug.log`

Key contradiction event from the latest run:

- log line `#00232`
- timestamp: `2026-04-19 21:28:24.426 -07:00`
- turn: `2`
- attempt: `7`
- event: `agent.reply_contradiction_detected`
- rule: `log_unresolved_but_say_resolved`
- say preview:
  - `Min was selected, and it's waiting on the PIN screen now. We can enter the PIN, use Forgot PIN, or go back and choose another profile.`
- log preview:
  - `Using the post-action UI snapshot first, the profile picker is gone and Netflix shows the Min profile lock flow: "Profile Lock is currently on." with PIN Entry Input 1 focused and a "Forgot PIN?" link. The second-pass snapshot after the extra wait confirms the same state and adds "Enter your PIN to access this profile." plus four PIN entry boxes. Netflix home has not opened yet, but the requested condition is satisfied because Min's profile PIN prompt is visible. Uncertainty is low from the UI Automation evidence; no conflicting current evidence is present.`

Final corrected reply for the same turn:

- the final `assistant.reply` for turn 2 reported:
  - say: `Min is selected, and Netflix is waiting at the PIN screen now.`
  - log: `The second-pass UI Automation snapshot confirms the Netflix page is focused on the profile lock prompt, not the profile picker or home. It shows "Profile Lock is currently on.", "Enter your PIN to access this profile.", four PIN entry boxes, PIN Entry Input 1 focused, and a "Forgot PIN?" link. The requested condition is satisfied: Min's profile PIN prompt is visible.`

Important non-bug observation from the same run:

- no hidden `netflix_discrete_slot_text_entry` continuation started in this
  trace
- that keeps this bug separate from the earlier stale-continuation issue in
  [2026-04-19-netflix-stale-pin-continuation.md](./2026-04-19-netflix-stale-pin-continuation.md)

## Reproduction

1. Clear or archive the runtime logs under
   `src/head/brain/bin/Debug/net10.0-windows/logs`.
2. Ensure `NETFLIX_PROFILE_PIN` is available in the environment.
3. Run:

```powershell
$env:NETFLIX_PROFILE_PIN='3579'
.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

4. Inspect the logs for:
   - `agent.reply_contradiction_detected`
   - `assistant.reply`
5. Confirm that turn 2 reaches the PIN prompt and that a contradiction event is
   still emitted for an otherwise valid stop-at-PIN outcome.
6. Confirm that the scenario ends with:
   - `Forbidden category "agent.reply_contradiction_detected" was present in the scenario log.`

## Diagnosis

Current diagnosis:

- the contradiction heuristic treats `Netflix home has not opened yet` as
  decisive unresolved language
- it does not give enough weight to the later clause
  `the requested condition is satisfied because Min's profile PIN prompt is visible`
- for explicit alternative stop conditions, this combination should be
  interpreted as resolved, not contradictory

Most likely root cause:

- `GetReplyOutcomeContradictionRule(...)` currently fires whenever:
  - `HasExplicitlyUnresolvedOutcome(reply.LogText)` is true, and
  - `HasExplicitlyUnresolvedOutcome(reply.SpokenText)` is false
- `HasExplicitlyUnresolvedOutcome(...)` is phrase based and currently treats
  fragments such as `not opened` or `not yet` as unresolved markers
- that heuristic appears to ignore the stronger completion marker
  `requested condition is satisfied`

Relevant runtime touchpoints:

- `src/head/brain/Conversation.cs`
  - `GetReplyOutcomeContradictionRule(...)`
  - `HasExplicitlyUnresolvedOutcome(...)`
  - `AlignReplyOutcomeConsistency(...)`

Architecture boundary:

- the fix belongs in app-agnostic runtime reply-outcome heuristics
- the Netflix skill prompt does not appear to be the primary fault here

## Fix Landed

Implemented in app-agnostic runtime:

- `HasExplicitlyUnresolvedOutcome(...)` now recognizes strong
  `requested condition is satisfied` / `requested wait condition is satisfied`
  markers as resolved alternate-stop outcomes unless the same text also says
  the request remains incomplete or failed
- `NeedsAdditionalDesktopEvidence(...)` now distinguishes genuine uncertainty
  from confidence wording such as `Uncertainty is low ...`

Code touchpoints:

- `src/head/brain/Conversation.cs`
- `src/head/brain.tests/AgentRunnerDecisionTests.cs`

Added test coverage for:

- low-uncertainty wording that should not trigger extra evidence collection
- alternate-stop success wording that should not count as unresolved
- a guardrail case where `requested condition is satisfied` still remains
  unresolved because the text explicitly says the request remains incomplete
- direct contradiction-rule coverage for the Netflix PIN-prompt case

## Fix Plan

### 1. Add focused contradiction tests

Add tests around `GetReplyOutcomeContradictionRule(...)` and/or
`HasExplicitlyUnresolvedOutcome(...)` that cover:

- alternate-stop success text such as:
  - `Netflix home has not opened yet, but the requested condition is satisfied because Min's profile PIN prompt is visible.`
- spoken text such as:
  - `Min was selected, and it's waiting on the PIN screen now.`
- expected result:
  - no contradiction rule

Keep nearby true positives covered, for example:

- `home has not opened yet and the next step remains entering the PIN`
- `the request is not complete yet`

### 2. Teach unresolved detection about satisfied alternate stop conditions

Update the app-agnostic unresolved-outcome heuristic so it recognizes strong
completion markers before generic unresolved fragments dominate the result.

Candidate completion signals:

- `requested condition is satisfied`
- `requested wait condition is satisfied`
- `stop condition is satisfied`
- `requested condition is visible`
- `requested condition was reached`

Guardrail:

- do not broadly suppress unresolved detection when the text also says the task
  failed, could not be completed, or still needs another mandatory step

### 3. Preserve and extend debug instrumentation

When debug mode is on, keep enough trace detail to explain why a contradiction
was or was not emitted.

Useful trace improvements:

- record whether an alternate-stop completion marker was detected
- record which heuristic branch classified the outcome as resolved or
  unresolved
- keep previews redacted and explanation-oriented rather than copying long raw
  text unnecessarily

### 4. Verify against the live Netflix scenario

After the heuristic change:

- rerun the Netflix scenario
- confirm turn 2 still stops correctly at the PIN prompt
- confirm no `agent.reply_contradiction_detected` event is emitted for that
  turn
- confirm the whole scenario passes

## Verification Plan

- run:

```powershell
dotnet test src/head/brain.tests/HeronWin.Brain.Tests.csproj --no-restore
```

- rerun:

```powershell
.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

- verify:
  - no `agent.reply_contradiction_detected` for the stop-at-PIN turn
  - turn 2 still reports the PIN prompt as the satisfied condition
  - no regression of the earlier stale-PIN-continuation fix
  - scenario passes without relaxing the forbidden category assertion

## Verification Result

Verified on 2026-04-19.

Automated tests:

```powershell
dotnet test src/head/brain.tests/HeronWin.Brain.Tests.csproj --no-restore
```

- result: `245 passed`

Live scenario rerun:

```powershell
$env:NETFLIX_PROFILE_PIN='3579'
.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

- result: scenario passed
- turn 2 final `assistant.reply`:
  - timestamp: `2026-04-19 22:12:21.674 -07:00`
  - attempts: `4`
  - additional desktop evidence attempts: `0`
  - say: `Min is at the PIN prompt now.`
  - log: `Confirmed from the freshest post-action UI snapshot after selecting Min: Netflix shows "Profile Lock is currently on." and the focused "PIN Entry Input 1." field. That satisfies the requested wait condition for Min's profile PIN prompt; Min has not opened past the lock yet.`
- no `agent.reply_contradiction_detected` event was emitted in the rerun log
- no `agent.additional_desktop_evidence_requested` event was emitted for turn 2

## Status

Verified and closed by code change plus live scenario rerun.
