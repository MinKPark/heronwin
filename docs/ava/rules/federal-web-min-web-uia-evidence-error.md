# federal-web-min-web-uia-evidence-error

## Metadata

- Profile: `federal-web-min`
- Rule ID: `WEB-UIA-EVIDENCE-ERROR`
- Source: UI Automation evidence for federal-web-min
- Finding prefix: `AVA-EVIDENCE-ERROR-`

## Purpose

Evidence collection tools should succeed so web accessibility validation has trustworthy inputs.

## Evidence

AVA checks error records in the step evidence manifest.

## Passing Condition

No evidence collector record for the step has `error` status.

## Review Notes

Open the linked manifest and raw output to identify the failed tool and whether the issue is environmental.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
