# Bug Docs

This folder is the standard home for bug reports and fix plans in
`heronwin`.

We should create one bug doc before or alongside implementation whenever a
bug is worth investigation, debugging, or a non-trivial fix. The goal is to
make future debugging faster, preserve the reasoning behind the fix, and keep
evidence close to the plan.

## Standard Process

1. Create a dated bug doc in this folder:
   - `YYYY-MM-DD-short-slug.md`
2. Capture the bug report in concrete terms:
   - what happened
   - what should have happened
   - how severe it is
3. Attach evidence:
   - scenario name
   - trace categories or event IDs
   - screenshots or log files when relevant
4. Write the current diagnosis:
   - confirmed cause, likely cause, or open hypotheses
5. Write the fix plan before broad code changes:
   - runtime changes
   - skill or prompt changes
   - test coverage
   - verification steps
6. After implementation, update the same doc with:
   - final fix summary
   - verification result
   - remaining follow-up work

## Required Sections

Each bug doc should include these sections:

- `Summary`
- `Bug Report`
- `Impact`
- `Evidence`
- `Reproduction`
- `Diagnosis`
- `Fix Plan`
- `Verification Plan`
- `Status`

Add `Open Questions` or `Follow-Up` when they matter.

## Scope Rule

Keep app-agnostic reliability work in runtime code, and keep app-specific
behavior in app-specific skill or agent files. Bug docs should call out that
boundary explicitly when the fix touches both layers.

## Status Values

Use one of these status values near the top of each bug doc:

- `proposed`
- `in_progress`
- `implemented`
- `verified`
- `closed`

## Current Bugs

- [Netflix stale PIN continuation](./2026-04-19-netflix-stale-pin-continuation.md)
