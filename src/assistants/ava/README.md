# AVA Assistant

AVA is the HeronWin accessibility validation assistant host.

Phase 1 provides the runnable CLI skeleton, assistant prompt profile, and config normalization. The help surface is available now:

```powershell
dotnet run --project src/assistants/ava -- --help
```

The future validation entry point is reserved for Phase 2:

```powershell
dotnet run --project src/assistants/ava -- --ux-scenario .\scenario.yml --validation-config .\validation.yml
```

Local config normally lives in `src/assistants/ava/.env`. Start from `.env.example`; relative MCP paths in that file are resolved from the `ava` folder.
