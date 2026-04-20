# App-Agnostic Runtime and Skills Plan

Last updated: 2026-04-19
Status: proposed
Depends on: `docs/designs/brain-debuggability-and-rewrite-guardrails.md`

## Summary

This plan defines the follow-on migration after the debuggability work:

- keep runtime code app agnostic
- keep the core agent prompt app agnostic
- move app-specific behavior into skill markdown files

The goal is to make `heronwin` easier to extend, easier to debug, and less
likely to accumulate one-off app branches in `Conversation.cs`.

## Why This Plan Exists

The repository already has the right high-level policy in
`.github/agents/skill-vs-code-policy.md`:

- prompts and skills define app behavior
- runtime code enforces generic reliability

But the current implementation has drifted from that boundary in a few places.
The clearest example is Netflix-specific behavior in
`src/head/brain/Conversation.cs`, where app-specific continuation, target
matching, and PIN handling currently live in runtime code.

This plan turns that policy into an executable migration plan.

## Desired End State

After this migration:

- `.github/agents/her.agent.md` and `.github/agents/her.agent.core.md` stay
  app agnostic
- runtime code in `src/head/brain` contains only generic orchestration,
  guardrails, evidence collection, retries, and trace plumbing
- app-specific playbooks live in `.github/agents/skills/<app>/*.skill.md`
- scenario-specific wording lives in `src/scenarios/*.yml`
- when runtime code needs extra capability, it is introduced as a reusable,
  app-agnostic primitive instead of an app-specific branch

## Boundary Rules

## What Belongs In Runtime Code

Runtime code should own behavior that remains valuable even if every app skill
is rewritten tomorrow.

Allowed in code:

- generic tool-call validation and rewrite guardrails
- generic evidence refresh loops
- generic contradiction prevention
- generic continuation lifecycle and tracing
- generic action retry and abort behavior
- generic helpers for reusable UI patterns when they are not tied to one app
- tests for general reliability guarantees

Not allowed in code:

- app names such as Netflix, YouTube, Spotify, or Outlook in decision logic
- app-specific surface detection such as profile picker, browse page, or
  title-detail heuristics
- app-specific target-selection rules such as exact profile-tile matching
- app-specific success criteria
- app-specific internal follow-through branches

## What Belongs In The Core Agent Prompt

The core prompt should remain universal.

Allowed in the core prompt:

- response contract
- evidence standards
- tool-usage rules
- generic UI workflow rules
- generic retry and reporting rules

Not allowed in the core prompt:

- app-specific vocabulary
- app-specific workflows
- app-specific wait conditions
- app-specific fallback instructions

## What Belongs In Skills

Skills are the home for app behavior.

Skills should own:

- app vocabulary and visible surfaces
- app-specific sequencing
- app-specific target disambiguation
- app-specific success and stop conditions
- app-specific ASR repair hints
- app-specific examples and phrasing

Cross-app skills such as `any-app`, `generic-app`, or browser-host skills may
keep generic workflow rules, but product-specific playbooks should live in the
matching app skill group instead of being scattered across unrelated skills.

For Netflix today, this means the behavior should live primarily in:

- `.github/agents/skills/netflix/netflix-surface-and-state.skill.md`
- `.github/agents/skills/netflix/netflix-profile-and-pin.skill.md`
- `.github/agents/skills/netflix/netflix-browse-and-play.skill.md`
- `.github/agents/skills/netflix/netflix-playback-controls.skill.md`

## Current Migration Targets

The first pass should inventory and then remove or genericize app-specific
logic that currently lives in runtime code.

Current Netflix-shaped hotspots include:

- `MaybeContinueNetflixProfileSelectionAsync(...)`
- `MaybeContinueNetflixPinEntryAsync(...)`
- `TryFindNetflixProfileSelectionTargetPath(...)`
- `TryBuildRemainingNetflixPinDigits(...)`
- `ShouldRefreshNetflixPinFocusBeforeContinuation(...)`
- `ExecuteStructuredNetflixPinEntryAsync(...)`
- `SnapshotLooksLikeNetflixPinFocus(...)`
- `SnapshotLooksLikeNetflixPinWindow(...)`
- `TryExtractNetflixPinInputOrdinal(...)`
- `ElementLooksLikeNetflixPinInput(...)`
- `RequestedAppLikelySupportsWebsiteFallback(...)` app-name allowlists
- any other app-name switch or allowlist in `Conversation.cs` that changes
  behavior based on the requested product

These should be treated as migration debt, not architecture to extend.

## Target Architecture

## 1. Core Agent Stays Generic

`.github/agents/her.agent.md` and `.github/agents/her.agent.core.md` should
contain only cross-app rules:

- how to reason from evidence
- how to use tools safely
- how to verify actions
- how to report uncertainty
- how to combine with skills

They should not carry app-specific examples or app-specific workflow advice.

## 2. Skills Carry App Playbooks

Each app skill group should describe:

- what surfaces matter in that app
- how to recognize the main states the agent cares about
- what actions are appropriate on each state
- what counts as success, no-op, or stop-and-report

If an app has multiple independent surfaces, it is fine to split skills by
surface, as the Netflix group already does. The split should remain based on
distinct decision logic, not on arbitrary file size.

## 3. Runtime Exposes Generic Primitives

When prompt guidance alone is not reliable enough, runtime should provide a
generic primitive instead of an app branch.

Examples of acceptable generic primitives:

- a shared internal continuation runner
- a generic named-target rewrite guard
- a generic sequential single-character entry helper
- a generic visible picker selection helper
- a generic wait-refresh-verify loop

Every generic primitive also needs a generic trigger contract. The runtime
should not guess "this is the Netflix case again" and silently branch. Instead,
the primitive should be activated by reusable evidence- and pattern-based
conditions, or by a model-visible continuation contract that is itself named
after the UI pattern rather than the app.

Examples of unacceptable runtime features:

- `if Netflix profile picker is visible, do X`
- `if Netflix PIN screen is visible, do Y`
- app-name-specific skip reasons, logs, or trace events

## 4. Scenarios Stay Thin

Scenario YAML files should describe the requested user flow and assertions, not
carry low-level runtime policy.

Scenarios may mention app-specific goals such as "select the Min profile" or
"search for Boyfriend on Demand," but they should rely on skills to teach the
agent how to perform those app-native steps.

## Migration Strategy

## Phase 0. Finish Debuggability Work First

Do not combine this migration with the current debuggability patch.

Finish first:

- generic decision tracing
- generic continuation tracing
- rewrite guardrails
- trace-driven regression coverage for the Netflix loop

Reason:

- the debug work will make migration safer
- we want before-and-after traces when removing app-specific runtime branches

## Phase 1. Inventory App-Specific Runtime Logic

Create a small migration checklist of every app-specific item currently in:

- `src/head/brain/Conversation.cs`
- `src/head/brain/AgentPrompts.cs`
- `.github/agents/her.agent.md`
- `.github/agents/her.agent.core.md`
- cross-app skill files that currently mention a specific product outside that
  product's own skill group, especially under `.github/agents/skills/any-app`,
  `.github/agents/skills/generic-app`, and browser-host skills such as
  `.github/agents/skills/edge`

For each item, classify it as one of:

- move to skill text
- replace with generic primitive
- delete as unnecessary once skill guidance improves

Deliverable:

- a checked migration list, not just ad hoc edits

## Phase 2. Tighten Skill Ownership

Move app-specific playbook rules into skill files.

For Netflix this likely includes:

- explicit separation between passive visibility checks and actionable profile
  selection
- exact profile-selection rules
- profile-lock and PIN-entry behavior
- browse/search/play sequencing
- app-specific success criteria

Expected result:

- the core agent no longer needs Netflix-shaped instructions
- changes to Netflix behavior become prompt edits instead of code edits

## Phase 3. Introduce Missing Generic Primitives

For any remaining behavior that cannot be held reliably through skills alone,
add generic runtime capability.

Promotion rule:

- only promote if the behavior is reusable across apps or UI patterns
- name it after the UI pattern, not after the app
- cover it with focused tests

Possible first primitives:

- generic continuation runner
- generic sequential text entry across discrete single-character inputs
- generic visible named choice selection

Expected result:

- runtime grows reusable capabilities instead of app branches

## Phase 4. Migrate Netflix Off Runtime Special Cases

Replace Netflix-specific code paths with:

- skill guidance where strategy is enough
- generic primitives where deterministic support is still needed

Concrete target:

- remove the Netflix-specific continuation and PIN helpers from
  `Conversation.cs`
- keep only generic runtime hooks and tracing

This is the phase where the architecture change becomes real.

## Phase 5. Add Boundary Enforcement

Add lightweight guardrails so the codebase does not drift back.

Candidate enforcement mechanisms:

- a unit test or script that scans core runtime and core prompt files for
  app-specific names
- a unit test or script that scans cross-app skill groups for foreign
  product-specific workflow guidance
- an allowlist of approved locations for app-specific vocabulary
- review checklist updates for app-boundary decisions

Approved locations should include:

- `.github/agents/skills/<app>/**` for that app's playbooks
- `src/scenarios/**`
- tests that explicitly exercise a named app flow
- design docs

Cross-app skill groups should stay app agnostic, aside from neutral examples
such as a bare domain or vendor name when no product-specific workflow rule is
being taught.

Restricted locations should include:

- production code under `src/head/brain/**/*.cs`
- `.github/agents/her.agent.md`
- `.github/agents/her.agent.core.md`
- cross-app skill groups such as `.github/agents/skills/any-app/**` and
  `.github/agents/skills/generic-app/**` for app-specific workflow policy

## Implementation Principles

- Prefer moving policy into skills before inventing new runtime mechanisms.
- If code is needed, make it generic enough that a second app could reuse it.
- Name code by UI pattern, not by product.
- Keep traces generic too; app identity can be data, not event shape.
- Avoid carrying old app branches forward behind a new abstraction wrapper.
- Migrate one app fully enough to prove the boundary before scaling the
  pattern widely.

## Risks And Mitigations

- Risk: some Netflix behavior may regress when removed from runtime
  - Mitigation: land debuggability first, migrate in small steps, and rerun
    scenario traces after each phase

- Risk: skills alone may not be strong enough for some structured UI patterns
  - Mitigation: promote only the reusable pattern into runtime, not the app

- Risk: boundary enforcement becomes annoying or brittle
  - Mitigation: use a small allowlist and target only the highest-value files

- Risk: the migration stalls halfway and leaves both skill and runtime policy
  active
  - Mitigation: treat Netflix as a full end-to-end pilot and explicitly remove
    the old branches before calling the migration done

## Deliverables

If we execute this plan after the debuggability work, the end result should be:

- generic core runtime
- generic core agent prompt
- app-specific behavior concentrated in skills
- at least one migrated pilot app, starting with Netflix
- boundary tests or checks to keep the split intact

## Review Questions

- Is the "no app names in core runtime or core prompt" rule strict enough, or
  do we want limited exceptions?
- Should the first pilot fully remove Netflix-specific code, or is a temporary
  compatibility shim acceptable during migration?
- Which generic primitive should be introduced first if skills alone do not
  hold: continuation runner, named picker selection, or sequential PIN entry?
- Do we want the boundary-enforcement check in unit tests, CI, or both?

## Expected Outcome

Once this plan is executed, new app support should mostly look like:

1. add or refine skill files
2. improve scenario wording if needed
3. only then add generic runtime support when a cross-app UI pattern proves it
   is needed

That should make the system easier to extend and keep `brain` from slowly
turning into a pile of app-specific branches.
