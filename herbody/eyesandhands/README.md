# eyesandhands

`eyesandhands` is a Windows-only MCP server that lets an agent inspect desktop UI state and perform a small set of accessibility-style interactions through Win32 and UI Automation.

It is designed for "look at the current app, pick the right window, inspect the control tree, focus the element I want, and activate a top-level menu item" workflows rather than full GUI automation.

## What It Can Do

| Tool | Description |
|------|-------------|
| `list_windows` | List visible top-level windows on the current desktop session |
| `select_window` | Select a window by handle or title substring and bring it to the foreground |
| `describe_active_window` | Return a UI Automation tree for the selected window, either bounded or full depth |
| `capture_active_window_screenshot` | Capture a PNG screenshot of the selected or foreground window |
| `focus_active_window_element` | Focus an element from the selected window tree using its path |
| `describe_focused_element` | Return a bounded UI Automation tree rooted at the currently focused element inside the selected window |
| `list_main_menu_items` | List traditional main-menu sections and their immediate visible items |
| `invoke_main_menu_item` | Open or invoke a menu path such as `File > Open` |
| `list_context_menu_items` | Open the focused element's context menu and list its immediate visible items |
| `invoke_context_menu_item` | Open the focused element's context menu and invoke a menu path |
| `list_taskbar_elements` | List visible elements on the main Windows taskbar strip |
| `activate_taskbar_app` | Activate one visible app button from the main Windows taskbar |
| `search_taskbar_app` | Open taskbar Search, type an app name, and press Enter |

## How It Works

- Window discovery uses Win32 window enumeration.
- Taskbar discovery uses the main `Shell_TrayWnd` window plus UI Automation.
- UI inspection and interaction use `System.Windows.Automation`.
- All UI Automation work is serialized onto a dedicated STA thread, which is the safest way to interact with many Windows accessibility APIs.
- The server keeps an in-memory "selected window" handle. That state is used by `list_windows`, the `describe_*` tools, `focus_active_window_element`, and lets `list_main_menu_items` and `invoke_main_menu_item` omit `windowHandle`.

## Requirements

- Windows desktop session
- .NET 8 SDK or newer
- An interactive user session if you want to inspect or focus real windows

If you start the server from a non-interactive session, window enumeration and focus behavior may be incomplete or unavailable.

## Build and Run

From this directory:

```bash
dotnet restore
dotnet build
dotnet run --project .\eyesandhands.csproj
```

The normal startup mode is an MCP stdio server intended to be launched by an MCP client.

### Console Helpers

`eyesandhands` also includes a small console mode for quick sanity checks:

```bash
dotnet run --project .\eyesandhands.csproj -- --help
dotnet run --project .\eyesandhands.csproj -- --selftest
dotnet run --project .\eyesandhands.csproj -- --selftest-json
```

- `--help` prints the supported console flags.
- `--selftest` prints a human-readable list of visible titled windows.
- `--selftest-json` prints the same information as JSON.

## MCP Client Configuration

### Run From Source

```json
[
  {
    "name": "eyesandhands",
    "command": "dotnet",
    "args": ["run", "--project", "../herbody/eyesandhands/eyesandhands.csproj"]
  }
]
```

### Run From a Built Executable

After `dotnet build`, point your MCP client at the compiled executable instead. This is a good fit when you want predictable startup time and do not need a source-based workflow.

## Recommended Flow

Most clients should use the tools in this order:

1. Call `list_windows`.
2. Call `select_window` with a specific `windowHandle` when possible.
3. Call `describe_active_window` to inspect the selected window's control tree.
4. If the UI Automation tree is too sparse, call `capture_active_window_screenshot` and inspect the saved image.
5. Use a returned `path` with `focus_active_window_element`.
6. Optionally call `describe_focused_element` to verify where focus landed.
7. Use `list_main_menu_items` to discover traditional menu commands and `invoke_main_menu_item` to run a chosen path.
8. If the user wants an action on the focused control, use `list_context_menu_items` and then `invoke_context_menu_item`.

For taskbar-driven workflows:

1. Call `list_taskbar_elements`.
2. Pick an app button from the returned `elements`.
3. Call `activate_taskbar_app`, preferably with the returned `path`.
4. The activated app window becomes the selected window for subsequent eyesandhands actions.

For launching apps that are not pinned to the taskbar:

1. Call `search_taskbar_app` with the app name you want to launch.
2. The tool opens Windows Search from the taskbar, types the query, and presses `Enter` on the top result.
3. The launched app window becomes the selected window for subsequent eyesandhands actions.

This sequence matters because `select_window` persists target-window state, and subsequent inspection, focus, and menu operations refocus that saved window before acting.

## Tool Reference

### `list_windows`

Lists visible top-level windows that have a non-empty title.

Response shape:

```json
{
  "selectedWindowHandle": "0x00123456",
  "windows": [
    {
      "handle": "0x00123456",
      "title": "Untitled - Notepad",
      "className": "Notepad",
      "processId": 12345,
      "bounds": {
        "left": 100,
        "top": 100,
        "width": 900,
        "height": 700
      },
      "isSelected": true
    }
  ]
}
```

Notes:

- `selectedWindowHandle` is `null` until `select_window` succeeds.
- `isSelected` marks the same handle inside the returned list.
- Windows are sorted with the selected one first, then by title.

### `select_window`

Brings a window to the foreground and stores it as the selected window.

Parameters:

- `windowHandle`: preferred; use a value returned by `list_windows`
- `titleContains`: case-insensitive title substring match; only used when `windowHandle` is omitted

Response shape:

```json
{
  "handle": "0x00123456",
  "title": "Untitled - Notepad",
  "className": "Notepad",
  "processId": 12345,
  "wasFocused": true
}
```

Notes:

- If `titleContains` matches multiple windows, the call fails and asks for a specific handle.
- Minimized windows are restored before focus is attempted.

### `describe_active_window`

Returns a UI Automation tree for the selected window. If no window has been selected yet, the server falls back to the current foreground window.

Parameters:

- `maxDepth`: required range `1..4` when `fullDepth` is `false`
- `fullDepth`: optional; when `true`, returns the full available UI Automation tree without a depth cap

Response shape:

```json
{
  "window": {
    "handle": "0x00123456",
    "title": "Untitled - Notepad",
    "className": "Notepad",
    "processId": 12345,
    "bounds": {
      "left": 100,
      "top": 100,
      "width": 900,
      "height": 700
    }
  },
  "maxDepth": 2,
  "fullDepth": false,
  "elementTree": {
    "path": "root",
    "name": "Untitled - Notepad",
    "controlType": "Window",
    "automationId": "",
    "className": "Notepad",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": false,
    "isKeyboardFocusable": true,
    "availableActions": ["close", "focus", "maximize", "minimize", "move", "resize", "restore"],
    "bounds": {
      "left": 100,
      "top": 100,
      "width": 900,
      "height": 700
    },
    "children": [
      {
        "path": "0",
        "name": "Menu bar",
        "controlType": "MenuBar",
        "automationId": "",
        "className": "",
        "isEnabled": true,
        "isOffscreen": false,
        "hasKeyboardFocus": false,
        "isKeyboardFocusable": false,
        "availableActions": [],
        "bounds": null,
        "children": []
      }
    ]
  }
}
```

Notes:

- When a selected window exists, the server attempts to bring it back to the foreground before capturing the tree.
- Child paths use slash-delimited indexes like `0`, `2/1`, or `3/0/4`.
- The root path is always `root`.
- `availableActions` is descriptive metadata only. This server currently exposes focus and menu tools, not a general action executor.
- When `fullDepth` is `true`, `maxDepth` is returned as `null` and the full visible UI Automation subtree is captured. This can produce a large payload.

### `capture_active_window_screenshot`

Captures a PNG screenshot of the selected window. If no window has been selected, the server falls back to the current foreground window.

Response shape:

```json
{
  "window": {
    "handle": "0x00123456",
    "title": "Untitled - Notepad",
    "className": "Notepad",
    "processId": 12345,
    "bounds": {
      "left": 100,
      "top": 100,
      "width": 900,
      "height": 700
    }
  },
  "imagePath": "C:\\\\Users\\\\name\\\\AppData\\\\Local\\\\Temp\\\\heronwin\\\\eyesandhands\\\\20260329-120102123-Untitled_-_Notepad-0x00123456.png",
  "imageFormat": "png",
  "imageSize": {
    "width": 900,
    "height": 700
  }
}
```

Notes:

- The screenshot is saved to a temporary local file and can be passed to an image-reading tool such as `read/viewImage`.
- If a selected window exists, the server refocuses that window before capture.
- The capture uses the current visible window bounds, so occlusion or overlapping windows can affect the image.

### `focus_active_window_element`

Attempts to focus a specific element in the selected window.

Parameters:

- `elementPath`: a path returned by `describe_active_window`, or `root`

Response shape:

```json
{
  "window": { "...": "same window descriptor as above" },
  "focusedElement": {
    "path": "2/1",
    "name": "Text Editor",
    "controlType": "Edit",
    "automationId": "15",
    "className": "RichEditD2DPT",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": true,
    "isKeyboardFocusable": true,
    "availableActions": ["focus", "set_value"],
    "bounds": {
      "left": 110,
      "top": 145,
      "width": 880,
      "height": 620
    },
    "children": []
  },
  "actionTaken": "focused"
}
```

Notes:

- If a selected window exists, the server focuses that window before attempting the element focus.
- If the requested element cannot take focus directly, the server walks downward and tries focusable descendants.
- `actionTaken` may be `focused`, `selected_and_focused`, or `scrolled_and_focused`.
- When focus lands on a descendant, the returned `focusedElement.path` may differ from the requested path.

### `describe_focused_element`

Returns a UI Automation tree rooted at the current focused element inside the selected window.

Parameters:

- `maxDepth`: required range `1..4`

Notes:

- The root path in this response is `focused`.
- If a selected window exists, the server focuses that window before inspection.
- The call fails if the currently focused element does not belong to the selected window.

### `list_main_menu_items`

Lists the selected window's traditional main-menu sections and the immediate visible items under each one.

Parameters:

- `windowHandle`: optional; when omitted, the server uses the current selected window

Response shape:

```json
{
  "window": { "...": "same window descriptor as above" },
  "menuBar": {
    "path": "menu_bar",
    "name": "Application",
    "controlType": "MenuBar",
    "automationId": "",
    "className": "",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": false,
    "isKeyboardFocusable": false,
    "availableActions": [],
    "bounds": null,
    "children": []
  },
  "menus": [
    {
      "label": "File",
      "menuPath": "File",
      "items": [
        {
          "label": "Open",
          "menuPath": "File > Open",
          "controlType": "MenuItem",
          "isEnabled": true,
          "hasSubmenu": false,
          "isSeparator": false,
          "availableActions": ["invoke"]
        }
      ]
    }
  ]
}
```

Notes:

- The tool opens each top-level menu one at a time and reports the immediate visible items exposed through UI Automation.
- This is intended for command discovery; use `invoke_main_menu_item` to execute a chosen path.
- Some apps expose only partial or no menu structure through UI Automation.

### `invoke_main_menu_item`

Invokes a traditional main-menu path such as `File > Open`.

Parameters:

- `menuPath`: `>`-separated menu labels
- `windowHandle`: optional; when omitted, the server uses the current selected window

Response shape:

```json
{
  "handle": "0x00123456",
  "title": "Untitled - Notepad",
  "processId": 12345,
  "menuPath": "File > Open",
  "actionTaken": "invoked"
}
```

Notes:

- Menu matching is forgiving: it ignores access-key markers such as `&File` and also tolerates trailing ellipses.
- Intermediate menu items are expanded or selected as needed.
- If no window is supplied and no window has been selected yet, the call fails with guidance to run `list_windows` and `select_window` first.
- This tool is intended for standard main menus exposed through UI Automation. Ribbon controls, custom toolbars, and owner-drawn menus may not appear.

### `list_context_menu_items`

Opens the context menu for the currently focused element and lists the immediate visible items.

Response shape:

```json
{
  "window": { "...": "same window descriptor as above" },
  "focusedElement": { "...": "same focused element snapshot as above" },
  "openActionTaken": "pressed_shift_f10",
  "items": [
    {
      "label": "Rename",
      "menuPath": "Rename",
      "controlType": "MenuItem",
      "isEnabled": true,
      "hasSubmenu": false,
      "isSeparator": false,
      "availableActions": ["invoke"]
    }
  ]
}
```

Notes:

- The tool targets the currently focused element inside the selected window.
- It tries `Shift+F10` first and falls back to the Apps key if needed.
- Focus an element first with `focus_active_window_element` when you want the context menu for a specific control.

### `invoke_context_menu_item`

Opens the context menu for the currently focused element and invokes a menu path such as `Rename` or `Open with > Choose another app`.

Parameters:

- `menuPath`: `>`-separated menu labels

Response shape:

```json
{
  "handle": "0x00123456",
  "title": "File Explorer",
  "processId": 12345,
  "menuPath": "Rename",
  "openActionTaken": "pressed_shift_f10",
  "actionTaken": "invoked"
}
```

Notes:

- The tool uses the currently focused element in the selected window as the context-menu target.
- Like the main-menu tool, matching is tolerant of access-key markers and trailing ellipses.

### `list_taskbar_elements`

Lists visible direct children of the main Windows taskbar strip. On modern Windows builds this usually includes Start, Search, Task View, and pinned or running app buttons.

Response shape:

```json
{
  "taskbarWindow": {
    "handle": "0x00000000",
    "title": "",
    "className": "Shell_TrayWnd",
    "processId": 1234,
    "bounds": {
      "left": 0,
      "top": 1040,
      "width": 1920,
      "height": 40
    }
  },
  "hostElement": {
    "path": "2/0",
    "name": "",
    "controlType": "Pane",
    "automationId": "TaskbarFrame",
    "className": "Taskbar.TaskbarFrameAutomationPeer",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": false,
    "isKeyboardFocusable": false,
    "availableActions": [],
    "bounds": null,
    "children": []
  },
  "elements": [
    {
      "path": "2/0/1",
      "name": "Start",
      "controlType": "Button",
      "automationId": "StartButton",
      "className": "ToggleButton",
      "isEnabled": true,
      "isOffscreen": false,
      "hasKeyboardFocus": false,
      "isKeyboardFocusable": true,
      "availableActions": ["focus", "invoke", "toggle"],
      "bounds": null,
      "isAppButton": false
    }
  ]
}
```

Notes:

- `elements` only includes visible direct children of the main taskbar host.
- `path` values can be passed directly to `activate_taskbar_app`.
- `isAppButton` distinguishes pinned/running app buttons from Start/Search-style taskbar controls.

### `activate_taskbar_app`

Activates one visible taskbar app button. If the app is pinned but not running, this typically starts it. If it is already running, this usually behaves like clicking its taskbar button.

Parameters:

- `elementPath`: preferred; use a `path` returned by `list_taskbar_elements`
- `titleContains`: optional fallback match against the visible app button label
- `automationIdContains`: optional fallback match against the visible app button automation id

Response shape:

```json
{
  "taskbarWindow": { "...": "same taskbar window descriptor as above" },
  "hostElement": { "...": "same host snapshot as above" },
  "activatedElement": {
    "path": "2/0/5",
    "name": "Netflix pinned",
    "controlType": "Button",
    "automationId": "Appid: 4DF9E0F8.Netflix_mcm4njqhnhss8!Netflix.App",
    "className": "Taskbar.TaskListButtonAutomationPeer",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": false,
    "isKeyboardFocusable": true,
    "availableActions": ["focus", "scroll_into_view"],
    "bounds": null,
    "isAppButton": true
  },
  "actionTaken": "focused_and_pressed_enter",
  "selectedWindow": {
    "handle": "0x00123456",
    "title": "File Explorer",
    "className": "CabinetWClass",
    "processId": 12345,
    "wasFocused": true
  }
}
```

Notes:

- `elementPath` is the most stable selector because taskbar labels can be localized or include running-window counts.
- If a substring match is ambiguous, the tool fails and asks you to use `elementPath`.
- This tool only targets visible app buttons from the main taskbar strip, not notification-area icons.
- When a taskbar button exposes `invoke`, `selection`, or `toggle`, the tool uses that UI Automation pattern first.
- If the button is only keyboard-focusable, the tool focuses it and sends `Enter`, which can start pinned apps that do not expose an invoke pattern.
- After launch or activation, the tool waits for the foreground app window and stores it as the selected window so follow-up UI actions target that app by default.

### `search_taskbar_app`

Opens the taskbar Search surface, types an app name into the search box, and presses `Enter` to launch the top result.

Parameters:

- `appName`: the app name or search query to type into Windows Search

Response shape:

```json
{
  "taskbarWindow": { "...": "same taskbar window descriptor as above" },
  "hostElement": { "...": "same host snapshot as above" },
  "searchElement": {
    "path": "2/0/2",
    "name": "Search",
    "controlType": "Button",
    "automationId": "SearchButton",
    "className": "ToggleButton",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": false,
    "isKeyboardFocusable": true,
    "availableActions": ["focus", "scroll_into_view", "toggle"],
    "bounds": null,
    "isAppButton": false
  },
  "searchInputElement": {
    "path": "focused",
    "name": "Search box",
    "controlType": "Edit",
    "automationId": "SearchTextBox",
    "className": "",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": true,
    "isKeyboardFocusable": true,
    "availableActions": ["focus", "set_value"],
    "bounds": null,
    "children": []
  },
  "query": "Notepad",
  "searchActionTaken": "toggled_search_on",
  "textEntryActionTaken": "focused_and_set_value",
  "launchActionTaken": "pressed_enter",
  "selectedWindow": {
    "handle": "0x00123456",
    "title": "Untitled - Notepad",
    "className": "Notepad",
    "processId": 12345,
    "wasFocused": true
  }
}
```

Notes:

- The tool prefers the visible `SearchButton` taskbar control when available.
- If Windows Search opens but the input field is not immediately detected, the tool falls back to the `Win+S` shortcut for the same Search surface and retries.
- This tool is best for starting apps from the top search result, not for browsing rich search result pages.
- After pressing `Enter`, the tool waits for the launched app window to become foreground and stores it as the selected window for subsequent eyesandhands interactions.

## Practical Caveats

- UI Automation coverage varies by application. Classic Win32 apps often work well; custom-rendered apps may expose little or nothing.
- Focus is inherently stateful. When a window has been selected, the server refocuses that window before inspection and focus operations. Without a selected window, `describe_active_window` and `describe_focused_element` still reflect the current foreground app.
- `describe_*` responses are intentionally depth-limited to keep MCP payloads bounded.
- Menu discovery waits briefly for submenu items to appear, but heavily animated or delayed UIs can still time out.
- The selected window is process-local state. Restarting the server clears it.

## Troubleshooting

- No windows returned: run from the logged-in desktop session, not a background service or detached shell.
- `maxDepth` error: use a value from `1` to `4`, or set `fullDepth` to `true`.
- "No window was supplied and no window is currently selected": call `select_window` first or pass `windowHandle`.
- Wrong window focused: prefer `windowHandle` over `titleContains` when multiple similar windows are open.
- Menu item not found: confirm the app exposes a standard menu bar through UI Automation and that the labels match what the accessibility tree reports.
