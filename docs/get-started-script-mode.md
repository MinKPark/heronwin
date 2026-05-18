# Get Started - Scenario Mode

Scenario mode runs `tars` non-interactively from YAML scenario files. It bypasses microphone capture and voice playback, enables JSONL tracing, and evaluates log-based assertions.

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
