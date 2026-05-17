# federal-windows-uia-min-uia-name-property

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-NAME-PROPERTY`
- Source: UI Automation Name property
- Finding prefix: `AVA-NAME-MISSING-`

## Purpose

Interactive or meaningful UI Automation nodes should expose a usable `Name` so assistive technologies and automation can identify the control.

## Evidence

AVA evaluates captured `describe_window` and `describe_window_focus` compact UI Automation trees.

## Passing Condition

Relevant nodes expose a non-empty accessible name or are intentionally structural and not user-facing.

## Review Notes

Confirm whether an unnamed node represents a real control. If it is decorative or structural, the validator may need a narrower ignore rule.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)
- Last updated: 2026-05-17
