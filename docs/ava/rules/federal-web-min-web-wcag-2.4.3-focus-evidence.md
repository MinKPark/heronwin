# federal-web-min-web-wcag-2.4.3-focus-evidence

## Metadata

- Profile: `federal-web-min`
- Rule ID: `WEB-WCAG-2.4.3-FOCUS-EVIDENCE`
- Source: WCAG 2.0 SC 2.4.3 Focus Order
- Finding prefix: `AVA-FOCUS-MISSING-`

## Purpose

AVA should capture focused web content evidence after each step so reviewers can inspect focus state and order.

## Evidence

AVA uses `describe_window_focus` focused-element tree evidence.

## Passing Condition

The focused element or focused subtree is captured and parseable.

## Review Notes

This finding does not prove focus order is wrong; it means AVA lacks the focused-state evidence needed to evaluate it.

## Source And Updates

- AVA source: [AvaProfileCatalog.cs](../../../src/assistants/ava/AvaProfileCatalog.cs)
- Upstream reference: [WCAG 2.0 Success Criterion 2.4.3 Focus Order](https://www.w3.org/TR/WCAG20/#navigation-mechanisms-focus-order)
- Last updated: 2026-05-17
