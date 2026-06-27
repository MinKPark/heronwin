# Development Guardrails

These guardrails describe how `heronwin` should be developed and debugged.

## Core Principles

1. Prefer skill and prompt changes first.
2. Use runtime code for general reliability, guardrails, and deterministic
   recovery.
3. Keep changes small and close to the behavior being improved.
4. Add focused automated tests when a runtime rule becomes important.
5. Verify with the real runtime path, not only with theory or static inspection.
6. Keep evidence clear after UI-changing actions.
7. Keep project docs and active todos up to date as the work moves.

## Agent Collaboration Guardrail

- Ask the user before making a decision that can lead to materially different
  outcomes.
- Do not hallucinate. Do not make claims, recommendations, or proposals without
  a supporting reference from the user's request, repository evidence, tool
  output, active project documentation, or another source-backed lookup. If no
  reference is available, say what is missing and gather evidence or ask first.

## Skill First, Code Last

Start with prompts and skills when the issue is mainly about:

- strategy,
- tool preference,
- action ordering,
- surface-specific playbooks,
- success criteria,
- response or evidence wording.

Promote a fix into runtime code when the issue is mainly about:

- deterministic recovery,
- repeated failure patterns,
- invariant enforcement,
- tool-output interpretation,
- safety,
- retry behavior,
- reusable state handling across multiple scenarios.

Reference: [skill-vs-code policy](../src/agents/skill-vs-code-policy.md)

## Verification Guardrail

Use this investigation order for normal repository work:

1. Run `dotnet test` for the relevant project or solution.
2. Add or tighten focused unit tests when the failure is reproducible.
3. Run the app normally with `dotnet run` or `.\buildandrun.ps1`.
4. Use scripted scenarios when the issue is about agent flow or tool ordering.
5. Inspect JSONL traces and normal debug logs to understand what happened.
6. Add small code or test helpers only when the earlier steps still leave a
   concrete gap.

## Windows Debugging Guardrail

Avoid ad hoc PowerShell reflection that loads built assemblies directly from the
repo output tree, especially patterns such as
`[System.Reflection.Assembly]::LoadFrom(...)`.

Reason:

- it can trigger Windows Defender,
- it is less repeatable than tests and runtime traces,
- it encourages one-off debugging instead of reusable verification.

## UI And Evidence Guardrail

For UI-facing work:

- prefer fresh evidence after UI-changing actions,
- use screenshots and trace artifacts when the automation tree is sparse or
  ambiguous,
- avoid claiming success from stale UI context,
- add tests when a repair or rewrite rule becomes general enough to matter.

## Change Hygiene Guardrail

- prefer conservative edits that match the existing codebase patterns,
- avoid app-specific runtime hacks when a skill can express the behavior,
- use structured parsing or explicit contracts instead of fragile string hacks
  when a better option already exists,
- keep scenario fixes and runtime fixes clearly separated in review,
- do not move runtime-consumed prompt assets unless the runtime is updated with
  them.
