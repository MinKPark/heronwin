# federal-windows-uia-min-uia-control-pattern

## Metadata

- Profile: `federal-windows-uia-min`
- Rule ID: `UIA-CONTROL-PATTERN`
- Source: UI Automation control patterns
- Finding prefix: `AVA-ACTION-MISSING-`

## Purpose

Actionable UI Automation nodes should expose at least one usable action or control pattern, such as invoke, select, expand, collapse, focus, set value, scroll, or a related operation.

## Evidence

AVA evaluates each captured UI Automation node's `availableActions` and related metadata.

## Passing Condition

An actionable node has an action that automation can call, or the node is reclassified as non-actionable structural content.

## Review Notes

Use the report's Trace column and linked evidence manifest to inspect the actual UI path. Browser chrome and web content can both appear in this profile.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [Microsoft UI Automation Control Patterns Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-control-patterns-overview)
- Last updated: 2026-05-17
