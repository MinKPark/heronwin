# federal-web-min-web-wcag-2.1.1-actionability

## Metadata

- Profile: `federal-web-min`
- Rule ID: `WEB-WCAG-2.1.1-ACTIONABILITY`
- Source: WCAG 2.0 SC 2.1.1 Keyboard
- Finding prefix: `AVA-ACTION-MISSING-`

## Purpose

Interactive web-derived controls should expose an automation action that indicates keyboard-operable behavior.

## Evidence

AVA evaluates `availableActions` and related metadata from captured UI Automation trees.

## Passing Condition

Actionable nodes expose an action such as focus, invoke, select, expand, collapse, set value, or scroll.

## Review Notes

This rule is an approximation over UI Automation evidence. Confirm keyboard behavior manually for high-impact controls.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [WCAG 2.0 Success Criterion 2.1.1 Keyboard](https://www.w3.org/TR/WCAG20/#keyboard-operation-keyboard-operable)
- Last updated: 2026-05-17
