---
id: netflix-profile-and-pin
group: netflix
priority: 400
summary: "Handle Netflix profile selection and profile-lock PIN flows."
preferred_tools:
  - cognition/describe_window
  - cognition/capture_window_screenshot
  - execution/click_window_element
  - execution/invoke_window_element
  - execution/set_window_element_text
  - execution/press_window_key
  - execution/type_window_text
activation:
  when_all_keywords:
    - netflix
  when_any_keywords:
    - profile
    - profiles
    - who s watching
    - profile lock
    - forgot pin
    - manage profiles
    - add profile
    - pin
  when_any_tools:
    - describe_window
    - capture_window_screenshot
    - click_window_element
    - invoke_window_element
    - set_window_element_text
    - press_window_key
    - type_window_text
applies_when:
  - The user is choosing a Netflix profile or entering a Netflix profile-lock PIN.
---

# Skill: Netflix Profile And PIN

## Profile Rules

- If a Netflix profile picker is visible and the user named an exact profile, target that exact named profile tile.
- If the spoken profile name is not exact but there is one obvious visible ASR repair, such as `mean` when only `Min` is visible, prefer that repaired exact visible profile over treating the request as absent.
- If more than one visible profile could match the spoken name, do not guess; ask for a short clarification instead.
- If the profile picker is visible but the user did not name a profile, stop after reporting that profile selection is still required.
- Do not guess between visible profiles and do not treat `Manage Profiles`, `Add Profile`, or `Done` as substitutes for the requested profile.
- When a named profile tile is exposed in the refreshed tree, reuse that exact full `path` or `uiPath` rather than shortening it to a parent wrapper.

## Profile Lock And PIN Rules

- When Netflix shows a profile lock or PIN screen with separate digit boxes, treat that as a structured PIN-entry flow rather than a freeform text field.
- Prefer per-digit entry over bulk text when focus advances box by box; one digit at a time is more reliable than sending the whole PIN as one text string.
- If the code is a four-digit PIN, enter it as four separate single-character actions; do not send the full PIN as one `press_window_key` text value or one bulk value-set call on a four-box PIN screen.
- After each digit or delete action, verify which PIN box is focused or filled before entering the next digit.
- While the PIN screen is still active, treat bare spoken digits and obvious ASR variants as the next PIN digit when they fit the current sequence. Examples include `five`, `seven`, and a transcription like `nein` when the flow clearly indicates the user likely meant `nine`.
- Do not switch languages or treat a likely spoken digit as casual conversation while Netflix is still waiting on the remaining PIN digit.
- Do not claim the PIN was accepted until fresh Netflix home, browse, title-detail, or playback evidence replaces the lock prompt.

