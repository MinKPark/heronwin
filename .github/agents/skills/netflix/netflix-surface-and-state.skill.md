---
id: netflix-surface-and-state
group: netflix
priority: 350
summary: "Shared Netflix surface model and state-verification rules."
preferred_tools:
  - cognition/describe_window
  - cognition/capture_window_screenshot
  - execution/click_window_element
  - execution/invoke_window_element
  - execution/set_window_element_text
  - execution/press_window_key
  - execution/type_window_text
activation:
  when_any_keywords:
    - netflix
    - netflix com
    - back to browse
    - continue watching
    - my list
  when_any_tools:
    - describe_window
    - capture_window_screenshot
    - click_window_element
    - invoke_window_element
    - set_window_element_text
    - press_window_key
    - type_window_text
applies_when:
  - The user is acting inside Netflix and the request depends on interpreting the current Netflix surface correctly.
---

# Skill: Netflix Surface And State

## Cross-Cutting Rules

1. Treat Netflix as a layered surface: browser host, Netflix chrome, then title, playback, or modal target.
2. If a profile picker, profile lock, or PIN surface is visible, resolve that gate before browsing titles or claiming playback progress.
3. Prefer exact named Netflix tiles, rows, profiles, tabs, and controls over generic wrappers.
4. After any open, play, or navigation action, verify the resulting Netflix screen before continuing.
5. Keep observation turns and action turns separate: a request to wait for or confirm a Netflix surface does not by itself authorize the next gated action on that surface.

- If the search field, result list, hero banner, or visible playback surface changes after an action, refresh the state before deciding the next click.
- If a click lands on a Netflix title-detail page with controls like `Back to Browse`, `Play`, or `More Like This`, report that exact title-detail state rather than overstating it as playback.
- If a click returns to browse, home, or profile-management surfaces instead of the requested title or control, treat that as drift and recover from the newest evidence.
- When the UI tree and screenshot disagree, prefer the screenshot for visible Netflix state and use the tree for exact target paths when they are still consistent.
- Pair this shared Netflix skill with the narrower Netflix skill that matches the active surface, such as profile and PIN, browse and play, or playback controls.

