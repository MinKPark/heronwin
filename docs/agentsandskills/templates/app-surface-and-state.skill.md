---
id: exampleapp-surface-and-state
group: exampleapp
priority: 350
summary: "Shared ExampleApp surface model and state-verification rules."
preferred_tools:
  - cognition/describe_window
  - cognition/capture_window_screenshot
  - execution/click_window_element
  - execution/invoke_window_element
  - execution/set_window_element_text
  - execution/type_window_text
activation:
  when_any_keywords:
    - exampleapp
  when_any_tools:
    - describe_window
    - capture_window_screenshot
    - click_window_element
    - invoke_window_element
    - set_window_element_text
    - type_window_text
applies_when:
  - The user is acting inside ExampleApp and the request depends on interpreting ExampleApp state correctly.
---

# Skill: ExampleApp Surface And State

## Cross-Cutting Rules

- Treat ExampleApp as layered surfaces: host window, navigation chrome, primary content, then modals or transient controls.
- Prefer exact visible item, command, and control names from fresh evidence over generic containers.
- After any UI-changing action, verify the resulting ExampleApp surface before continuing.
- If the user only asks whether a surface or item is visible, report that state and stop.

## Surface Model

- Home: describe the visible entry points and what counts as being on the home surface.
- Search: describe how search is represented, how results appear, and what counts as a completed search.
- Detail: describe item detail pages, panels, or dialogs.
- Playback/editing/action surface: describe the app-specific work surface, if one exists.

## Action Rules

- Use visible labels and stable identifiers from the latest evidence.
- Do not click generic wrappers when a named child control is visible.
- Do not continue through a transition until fresh evidence confirms the new surface.
- Treat missing expected controls as a stop condition unless the skill defines a safe alternate path.

## Success And Stop Conditions

- Success means the target surface, item, or app state is visible in fresh evidence.
- If the requested target is not visible after a reasonable search or navigation step, report what is visible and stop.
- If the app shows an account, payment, permission, or destructive confirmation prompt, stop unless the user explicitly asked for that action.
