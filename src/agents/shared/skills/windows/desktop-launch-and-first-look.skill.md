---
id: desktop-launch-and-first-look
group: windows
priority: 100
summary: "Open or switch to an app, then verify the first visible state."
preferred_tools:
  - cognition/list_windows
  - execution/activate_window
  - cognition/list_taskbar_items
  - execution/activate_taskbar_app
  - execution/launch_application
  - cognition/describe_window
  - cognition/capture_window_screenshot
activation:
  when_any_intents:
    - launch_request
    - direct_browser_navigation_request
  when_any_tools:
    - list_windows
    - activate_window
    - list_taskbar_items
    - activate_taskbar_app
    - launch_application
applies_when:
  - The user asks to open, start, launch, switch to, or bring forward an application.
---

# Skill: Desktop Launch And First Look

Treat this skill as the startup phase for a larger task. It is complete once the
intended app window is foregrounded and the first visible state is verified from
fresh evidence.

## Workflow

1. First check whether a likely app window is already visible, using any startup inventory already provided in the prompt before asking for another window list.
2. If a matching window already exists, select it instead of launching a second instance.
3. If the app does not appear to be open, continue into the available launch routes rather than stopping after the initial inspection.
4. Prefer taskbar-based launch paths before asking the user to launch the app manually.
5. After selecting or launching the app, verify that the selected window is the intended target before going deeper.
6. Describe the first visible state from evidence before taking more actions.
7. Once startup is verified, continue from that fresh first-look state instead of repeating startup discovery inside the next phase.

## Tool Preference

- Use `cognition/list_windows` before launching only when fresh startup inventory was not already provided in the prompt.
- If the prompt already includes fresh startup inventory from `list_windows`, treat that as the window check and do not call `cognition/list_windows` again just to repeat it.
- If a likely window already exists, use `execution/activate_window`.
- If startup inventory or recent evidence already exposed a specific `windowHandle`, call `execution/activate_window` with that exact `windowHandle`.
- Do not call `execution/activate_window` with fuzzy or invented title fields such as `titleContains` when a concrete `windowHandle` is available from `list_windows`.
- If the app is not open, inspect the taskbar with `cognition/list_taskbar_items`.
- If the app looks pinned or already present on the taskbar, use `execution/activate_taskbar_app`.
- If it is not clearly available as a taskbar app button, use `execution/launch_application`.
- Ask the user to launch the app manually only after the available window-selection and taskbar-based launch routes are unavailable, ambiguous, or fail.

## First-Look Rules

- After launch or selection, verify that the selected window matches the intended app.
- Startup only gets the app/window ready. If the user's same command also asks for a destination, page, file, search, or content action, continue into that next action from the fresh first-look state instead of stopping after foregrounding the app.
- Be cautious of splash screens, login prompts, updaters, and error dialogs.
- If startup surfaces an unexpected dialog, treat that dialog as the current target.
- Report the dialog title, visible message text, and available buttons before asking the user what to do next.

## Evidence Rules

- Use `cognition/describe_window` first for the initial app description. Request debug evidence only when you need exact full-tree debugging detail.
- If UI Automation only exposes generic containers, sparse metadata, or otherwise does not support a useful description, capture a screenshot and describe the visible UI from the image.
- When using the screenshot fallback, say briefly that the visual description comes from the screenshot because UI Automation was not detailed enough.

## Stop Conditions

- Stop and ask the user for guidance after reporting a startup error or warning dialog.
- Do not imply that an app opened successfully unless the selected window evidence supports it.


