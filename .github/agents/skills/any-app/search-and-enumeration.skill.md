---
id: search-and-enumeration
group: any-app
priority: 210
summary: "Search within an app or enumerate visible UI state without over-claiming."
preferred_tools:
  - cognition/describe_window_compact
  - cognition/describe_window_focus_compact
  - execution/focus_window_element
  - execution/click_window_element
  - execution/invoke_window_element
  - execution/set_window_element_text
  - execution/press_window_key
  - execution/type_window_text
  - cognition/capture_window_screenshot
activation:
  when_any_intents:
    - search_or_enumeration_request
  when_any_tools:
    - describe_window
    - describe_window_compact
    - describe_window_focus
    - describe_window_focus_compact
    - capture_window_screenshot
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
- If the currently selected window already appears to be the correct app or site for the request, inspect that current window first. Do not begin an in-site search by calling `list_windows` or `list_taskbar_items` unless fresh evidence suggests the selected window is no longer the intended surface.
- If the currently selected window is already the correct app or site, do not call `activate_window` again with a broad title match before using the in-page search controls.
- If the user explicitly asked to search within the current site or app, do not use the browser address bar or an external search engine page to satisfy that request.
- If the current site is temporarily in fullscreen playback, a preview overlay, or another transient mode that hides the in-site search surface, first return to a normal in-site browsing state with site-native controls before searching.
- When a visible search control is exposed in the refreshed tree, reuse the full exact `path` or `uiPath` from that fresh tree. Do not trim segments or guess a nearby control.
- When the refreshed tree exposes a visible editable search field and the tools include `execution/set_window_element_text`, prefer setting that exact field value directly over blind window-level typing.
- Use `execution/press_window_key` for submit keys such as `Enter` only after the query text is confirmed in the intended visible field or the direct value-entry tool is unavailable.
- If a visible matching search result is already on screen, prefer that result tile or row over the search field. Do not re-enter the query unless fresh evidence shows the visible results are gone.
- If the previous turn already confirmed that the requested title is visible in the current search results, treat that exact visible title as the primary target on the next turn and avoid resetting the page through site-level navigation.
- If the requested title is visible as a named result tile and a hover preview or centered play overlay is also visible, prefer the named matching result tile unless the preview itself clearly matches the same title.
- If the refreshed tree contains multiple named result candidates, choose the exact title match. Do not substitute a nearby title, centered preview, or unnamed wrapper when the requested title has its own named actionable element.
- When a visible matching result tile or row is clearly the requested target and the tools include `execution/click_window_element`, prefer that exact-path click over guessed keyboard navigation if direct invocation is unavailable or unreliable.
- If the tool surface cannot directly type into or invoke the relevant control, say so plainly.
- If a direct search-control action fails, refresh the window state and pick a new exact target from the latest evidence instead of reusing a guessed variant of the old path.
- After entering or submitting a search, expect the accessibility tree to lag behind the visible UI.
- Retry `cognition/describe_window_compact` or `cognition/describe_window_focus_compact` a few times with short waits before concluding that the result is not exposed yet.
- Treat newly appearing named results as fresher evidence than an earlier sparse tree snapshot.
- If the user asked to wait until visible search results are on screen, keep that wait-and-refresh loop inside the same turn. Do not stop with "search is in progress" after only one sparse refresh while stronger evidence is still available.
- If the refreshed tree is still sparse after search entry but the URL, page state, or surrounding UI indicates the app is already on the results surface, capture a screenshot and use it as the source of truth for whether visible results are actually on screen.
- If the screenshot still shows the search field placeholder or an empty field after text entry, treat that as text entry not yet confirmed and retry with a materially different method.
- Do not count a search step as complete until the requested query or visible matching results are actually on screen.
- External search-engine results do not satisfy a request that explicitly said to search within the current site or app. Repair back to the intended in-site search flow instead of treating that drift as success.
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


