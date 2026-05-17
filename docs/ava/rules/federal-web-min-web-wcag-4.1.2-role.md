# federal-web-min-web-wcag-4.1.2-role

## Metadata

- Profile: `federal-web-min`
- Rule ID: `WEB-WCAG-4.1.2-ROLE`
- Source: WCAG 2.0 SC 4.1.2 Name, Role, Value
- Finding prefix: `AVA-ROLE-MISSING-`

## Purpose

Web-derived controls should expose a role so assistive technologies can understand the element type and interaction model.

## Evidence

AVA evaluates web content represented in captured UI Automation trees.

## Passing Condition

Relevant nodes expose a meaningful role or control type.

## Review Notes

If the element is structural, verify that it is not being treated as a control by the validator.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [WCAG 2.0 Success Criterion 4.1.2 Name, Role, Value](https://www.w3.org/TR/WCAG20/#ensure-compat-rsv)
- Last updated: 2026-05-17
