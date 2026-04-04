---
id: search-and-enumeration
summary: "Search within an app or enumerate visible UI state without over-claiming."
preferred_tools:
  - eyesandhands/describe_selected_window
  - eyesandhands/describe_selected_window_focus
  - eyesandhands/focus_selected_window_element
  - eyesandhands/invoke_selected_window_element
  - eyesandhands/send_input_to_window
  - eyesandhands/capture_selected_window_screenshot
applies_when:
  - The user asks to search within an app.
  - The user asks what is visible, what results are present, or which items are available.
---

# Skill: Search And Enumeration

## Workflow

1. Identify the currently visible search or result surface.
2. Enumerate what is visibly exposed before drilling deeper.
3. When the request is about a list or tree item, also note the hosting control when available.
4. For multi-step requests, finish the current search stage before drifting into later open, play, or navigation stages.
5. Report visible findings first and tool limitations second.

## Search Rules

- When the user asks to search within Explorer or another app, first identify the visible search-related UI element if possible.
- If the current app already exposes a visible search affordance such as a button, field, or search tab, prefer that site-native or app-native path over generic keyboard wandering.
- When a visible search control is exposed in the refreshed tree, reuse the full exact `path` or `uiPath` from that fresh tree. Do not trim segments or guess a nearby control.
- If the tool surface cannot directly type into or invoke the relevant control, say so plainly.
- If a direct search-control action fails, refresh the window state and pick a new exact target from the latest evidence instead of reusing a guessed variant of the old path.
- After entering or submitting a search, expect the accessibility tree to lag behind the visible UI.
- Retry `eyesandhands/describe_selected_window` or `eyesandhands/describe_selected_window_focus` a few times with short waits before concluding that the result is not exposed yet.
- Treat newly appearing named results as fresher evidence than an earlier sparse tree snapshot.
- Do not count a search step as complete until the requested query or visible matching results are actually on screen.
- If the search request is only the first stage of a larger task, stop that stage at confirmed visible results rather than skipping ahead to open or playback claims.

## Enumeration Rules

- Prefer enumerating the currently visible UI elements before doing any scrolling.
- If a selected element is a list or tree item, enumerate the other visible siblings at the same level before drilling deeper.
- When reporting a selected list or tree item, also name the hosting element when it is available.
- If the host viewport or scrollbar gives positive evidence that more items exist, say so.
- If there is no positive evidence that more items exist, treat that as no additional visible item.
- Prefer explicit scrollbar evidence over geometric inference when the host view is virtualized.

## Limitation Rules

- When enumeration is partial because of UI virtualization or tool limits, explain the limitation in one short sentence and then report the visible findings.
- If UI Automation does not expose all rows of a list, say that directly and distinguish visible rows from the full underlying data set.
- If a search result is visibly present on screen and the accessibility tree exposes a named matching result, prefer targeting that exact result before switching to generic keyboard navigation or screenshot-driven discovery.
