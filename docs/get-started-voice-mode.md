# Get Started - Voice And Text Mode

Interactive mode runs `cursor`. It supports typed requests, voice transcription with Whisper, speech playback, and local mode commands.

Configure `src\assistants\cursor\.env`, then run:

```powershell
dotnet run --project src\assistants\cursor
```

Or use the launcher:

```powershell
.\buildandrun.ps1 -CursorOnly
```

Provider defaults:

| `LLM_PROVIDER` | Default mode | Notes |
| --- | --- | --- |
| `openai-api` | voice | Needs `OPENAI_API_KEY` |
| `openai-codex` | text | Run `codex login` first |
| `claude-api` | voice | Needs `ANTHROPIC_API_KEY`; voice transcription also needs `OPENAI_API_KEY` |

Text commands:

```text
/reset
/exit
/mode:voice
/mode:text
```

For voice mode, confirm Windows has microphone access enabled for desktop apps. Set `DEBUG_TRACE=1` to keep JSONL traces for interactive sessions.
