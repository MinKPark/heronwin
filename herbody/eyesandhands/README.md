# eyesandhands

An MCP server for Windows UI inspection and interaction using UI Automation.

`eyesandhands` is intended to help `herface` understand desktop UI state and carry out basic accessibility-style actions against native Windows applications.

## First Tools

| Tool | Description |
|------|-------------|
| `list_windows` | List visible top-level windows on the desktop |
| `select_window` | Select a window by handle or title and bring it to the foreground |
| `describe_active_window` | Return a structured UI Automation tree for the current active window |
| `focus_active_window_element` | Focus a specific child element in the active window by path |
| `describe_focused_element` | Return a structured UI Automation tree rooted at the focused element |
| `invoke_main_menu_item` | Open or invoke a main-menu path such as `File > Open` |

## Notes

- Windows only
- Requires a .NET 8+ SDK to build and run
- Uses Win32 window enumeration plus `System.Windows.Automation` for menu discovery and focus
- Keeps track of the currently selected window so menu actions can omit `windowHandle`
- UI tree inspection tools cap `maxDepth` at 4 levels to keep JSON payloads bounded

## Build and Run

```bash
dotnet restore
dotnet build
dotnet run --project ./eyesandhands.csproj
```

The server communicates over stdio using MCP and is meant to be launched by an MCP client.

For a quick manual desktop check without MCP:

```bash
dotnet run --project ./eyesandhands.csproj -- --selftest
```

Or print the same result as JSON:

```bash
dotnet run --project ./eyesandhands.csproj -- --selftest-json
```

## MCP Client Configuration

From `herface`, add an entry like this to `MCP_SERVERS`:

```json
[
  {
    "name": "eyesandhands",
    "command": "dotnet",
    "args": ["run", "--project", "../herbody/eyesandhands/eyesandhands.csproj"]
  }
]
```

If you prefer a build-first workflow, point `command` and `args` at the compiled executable instead.
