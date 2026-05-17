# federal-windows-uia-min-uia-evidence-tree-parse

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-EVIDENCE-TREE-PARSE`
- Source: UI Automation deterministic evidence
- Finding prefix: `AVA-TREE-PARSE-`

## Purpose

Captured tree evidence should be valid JSON in the expected AVA evidence shape.

## Evidence

AVA parses raw `describe_window` and `describe_window_focus` outputs.

## Passing Condition

The raw evidence parses successfully and exposes readable original UI Automation tree evidence, or legacy compact tree evidence for older runs.

## Review Notes

This usually points to a tool output or serialization issue. Inspect the raw evidence file before treating product UI as the cause.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
