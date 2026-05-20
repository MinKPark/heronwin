# cursor

`cursor` is the HeronWin interactive assistant for voice and text sessions. It is the host for direct user-driven control of Windows apps and browsers: inspect the current UI, act through local MCP tools, and keep a live conversation moving in text or voice.

Use `cursor` for interactive work. Use `tars` when the same work should become a repeatable YAML scenario. Use AVA when the work should produce accessibility evidence and reports.

Run from the repository root:

```powershell
dotnet run --project src/assistants/cursor
```

Interactive mode is provider-defined:

- `LLM_PROVIDER=openai-api` starts in voice mode.
- `LLM_PROVIDER=openai-codex` starts in text mode.
- `LLM_PROVIDER=claude-api` starts in voice mode.

Text mode commands:

```powershell
/reset
/exit
/mode:voice
/mode:text
```

Trace reports use the shared brain diagnostic engine:

```powershell
dotnet run --project src/assistants/cursor -- --trace-report .\logs\<trace>.jsonl
```

Local config normally lives in `src/assistants/cursor/.env`. Start from `.env.example`; relative MCP paths in that file are resolved from the `cursor` folder.
