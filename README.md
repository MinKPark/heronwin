# heronwin

*Her on Windows platform* — an AI agent system with a voice-driven UI and local MCP tool servers.

## Repository Layout

```
heronwin/
└── src/
    ├── herhead/
    │   ├── brain/          # .NET 10 AI agent runtime (voice input, scripted runs, MCP client)
    │   ├── face/           # WPF desktop companion window for status, settings, and live state
    │   └── brain.tests/    # xUnit tests for brain
    ├── herbody/          # MCP servers (TypeScript / C#)
    │   ├── process-manager/   # Start, stop, and list processes on the local machine
    │   └── eyesandhands/      # Inspect Windows UI and interact with windows via UI Automation
    └── scenarios/        # YAML scenarios for scripted runs
```

## Quick Start

### 1. Start the process-manager MCP server (build once)

```bash
cd src/herbody/process-manager
npm install
npm run build
```

### 2. Configure and start the brain agent

```powershell
# Edit src/herhead/brain/.env as needed, then run:
dotnet run --project src/herhead/brain
```

### 3. Start the face companion UI

```powershell
dotnet run --project src/herhead/face
```

`face` connects to `brain` over a local named pipe and can also edit the selected `brain` `.env` file from its settings window.

### Running without a build step

The TypeScript MCP server supports `npm run dev` (via [tsx](https://tsx.is)) for hot-reload development. The C# server can be started directly with `dotnet run --project src/herbody/eyesandhands/eyesandhands.csproj`.

## Documentation

- [progress and status](./PROGRESS.md)
- [brain README](./src/herhead/brain/README.md)
- [herbody README](./src/herbody/README.md)
- [process-manager README](./src/herbody/process-manager/README.md)
- [eyesandhands README](./src/herbody/eyesandhands/README.md)
