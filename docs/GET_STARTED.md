# Get Started

Pick the assistant that matches the work:

- [Scenario mode](./get-started-script-mode.md): run YAML scenarios with `tars`.
- [Voice/text mode](./get-started-voice-mode.md): collaborate interactively with `cursor`.
- [AVA mode](./ava/getstarted.md): run repeatable accessibility validation with saved evidence and reports.

## Prerequisites

- Windows 10/11 x64
- .NET SDK 10.0.201 or newer
- This repository cloned locally

Build once:

```powershell
dotnet build src\heronwin.sln
```

Create local config for the assistant you plan to run:

```powershell
Copy-Item src\assistants\cursor\.env.example src\assistants\cursor\.env
Copy-Item src\assistants\tars\.env.example src\assistants\tars\.env
Copy-Item src\assistants\ava\.env.example src\assistants\ava\.env
```

Configure the `.env` files:

See [Environment Configuration](./ENV_CONFIGURATION.md) for file locations, provider routes, MCP server wiring, tracing, voice settings, scenario placeholders, and AVA role overrides.
