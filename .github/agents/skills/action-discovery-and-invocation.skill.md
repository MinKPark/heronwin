---
id: action-discovery-and-invocation
summary: "Find and perform a requested action in the current app."
preferred_tools:
  - eyesandhands/list_main_menu_items
  - eyesandhands/list_context_menu_items
  - eyesandhands/invoke_main_menu_item
  - eyesandhands/invoke_context_menu_item
  - eyesandhands/invoke_selected_window_element
  - eyesandhands/focus_selected_window_element
  - eyesandhands/send_input_to_window
applies_when:
  - The user asks to click, press, open, select, save, rename, or otherwise activate something in the current app.
---

# Skill: Action Discovery And Invocation

## Workflow

1. Identify the requested target or action from the currently selected app.
2. Check menus before guessing at a click path.
3. If the requested control is directly exposed in the UI tree, prefer direct element invocation.
4. Use keyboard input only when the user explicitly asked for keys, shortcuts, or text entry, or when it is a deliberate fallback after re-checking state.

## Menu Rules

- Check `eyesandhands/list_main_menu_items` first.
- If the requested action is not clearly in the main menu, check `eyesandhands/list_context_menu_items` for the currently focused element.
- If more than one menu action looks plausible, ask the user to confirm before invoking anything.
- When using a context menu, say briefly which element the menu belongs to.

## Direct Action Rules

- When the user explicitly asks to click, press, open, select, or invoke a visible UI element and you have an element path for it, use `eyesandhands/invoke_selected_window_element`.
- If the visible control is exposed only through a sparse or generic subtree, still prefer `eyesandhands/invoke_selected_window_element` over ad hoc `Tab` or arrow-key retries.
- Re-check focus before using keyboard fallback when possible.

## Keyboard Rules

- Use `eyesandhands/send_input_to_window` when the user explicitly asks for a shortcut, a named key press, or text entry.
- Do not use it as the first fallback for activating visible controls.
- Do not chain repeated standalone `Tab` presses without re-checking focus or the refreshed window tree after each navigation attempt.
- Treat keyboard navigation as a materially different fallback from direct invocation or click paths.

## Stop Conditions

- If the action still is not confirmed after a small number of materially different attempts, stop and ask the user for guidance.
