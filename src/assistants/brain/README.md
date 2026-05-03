# brain

`brain` is the shared HeronWin assistant library. It is not a runnable assistant.

It owns shared mechanics used by `tars` and `cursor`:

- prompt loading and skill composition
- LLM provider clients and model profiles
- MCP stdio client integration
- built-in process tools
- desktop session primitives
- shared message, reply, tool, trace, and YAML models
- debug trace writing, artifact cleanup, and trace-report generation
- `.env` loading and shared configuration parsing

Runnable hosts live next to this library:

```powershell
dotnet run --project src/assistants/cursor
dotnet run --project src/assistants/tars -- --scenario src/scenarios/netflix-boyfriend-on-demand.yml
```

Both assistants expose the shared trace-report command:

```powershell
dotnet run --project src/assistants/cursor -- --trace-report .\logs\<trace>.jsonl
dotnet run --project src/assistants/tars -- --trace-report .\logs\<trace>.jsonl
```

Keep assistant policy in the assistant projects and prompt profiles. Keep shared provider, tool, prompt, and diagnostic plumbing here.
