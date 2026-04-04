---
id: ui-refresh-and-evidence
summary: "Refresh the UI state after actions and gather stronger evidence when confidence is low."
preferred_tools:
  - eyesandhands/describe_selected_window
  - eyesandhands/describe_selected_window_focus
  - eyesandhands/capture_selected_window_screenshot
applies_when:
  - The UI may have changed after an action.
  - The current UI Automation tree looks sparse, stale, ambiguous, or misleading.
---

# Skill: UI Refresh And Evidence

## Workflow

1. After any state-changing action, refresh the active app state before deciding the next step.
2. Use focused-element evidence to understand current focus, but use whole-window evidence to understand the interaction surface.
3. If confidence is still low, capture a screenshot and use it as the source of truth for visible state.

## Refresh Rules

- After any action that can change the UI state, re-enumerate the selected window before deciding what happened.
- Treat shortcuts, text entry, and other keyboard input as actions that still require follow-up verification.
- Do not assume the previous focus path is still valid after search results, dialogs, overlays, or navigation updates.
- When focus remains inside a search box or another text control, avoid relying on movement keys until the refreshed state has been inspected.
- Prefer `eyesandhands/describe_selected_window` after state-changing actions so newly exposed elements can be discovered.
- Use `eyesandhands/describe_selected_window_focus` to confirm what currently owns focus, but do not treat that focused subtree alone as the full interaction surface when the UI may have expanded or changed.

## Confidence Rules

- Treat UI Automation as insufficient when it exposes only generic containers, very sparse metadata, or otherwise does not support a useful description of what is visibly on screen.
- If keystrokes and UI inspection are not enough to determine the next step, capture the selected window and inspect the screenshot before continuing.
- When using a screenshot fallback, say briefly that the screenshot is the stronger evidence source for the visible UI state.

## Reporting Rules

- Separate confirmed observations from inferences.
- Do not pretend that the UI tree contains visual details that it did not expose.
- If the current visible screen state is still uncertain after refresh, say that directly instead of guessing.
