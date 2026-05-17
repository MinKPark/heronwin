# federal-web-min-web-uia-evidence-tree-parse

## Metadata

- Profile: `federal-web-min`
- Rule ID: `WEB-UIA-EVIDENCE-TREE-PARSE`
- Source: UI Automation evidence for federal-web-min
- Finding prefix: `AVA-TREE-PARSE-`

## Purpose

Captured evidence should be valid JSON in the expected AVA UI Automation tree shape.

## Evidence

AVA parses raw `describe_window` and `describe_window_focus` outputs.

## Passing Condition

The raw evidence parses successfully and exposes readable original UI Automation tree evidence, or legacy compact tree evidence for older runs.

## Review Notes

This usually indicates a tool output or serialization issue rather than a product accessibility issue.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
