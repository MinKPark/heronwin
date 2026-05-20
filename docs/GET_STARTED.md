# Get Started

HeronWin is designed for local, explicit, testable desktop automation on Windows. The assistant observes UI state through local MCP inspection tools, chooses actions through local execution tools, records traces, and can replay or validate workflows from YAML.

Pick the assistant that matches the work:

- [Voice/text mode](./get-started-voice-mode.md): use `cursor` for live text or voice control of Windows apps and browsers.
- [Scenario mode](./get-started-script-mode.md): use `tars` for repeatable YAML automation and log-based assertions.
- [AVA mode](./ava/getstarted.md): use `ava` for scenario-backed accessibility validation with saved evidence and reports.

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
