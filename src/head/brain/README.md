# brain

.NET 10 implementation of `brain` for Windows.

Current scope:

- voice input from the Windows microphone
- interactive text input from the console
- scripted command input from the console for scenario testing
- OpenAI API, ChatGPT / Codex sign-in, and Claude API chat backends
- OpenAI Whisper transcription
- MCP stdio client integration
- built-in process listing, start, and stop tools

Development approach:

- Treat the agent core prompt and skills under `.github/agents/` as the main place for behavior tuning.
- Prefer prompt and skill updates first when a scenario needs better action ordering, search behavior, verification standards, or conditional handling.
- Use runtime code only for general guardrails, deterministic recovery, tool interpretation, or cases that are mostly impossible to stabilize through prompt guidance alone.

Run from this directory with:

```powershell
dotnet run --project .
```

Interactive mode is provider-defined:

- `LLM_PROVIDER=openai-api` starts in voice mode
- `LLM_PROVIDER=openai-codex` starts in text mode
- `LLM_PROVIDER=claude-api` starts in voice mode

In text mode:

```powershell
/reset
/exit
/mode:voice
```

Run scripted commands without waiting for voice input:

```powershell
dotnet run --project . -- --command "open netflix"
dotnet run --project . -- --command "open netflix" --command "play the trailer"
dotnet run --project . -- --commands-file .\steps.yml
```

Run a scenario file with log-based assertions:

```powershell
dotnet run --project . -- --scenario .\scenario.yml
```

Example `steps.yml`:

```yaml
commands:
  - "Go to the Netflix website."
  - "Search for the show Boyfriend on Demand."
```

Example `scenario.yml`:

```yaml
name: Netflix smoke
commands:
  - "open netflix"
assertions:
  requiredCategories:
    - assistant.reply
  forbiddenCategories:
    - agent.reply_contradiction_detected
  requiredFinalText:
    - Netflix
  forbiddenFinalText:
    - not complete
```

Notes:

- Scripted mode bypasses microphone capture and voice playback, but it still uses the normal brain agent and MCP tool flow.
- `openai-codex` uses the local Codex CLI login state. Run `codex login` first, then set `LLM_PROVIDER=openai-codex`.
- Legacy `LLM_PROVIDER=chatgpt` and `LLM_PROVIDER=chatgpt-web` inputs now map to the experimental `openai-codex` provider.
- The default repository workflow is skill first, code last. See `.github/agents/skill-vs-code-policy.md`.
- `--commands-file` accepts a YAML sequence of strings, or a YAML mapping with a `commands:` sequence.
- Scripted mode enables the debug JSONL trace automatically and marks a turn as failed when the log shows tool errors, reply contradictions, or an explicitly unresolved final outcome unless the scenario allows them.
- Set `DEBUG_TRACE=1` if you also want persistent debug logs in normal voice mode.
- `VOICE_LANGUAGES` accepts a comma-separated list of the user's main voice languages. The default is `American English, Korean`.
- Debug artifacts such as `.log`, `.jsonl`, `.png`, and saved `.wav` files are written under a `logs` folder next to the executable, and old artifacts are cleared when the next session starts.
- `MCP_TOOL_TIMEOUT_MS` can be set to override the default per-tool MCP timeout of 20 seconds.

## Debugging Safety On Windows

- Prefer `dotnet test`, normal app execution, JSONL traces, and purpose-built code/test helpers over ad hoc PowerShell reflection against build outputs.
- Avoid PowerShell one-liners that load assemblies in memory with patterns such as `[System.Reflection.Assembly]::LoadFrom(...)` when inspecting `Brain.dll` or other local binaries.
- On Windows, Defender may classify those reflection-style inspection commands as trojan-like behavior even when they were only used for local debugging.
