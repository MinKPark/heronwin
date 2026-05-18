# Get Started With AVA

## What It Is

AVA is the HeronWin accessibility validation assistant. It reads a UX scenario, drives the target UI through local MCP action tools, collects UI evidence, runs deterministic accessibility validators, and writes Markdown/JSON reports.

Use AVA when you want a repeatable accessibility validation run for a Windows app or browser workflow, with saved evidence and findings that can be reviewed after the UI work is done.

## How To Run

From the repository root, build the solution once:

```powershell
dotnet build src\heronwin.sln
```

Create local configuration from the example file:

```powershell
Copy-Item src\assistants\ava\.env.example src\assistants\ava\.env
```

Edit `src\assistants\ava\.env` and set `LLM_PROVIDER` plus the matching provider credentials. See [Environment Configuration](../ENV_CONFIGURATION.md) for provider routes, MCP server wiring, tracing, AVA role overrides, and CDP web-evidence settings.

Show the available AVA commands:

```powershell
dotnet run --project src\assistants\ava -- --help
```

Run the included active-window smoke bundle:

```powershell
dotnet run --project src\assistants\ava -- --run src\scenarios\accessibility\active-window-smoke.bundle.yml
```

Or run with direct UX scenario and validation config inputs:

```powershell
dotnet run --project src\assistants\ava -- --ux-scenario src\scenarios\accessibility\ux\active-window-smoke.yml --validation-config src\scenarios\accessibility\validation-configs\federal-windows-uia-min.yml
```

Regenerate reports from a saved run without driving the UI again:

```powershell
dotnet run --project src\assistants\ava -- --regenerate-report latest
dotnet run --project src\assistants\ava -- --regenerate-report .\artifacts\ava\<run-id>
```

To render a Markdown latency report from a saved JSONL trace:

```powershell
dotnet run --project src\assistants\ava -- --trace-report .\logs\<trace>.jsonl
```

## What To Expect

AVA starts, loads the configured provider and local MCP servers, reads the UX scenario and validation config, then performs the scenario steps against the current desktop or browser state. It collects evidence at configured checkpoints and evaluates that evidence against the selected accessibility profile.

Validation output is written under `artifacts\ava`. Expect saved run data, Markdown/JSON reports, and rule findings that identify the affected control, evidence source, severity, and validation rule where available.

If a UI step fails, AVA should continue or stop based on the validation config policy. The included minimum profiles are designed for smoke-level checks, so they are useful for verifying the validation loop before adding broader scenario coverage.
