# federal-web-min-web-uia-evidence-missing

## Metadata

- Profile: `federal-web-min`
- Rule ID: `WEB-UIA-EVIDENCE-MISSING`
- Source: UI Automation evidence for federal-web-min
- Finding prefixes: `AVA-EVIDENCE-MISSING-`, `AVA-TREE-EVIDENCE-MISSING-`

## Purpose

AVA needs captured UI Automation evidence to evaluate web-derived accessibility rules.

## Evidence

AVA checks the step evidence manifest and raw evidence records.

## Passing Condition

The step has captured evidence records with usable raw output.

## Review Notes

Check whether the run had a deterministic window handle and whether evidence tools were available.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
