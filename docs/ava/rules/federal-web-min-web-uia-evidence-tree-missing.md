# federal-web-min-web-uia-evidence-tree-missing

## Metadata

- Profile: `federal-web-min`
- Rule ID: `WEB-UIA-EVIDENCE-TREE-MISSING`
- Source: UI Automation evidence for federal-web-min
- Finding prefix: `AVA-TREE-MISSING-`

## Purpose

Captured evidence should include UI Automation tree data so AVA can evaluate web-derived accessibility rules.

## Evidence

AVA inspects captured evidence records for a `compactTree` payload.

## Passing Condition

At least one relevant evidence record contains original UI Automation tree data for the page or focused subtree, or legacy compact tree data for older runs.

## Review Notes

If screenshots exist but tree data is missing, the run can show visual state but cannot validate accessible structure.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
