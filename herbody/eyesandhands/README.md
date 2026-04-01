# eyesandhands

`eyesandhands` is a Windows-only MCP server that lets an agent inspect desktop UI state and perform a small set of accessibility-style interactions through Win32 and UI Automation.

It is designed for "look at the current app, pick the right window, inspect the control tree, focus the element I want, and activate a top-level menu item" workflows rather than full GUI automation.

## What It Can Do

| Tool | Description |
|------|-------------|
| `list_windows` | List visible top-level windows on the current desktop session |
| `select_window` | Select a window by handle or title substring and bring it to the foreground |
| `describe_selected_window` | Return a UI Automation tree for the selected window, either bounded or full depth |
| `capture_selected_window_screenshot` | Capture a PNG screenshot of the selected or foreground window |
| `focus_selected_window_element` | Focus an element from the selected window tree using its path |
| `click_selected_window_element` | Mouse-click an element from the selected window tree using its path |
| `invoke_selected_window_element` | Fallback-invoke an element by navigating focus with Tab and arrow keys, then pressing Enter |
| `send_input_to_window` | Send a key press, shortcut, or typed text to the selected window |
| `describe_selected_window_focus` | Return a bounded UI Automation tree rooted at the currently focused element inside the selected window |
| `list_main_menu_items` | List traditional main-menu sections and their immediate visible items |
| `invoke_main_menu_item` | Open or invoke a menu path such as `File > Open` |
| `list_context_menu_items` | Open the focused element's context menu and list its immediate visible items |
| `invoke_context_menu_item` | Open the focused element's context menu and invoke a menu path |
| `list_taskbar_elements` | List visible elements on the main Windows taskbar strip |
| `select_taskbar_app` | Select or start one visible app button from the main Windows taskbar |
| `launch_app_via_taskbar_search` | Open taskbar Search, type an app name, and press Enter |

## How It Works

- Window discovery uses Win32 window enumeration.
- Taskbar discovery uses the main `Shell_TrayWnd` window plus UI Automation.
- UI inspection and interaction use `System.Windows.Automation`.
- All UI Automation work is serialized onto a dedicated STA thread, which is the safest way to interact with many Windows accessibility APIs.
- The server keeps an in-memory "selected window" handle. That state is used by `list_windows`, the `describe_*` tools, `focus_selected_window_element`, `invoke_selected_window_element`, and lets `list_main_menu_items` and `invoke_main_menu_item` omit `windowHandle`.

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
dotnet run --project .\eyesandhands.csproj -- --debug
```

The normal startup mode is an MCP stdio server intended to be launched by an MCP client.
Use `--debug` or set `EYESANDHANDS_DEBUG=1` to emit timestamped diagnostics on stderr, including per-poll UI settle checks.

### Console Helpers

`eyesandhands` also includes a small console mode for quick sanity checks:

```bash
dotnet run --project .\eyesandhands.csproj -- --help
dotnet run --project .\eyesandhands.csproj -- --debug --selftest
dotnet run --project .\eyesandhands.csproj -- --selftest
dotnet run --project .\eyesandhands.csproj -- --selftest-json
```

- `--help` prints the supported console flags.
- `--debug` enables timestamped diagnostic output. Human-readable console output is timestamped in this mode; JSON tool payloads remain unmodified.
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
3. Call `describe_selected_window` to inspect the selected window's control tree.
4. If the UI Automation tree is too sparse, call `capture_selected_window_screenshot` and inspect the saved image.
5. Use a returned `path` with `focus_selected_window_element`.
6. Use `click_selected_window_element` when the user wants a left or right mouse click on a specific UI element.
7. Use `invoke_selected_window_element` as a fallback when direct UI Automation invoke behavior or mouse clicking does not activate the target control.
8. Use `send_input_to_window` when the user wants to press a shortcut or type text into the current app.
9. Optionally call `describe_selected_window_focus` to verify where focus landed.
10. Use `list_main_menu_items` to discover traditional menu commands and `invoke_main_menu_item` to run a chosen path.
11. If the user wants an action on the focused control, use `list_context_menu_items` and then `invoke_context_menu_item`.

For taskbar-driven workflows:

1. Call `list_taskbar_elements`.
2. Pick an app button from the returned `elements`.
3. Call `select_taskbar_app`, preferably with the returned `path`.
4. The activated app window becomes the selected window for subsequent eyesandhands actions.

For launching apps that are not pinned to the taskbar:

1. Call `launch_app_via_taskbar_search` with the app name you want to launch.
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
  "wasFocused": true,
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "ReadyForUserInteraction",
    "windowInteractionStateChangeCount": 1,
    "structureChangedEventCount": 0,
    "asyncContentLoadedEventCount": 0,
    "elapsedMilliseconds": 2294,
    "initialDelayMilliseconds": 2000,
    "traceLines": [
      "[2026-03-31 09:15:30.123 -07:00] ui-settle begin handle=0x00123456 observerAttached=True initialDelayMs=2000 pollMs=300 timeoutMs=180000",
      "[2026-03-31 09:15:32.417 -07:00] ui-settle check handle=0x00123456 windowAvailable=True interactionState=ReadyForUserInteraction interactionChanges=1 structureChanges=0 asyncChanges=0 elapsedMs=2294 settled=True timedOut=False"
    ]
  }
}
```

Notes:

- If `titleContains` matches multiple windows, the call fails and asks for a specific handle.
- Minimized windows are restored before focus is attempted.
- State-changing tools use a 2-second initial settle delay, then poll every 300 ms for up to 3 minutes using UI Automation state.

### `describe_selected_window`

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
    "uiPath": "root",
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
        "uiPath": "0",
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
- Each element also includes `uiPath`, which is the original UI Automation path and currently matches `path`.
- In bounded mode, the server may elide structural-only wrapper nodes so meaningful descendants from browser or framework UI trees appear sooner. Use each element's explicit `uiPath` or `path` value verbatim for follow-up calls, even if the compacted JSON nesting skips some intermediate path segments.
- `availableActions` is descriptive metadata only. This server currently exposes focus and menu tools, not a general action executor.
- When `fullDepth` is `true`, `maxDepth` is returned as `null` and the full visible UI Automation subtree is captured. This can produce a large payload.

### `capture_selected_window_screenshot`

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

### `focus_selected_window_element`

Attempts to focus a specific element in the selected window.

Parameters:

- `elementPath`: a path returned by `describe_selected_window`, or `root`

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
  "actionTaken": "focused",
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "ReadyForUserInteraction",
    "windowInteractionStateChangeCount": 1,
    "structureChangedEventCount": 0,
    "asyncContentLoadedEventCount": 0,
    "elapsedMilliseconds": 1178
  }
}
```

Notes:

- If a selected window exists, the server focuses that window before attempting the element focus.
- If the requested element cannot take focus directly, the server walks downward and tries focusable descendants.
- `actionTaken` may be `focused`, `selected_and_focused`, or `scrolled_and_focused`.
- When focus lands on a descendant, the returned `focusedElement.path` may differ from the requested path.
- Action tools include `uiSettle`, which waits 2 seconds, then polls every 300 ms for up to 3 minutes using `WindowInteractionState`.
- `uiSettle.status` is typically `settled`, may be `window_unavailable` when the action closes the target window, and is `timed_out` if `WindowInteractionState` never became definite within the polling window.
- In debug mode, `uiSettle.traceLines` includes timestamped settle checks directly in the tool result JSON.

### `click_selected_window_element`

Mouse-clicks a specific element in the selected window.

Parameters:

- `elementPath`: a path returned by `describe_selected_window`, or `root`
- `mouseButton`: optional; `left` or `right`, default `left`

Response shape:

```json
{
  "window": { "...": "same window descriptor as above" },
  "clickedElement": {
    "path": "2/1",
    "name": "Open",
    "controlType": "Button",
    "automationId": "OpenButton",
    "className": "Button",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": true,
    "isKeyboardFocusable": true,
    "availableActions": ["focus", "invoke"],
    "bounds": {
      "left": 200,
      "top": 160,
      "width": 90,
      "height": 28
    },
    "children": []
  },
  "mouseButton": "left",
  "clickPoint": {
    "x": 245,
    "y": 174
  },
  "preparationActionTaken": "focused",
  "actionTaken": "left_clicked",
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "ReadyForUserInteraction",
    "windowInteractionStateChangeCount": 1,
    "structureChangedEventCount": 2,
    "asyncContentLoadedEventCount": 0,
    "elapsedMilliseconds": 1315
  }
}
```

Notes:

- The tool brings the selected or foreground window to the front before clicking.
- The click target is the center of the element's visible bounds.
- If the requested element does not expose usable bounds, the tool searches downward for a descendant that does.
- The tool may scroll, select, or focus the target element before clicking so that a clickable point becomes available.
- The mouse cursor is moved to the clicked screen point as part of the interaction.

### `invoke_selected_window_element`

Fallback-invokes a specific element in the selected window by moving focus with keyboard navigation and then pressing `Enter`.

Parameters:

- `elementPath`: a path returned by `describe_selected_window`, or `root`

Response shape:

```json
{
  "window": { "...": "same window descriptor as above" },
  "invokedElement": {
    "path": "2/1",
    "name": "Open",
    "controlType": "Button",
    "automationId": "OpenButton",
    "className": "Button",
    "isEnabled": true,
    "isOffscreen": false,
    "hasKeyboardFocus": true,
    "isKeyboardFocusable": true,
    "availableActions": ["focus", "invoke"],
    "bounds": {
      "left": 200,
      "top": 160,
      "width": 90,
      "height": 28
    },
    "children": []
  },
  "strategy": "keyboard_navigation",
  "navigationKeys": ["Tab", "Right", "Down"],
  "navigationStepCount": 3,
  "actionTaken": "focused_via_keyboard_then_pressed_enter",
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "ReadyForUserInteraction",
    "windowInteractionStateChangeCount": 1,
    "structureChangedEventCount": 1,
    "asyncContentLoadedEventCount": 0,
    "elapsedMilliseconds": 1260
  }
}
```

Notes:

- The tool is intended as a fallback for controls that do not activate reliably through `InvokePattern` or `click_selected_window_element`.
- It keeps the selected window in the foreground, watches the live focused element, and stops as soon as the target element or one of its descendants receives focus.
- `navigationKeys` shows the exact Tab and arrow keys the tool sent while searching for the target.
- `actionTaken` is `pressed_enter_on_focused_element` when focus was already on target, or `focused_via_keyboard_then_pressed_enter` when keyboard navigation was needed.

### `send_input_to_window`

Sends a key press, shortcut, or typed text to the selected window. If no window has been selected, the server falls back to the current foreground window.

Parameters:

- `key`: named key such as `Enter`, `Escape`, `Tab`, `Up`, `F5`, `A`, or `1`
- `modifiers`: optional array such as `["Control"]`, `["Shift"]`, or `["Control", "Shift"]`
- `text`: Unicode text to type into the currently focused control
- `repeatCount`: optional repeat count, minimum `1`

Provide exactly one of `key` or `text`.

Response shape:

```json
{
  "window": { "...": "same window descriptor as above" },
  "inputMode": "key",
  "key": "A",
  "modifiers": ["control"],
  "repeatCount": 1,
  "textLength": null,
  "actionTaken": "pressed_modified_key",
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "ReadyForUserInteraction",
    "windowInteractionStateChangeCount": 0,
    "structureChangedEventCount": 1,
    "asyncContentLoadedEventCount": 0,
    "elapsedMilliseconds": 1208
  }
}
```

Notes:

- The tool brings the selected or foreground window to the front before sending input, but it avoids intentionally changing the focused child control inside that window.
- Use `text` for direct typing, including punctuation and multi-character input.
- Use `key` plus `modifiers` for shortcuts such as `Ctrl+A`, `Alt+F4`, or `Shift+Tab`.

### `describe_selected_window_focus`

Returns a UI Automation tree rooted at the current focused element inside the selected window.

Parameters:

- `maxDepth`: required range `1..4`

Notes:

- The root path in this response is `focused`.
- If a selected window exists, the server focuses that window before inspection.
- The call fails if the currently focused element does not belong to the selected window.
- As with `describe_selected_window`, bounded focus trees may compact structural-only wrapper nodes while preserving each element's original `uiPath` and `path`.

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
  "actionTaken": "invoked",
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "BlockedByModalWindow",
    "windowInteractionStateChangeCount": 1,
    "structureChangedEventCount": 1,
    "asyncContentLoadedEventCount": 0,
    "elapsedMilliseconds": 1409
  }
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
- Focus an element first with `focus_selected_window_element` when you want the context menu for a specific control.

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
  "actionTaken": "invoked",
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "ReadyForUserInteraction",
    "windowInteractionStateChangeCount": 0,
    "structureChangedEventCount": 1,
    "asyncContentLoadedEventCount": 0,
    "elapsedMilliseconds": 1220
  }
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
- `path` values can be passed directly to `select_taskbar_app`.
- `isAppButton` distinguishes pinned/running app buttons from Start/Search-style taskbar controls.

### `select_taskbar_app`

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
  },
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "ReadyForUserInteraction",
    "windowInteractionStateChangeCount": 1,
    "structureChangedEventCount": 3,
    "asyncContentLoadedEventCount": 0,
    "elapsedMilliseconds": 1682
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

### `launch_app_via_taskbar_search`

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
  },
  "uiSettle": {
    "status": "settled",
    "completed": true,
    "windowInteractionState": "ReadyForUserInteraction",
    "windowInteractionStateChangeCount": 2,
    "structureChangedEventCount": 4,
    "asyncContentLoadedEventCount": 1,
    "elapsedMilliseconds": 1887
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
- Focus is inherently stateful. When a window has been selected, the server refocuses that window before inspection and focus operations. Without a selected window, `describe_selected_window` and `describe_selected_window_focus` still reflect the current foreground app.
- `describe_*` responses are intentionally depth-limited to keep MCP payloads bounded.
- Menu discovery waits briefly for submenu items to appear, but heavily animated or delayed UIs can still time out.
- The selected window is process-local state. Restarting the server clears it.

## Troubleshooting

- No windows returned: run from the logged-in desktop session, not a background service or detached shell.
- `maxDepth` error: use a value from `1` to `4`, or set `fullDepth` to `true`.
- "No window was supplied and no window is currently selected": call `select_window` first or pass `windowHandle`.
- Wrong window focused: prefer `windowHandle` over `titleContains` when multiple similar windows are open.
- Menu item not found: confirm the app exposes a standard menu bar through UI Automation and that the labels match what the accessibility tree reports.
