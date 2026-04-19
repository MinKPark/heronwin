# heronwin

*Her on Windows platform* — an AI agent system with a voice-driven UI and local MCP tool servers.

## Repository Layout

```
heronwin/
└── src/
    ├── head/
    │   ├── brain/          # .NET 10 AI agent runtime (voice input, scripted runs, MCP client)
    │   ├── face/           # WPF desktop companion window for status, settings, and live state
    │   └── brain.tests/    # xUnit tests for brain
    ├── body/             # MCP servers and shared desktop automation code
    │   ├── process-manager/   # Start, stop, and list processes on the local machine
    │   ├── cognition/         # Inspect Windows UI and window structure
    │   ├── execution/         # Interact with Windows UI and applications
    │   └── desktop-automation/# Shared UI Automation library
    └── scenarios/        # YAML scenarios for scripted runs
```

## Quick Start

### 1. Start the process-manager MCP server (build once)

```bash
cd src/body/process-manager
npm install
npm run build
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

The TypeScript MCP server supports `npm run dev` (via [tsx](https://tsx.is)) for hot-reload development. The C# MCP servers can be started directly with `dotnet run --project src/body/cognition/cognition.csproj` and `dotnet run --project src/body/execution/execution.csproj`.

## Documentation

- [docs index](./docs/README.md)
- [goal and design](./docs/GOAL_AND_DESIGN.md)
- [history and todos](./docs/HISTORY_AND_TODOS.md)
- [development guardrails](./docs/DEVELOPMENT_GUARDRAILS.md)
- [brain README](./src/head/brain/README.md)
- [face README](./src/head/face/README.md)
- [body README](./src/body/README.md)
- [process-manager README](./src/body/process-manager/README.md)
- [desktop-automation README](./src/body/desktop-automation/README.md)
