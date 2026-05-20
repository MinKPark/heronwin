# Get Started - Scenario Mode

Scenario mode runs `tars`, the repeatable automation assistant. Use it when the workflow should be saved, replayed, debugged, or checked as a smoke test or regression scenario.

`tars` runs non-interactively from YAML scenario files. It bypasses microphone capture and voice playback, enables JSONL tracing, drives UI through local MCP tools, and evaluates log-based assertions.

Run the included smoke scenario:

```powershell
dotnet run --project src\assistants\tars -- --scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

Or use the launcher:

```powershell
.\buildandrun.ps1 -TarsOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

One-step scripted work should be represented as a one-command scenario file:

```yaml
name: Open Notepad
commands:
  - "Open Notepad."
assertions:
  requiredCategories:
    - assistant.reply
```

Render a Markdown report from a saved trace:

```powershell
dotnet run --project src\assistants\tars -- --trace-report .\logs\<trace>.jsonl
```

Local config normally lives in `src\assistants\tars\.env`; start from `src\assistants\tars\.env.example`. See [Environment Configuration](./ENV_CONFIGURATION.md) for provider routes, MCP server wiring, tracing, and scenario placeholders.
