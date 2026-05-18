# Get Started With cursor

## What It Is

`cursor` is the HeronWin interactive assistant. It supports typed requests, voice transcription, speech playback, and local desktop or browser automation through the configured MCP tools.

Use `cursor` when you want to collaborate live with the assistant: ask it to inspect the current Windows UI, control apps, perform a workflow, or switch between text and voice interaction during the same session.

## How To Run

From the repository root, build the solution once:

```powershell
dotnet build src\heronwin.sln
```

Create local configuration from the example file:

```powershell
Copy-Item src\assistants\cursor\.env.example src\assistants\cursor\.env
```

Edit `src\assistants\cursor\.env` and set `LLM_PROVIDER` plus the matching provider credentials. The supported routes are `openai-api`, `openai-codex`, and `claude-api`.

Start the assistant:

```powershell
dotnet run --project src\assistants\cursor
```

Or use the launcher:

```powershell
.\buildandrun.ps1 -CursorOnly
```

Interactive mode depends on the configured provider:

| `LLM_PROVIDER` | Default mode | Notes |
| --- | --- | --- |
| `openai-api` | voice | Needs `OPENAI_API_KEY` |
| `openai-codex` | text | Run `codex login` first |
| `claude-api` | voice | Needs `ANTHROPIC_API_KEY`; voice transcription also needs `OPENAI_API_KEY` |

Text mode commands:

```text
/reset
/exit
/mode:voice
/mode:text
```

To render a Markdown report from a saved JSONL trace:

```powershell
dotnet run --project src\assistants\cursor -- --trace-report .\logs\<trace>.jsonl
```

## What To Expect

`cursor` starts, loads the configured provider and local MCP servers, then opens either voice or text mode. In text mode, type a request at the prompt. In voice mode, speak the configured wake word before a request; the default wake word is `Hello there`.

The assistant may inspect visible windows, read UI Automation trees, take screenshots, launch apps, click or focus controls, type text, and send shortcuts through local tools. It prints user and assistant turns to the console, and can save trace artifacts when `DEBUG_TRACE=true`.

For voice mode, confirm Windows microphone access is enabled for desktop apps and keep `OPENAI_API_KEY` configured for Whisper transcription.
