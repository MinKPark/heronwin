---
id: netflix-profile-selection-and-playback
group: netflix
priority: 400
summary: "Handle Netflix-specific profile selection, search, and playback follow-through."
preferred_tools:
  - eyesandhands/describe_selected_window
  - eyesandhands/capture_selected_window_screenshot
  - eyesandhands/click_selected_window_element
  - eyesandhands/invoke_selected_window_element
  - eyesandhands/set_selected_window_element_value
  - eyesandhands/send_input_to_window
activation:
  when_any_keywords:
    - netflix
    - netflix com
    - who s watching
    - manage profiles
    - add profile
    - continue watching
    - my list
    - profile
  when_any_tools:
    - describe_selected_window
    - capture_selected_window_screenshot
    - click_selected_window_element
    - invoke_selected_window_element
    - set_selected_window_element_value
    - send_input_to_window
applies_when:
  - The user is trying to open, search, select a profile, browse, or play something in Netflix.
---

# Skill: Netflix Profile Selection And Playback

## Workflow

1. Treat Netflix as a layered surface: browser host, Netflix chrome, then title or playback target.
2. If a profile picker is visible, resolve that gate before browsing titles or claiming playback.
3. Prefer exact named Netflix tiles, rows, profiles, and title matches over generic wrappers.
4. After any play or open action, verify the resulting Netflix screen before continuing.

## Profile Rules

- If a Netflix profile picker is visible and the user named an exact profile, target that exact named profile tile.
- If the profile picker is visible but the user did not name a profile, stop after reporting that profile selection is still required.
- Do not guess between visible profiles and do not treat `Manage Profiles`, `Add Profile`, or `Done` as substitutes for the requested profile.
- When a named profile tile is exposed in the refreshed tree, reuse that exact full `path` or `uiPath` rather than shortening it to a parent wrapper.

## Search And Title Rules

- If the user asked for a Netflix title and an exact named result tile is already visible, prefer that exact tile over re-focusing the search field.
- Treat Netflix hero banners, centered previews, and generic home navigation as lower-priority targets than an exact named title match.
- If the search field or result list changes after entry, refresh the state before deciding the next click.

## Playback Rules

- Do not claim that Netflix started playback until the refreshed UI or screenshot shows a playback or title-detail state consistent with the requested action.
- If a click returns to browse, home, or profile-management surfaces instead of the requested title, treat that as drift and recover from the newest evidence.
- When the UI tree and screenshot disagree, prefer the screenshot for visible Netflix state and use the tree for exact target paths when they are still consistent.
