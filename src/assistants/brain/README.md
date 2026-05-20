# brain

`brain` is the shared HeronWin assistant library. It is not a runnable assistant.

It owns shared mechanics used by `cursor`, `tars`, and AVA:

- prompt loading and skill composition
- LLM provider clients and model profiles
- MCP stdio client integration
- built-in process tools
- desktop session primitives
- shared message, reply, tool, trace, and YAML models
- debug trace writing, artifact cleanup, and trace-report generation
- `.env` loading and shared configuration parsing

The runnable assistants define the workflow role:

- `cursor`: live text and voice control.
- `tars`: repeatable YAML scenario automation.
- AVA: scenario-backed accessibility validation and reporting.

Runnable hosts live next to this library:

```powershell
dotnet run --project src/assistants/cursor
dotnet run --project src/assistants/tars -- --scenario src/scenarios/netflix-boyfriend-on-demand.yml
dotnet run --project src/assistants/ava -- --help
```

The runnable assistants expose the shared trace-report command:

```powershell
dotnet run --project src/assistants/cursor -- --trace-report .\logs\<trace>.jsonl
dotnet run --project src/assistants/tars -- --trace-report .\logs\<trace>.jsonl
dotnet run --project src/assistants/ava -- --trace-report .\logs\<trace>.jsonl
```

Keep assistant policy in the assistant projects and prompt profiles. Keep shared provider, tool, prompt, and diagnostic plumbing here.
