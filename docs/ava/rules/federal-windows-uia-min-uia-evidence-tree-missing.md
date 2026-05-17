# federal-windows-uia-min-uia-evidence-tree-missing

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-EVIDENCE-TREE-MISSING`
- Source: UI Automation deterministic evidence
- Finding prefix: `AVA-TREE-MISSING-`

## Purpose

Captured evidence should include a compact UI Automation tree so AVA can run deterministic validators.

## Evidence

AVA inspects captured evidence records for a `compactTree` payload.

## Passing Condition

At least one relevant evidence record contains original UI Automation tree data for the window or focused element, or legacy compact tree data for older runs.

## Review Notes

If screenshots exist but tree data is missing, the run can show visual state but cannot validate UI Automation structure.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
