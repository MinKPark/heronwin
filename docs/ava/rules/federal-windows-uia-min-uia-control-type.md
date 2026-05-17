# federal-windows-uia-min-uia-control-type

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-CONTROL-TYPE`
- Source: UI Automation ControlType property
- Finding prefix: `AVA-ROLE-MISSING-`

## Purpose

UI Automation nodes should expose a specific `ControlType` so tools and assistive technologies can understand what kind of element is present.

## Evidence

AVA evaluates captured `describe_window` and `describe_window_focus` compact UI Automation trees.

## Passing Condition

Relevant nodes expose a recognized control type instead of an empty or unknown role.

## Review Notes

Check whether the node is a meaningful user-facing element. Structural containers can be acceptable when they are not presented as controls.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Control Types Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controltypesoverview)
- Last updated: 2026-05-17
