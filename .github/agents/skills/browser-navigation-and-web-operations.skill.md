---
id: browser-navigation-and-web-operations
summary: "Operate browser chrome and navigate directly to websites without turning URLs into search queries."
preferred_tools:
  - eyesandhands/describe_selected_window
  - eyesandhands/describe_selected_window_focus
  - eyesandhands/invoke_selected_window_element
  - eyesandhands/focus_selected_window_element
  - eyesandhands/send_input_to_window
  - eyesandhands/capture_selected_window_screenshot
applies_when:
  - The user asks to go to a website, URL, address bar, page, tab, or other browser chrome control.
---

# Skill: Browser Navigation And Web Operations

## Workflow

1. Determine whether the user wants direct website navigation, web search, browser chrome control, or page interaction.
2. Distinguish browser chrome controls such as the address bar, tabs, back, forward, and refresh from the web page content.
3. When the user wants a website, navigate through the browser address bar with a clean URL.
4. Unless the user explicitly wants to modify the existing address-bar text in place, clear the current address-bar contents before entering a new address or URL.
5. After navigation, refresh the visible browser state and verify that the page actually changed to the intended site.

## Direct Website Navigation Rules

- When the user says to go to a website, open a site, open a URL, or use the address bar, treat that as direct URL navigation, not a search-engine query.
- Do not satisfy a direct website request by staying on a search results page unless the user explicitly asked to search the web or click a specific search result.
- Prefer a clean canonical URL such as `https://www.netflix.com` or the plain domain when appropriate.
- Do not mix search text, prior address-bar contents, or extra words into the URL.
- Do not append a URL to the existing page query or malformed address-bar text.

## Address Bar Rules

- If the browser UI exposes the address bar as a visible element such as `Address and search bar`, prefer focusing or invoking that element first.
- If direct element targeting is unavailable or fails, use the browser-standard address-bar shortcut such as `Ctrl+L` as the fallback.
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
- If the browser lands on a search page for a malformed string such as a search term glued to a URL, report that the direct navigation did not succeed and repair it by returning to the address bar, replacing the full contents, and retrying with a clean URL.

## Verification Rules

- Verify direct navigation from refreshed evidence such as the tab title, page title, visible page content, or browser error state.
- If the result is still a search page, say so directly.
- If the result is a browser error page such as name resolution failure, say that directly and treat it as failed navigation, not as a successful site visit.
- When UI Automation is too sparse to verify the destination page confidently, capture a screenshot and use it as the source of truth.

## Browser Chrome Rules

- Treat back, forward, refresh, home, tabs, and the address bar as browser chrome controls rather than page elements.
- Prefer direct browser chrome targeting when those controls are exposed by the accessibility tree.
- When the user asks to switch tabs or use browser chrome, avoid guessing from page content alone.
