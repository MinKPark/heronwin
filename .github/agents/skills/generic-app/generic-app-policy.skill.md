---
id: generic-app-policy
group: generic-app
priority: 140
summary: "Cross-app desktop guardrails for window targeting, close actions, verification, and reporting."
preferred_tools:
  - eyesandhands/list_windows
  - eyesandhands/select_window
  - eyesandhands/describe_selected_window
  - eyesandhands/capture_selected_window_screenshot
  - eyesandhands/invoke_selected_window_element
  - eyesandhands/click_selected_window_element
  - eyesandhands/send_input_to_window
activation:
  when_any_tools:
    - select_window
    - launch_app_via_taskbar_search
    - describe_selected_window
    - capture_selected_window_screenshot
    - send_input_to_window
    - click_selected_window_element
    - invoke_selected_window_element
    - focus_selected_window_element
    - set_selected_window_element_value
    - invoke_main_menu_item
    - invoke_context_menu_item
    - select_taskbar_app
applies_when:
  - The request depends on generic desktop behavior rather than app-specific rules.
  - Window targeting, closing, keyboard fallback, or verification decisions are needed.
---

# Skill: Generic App Policy

## Window Targeting

- When recent tool evidence already provides a stable target identifier such as `windowHandle`, prefer reusing that exact identifier over a broader text match.
- For requests to open or play a named app such as `Netflix`, do not satisfy the request by selecting an unrelated already-open window just because it exists.
- Select a matching app window only when its title matches the requested app. Otherwise, launch the requested app.
- If a browser or app window is already active from the previous turn, first decide whether the new request should stay inside that current app or be handled by Windows itself.
- Prefer staying in the current app for follow-up content, navigation, and selection requests. Use Windows or taskbar app actions only when the user explicitly asks to open, launch, switch, or manage apps or windows, or when the current app clearly cannot satisfy the request.

## Close Rules

- For generic close requests such as `close the app`, `close this`, or `close the window`, prefer closing the currently selected window instead of switching by a broad app-name match.
- If a visible top-right Close button or other explicit close control is available, use that first.
- If direct close controls are unavailable or unreliable, use `eyesandhands/send_input_to_window` with `Alt+F4`.
- Treat a close attempt as unconfirmed until the freshest post-action tree or screenshot shows the requested state change.

## Verification Rules

- Treat `eyesandhands/send_input_to_window` as explicit keyboard or text input that still requires follow-up verification. Key presses and text entry alone do not confirm that the intended visible UI result occurred.
- Final replies must report what already happened in this turn. Do not say things like `I'm turning it off now` or `I'll open it` unless you already performed a desktop action toward that outcome.
- For conditional instructions such as `if profile selection is visible` or `if a passcode is required`, inspect the current UI first.
- If the condition is present and the user named a target or action, perform that action instead of stopping just because the target is visible.
- If the condition is absent, treat that conditional step as a successful no-op and say so plainly rather than framing it as an incomplete or failed outcome.

## Discovery Rules

- If a profile picker is visible, do not guess between profile tiles or click controls such as `Manage Profiles`, `Add Profile`, or `Done` unless the user explicitly named that exact target.
- If no exact profile or picker control was requested, stop after reporting that profile selection is still required.
- Use `eyesandhands/launch_app_via_taskbar_search` only for launching Windows apps.
- When a browser window is already selected and the user wants to search for content such as a show, movie, article, or page, keep the interaction inside the browser or website instead of using Windows Search.
- If no more-specific app or site skill clearly applies and the user needs product-specific instructions, use the browser to look up guidance instead of improvising.
- Prefer official help, support, or documentation pages for well-known apps and services before third-party guides.