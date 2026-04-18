# Skill Versus Code Policy

This document defines how `heronwin` should decide between:

- changing prompts or skills
- changing `herface` runtime code

The default policy is:

- Prefer skill changes first.
- Treat code as the last resort when the needed behavior is mostly impossible to express or stabilize through prompt or skill guidance alone.
- Use code changes for general guardrails, deterministic recovery, and reusable runtime behavior.

## Goal

Keep scenario behavior easy to evolve in prompts while reserving code for logic that must be reliable across turns, models, and prompt wording.

This is the main development approach for `herface`, not a secondary preference.

## Default Rule

When a new issue is discovered, start by asking:

1. Is this mainly a strategy or playbook problem?
2. Or is it a reliability and control problem?

If it is mainly about strategy, ordering, wording, or scenario guidance, change a skill.

If it is mainly about deterministic recovery, tool-output interpretation, safety, state tracking, or enforcing an invariant, change code.

## Prefer Skills When

Use a skill as the primary fix when the desired change is about how the agent should approach a task.

- The fix is about tool preference, not tool enforcement.
- The fix is about action ordering, such as "check X before Y."
- The fix is about domain or scenario playbooks, such as browser navigation, app launch, or search workflows.
- The fix is about what counts as success or what must be verified.
- The fix is about wording, evidence standards, or reporting style within a scenario.
- The behavior may evolve quickly and should stay easy to tune without recompiling code.

Typical examples:

- "For website requests, treat the request as direct URL navigation, not search."
- "Open a new Edge tab first unless the user explicitly wants the current tab."
- "After a launch attempt, refresh the visible state before claiming success."

## Prefer Code When

Use runtime code when the behavior needs to hold even if the model takes a weak path.

- The fix enforces a general invariant.
- The fix rewrites a fragile action into a safer one.
- The fix depends on parsing tool output or UI state deterministically.
- The fix handles a known tool or platform failure mode.
- The fix improves recovery after partial failure.
- The fix reduces ambiguity by collecting or refreshing evidence automatically.
- The fix should apply across multiple skills or scenarios.
- The fix is important for safety, consistency, or repeated reliability.

Typical examples:

- Prefer a specific `windowHandle` over a broad title match when the handle is already known.
- Retry with fresh evidence after an uncertain UI-changing action.
- Detect browser fullscreen content and exit fullscreen before browser-shortcut navigation.
- Rewrite brittle browser address-bar element activation to `Ctrl+L`.

## What Should Not Live In Code

Avoid adding code for behavior that is primarily scenario policy.

- Do not hardcode app-specific playbooks unless they are truly general runtime rules.
- Do not add code only to restate prompt guidance that a skill can express clearly.
- Do not promote one model's temporary weakness into permanent runtime complexity unless the issue is repeated and general.
- Do not encode narrow preferences that are safe to leave heuristic.

## What Should Not Live Only In Skills

Do not rely on skills alone when repeated logs show that prompt guidance is not enough.

- Repeated deterministic failures from the same tool pattern.
- Known UI Automation blind spots or browser-chrome visibility issues.
- Required recovery sequences that are mechanical and generic.
- Evidence refresh logic that should happen automatically.
- Rules whose failure would cause misleading success claims or unsafe behavior.

## Promotion Rule: Skill First, Then Escalate

When a problem is first discovered, follow this order:

1. Tighten the relevant skill if the issue looks like a playbook gap.
2. Observe whether the issue still repeats in logs, screenshots, or tests.
3. Promote the fix into code only if the failure is repeatable and the code change can be framed as a general runtime improvement.
4. Add tests for the new runtime behavior.
5. Keep the skill focused on scenario intent after the code change lands.

In short:

- prompts define intent
- code enforces reliability

## Promotion Signals

Promote from skill to code when most of these are true:

- The same failure appears in more than one run or trace.
- The failing pattern can be detected from tool output or local state.
- The recovery action is mechanical, not subjective.
- The rule is useful beyond one app or one exact page.
- The rule would still be desirable even if the prompt were excellent.

## Review Checklist

Before implementing a fix, answer these questions:

1. Is the issue about choosing the right playbook, or about enforcing a reliable action?
2. Can the runtime detect the problem deterministically from available evidence?
3. Would this logic still be valuable if the skill were rewritten tomorrow?
4. Is the proposed code generic across scenarios, or is it secretly app-specific policy?
5. If added to code, can it be covered by a focused unit test?

## Repository Policy

For `herface`, use this split:

- Core prompt:
  stable behavior, response contract, evidence rules, uncertainty handling, and skill interaction rules
- Skills:
  scenario playbooks, preferred tool usage, sequencing, and scenario-specific success criteria
- Runtime code:
  guardrails, tool-call rewriting, deterministic state interpretation, evidence refresh, retries, and contradiction prevention

## Current Recommendation

For this repository, the operating rule is:

- Treat skills as the main mechanism for behavioral tuning.
- Prefer updating the agent core or skills before adding runtime logic.
- Treat code as the place for general improvements and hard guardrails.

That means most new issues should start as skill changes, and only graduate to code when they prove to be general reliability problems or mostly impossible to hold through prompt guidance alone.

## Example: Browser Fullscreen

The YouTube fullscreen blocking Netflix navigation issue belongs in code, not only in the browser skill, because:

- the failure is deterministic
- it is visible in tool evidence
- the recovery is mechanical
- the fix protects the agent even when the model chooses a brittle UIA path

By contrast, "prefer direct URL navigation over web search" belongs primarily in the browser skill because it is a scenario playbook rule.


