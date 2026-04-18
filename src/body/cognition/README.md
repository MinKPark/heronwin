# cognition

Stateless Windows UI inspection MCP server for `heronwin`.

## Tools

| Tool | Description |
|------|-------------|
| `list_windows` | List visible top-level windows on the current desktop |
| `list_taskbar_items` | List visible items on the main Windows taskbar |
| `describe_window` | Return a structured UI Automation tree for a window |
| `capture_window_screenshot` | Capture a PNG screenshot of a window |
| `describe_window_focus` | Describe the currently focused element in a window |
| `list_window_main_menu_items` | List visible main-menu sections and items |
| `list_window_context_menu_items` | Open and inspect the current context menu |

## Usage

Run the MCP server over stdio:

```powershell
dotnet run --project .
```

Run the built-in self-test window listing:

```powershell
dotnet run --project . -- --selftest
dotnet run --project . -- --selftest-json
```

This server is intended to be launched by `brain` through `MCP_SERVERS`.
