# federal-windows-uia-min-uia-keyboard-focus-evidence

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-KEYBOARD-FOCUS-EVIDENCE`
- Source: UI Automation keyboard focus evidence
- Finding prefix: `AVA-FOCUS-MISSING-`

## Purpose

Keyboard focus evidence should be available when AVA validates a step, so the report can show which UI Automation subtree was active after the command.

## Evidence

AVA uses `describe_window_focus` evidence and compares it with the broader window tree.

## Passing Condition

The focused element or focused subtree is captured and can be parsed.

## Review Notes

This rule usually means the run needs better focus capture, not necessarily that the product UI is inaccessible.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Properties Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-propertiesoverview)
- Last updated: 2026-05-17
