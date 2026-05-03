# tars

`tars` is the HeronWin scenario assistant. It runs YAML scenario files with log-based assertions and debug trace output.

Run from the repository root:

```powershell
dotnet run --project src/assistants/tars -- --scenario src/scenarios/netflix-boyfriend-on-demand.yml
```

One-step scripted work should still be written as a one-command scenario file. Ad-hoc command flags are intentionally not supported.

Trace reports use the shared brain diagnostic engine:

```powershell
dotnet run --project src/assistants/tars -- --trace-report .\logs\<trace>.jsonl
```

Local config normally lives in `src/assistants/tars/.env`. Start from `.env.example`; relative MCP paths in that file are resolved from the `tars` folder.
