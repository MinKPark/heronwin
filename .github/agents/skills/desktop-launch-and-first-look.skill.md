---
id: desktop-launch-and-first-look
summary: "Open or switch to an app, then verify the first visible state."
preferred_tools:
  - eyesandhands/list_windows
  - eyesandhands/select_window
  - eyesandhands/list_taskbar_elements
  - eyesandhands/select_taskbar_app
  - eyesandhands/launch_app_via_taskbar_search
  - eyesandhands/describe_selected_window
  - eyesandhands/capture_selected_window_screenshot
activation:
  when_any_intents:
    - launch_request
    - direct_browser_navigation_request
  when_any_tools:
    - list_windows
    - select_window
    - list_taskbar_elements
    - select_taskbar_app
    - launch_app_via_taskbar_search
applies_when:
  - The user asks to open, start, launch, switch to, or bring forward an application.
---

# Skill: Desktop Launch And First Look

## Workflow

1. First check whether a likely app window is already visible.
2. If a matching window already exists, select it instead of launching a second instance.
3. If the app does not appear to be open, continue into the available launch routes rather than stopping after the initial inspection.
4. Prefer taskbar-based launch paths before asking the user to launch the app manually.
5. After selecting or launching the app, verify that the selected window is the intended target before going deeper.
6. Describe the first visible state from evidence before taking more actions.

## Tool Preference

- Use `eyesandhands/list_windows` before launching.
- If a likely window already exists, use `eyesandhands/select_window`.
- If recent evidence already exposed a specific `windowHandle`, prefer that exact handle over a broader title match.
- If the app is not open, inspect the taskbar with `eyesandhands/list_taskbar_elements`.
- If the app looks pinned or already present on the taskbar, use `eyesandhands/select_taskbar_app`.
- If it is not clearly available as a taskbar app button, use `eyesandhands/launch_app_via_taskbar_search`.
- Ask the user to launch the app manually only after the available window-selection and taskbar-based launch routes are unavailable, ambiguous, or fail.

## First-Look Rules

- After launch or selection, verify that the selected window matches the intended app.
- Be cautious of splash screens, login prompts, updaters, and error dialogs.
- If startup surfaces an unexpected dialog, treat that dialog as the current target.
- Report the dialog title, visible message text, and available buttons before asking the user what to do next.

## Evidence Rules

- Use `eyesandhands/describe_selected_window` first for the initial app description.
- If UI Automation only exposes generic containers, sparse metadata, or otherwise does not support a useful description, capture a screenshot and describe the visible UI from the image.
- When using the screenshot fallback, say briefly that the visual description comes from the screenshot because UI Automation was not detailed enough.

## Stop Conditions

- Stop and ask the user for guidance after reporting a startup error or warning dialog.
- Do not imply that an app opened successfully unless the selected window evidence supports it.
