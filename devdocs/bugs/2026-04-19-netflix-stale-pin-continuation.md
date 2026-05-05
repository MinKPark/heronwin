# Netflix Stale PIN Continuation

Last updated: 2026-04-19
Status: fixed

## Summary

The latest rerun of the Netflix Boyfriend On Demand smoke changed the shape of
this bug.

The earlier draft of this note described a stale PIN continuation that started
after Netflix had already advanced to Home. The newest trace shows a more
specific failure:

- turn 2 correctly reached the Netflix profile PIN prompt
- turn 2 should have stopped there
- instead, `brain` started a hidden internal
  `netflix_discrete_slot_text_entry` continuation
- that continuation entered a six-character value into the PIN surface
- turn 2 still ended with the PIN screen visible
- turn 3 then explicitly re-entered the real four-digit PIN and succeeded

So the current bug is not "duplicate PIN entry after success." It is "false
positive discrete-slot continuation on the stop-at-PIN turn, which types the
wrong hidden value and leaves the site waiting for re-entry."

The trace does not preserve the website's visible error string, but the turn
sequence is consistent with what was observed live on screen:

1. PIN prompt appears
2. wrong PIN is entered internally
3. Netflix shows an error and keeps the PIN surface up
4. the next turn re-enters the correct PIN

## Bug Report

Observed behavior:

- turn 2 user text only asked to select Min and continue until Min opened or
  the PIN prompt became visible
- the model reached that stop condition and described the PIN prompt correctly
- after that, runtime still started an internal discrete-slot continuation
- the trace recorded `inputLength = 6` for the internal entry
- turn 2 ended with the PIN prompt still visible, meaning the hidden entry did
  not unlock the profile
- turn 3 then issued four explicit `type_window_text` calls and unlocked the
  profile successfully

Expected behavior:

- turn 2 should stop once the PIN prompt is visible
- no internal discrete-slot entry should start unless the current turn text
  explicitly provides a code value to enter
- words such as `prompt`, `visible`, `screen`, or similar surface-description
  language must never be treated as a PIN or code value
- the trace should make it clear why continuation was skipped or started,
  without logging the secret itself

## Impact

- It mutates live app state after the requested stop condition has already been
  reached.
- It can create hidden bad inputs that force the next turn down a different UI
  branch.
- It makes a passing scenario less trustworthy because the success can depend
  on a later recovery turn.
- The underlying issue is app agnostic: any generic discrete-slot continuation
  can misfire if it incorrectly extracts a code value from descriptive text.

## Evidence

Source run:

- scenario: `src/scenarios/netflix-boyfriend-on-demand.yml`
- command:

```powershell
.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

- provider banner: `LLM: ChatGPT / Codex sign-in`
- result: scenario passed

Relevant logs:

- `src/head/brain/bin/Debug/net10.0-windows/logs/brain.debug.jsonl`
- `src/head/brain/bin/Debug/net10.0-windows/logs/brain.debug.log`

Relevant trace pattern from the latest run:

- turn 2 assistant draft already reported the intended stop condition:
  `Min is selected, and it's asking for the profile PIN now.`
- turn 2 then recorded:
  - `agent.internal_continuation_started`
  - `policyName = "netflix_discrete_slot_text_entry"`
  - `continuationKind = "enter_remaining_discrete_text"`
  - `inputLength = 6`
- turn 2 then recorded:
  - `agent.discrete_slot_text_entry.start`
  - six `agent.discrete_slot_text_entry.character_input` events
  - `agent.discrete_slot_text_entry.completed`
- turn 2 still ended with the assistant saying the PIN screen was visible and
  Min had not opened yet
- turn 3 then issued four explicit `type_window_text` tool calls
- on the fourth explicit digit, the tool result window title changed to
  `Home - Netflix ...`
- turn 3 assistant reply then confirmed Min was open on Netflix Home

Important constraint from the latest trace:

- the logs do not capture a clean website error string such as `Incorrect PIN`
  or `Try again`
- the failed hidden entry is inferred from the combination of:
  - turn 2 internal entry work happening
  - the PIN prompt remaining visible afterward
  - turn 3 explicit re-entry succeeding

Secondary observation:

- turn 2 also considered `netflix_named_choice_continuation` on the PIN
  surface, but it correctly skipped with
  `skipReason = "no_exact_visible_named_choice_match"`
- that looks noisy, but it is not the primary bug here

## Reproduction

1. Clear runtime logs under `src/head/brain/bin/Debug/net10.0-windows/logs`.
2. Ensure `NETFLIX_PROFILE_PIN` is available, either through the shell or the
   local environment file.
3. Run:

```powershell
$env:NETFLIX_PROFILE_PIN='3579'
.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

4. Inspect the JSONL log for turn 2:
   - `agent.internal_continuation_considered`
   - `agent.internal_continuation_started`
   - `agent.discrete_slot_text_entry.*`
   - `assistant.reply`
5. Confirm whether turn 2 starts
   `policyName = "netflix_discrete_slot_text_entry"` with `inputLength = 6`
   even though the turn only asked to stop at the PIN prompt.
6. Confirm that turn 3 still performs four explicit PIN digits and unlocks the
   profile.

## Diagnosis

Current diagnosis:

- the generic discrete-slot continuation is starting on a turn that did not
  actually provide a PIN or code value
- the continuation gate is misclassifying descriptive stop-condition wording as
  a user-provided discrete-slot value

Most likely root cause:

- `TryExtractRequestedDiscreteSlotTextFromUserText(...)` is too permissive
- in particular, the current code-term pattern can match a phrase like
  `PIN prompt` and treat `prompt` as the candidate value
- that candidate then passes the generic multi-character alphanumeric check
  because `prompt` is six letters long
- once the PIN surface is visible, `TryBuildDiscreteSlotTextContinuation(...)`
  sees both a visible discrete-slot surface and a seemingly valid six-character
  value, so it starts the internal continuation

Why the latest run fits that diagnosis:

- turn 2 user text did not contain the real PIN digits
- the trace still shows `inputLength = 6`
- six is consistent with `prompt`
- turn 2 did not unlock anything
- turn 3 explicit digit entry did unlock

Gaps in current instrumentation:

- the trace shows `inputLength`, but not the redacted extraction source or
  extraction pattern
- that makes it harder than necessary to understand why the continuation
  thought a code value existed

Architecture boundary:

- the fix belongs in app-agnostic runtime extraction, gating, and tracing code
- Netflix-specific selection and PIN policy should stay in Netflix skill files

## Fix Landed

Implemented in app-agnostic runtime:

- tightened `TryExtractRequestedDiscreteSlotTextFromUserText(...)` so code-term
  extraction only accepts explicit value-bearing phrases such as
  `enter passcode 3579` or `PIN is 3579`
- stopped treating descriptive phrases such as `PIN prompt`, `verification code
  screen`, or `code field` as candidate discrete-slot values
- added redacted continuation trace fields:
  - `valueExtractionMatched`
  - `valueExtractionPattern`
  - `candidateLength`
- kept the continuation gate generic:
  - explicit value text is still required
  - a visible discrete-slot surface is still required before entry starts
  - raw secret text is still never written to trace

Code touchpoints:

- `src/head/brain/Conversation.cs`
- `src/head/brain.tests/AgentRunnerContinuationTests.cs`
- `src/head/brain.tests/AgentRunnerDecisionTests.cs`

## Fix Plan

### 1. Tighten discrete-slot text extraction

Update `TryExtractRequestedDiscreteSlotTextFromUserText(...)` so it only
extracts a value when the user text explicitly provides one.

Planned runtime behavior:

- keep accepting clear value-bearing phrases such as:
  - `type 3579`
  - `enter 3579`
  - `PIN is 3579`
  - `passcode: 3579`
- stop treating arbitrary words after `PIN`, `code`, or `passcode` as values
- ensure phrases like `PIN prompt`, `code field`, `verification code screen`,
  or `PIN prompt is visible` do not produce a candidate value

### 2. Add explicit trace fields for extraction decisions

The trace should tell us why discrete-slot continuation believed a value was
available, without logging the secret itself.

Add generic debug fields such as:

- `valueExtractionMatched`
- `valueExtractionPattern`
- `candidateLength`
- `skipReason`

Important requirement:

- never log the raw PIN, passcode, OTP, or extracted candidate text

### 3. Strengthen the continuation start gate

Before `enter_remaining_discrete_text` starts:

- require both:
  - a visible discrete-slot surface
  - an explicit value-bearing signal from the current turn text
- if the turn only asks to observe or stop at the PIN prompt, skip with a clear
  generic reason such as `no_discrete_slot_text_in_user_text`

This keeps the runtime aligned with the actual turn contract:

- turn 2 should stop at the prompt
- turn 3 should perform entry

### 4. Add focused regression tests

Add tests that cover both the extraction helper and the continuation gate.

Proposed coverage:

- extractor returns `false` for:
  - `Min's profile PIN prompt is visible`
  - `wait until the verification code screen appears`
  - `the code field is focused`
- extractor still returns `true` for:
  - `type 3579`
  - `enter passcode 3579`
- discrete-slot continuation skips a turn-2-like PIN-prompt scenario with
  `skipReason = "no_discrete_slot_text_in_user_text"`
- discrete-slot continuation still works for a turn-3-like explicit entry turn

### 5. Rerun the live scenario and review the trace

After the code change:

1. clear logs
2. rerun the Netflix scenario
3. verify that turn 2 no longer starts
   `netflix_discrete_slot_text_entry`
4. verify that turn 2 stops cleanly at the PIN prompt
5. verify that turn 3 still enters four explicit digits and unlocks
6. confirm the trace explains the decision cleanly

### 6. Keep the current bug scope tight

This fix should stay focused on the false-positive discrete-slot continuation.

Do not bundle in unrelated cleanup unless needed for the fix:

- the named-choice continuation noise on the PIN surface
- broader trace redaction work outside discrete-slot extraction

Those can be tracked separately if they still matter after the fix lands.

## Verification

Completed verification:

- `dotnet test src/head/brain.tests/HeronWin.Brain.Tests.csproj --no-restore`
  - result: 241 passed, 0 failed
- new focused regressions cover:
  - stop-at-PIN descriptive text returning
    `skipReason = "no_discrete_slot_text_in_user_text"`
  - descriptive phrases such as `PIN prompt is visible`,
    `verification code screen`, and `code field is focused` not producing a
    discrete-slot value
  - explicit passcode text still producing remaining slot text correctly
- reran `src/scenarios/netflix-boyfriend-on-demand.yml`
  - result: scenario passed

Post-fix live trace notes from the latest rerun:

- this rerun landed on Min's Netflix Home immediately, so the scenario did not
  re-enter the visible PIN-prompt branch
- turn 2 still considered `netflix_discrete_slot_text_entry`, but skipped with:
  - `valueExtractionMatched = false`
  - `valueExtractionPattern = null`
  - `candidateLength = null`
  - `skipReason = "no_discrete_slot_text_in_user_text"`
- turn 3 explicitly supplied a PIN value and the trace now shows:
  - `valueExtractionMatched = true`
  - `valueExtractionPattern = "explicit_input"`
  - `candidateLength = 4`
  - `skipReason = "discrete_slot_surface_not_visible"`
- no post-fix `agent.internal_continuation_started` event occurred for
  `netflix_discrete_slot_text_entry` on turns 2 or 3 of the rerun

Remaining verification caveat:

- because the rerun started from Home, the exact "PIN prompt visible and stop"
  branch was re-verified by unit regressions rather than by this specific live
  smoke pass

## Status

Current state:

- fix implemented in runtime extraction, gating, and trace instrumentation
- focused regressions are in place and passing
- full `HeronWin.Brain.Tests` suite is passing
- latest live scenario rerun passed without any post-fix internal
  `netflix_discrete_slot_text_entry` start on turns 2 or 3
- the earlier interpretation in this note has been superseded
- the root cause remains the same:
  false-positive value extraction from stop-condition wording
- the latest live rerun did not reopen the profile PIN prompt, so the prompt
  branch confirmation now rests on unit coverage plus the historical repro trace

## Follow-Up

If this fix works well, we should treat "explicit value-bearing text required
before discrete-slot auto-follow-through starts" as a standard runtime rule for
all future structured code-entry continuations, not only Netflix PIN flows.
