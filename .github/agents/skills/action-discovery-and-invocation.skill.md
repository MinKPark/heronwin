---
id: action-discovery-and-invocation
summary: "Find and perform a requested action in the current app."
preferred_tools:
  - eyesandhands/list_main_menu_items
  - eyesandhands/list_context_menu_items
  - eyesandhands/invoke_main_menu_item
  - eyesandhands/invoke_context_menu_item
  - eyesandhands/click_selected_window_element
  - eyesandhands/invoke_selected_window_element
  - eyesandhands/focus_selected_window_element
  - eyesandhands/send_input_to_window
applies_when:
  - The user asks to click, press, open, select, save, rename, or otherwise activate something in the current app.
---

# Skill: Action Discovery And Invocation

## Workflow

1. Identify the requested target or action from the currently selected app.
2. For conditional requests, decide first whether the condition is actually present on the current screen.
3. Check menus before guessing at a click path.
4. If the requested control is directly exposed in the UI tree, prefer direct element invocation.
5. Use keyboard input only when the user explicitly asked for keys, shortcuts, or text entry, or when it is a deliberate fallback after re-checking state.

## Menu Rules

- Check `eyesandhands/list_main_menu_items` first.
- If the requested action is not clearly in the main menu, check `eyesandhands/list_context_menu_items` for the currently focused element.
- If more than one menu action looks plausible, ask the user to confirm before invoking anything.
- When using a context menu, say briefly which element the menu belongs to.

## Direct Action Rules

- When the user explicitly asks to click, press, open, select, or invoke a visible UI element and you have an element path for it, use `eyesandhands/invoke_selected_window_element` first and prefer `eyesandhands/click_selected_window_element` when direct invocation is unavailable or unreliable for that visible target.
- If the user asks to open something from visible search results and a matching visible result tile or row is already on screen, target that visible result instead of re-entering the search query or re-focusing the search field.
- If the requested title is visible as a named result tile and a hover preview or play overlay is also visible, prefer the named matching result tile unless the preview itself clearly shows the same title.
- If the refreshed tree contains a named actionable element whose text exactly matches the requested title, use that exact named path and do not click an unnamed or differently named wrapper while the exact match exists.
- When you have an exact `path` or `uiPath` from a refreshed tree, reuse it exactly as shown. Do not shorten it, normalize it, or guess a neighboring path.
- If a conditional request says "if X is visible, do Y," then perform `Y` when `X` is present. Do not stop just because the target is visible.
- If the condition is absent, report a successful no-op instead of framing the step as failed or incomplete.
- For that successful no-op reply, prefer language like "No action was needed because the requested prompt was not present" and avoid phrases like "I did not click" or "I did not type" unless the user explicitly asked for a postmortem.
- For a conditional prompt, dialog, passcode, or overlay check, start with `eyesandhands/describe_selected_window` and, if needed, `eyesandhands/capture_selected_window_screenshot` before trying focus or invocation tools.
- Do not call `eyesandhands/describe_selected_window_focus` just to determine whether a conditional prompt, dialog, passcode, or overlay exists. Use focus inspection only after the prompt is already visible and you need to confirm the active input target.
- If the current selected window already appears to be the right app and the condition is absent in the fresh evidence, stop immediately instead of re-selecting the same window or attempting unrelated element actions.
- If the visible control is exposed only through a sparse or generic subtree, still prefer direct element targeting with `eyesandhands/invoke_selected_window_element` or `eyesandhands/click_selected_window_element` over ad hoc `Tab` or arrow-key retries.
- When a visible control is already exposed in the tree, do not replace direct targeting with repeated standalone `Tab` or arrow-key exploration.
- If a direct element activation attempt fails once, refresh the UI state and choose a fresh exact target from the new evidence rather than mutating the old path into an approximate guess.
- After a direct open or play action that changes the screen toward playback, refresh both the UI tree and a screenshot before deciding whether the requested content is actually playing.
- Re-check focus before using keyboard fallback when possible.

## Keyboard Rules

- Use `eyesandhands/send_input_to_window` when the user explicitly asks for a shortcut, a named key press, or text entry.
- Do not use it as the first fallback for activating visible controls.
- Do not chain repeated standalone `Tab` presses without re-checking focus or the refreshed window tree after each navigation attempt.
- After shortcuts, text entry, or other keyboard input that could change the UI, refresh state before claiming success.
- Treat keyboard navigation as a materially different fallback from direct invocation or click paths.

## Stop Conditions

- If the action still is not confirmed after a small number of materially different attempts, stop and ask the user for guidance.
