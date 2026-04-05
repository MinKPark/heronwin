---
id: browser-navigation-and-web-operations
group: edge
priority: 300
summary: "Operate browser chrome and navigate directly to websites without turning URLs into search queries."
preferred_tools:
  - eyesandhands/describe_selected_window
  - eyesandhands/describe_selected_window_focus
  - eyesandhands/click_selected_window_element
  - eyesandhands/invoke_selected_window_element
  - eyesandhands/focus_selected_window_element
  - eyesandhands/set_selected_window_element_value
  - eyesandhands/send_input_to_window
  - eyesandhands/capture_selected_window_screenshot
activation:
  when_any_intents:
    - browser_request
  when_any_tools:
    - describe_selected_window
    - describe_selected_window_focus
    - invoke_selected_window_element
    - click_selected_window_element
    - focus_selected_window_element
    - set_selected_window_element_value
    - send_input_to_window
    - capture_selected_window_screenshot
applies_when:
  - The user asks to go to a website, URL, address bar, page, tab, or other browser chrome control.
---

# Skill: Browser Navigation And Web Operations

## Workflow

1. Determine whether the user wants direct website navigation, web search, browser chrome control, or page interaction.
2. Distinguish browser chrome controls such as the address bar, tabs, back, forward, and refresh from the web page content and from site-native search controls inside the page.
3. If the current selected window is not a browser, first switch to an existing browser window or launch the browser before interacting with controls in the current non-browser window.
4. In Microsoft Edge, when the user wants to open a new website or web page, open a new tab first unless the user explicitly asked to reuse the current tab.
5. When the user wants a website, navigate through the browser address bar with a clean URL.
6. When the user wants to search within the currently visible website, stay inside that site and use the site's own search surface rather than Windows Search or a generic web-search route.
7. Unless the user explicitly wants to modify the existing address-bar text in place, clear the current address-bar contents before entering a new address or URL.
8. After navigation or site-search actions, refresh the visible browser state and verify that the page actually changed to the intended site or result state.

## Direct Website Navigation Rules

- When the user says to go to a website, open a site, open a URL, or use the address bar, treat that as direct URL navigation, not a search-engine query.
- If the current visible window is not a browser, do not treat visible controls in that non-browser window as part of the website-opening flow; first select or launch the browser itself.
- In Microsoft Edge, if the user wants to open a different website or new web page, prefer creating a new tab for that destination before navigating unless they explicitly asked to reuse the current tab.
- Do not satisfy a direct website request by staying on a search results page unless the user explicitly asked to search the web or click a specific search result.
- Prefer a clean canonical URL such as `https://www.netflix.com` or the plain domain when appropriate.
- Do not mix search text, prior address-bar contents, or extra words into the URL.
- Do not append a URL to the existing page query or malformed address-bar text.

## Address Bar Rules

- If the browser UI exposes the address bar as a visible element such as `Address and search bar`, prefer focusing or invoking that element first.
- If the user wants a new site in Microsoft Edge and a new-tab control is available, prefer invoking that control first. If direct element targeting is unavailable or fails, use the browser-standard new-tab shortcut such as `Ctrl+T`.
- If direct element targeting is unavailable or fails, use the browser-standard address-bar shortcut such as `Ctrl+L` as the fallback.
- If browser chrome may be hidden, offscreen, or temporarily unreliable, prefer browser-standard shortcuts such as `Ctrl+T` and `Ctrl+L` over repeated attempts to activate the address bar element through UI Automation.
- If browser content is fullscreen, exit fullscreen with `Escape` or `F11` before trying to use the address bar or new-tab flow.
- Treat in-page fields such as a webpage `Search box`, site search field, or Bing results search box as page content, not as the browser address bar.
- For direct website navigation, do not type the site domain or URL into an in-page `Search box` just because it currently has focus.
- Unless the user explicitly asked to edit the current address-bar text in place, clear the full address bar before entering a new address or replacement URL.
- Before typing a replacement URL, replace the entire existing address-bar contents rather than appending to it.
- If the address bar may still contain old text, select all and delete it or otherwise clear it before entering the new URL.
- If the user is intentionally modifying text already in the address bar, preserve that text and make only the requested edit instead of clearing it automatically.
- After entering the URL, submit it with `Enter`, then refresh the browser state before answering.

## Search Versus URL Rules

- If the user explicitly asks to search the web, then use search behavior.
- If the user asks for a specific website or URL, do not convert that into a search query.
- If the user asks to search for a title, show, movie, article, or other content within the current website, do not switch to Windows taskbar search and do not replace the site flow with a browser-level web search unless the user explicitly asked for that.
- If the current website is temporarily showing fullscreen playback, a preview overlay, or another mode that hides the site search UI, first recover the site’s normal browsing surface with site-native controls such as Back, Back to Browse, Escape, or the site header before attempting the in-site search.
- For site-native search, first identify the visible site search control or search affordance from the refreshed page state before typing.
- When a site search control is exposed in the UI tree, copy its exact full `path` or `uiPath` from the latest evidence. Do not shorten or approximate that identifier.
- When a visible site search field is editable and the tools include `eyesandhands/set_selected_window_element_value`, prefer setting that exact field value directly before using generic typing.
- When a visible site result tile, poster, or play control is clearly the requested target and the tools include `eyesandhands/click_selected_window_element`, prefer that exact-path click over guessed keyboard navigation if direct invocation is unavailable or unreliable.
- If a site search control path fails once, refresh the page state and select a fresh exact target from the newest evidence instead of mutating the old path.
- Do not claim a site search step succeeded until the search field, query text, or visible results for the requested title are actually on screen.
- If the browser lands on a web search engine results page during a request to search within the current site, treat that as a wrong surface and repair back to the intended site-native flow.
- If the browser lands on a search page for a malformed string such as a search term glued to a URL, report that the direct navigation did not succeed and repair it by returning to the address bar, replacing the full contents, and retrying with a clean URL.

## Verification Rules

- Verify direct navigation from refreshed evidence such as the tab title, page title, visible page content, or browser error state.
- Do not stop after merely focusing the browser chrome or entering the URL. Continue until the requested site is confirmed, clearly failed, or still lacks sufficient evidence.
- For site-native search, do not stop after only clicking the search affordance or typing text. Continue until the requested visible result state is confirmed, clearly failed, or still lacks sufficient evidence.
- If the result is still a search page, say so directly.
- If the result is a browser error page such as name resolution failure, say that directly and treat it as failed navigation, not as a successful site visit.
- When UI Automation is too sparse to verify the destination page confidently, capture a screenshot and use it as the source of truth.

## Browser Chrome Rules

- Treat back, forward, refresh, home, tabs, and the address bar as browser chrome controls rather than page elements.
- Prefer direct browser chrome targeting when those controls are exposed by the accessibility tree.
- When the user asks to switch tabs or use browser chrome, avoid guessing from page content alone.
