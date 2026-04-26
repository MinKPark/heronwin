# heronwin

*Her on Windows platform* — an AI agent system with a voice-driven UI, built-in local process tools, and local MCP tool servers.

## Repository Layout

```
heronwin/
└── src/
    ├── head/
    │   ├── brain/                    # .NET 10 AI agent runtime (voice input, scripted runs, MCP client, process tools)
    │   ├── brain.tests/              # xUnit tests for brain
    │   └── face/                     # WPF desktop companion window for status, settings, and live state
    ├── body/                         # MCP servers and shared desktop automation code
    │   ├── cognition/                # Inspect Windows UI and window structure
    │   ├── execution/                # Interact with Windows UI and applications
    │   ├── desktop-automation/       # Shared UI Automation library
    │   ├── desktop-automation.tests/ # xUnit tests for desktop-automation
    │   └── micrecorder/              # Microphone capture helper used by brain
    └── scenarios/                    # YAML scenarios for scripted runs
```

## Quick Start

### 1. Build the .NET solution

```powershell
dotnet build src/heronwin.sln
```

### 2. Configure and start the brain agent

```powershell
# Copy src/head/brain/.env.example to src/head/brain/.env, edit it as needed, then run:
dotnet run --project src/head/brain
```

### 3. Start the face companion UI

```powershell
dotnet run --project src/head/face
```

`face` connects to `brain` over a local named pipe and can also edit the selected `brain` `.env` file from its settings window.

### Running without a build step

The C# MCP servers can be started directly with `dotnet run --project src/body/cognition/cognition.csproj` and `dotnet run --project src/body/execution/execution.csproj`. Process listing, start, and stop tools are built into `brain`.

## Documentation

- [get started](./docs/GET_STARTED.md)
  - [script mode](./docs/get-started-script-mode.md)
  - [voice mode](./docs/get-started-voice-mode.md)
- [docs index](./docs/README.md)
- [goal and design](./docs/GOAL_AND_DESIGN.md)
- [history and todos](./docs/HISTORY_AND_TODOS.md)
- [development guardrails](./docs/DEVELOPMENT_GUARDRAILS.md)
- [brain README](./src/head/brain/README.md)
- [face README](./src/head/face/README.md)
- [body README](./src/body/README.md)
- [desktop-automation README](./src/body/desktop-automation/README.md)

## License

This project is licensed under the Apache License 2.0. See [LICENSE](./LICENSE) for the full text.
