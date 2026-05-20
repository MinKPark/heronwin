# tars

`tars` is the HeronWin scenario assistant. It runs YAML scenario files with log-based assertions and debug trace output, using the same local inspection and execution tools as the interactive assistant.

Use `tars` for repeatable automation: smoke tests, app playbooks, regression checks, and one-command scripted tasks that should be stored as scenario files. Use `cursor` for live text/voice control, and use AVA when the scenario should produce accessibility evidence and reports.

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
