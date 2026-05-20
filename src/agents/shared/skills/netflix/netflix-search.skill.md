---
id: netflix-search
group: netflix
priority: 430
summary: "Handle Netflix in-site search entry and visible result verification."
preferred_tools:
  - cognition/describe_window
  - cognition/capture_window_screenshot
  - execution/invoke_window_element
  - execution/click_window_element
  - execution/set_window_element_text
  - execution/type_window_text
activation:
  when_all_keywords:
    - netflix
  when_any_keywords:
    - search
    - results
    - result
    - title
  when_any_tools:
    - describe_window
    - capture_window_screenshot
    - invoke_window_element
    - click_window_element
    - set_window_element_text
    - type_window_text
applies_when:
  - The user is searching for a Netflix title or waiting for Netflix search results.
---

# Skill: Netflix Search

## Stable Search Entry Batch

- Treat opening Netflix Search and entering the requested query as one stable same-surface search-entry phase when Netflix home or browse is visible, the visible `Search` control has an exact path, and the query text is known.
- If the visible Netflix `Search` control is already an editable element or exposes `set_value`, call `set_window_element_text` on that exact path with the query as the first search-entry action. Do not invoke the Search control first when direct value entry is available on that same visible control.
- In the closed-search case where the visible `Search` control is invoke-only, return `invoke_window_element` for the exact visible Netflix `Search` control followed by `type_window_text` with the query in the same tool-call response. Netflix normally focuses the search field after the Search control opens, so do not spend a separate LLM attempt merely to decide whether to type the already-known query.
- Direct Search value entry, or the `Search` invocation plus query typing pair when direct value entry is unavailable, is an explicit exception to the default one-tool-at-a-time preference. A response that only opens the Search control but omits the known query is incomplete unless the Search control target, current Netflix surface, or query text is uncertain.
- Do not return only `invoke_window_element` for the visible Netflix `Search` control when the query is already known. Either set the editable Search control directly, or open Search and type the query in that same tool-call response.
- Stop that batch after query entry. Wait for fresh Netflix evidence before deciding whether visible results appeared, whether a retry is needed, or whether the requested result can be opened.

## Already-Open Search Field

- If the Netflix search input is already visible and exposes an exact editable path, prefer `set_window_element_text` on that path with the requested query.
- If the search input is already focused but no reliable editable path is available, use `type_window_text` with the requested query.
- Do not re-invoke the Search control when the search field or matching search results are already visible.

## Result Verification

- Do not claim the search step is complete until fresh evidence shows the requested query in the search field or visible Netflix results that include the requested title.
- If the result list includes the exact requested title, stop the search stage there unless the current user command also asks to open or play it.
- If the fresh UI tree is sparse after query entry, capture a screenshot before concluding the result is missing.
- If the query did not appear or the results stayed unchanged, retry with one materially different entry method from the newest evidence instead of repeating the same Search invocation.
