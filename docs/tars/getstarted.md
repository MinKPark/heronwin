# Get Started With tars

## What It Is

`tars` is the HeronWin scenario assistant. It runs scripted YAML scenarios non-interactively, drives desktop or browser workflows through the local MCP tools, records trace output, and checks the run against log-based assertions.

Use `tars` when you want a repeatable workflow: smoke tests, app playbooks, regression checks, or one-command scripted tasks that should be saved as scenario files.

## How To Run

From the repository root, build the solution once:

```powershell
dotnet build src\heronwin.sln
```

Create local configuration from the example file:

```powershell
Copy-Item src\assistants\tars\.env.example src\assistants\tars\.env
```

Edit `src\assistants\tars\.env` and set `LLM_PROVIDER` plus the matching provider credentials. See [Environment Configuration](../ENV_CONFIGURATION.md) for provider routes, MCP server wiring, tracing, and scenario placeholders.

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

To render a Markdown report from a saved JSONL trace:

```powershell
dotnet run --project src\assistants\tars -- --trace-report .\logs\<trace>.jsonl
```

## What To Expect

`tars` starts, loads the configured provider and local MCP servers, reads the scenario file, and runs each command in order. It prints progress to the console while it observes the UI, takes actions, and evaluates scenario assertions.

Scenario mode bypasses microphone capture and voice playback. Scenario runs are intended to be deterministic enough to debug. If a command or assertion fails, the console output and saved trace artifacts should point to the failed step. Trace and debug artifacts are written under the assistant logs directory; scenario mode enables JSONL tracing automatically.

For ad-hoc work, create a small scenario with a single command instead of passing free-form command text on the CLI.
