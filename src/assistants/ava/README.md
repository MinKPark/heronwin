# AVA Assistant

AVA is the HeronWin accessibility validation assistant host.

AVA owns the validation run: it reads the UX scenario, drives UI through action
tools, collects UI evidence, runs deterministic accessibility validators, and
writes Markdown/JSON reports.

```powershell
dotnet run --project src/assistants/ava -- --help
```

Run with direct inputs:

```powershell
dotnet run --project src/assistants/ava -- --ux-scenario .\scenario.yml --validation-config .\validation.yml
```

Run with a bundle:

```powershell
dotnet run --project src/assistants/ava -- --run .\bundle.yml
```

Local config normally lives in `src/assistants/ava/.env`. Start from `.env.example`; relative MCP paths in that file are resolved from the `ava` folder.
