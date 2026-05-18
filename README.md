# HeronWin

HeronWin is a Windows-local AI assistant workspace for inspecting, controlling, and validating desktop and browser workflows. It combines assistant hosts, a shared .NET runtime, local MCP tool servers, prompt/skill composition, scripted scenarios, and accessibility validation reports.

The project is designed around local, explicit, testable desktop automation: the assistant observes Windows UI state, chooses actions through local tools, records traces, and can replay or validate workflows from YAML.

## What It Can Do

- Run an interactive assistant with typed requests or voice mode through `cursor`.
- Run reproducible YAML scenarios with log-based assertions through `tars`.
- Drive accessibility validation runs through `ava`, collecting UI evidence and writing Markdown/JSON reports.
- Inspect Windows UI state with local MCP tools: visible windows, taskbar items, UI Automation trees, focused controls, screenshots, main menus, and context menus.
- Act on Windows UI with local MCP tools: activate windows/apps, launch apps through taskbar search, focus/click/invoke controls, set text, type text, send shortcuts, and invoke menu paths.
- Compose assistant prompts from shared and assistant-specific skills under `.github/agents`, including app/site playbooks for Windows, Edge, generic apps, and Netflix.
- Use multiple LLM routes: OpenAI Platform API, ChatGPT/Codex CLI sign-in, and Anthropic API, depending on the assistant `.env`.
- Produce JSONL traces and trace-report Markdown for debugging scenario and interactive runs.

## Main Components

| Path | Purpose |
| --- | --- |
| `src/assistants/cursor` | Interactive voice/text assistant host. |
| `src/assistants/tars` | Scenario assistant for non-interactive YAML runs and assertions. |
| `src/assistants/ava` | Accessibility validation assistant for drive-and-inspect validation bundles. |
| `src/assistants/brain` | Shared runtime library for providers, prompts, tools, config, traces, and desktop session primitives. |
| `src/tools/cognition` | MCP server for read-only Windows UI inspection. |
| `src/tools/execution` | MCP server for Windows UI actions. |
| `src/tools/desktop-automation` | Shared Win32/UI Automation library used by the tool servers. |
| `.github/agents` | Prompt profiles and runtime-loaded skills. |
| `src/scenarios` | Scenario and accessibility validation YAML files. |
| `docs` | User setup and workflow documentation. |
| `devdocs` | Design notes, guardrails, history, bugs, and performance notes. |

## Requirements

- Windows 10/11 x64
- .NET SDK 10.0.201 or newer
- One configured provider route:
  - `openai-api` with `OPENAI_API_KEY`
  - `openai-codex` after `codex login`
  - `claude-api` with `ANTHROPIC_API_KEY`

Voice mode also uses OpenAI Whisper transcription, so it needs `OPENAI_API_KEY` even when the chat provider is `claude-api`.

## Quick Start

Build everything:

```powershell
dotnet build src\heronwin.sln
```

Create local assistant config from the examples:

```powershell
Copy-Item src\assistants\cursor\.env.example src\assistants\cursor\.env
Copy-Item src\assistants\tars\.env.example src\assistants\tars\.env
Copy-Item src\assistants\ava\.env.example src\assistants\ava\.env
```

Then edit the `.env` file for the assistant you plan to run. The examples include the default local MCP server registrations for `cognition` and `execution`.

## Run The Assistants

Start the interactive assistant:

```powershell
dotnet run --project src\assistants\cursor
```

Run the included scenario smoke test:

```powershell
dotnet run --project src\assistants\tars -- --scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

Show AVA options:

```powershell
dotnet run --project src\assistants\ava -- --help
```

Run an AVA accessibility validation bundle:

```powershell
dotnet run --project src\assistants\ava -- --run src\scenarios\accessibility\active-window-smoke.bundle.yml
```

Regenerate AVA reports from the latest saved run:

```powershell
dotnet run --project src\assistants\ava -- --regenerate-report latest
```

The helper launcher builds and runs `cursor` by default, or routes `-Scenario` to `tars`:

```powershell
.\buildandrun.ps1 -CursorOnly
.\buildandrun.ps1 -TarsOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

## Debugging And Reports

Enable `DEBUG_TRACE=true` in an assistant `.env` to keep JSONL traces for a run. Render a trace report with either `cursor` or `tars`:

```powershell
dotnet run --project src\assistants\cursor -- --trace-report .\logs\<trace>.jsonl
dotnet run --project src\assistants\tars -- --trace-report .\logs\<trace>.jsonl
```

AVA writes validation evidence and reports under `artifacts\ava`. Desktop screenshots and compact UI artifacts are written beside the relevant trace/debug output unless overridden by environment settings.

## Test

Run the full test suite:

```powershell
dotnet test src\heronwin.sln
```

Focused test projects live next to each assistant and tool package, for example:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj
dotnet test src\tools\desktop-automation.tests\HeronWin.Tools.DesktopAutomation.Tests.csproj
```

## Documentation

User docs:

- [Get Started](./docs/GET_STARTED.md)
- [Scenario Mode](./docs/get-started-script-mode.md)
- [Voice And Text Mode](./docs/get-started-voice-mode.md)
- [OpenAI Configuration](./docs/get-started-openaiconfig.md)
- [Create App Skills](./docs/APP_SKILLS.md)
- [AVA Rule Catalog](./docs/ava/rules/README.md)
- [Docs Index](./docs/README.md)

Component docs:

- [brain](./src/assistants/brain/README.md)
- [cursor](./src/assistants/cursor/README.md)
- [tars](./src/assistants/tars/README.md)
- [ava](./src/assistants/ava/README.md)
- [tools](./src/tools/README.md)
- [desktop automation](./src/tools/desktop-automation/README.md)
- [agent prompts and skills](./.github/agents/README.md)

Developer docs:

- [Developer Docs Index](./devdocs/README.md)
- [Goal And Design](./devdocs/GOAL_AND_DESIGN.md)
- [Development Guardrails](./devdocs/DEVELOPMENT_GUARDRAILS.md)
- [History And Todos](./devdocs/HISTORY_AND_TODOS.md)

## License

Apache License 2.0. See [LICENSE](./LICENSE).
