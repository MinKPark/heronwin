# herface

.NET 10 implementation of `herface` for Windows.

Current scope:

- voice input from the Windows microphone
- scripted command input from the console for scenario testing
- OpenAI API and Claude API chat backends
- OpenAI Whisper transcription
- MCP stdio client integration

Not included in this first version:

- browser-backed ChatGPT mode

Run with:

```powershell
dotnet run --project herface
```

Run scripted commands without waiting for voice input:

```powershell
dotnet run --project herface -- --command "open netflix"
dotnet run --project herface -- --command "open netflix" --command "play the trailer"
dotnet run --project herface -- --commands-file .\steps.txt
```

Run a scenario file with log-based assertions:

```powershell
dotnet run --project herface -- --scenario .\scenario.json
```

Example `scenario.json`:

```json
{
  "name": "Netflix smoke",
  "commands": [
    "open netflix"
  ],
  "assertions": {
    "requiredCategories": ["assistant.reply"],
    "forbiddenCategories": ["agent.reply_contradiction_detected"],
    "requiredFinalText": ["Netflix"],
    "forbiddenFinalText": ["not complete"]
  }
}
```

Notes:

- Scripted mode bypasses microphone capture and voice playback, but it still uses the normal herface agent and MCP tool flow.
- Scripted mode enables the debug JSONL trace automatically and marks a turn as failed when the log shows tool errors, reply contradictions, or an explicitly unresolved final outcome unless the scenario allows them.
- Set `DEBUG_TRACE=1` if you also want persistent debug logs in normal voice mode.
