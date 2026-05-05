# heronwin

HeronWin is a Windows desktop assistant system with local MCP tool servers, shared runtime plumbing, and two assistant hosts:

- `cursor`: interactive voice/text assistant
- `tars`: scenario assistant for reproducible YAML runs
- `brain`: shared .NET library used by both assistants

## Repository Layout

```text
src/
  assistants/
    brain/        shared provider, prompt, MCP, trace, config, and desktop primitives
    cursor/       interactive voice/text assistant
    tars/         scenario assistant
    *.tests/      assistant and shared runtime tests
  tools/          MCP servers and shared desktop automation code
  scenarios/      YAML scenarios for tars
```

## Quick Start

```powershell
dotnet build src/heronwin.sln
Copy-Item src/assistants/cursor/.env.example src/assistants/cursor/.env
dotnet run --project src/assistants/cursor
```

Run a scenario:

```powershell
Copy-Item src/assistants/tars/.env.example src/assistants/tars/.env
dotnet run --project src/assistants/tars -- --scenario src/scenarios/netflix-boyfriend-on-demand.yml
```

The launcher defaults to `cursor` and routes `-Scenario` to `tars`:

```powershell
.\buildandrun.ps1 -CursorOnly
.\buildandrun.ps1 -TarsOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

## Documentation

User docs:

- [get started](./docs/GET_STARTED.md)
- [scenario mode](./docs/get-started-script-mode.md)
- [voice/text mode](./docs/get-started-voice-mode.md)
- [OpenAI configuration](./docs/get-started-openaiconfig.md)
- [create app skills](./docs/APP_SKILLS.md)
- [docs index](./docs/README.md)

Developer docs:

- [developer docs index](./devdocs/README.md)
- [goal and design](./devdocs/GOAL_AND_DESIGN.md)
- [development guardrails](./devdocs/DEVELOPMENT_GUARDRAILS.md)
- [history and todos](./devdocs/HISTORY_AND_TODOS.md)

Component docs:

- [brain README](./src/assistants/brain/README.md)
- [tars README](./src/assistants/tars/README.md)
- [cursor README](./src/assistants/cursor/README.md)
- [tools README](./src/tools/README.md)

## License

Apache License 2.0. See [LICENSE](./LICENSE).
