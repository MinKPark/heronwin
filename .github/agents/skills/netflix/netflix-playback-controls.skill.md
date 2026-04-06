---
id: netflix-playback-controls
group: netflix
priority: 500
summary: "Handle Netflix playback controls, audio changes, and subtitle changes during active playback."
preferred_tools:
  - eyesandhands/describe_selected_window
  - eyesandhands/capture_selected_window_screenshot
  - eyesandhands/click_selected_window_element
  - eyesandhands/invoke_selected_window_element
  - eyesandhands/set_selected_window_element_value
  - eyesandhands/send_input_to_window
activation:
  when_all_keywords:
    - netflix
  when_any_keywords:
    - subtitle
    - subtitles
    - caption
    - captions
    - audio
    - audio and subtitles
    - playback controls
    - full screen
    - pause
    - seek
    - volume
  when_any_tools:
    - describe_selected_window
    - capture_selected_window_screenshot
    - click_selected_window_element
    - invoke_selected_window_element
    - set_selected_window_element_value
    - send_input_to_window
applies_when:
  - The user is changing Netflix playback controls, audio, or subtitle settings.
---

# Skill: Netflix Playback Controls

## Playback Controls, Audio, And Subtitle Rules

- Treat requests such as `turn off subtitles`, `turn on captions`, `change audio`, or `open audio and subtitles` during active Netflix playback as action requests, not status-only questions.
- If playback is active and only subtitle text or the bare video surface is visible, first try to reveal the playback controls by activating the current playback surface or focused playback group, then refresh the visible state before deciding the next control target.
- Treat browser host chrome such as `Minimize`, `Restore`, and `Close` as out of scope for Netflix playback-control requests unless the user explicitly asked for a window-management action.
- After controls are revealed, prefer exact visible playback controls such as `Audio & Subtitles`, `Subtitles`, `Off`, `English`, `Korean`, or other exact visible option names over generic container clicks.
- If the `Audio & Subtitles` panel is already open and the request is to turn subtitles off, do not click `Audio & Subtitles` again. Stay inside the open panel and target the exact visible `Off` option under the `Subtitles` section.
- When subtitle choices are visible, do not click unlabeled wrappers, the video surface, or a deeper unnamed child just because it is nested near the right area. Reuse the exact labeled subtitle option row or button whose visible text includes `Off`.
- If a checked subtitle option such as `English (CC)` is visible, treat the request as incomplete until `Off` itself becomes the selected subtitle option or the refreshed evidence clearly shows subtitles are disabled.
- If the first control-reveal attempt still leaves only the video frame or subtitle text visible, say that playback controls are not exposed yet rather than pretending the subtitle setting was changed.
- Do not stop at a screenshot-only description when the request is an actionable playback control change and the current Netflix playback surface is still active.
- Do not say `I'm turning them off`, `I'll open the subtitle menu`, or similar future-action promises unless this turn has already performed a desktop action toward that outcome.
- After changing a subtitle or audio option, verify the result from fresh evidence. Prefer confirmation that the visible subtitle line disappeared or that the selected option now shows `Off` before claiming success.