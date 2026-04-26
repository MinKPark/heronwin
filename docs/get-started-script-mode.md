# Get Started — Script Mode

Script mode runs `brain` non-interactively. It bypasses the microphone and voice
playback, but still uses the normal agent loop and MCP tool flow. This is the
recommended way to reproduce scenarios, run smoke tests, and capture traces.

If you have not yet completed the shared setup, start at
[Get Started](./GET_STARTED.md).

## 1. Pick what to run

Script mode supports three input shapes:

| Input              | Use it when                                     |
| ------------------ | ----------------------------------------------- |
| `--command`        | One or more ad-hoc commands on the CLI          |
| `--commands-file`  | A reusable list of commands in a YAML file      |
| `--scenario`       | A scenario with commands **and** assertions     |

## 2. Run a one-shot command

```powershell
dotnet run --project src\head\brain -- --command "Open the Netflix website."
```

Chain multiple commands by repeating `--command`:

```powershell
dotnet run --project src\head\brain `
    --command "Go to the Netflix website." `
    --command "Search for Boyfriend on Demand."
```

## 3. Run a commands file

Create a `steps.yml`:

```yaml
commands:
  - "Go to the Netflix website."
  - "Search for the show Boyfriend on Demand."
```

Run it:

```powershell
dotnet run --project src\head\brain -- --commands-file .\steps.yml
```

`--commands-file` also accepts a plain YAML sequence of strings.

## 4. Run a scenario with assertions

A scenario adds pass/fail checks against the trace log. Example
`scenario.yml`:

```yaml
name: Netflix smoke
commands:
  - "Navigate the active browser tab directly to https://www.netflix.com/."
  - "Search for Boyfriend on Demand and play the first episode."
assertions:
  requiredCategories:
    - assistant.reply
  forbiddenCategories:
    - agent.reply_contradiction_detected
  requiredFinalText:
    - Boyfriend on Demand
  forbiddenFinalText:
    - not complete
```

Run it directly:

```powershell
dotnet run --project src\head\brain -- --scenario .\scenario.yml
```

Or through the repo launcher (builds first, runs `brain` only):

```powershell
.\buildandrun.ps1 -BrainOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

A real working example lives at
[src/scenarios/netflix-boyfriend-on-demand.yml](../src/scenarios/netflix-boyfriend-on-demand.yml).

## 5. Inspect the trace

Script mode automatically enables the JSONL debug trace and marks the run
failed when the trace shows tool errors, reply contradictions, or an
explicitly unresolved final outcome (unless the scenario allows them).

Artifacts are written to a `logs` folder next to the `brain` executable, and
old artifacts are cleared at the start of each run. Useful files:

- `*.jsonl` — full event trace
- `*.log`   — human-readable log
- `*.png`   — screenshots captured by tools
- `*.wav`   — saved audio clips (only relevant in voice mode)

Render a Markdown summary of any saved trace:

```powershell
dotnet run --project src\head\brain -- --trace-report .\logs\<trace>.jsonl
```

## Useful environment variables

Set these in `src\head\brain\.env` (or the current shell):

- `NETFLIX_PROFILE_PIN` — substituted into scenarios that reference
  `${NETFLIX_PROFILE_PIN}`
- `MCP_TOOL_TIMEOUT_MS` — override the default 20s per-tool MCP timeout
- `MAX_CONTEXT_TOKENS` — cap on conversation context size
- `DEBUG_TRACE=1` — also keep the JSONL trace in voice mode

## Next steps

- Try [voice mode](./get-started-voice-mode.md) for interactive use.
- Read the [brain README](../src/head/brain/README.md) for the full CLI surface.
- See [Development Guardrails](./DEVELOPMENT_GUARDRAILS.md) before changing
  agent behavior.
