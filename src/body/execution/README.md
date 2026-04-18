# execution

Stateless Windows UI interaction MCP server for `heronwin`.

## Tools

| Tool | Description |
|------|-------------|
| `activate_window` | Bring a window to the foreground |
| `activate_taskbar_app` | Activate a visible app button on the taskbar |
| `launch_application` | Launch an app through the Windows taskbar search flow |
| `focus_window_element` | Focus a child element inside a window |
| `click_window_element` | Click a child element inside a window |
| `invoke_window_element` | Invoke a child element inside a window |
| `set_window_element_text` | Set text on an editable child element |
| `press_window_key` | Send a named key or shortcut to a window |
| `type_window_text` | Type Unicode text into the focused control |
| `invoke_window_main_menu_item` | Invoke a main-menu path such as `File > Open` |
| `invoke_window_context_menu_item` | Invoke a context-menu path |

## Usage

Run the MCP server over stdio:

```powershell
dotnet run --project .
```

This server is intended to be launched by `brain` through `MCP_SERVERS`.
