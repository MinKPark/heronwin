# Body Refactor And Tool Naming Normalization

Last updated: 2026-04-18

## Summary

This design records the cutover from `src/herbody` to `src/body` and the
replacement of the former `eyesandhands` MCP server with two MCP servers:
`cognition` and `execution`.

The main goals are:

- rename `herbody` to `body` in one pass,
- split observation and action into separate MCP servers,
- move desktop session ownership into `brain`,
- make the MCP layer stateless and explicit,
- normalize tool names to a strict `verb_object` format,
- split overloaded keyboard/text input into separate tools.

## Current Status

As of 2026-04-18, the structural cutover is mostly complete:

- `src/body` is the active tree, and `brain` references `cognition` and
  `execution`.
- `DesktopSessionContext` is in place, and the main brain/test call paths are
  retargeted to the new tool and server names.
- `.github/agents`, repo docs, and code-facing references have been updated to
  use `cognition/...` and `execution/...`.
- `dotnet build src\heronwin.sln` and `dotnet test src\heronwin.sln` both pass.

The previous build failure was not caused by low disk space. It came from a
repo-local ACL issue on generated `obj` and `bin` folders, which has been
repaired. A reboot is still useful before the next pass because it can clear any
lingering file handles.

Remaining work for the next pass:

- rerun `npm run build` in `src\body\process-manager`,
- smoke-test `brain`, `cognition`, `execution`, and `process-manager` together
  through `MCP_SERVERS`,
- remove any empty historical `src\herbody` leftovers or stale local config
  after the smoke test,
- keep the rename map below as the historical record of the migration.

## Decisions

### Repository and project structure

- `src/herbody` becomes `src/body`.
- `src/body/process-manager` stays functionally the same, only relocated.
- The current `eyesandhands` code is split into:
  - a shared Windows automation library,
  - a `cognition` MCP host,
  - an `execution` MCP host.
- Existing namespaces and project names move from `HeronWin.HerBody.*` to
  `HeronWin.Body.*`.
- `micrecorder` moves under `src/body` as a rename-only carry-forward item.

### Session ownership

- `brain` owns the current desktop session for the active conversation.
- MCP servers do not keep cross-call selected-window state.
- `brain` tracks:
  - current window handle,
  - current window title,
  - recent `list_windows` output,
  - recent UI tree evidence,
  - recent focus evidence.

### Tool contract rules

- All tool names use `verb_object`.
- All window-scoped tools take explicit `windowHandle`.
- Element-targeting tools take explicit `elementPath`, using the `uiPath`
  values returned by cognition tools.
- `launch_application` keeps the current taskbar-search launch strategy for now,
  but the route is not encoded in the tool name.

## Tool Naming

### Nouns

Use these nouns consistently:

- `window`
- `taskbar_items`
- `taskbar_app`
- `application`
- `window_element`
- `window_main_menu_items`
- `window_context_menu_items`

### Verbs

- cognition: `list`, `describe`, `capture`
- execution: `activate`, `focus`, `click`, `invoke`, `set`, `press`, `type`,
  `launch`

### Rename map

#### Cognition

- `list_windows` -> `list_windows`
- `list_taskbar_elements` -> `list_taskbar_items`
- `describe_selected_window` -> `describe_window`
- `capture_selected_window_screenshot` -> `capture_window_screenshot`
- `describe_selected_window_focus` -> `describe_window_focus`
- `list_main_menu_items` -> `list_window_main_menu_items`
- `list_context_menu_items` -> `list_window_context_menu_items`

#### Execution

- `select_window` -> `activate_window`
- `select_taskbar_app` -> `activate_taskbar_app`
- `launch_app_via_taskbar_search` -> `launch_application`
- `focus_selected_window_element` -> `focus_window_element`
- `click_selected_window_element` -> `click_window_element`
- `invoke_selected_window_element` -> `invoke_window_element`
- `set_selected_window_element_value` -> `set_window_element_text`
- `send_input_to_window` -> `press_window_key` and `type_window_text`
- `invoke_main_menu_item` -> `invoke_window_main_menu_item`
- `invoke_context_menu_item` -> `invoke_window_context_menu_item`

## Implementation Notes

### Brain

- Add a `DesktopSessionContext` for the active conversation.
- Inject `windowHandle` into cognition and execution calls when the action is
  clearly targeting the current window.
- Retarget existing tool rewrite logic, follow-up evidence collection, and
  confidence checks to the new tool names.
- Replace `eyesandhands`-specific MCP server detection with explicit handling
  for `cognition` and `execution`.

### Body

- Split the current `eyesandhands` host into a shared automation library plus
  separate cognition and execution hosts.
- Keep taskbar discovery broad with `list_taskbar_items`.
- Keep taskbar execution app-specific with `activate_taskbar_app` and
  `launch_application`.
- Split the old mixed input tool into:
  - `press_window_key`
  - `type_window_text`

### Docs and prompts

- Update `.github/agents` prompts and skills from `eyesandhands/...` to
  `cognition/...` and `execution/...`.
- Update repository docs to describe `body`, the server split, and
  `brain`-owned desktop session state.

## Verification

Verified on 2026-04-18:

```powershell
dotnet build src\heronwin.sln
dotnet test src\heronwin.sln
```

- `dotnet build src\heronwin.sln` passed with 0 warnings and 0 errors.
- `dotnet test src\heronwin.sln` passed with 266 total tests.

Still to rerun after reboot:

```powershell
cd src\body\process-manager
npm run build
```

Then smoke-test the existing Netflix scenario with `brain`, `cognition`,
`execution`, and `process-manager` wired through `MCP_SERVERS`.

## Assumptions

- This is a full cutover with no long-lived `herbody` or `eyesandhands`
  compatibility aliases.
- `brain` session state is runtime-local and not persisted across restarts.
- `list_taskbar_items` remains broader than `activate_taskbar_app` by design.
- Splitting keyboard and text input is part of this refactor, not a follow-up.
