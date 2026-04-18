# desktop-automation

Shared Windows automation library for the `cognition` and `execution` MCP servers.

This project contains the Win32, UI Automation, selection, menu, screenshot, and input helpers that power the desktop tools. It is not the public MCP surface by itself.

## Projects

- `../cognition/`: read-only desktop inspection tools
- `../execution/`: desktop action tools

## Debugging

Set `BODY_WINDOWS_DEBUG=1` or pass `--debug` to the host project you are running to emit timestamped diagnostics on stderr.

Screenshots and other debug artifacts continue to use the `BRAIN_DEBUG_ARTIFACT_DIR` override when it is available.
