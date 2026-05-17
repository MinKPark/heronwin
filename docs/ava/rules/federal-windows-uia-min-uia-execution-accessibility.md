# federal-windows-uia-min-uia-execution-accessibility

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-EXECUTION-ACCESSIBILITY`
- Source: Automated execution accessibility evidence
- Finding prefix: `AVA-EXECUTION-FAILED-`

## Purpose

Scenario execution should complete cleanly enough for AVA to collect trustworthy accessibility evidence for the step.

## Evidence

AVA uses command execution status, tool errors, observations, and any available UI Automation evidence.

## Passing Condition

The scenario command completes without functional failure and evidence collection proceeds.

## Review Notes

Treat this as a run-quality blocker first. Fix the scenario or automation failure before drawing conclusions from downstream accessibility findings.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview)
- Last updated: 2026-05-17
