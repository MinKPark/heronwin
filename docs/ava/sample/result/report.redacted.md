# AVA Validation Report: Netflix Boyfriend On Demand Smoke

> Sanitized sample copied from source run `20260518T025245342Z-2708`.
> Raw evidence files, screenshots, local absolute paths, browser profile labels,
> profile names, profile passcodes, and local debug endpoints were redacted or
> omitted before placing this report under `docs/ava/sample/result`.

- Run ID: `20260518T025245342Z-2708`
- Validation config: `Netflix Boyfriend On Demand Windows UIA minimum`
- Profile: `federal-windows-uia-min`
- Continuation policy: `continue-and-report`
- Default checkpoints: `after`
- UX scenario: `src/scenarios/netflix-boyfriend-on-demand.yml`
- Validation config path: `src/scenarios/accessibility/validation-configs/netflix-boyfriend-on-demand-windows-uia-min.yml`

## Summary

### Steps

| Total | Pass | Fail | Needs Review | Not Tested |
| --- | --- | --- | --- | --- |
| 5 | 4 | 1 | 0 | 0 |

### Findings

| Step | Total | Fail | Needs Review | Not Tested |
| --- | --- | --- | --- | --- |
| `step-001` | 0 | 0 | 0 | 0 |
| `step-002` | 1 | 1 | 0 | 0 |
| `step-003` | 0 | 0 | 0 | 0 |
| `step-004` | 0 | 0 | 0 | 0 |
| `step-005` | 0 | 0 | 0 | 0 |
| **Total** | 1 | 1 | 0 | 0 |

## Steps

### 1. Netflix landing or home state

- Command: `Navigate the active browser tab directly to https://www.netflix.com/ and wait until either the Netflix profile selection screen or Netflix home is visible.`
- Continuation policy: `continue-and-report`
- Execution: `passed` - AVA command completed functional checks.
- Execution tool calls: `4` (`0` errors)
- Checkpoints: `after`
- Evidence: omitted from sanitized sample.

#### Web Evidence

- Web Dom Snapshot warning: CDP endpoint was not available; web/W3C validation skipped.

#### Findings

_No findings._

### 2. Profile selection accessibility

- Command: `If Netflix is showing the profile selection screen, select the redacted profile and continue until either the profile opens or its profile PIN prompt is visible.`
- Continuation policy: `continue-and-report`
- Execution: `passed` - AVA command completed functional checks.
- Execution tool calls: `4` (`0` errors)
- Checkpoints: `after`
- Evidence: omitted from sanitized sample.

#### Web Evidence

- Web Dom Snapshot warning: CDP endpoint was not available; web/W3C validation skipped.

#### Findings

##### Automated Failures (1)

| Finding | Source | Checkpoint | Summary | Rule | Evidence | Element Path | Automation ID | ARIA |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AVA-NAME-MISSING` | `DESCRIBE-WINDOW` | `after` | Actionable UI node is missing an accessible name. | [federal-windows-uia-min-uia-name-property](../../rules/federal-windows-uia-min-uia-name-property.md) | Omitted from sanitized sample. | `Window "Netflix and 1 more page - Microsoft Edge" / Pane "Netflix - Microsoft Edge" / ... / Pane / Document "Netflix" / Group / Group / Hyperlink` |  | `aria-expanded: false; aria-hasactions: false; aria-multiline: false; aria-multiselectable: false; aria-readonly: true; aria-required: false; aria-role: link` |

##### Human Review Needed (0)

_No human review findings._

### 3. Profile PIN accessibility

- Command: `If Netflix asks for a profile passcode, type [redacted] one digit at a time and continue until the profile lock is gone and Netflix browse or home is visible.`
- Continuation policy: `continue-and-report`
- Execution: `passed` - AVA command completed functional checks.
- Execution tool calls: `6` (`0` errors)
- Checkpoints: `after`
- Evidence: omitted from sanitized sample.

#### Web Evidence

- Web Dom Snapshot warning: CDP endpoint was not available; web/W3C validation skipped.

#### Findings

_No findings._

### 4. Netflix search results accessibility

- Command: `Search for Boyfriend on Demand within Netflix using the visible Search control, and wait until visible search results for that title are on screen.`
- Continuation policy: `continue-and-report`
- Execution: `passed` - AVA command completed functional checks.
- Execution tool calls: `4` (`0` errors)
- Checkpoints: `after`
- Evidence: omitted from sanitized sample.

#### Web Evidence

- Web Dom Snapshot warning: CDP endpoint was not available; web/W3C validation skipped.

#### Findings

_No findings._

### 5. Title details and playback actionability

- Command: `Open Boyfriend on Demand from the visible Netflix search results, then play the first episode.`
- Continuation policy: `continue-and-report`
- Execution: `passed` - AVA command completed functional checks.
- Execution tool calls: `5` (`0` errors)
- Checkpoints: `after`
- Evidence: omitted from sanitized sample.

#### Web Evidence

- Web Dom Snapshot warning: CDP endpoint was not available; web/W3C validation skipped.

#### Findings

_No findings._
