# Get Started

Welcome to `heronwin`. Pick the mode that matches how you want to drive the agent:

- **[Script mode](./get-started-script-mode.md)** — non-interactive runs from `.yml` scenarios or one-shot commands. Best for testing, demos, and reproducible scenarios.
- **[Voice mode](./get-started-voice-mode.md)** — interactive voice-driven sessions through the microphone, optionally with the `face` companion UI.

## Prerequisites (both modes)

Install once on the machine that will run the agent:

- Windows 10/11 (x64)
- [.NET SDK 10](https://dotnet.microsoft.com/download) (`10.0.201` or newer)
- A clone of this repository

Create your local `brain` config from the template:

```powershell
Copy-Item src\head\brain\.env.example src\head\brain\.env
```

Open `src\head\brain\.env` and fill in at least:

- `LLM_PROVIDER` — one of `openai-api`, `openai-codex`, `claude-api`
- `OPENAI_API_KEY` — required for `openai-api` and for Whisper voice transcription
- `ANTHROPIC_API_KEY` — required for `claude-api`
- For `openai-codex`: run `codex login` first; no API key is needed

For OpenAI API and Codex model settings, see
[OpenAI configuration](./get-started-openaiconfig.md).

Verify the build:

```powershell
dotnet build src\heronwin.sln
```

Once the prerequisites are in place, head to the mode-specific guide:

- [Script mode →](./get-started-script-mode.md)
- [Voice mode →](./get-started-voice-mode.md)
