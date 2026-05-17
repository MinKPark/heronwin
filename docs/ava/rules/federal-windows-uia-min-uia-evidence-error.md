# federal-windows-uia-min-uia-evidence-error

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-EVIDENCE-ERROR`
- Source: UI Automation deterministic evidence
- Finding prefix: `AVA-EVIDENCE-ERROR-`

## Purpose

Evidence collection tools should complete successfully. Tool errors reduce confidence in the step's accessibility results.

## Evidence

AVA checks error records in the step evidence manifest.

## Passing Condition

No evidence collector record for the step has `error` status.

## Review Notes

Open the linked manifest and raw output to see which tool failed and whether the failure is environmental or product-specific.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
