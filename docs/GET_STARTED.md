# Get Started

Pick the assistant that matches the work:

- [Scenario mode](./get-started-script-mode.md): run YAML scenarios with `tars`.
- [Voice/text mode](./get-started-voice-mode.md): collaborate interactively with `cursor`.

## Prerequisites

- Windows 10/11 x64
- .NET SDK 10.0.201 or newer
- This repository cloned locally

Build once:

```powershell
dotnet build src\heronwin.sln
```

Create local config for the assistant you plan to run:

```powershell
Copy-Item src\assistants\cursor\.env.example src\assistants\cursor\.env
Copy-Item src\assistants\tars\.env.example src\assistants\tars\.env
```

Fill in provider settings:

- `LLM_PROVIDER`: `openai-api`, `openai-codex`, or `claude-api`
- `OPENAI_API_KEY`: required for `openai-api` and Whisper voice transcription
- `ANTHROPIC_API_KEY`: required for `claude-api`
- For `openai-codex`, run `codex login` first

`src/assistants/.env` is also supported as a shared convenience file. Relative MCP paths in that file must be relative to `src/assistants`.
