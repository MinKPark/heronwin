---
id: netflix-browse-and-play
group: netflix
priority: 450
summary: "Handle Netflix browse navigation, search results, title targeting, and play follow-through."
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
    - search
    - browse
    - shows
    - movies
    - games
    - new popular
    - continue watching
    - my list
    - play
    - episode
    - season
    - title
    - result
    - results
    - back to browse
  when_any_tools:
    - describe_window
    - capture_window_screenshot
    - click_window_element
    - invoke_window_element
    - set_window_element_text
    - press_window_key
    - type_window_text
applies_when:
  - The user is browsing Netflix, choosing a title, or trying to start playback.
---

# Skill: Netflix Browse And Play

## Home Navigation And Title Rules

- For pure Netflix search-entry requests, follow the narrower `netflix-search` skill first. This browse-and-play skill takes over once visible results, a title-detail page, or a playback target needs action.
- If Netflix home or browse navigation is visible and the user asks to browse, watch, open, or go to `Shows`, `Movies`, `Games`, `New & Popular`, or another visible top-level Netflix section, activate that matching visible nav item instead of stopping with an ambiguity-only reply.
- For title requests, accept minor ASR or spelling drift when there is one obvious visible Netflix near-match and then target the corrected exact visible title. Example: `Expandables` can map to a visible `The Expendables` title when it is the single clear match.
- If the user asked for a Netflix title and an exact named result tile is already visible, prefer that exact tile over re-focusing the search field.
- Treat Netflix hero banners, centered previews, and generic home navigation as lower-priority targets than an exact named title match.
- If the hero banner itself is the single obvious corrected match for the requested title, it is acceptable to use that visible hero target.

## Playback Follow-Through Rules

- Do not claim that Netflix started playback until the refreshed UI or screenshot shows a playback or title-detail state consistent with the requested action.
- If a click lands on a title-detail page with controls such as `Back to Browse`, treat that as title-detail evidence and continue from there instead of claiming playback already started.
- If playback is not visible yet, continue from the freshest title-detail, row, or result state instead of stopping after the first successful click.
- For multi-step requests such as search, open, and play, do not stop after the earlier stage if the requested play state is still unfinished.

