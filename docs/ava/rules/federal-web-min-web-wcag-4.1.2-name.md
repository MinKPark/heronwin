# federal-web-min-web-wcag-4.1.2-name

## Metadata

- Profile: `federal-web-min`
- Rule ID: `WEB-WCAG-4.1.2-NAME`
- Source: WCAG 2.0 SC 4.1.2 Name, Role, Value
- Finding prefix: `AVA-NAME-MISSING-`

## Purpose

Web-derived controls should expose an accessible name so assistive technologies can identify their purpose.

## Evidence

AVA evaluates web content represented in captured UI Automation trees.

## Passing Condition

Relevant interactive or meaningful nodes expose a non-empty accessible name.

## Review Notes

Confirm the node corresponds to real web UI. Browser or host-shell nodes may need a Windows UIA profile instead.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [WCAG 2.0 Success Criterion 4.1.2 Name, Role, Value](https://www.w3.org/TR/WCAG20/#ensure-compat-rsv)
- Last updated: 2026-05-17
