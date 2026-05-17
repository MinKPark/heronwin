# federal-windows-uia-min-uia-evidence-missing

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-EVIDENCE-MISSING`
- Source: UI Automation deterministic evidence
- Finding prefixes: `AVA-EVIDENCE-MISSING-`, `AVA-TREE-EVIDENCE-MISSING-`

## Purpose

AVA needs deterministic UI Automation evidence to validate a step. Missing evidence means the finding set is incomplete.

## Evidence

AVA checks the step evidence manifest and raw evidence records.

## Passing Condition

The step has captured UI Automation evidence records with usable raw output.

## Review Notes

Check whether the validation config provided a deterministic window handle and whether the evidence collector was available.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
