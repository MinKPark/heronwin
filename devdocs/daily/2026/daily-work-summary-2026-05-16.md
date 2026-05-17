# Feature Change Summary - 2026-05-16

Status captured at 2026-05-16 20:25 PDT.

- Current branch: `main`
- Current committed head before this summary doc: `24853f5`
- Working tree before this summary doc: clean
- Focus: introducing AVA, the Accessibility Validation Assistant, as a
  scenario-driven accessibility validation host.

## Feature Sequence Table

| Seq | Commit | Area | Feature change |
| ---: | --- | --- | --- |
| 1 | `971f1b0` | Docs | Fixed project name capitalization in the root README. |
| 2 | `af3e14b` | AVA design | Added the AVA accessibility validation assistant plan. |
| 3 | `ddcf785` | Merge | Merged `origin/main` into `main`. |
| 4 | `2e12482` | AVA host | Introduced the runnable AVA assistant, prompt profile, policy skill, scenarios, validation configs, report models, deterministic validators, and tests. |
| 5 | `829a82d` | AVA runner | Replaced the no-op runner with AVA-owned command execution and evidence collection. |
| 6 | `fac5b05` | LLM config | Added role-specific LLM configuration for AVA driver, evaluator, and reporter roles. |
| 7 | `24853f5` | Reporting | Added AVA report regeneration and updated CLI options/docs. |

## AVA Assistant

AVA is now a third assistant host under `src/assistants/ava`. It reads UX
scenario files, applies validation config, drives UI through shared action
tools, collects evidence through cognition tools, and writes Markdown/JSON
reports under `artifacts/ava`.

The first implementation includes:

- AVA prompt files and an accessibility validation policy skill.
- `src/assistants/ava` and `src/assistants/ava.tests` in the solution.
- CLI support for `--help`, direct UX scenario/config inputs, `--run` bundles,
  and report regeneration.
- Sample active-window and browser-page accessibility bundles.
- Deterministic validators for evidence gaps, parse errors, name/role/action
  gaps, and focus evidence gaps.
- Per-step Markdown report output with a `#### Findings` table below each step.

## Active Runner And Evidence

The active runner now owns the validation loop instead of wrapping TARS. For
each scenario command it can collect evidence, execute the command, collect
post-command or failure evidence, create execution-accessibility findings, and
continue according to the validation config.

Evidence and report output are keyed by generated step ids such as
`step-001`, keeping scenario steps, evidence manifests, findings, and
regenerated reports aligned.

## Role-Specific LLM Config

AVA now has role-oriented LLM settings for:

- `driver`
- `evaluator`
- `reporter`

The driver role is active. Evaluator and reporter settings are reserved for
future LLM-assisted review and triage passes. Sensitive role configuration
values are redacted in command/report text.

## Reporting

Markdown/JSON reports include summary tables, per-step execution and evidence
metadata, findings, stable export ids, triage categories, and evidence
summaries. Report regeneration can rebuild Markdown/JSON from a saved AVA run
without driving the UI again.

New P0 follow-up added during wrap-up:

| Priority | Follow-up | Next move |
| --- | --- | --- |
| `P0` | Review the AVA findings table under each scenario step. | Inspect generated Markdown from `AvaReportWriter.ToMarkdown`; confirm the per-step `#### Findings` table has the right columns, order, and readability for reviewers, then update report tests and docs before expanding validators. |

## Verification

Verified during this wrap-up pass:

```powershell
dotnet test src\assistants\ava.tests\HeronWin.Ava.Tests.csproj
```

Result: passed with 49 tests.

Builds and full-solution tests were not rerun during this wrap-up pass.

## Next Session

First steps:

1. Review and commit the wrap-up docs if they look right.
2. Open a generated AVA Markdown report and review the per-step `#### Findings`
   table for reviewer readability, column order, and future ACR/export needs.
3. Update `AvaReportWriter.ToMarkdown`, `AvaReportTests`, and AVA docs if that
   report review changes the table shape.
4. After the table shape is stable, continue Phase 4 validator expansion from
   the AVA plan.
