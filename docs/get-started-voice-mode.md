# Get Started — Voice Mode

Voice mode runs `brain` interactively. It listens to the Windows microphone,
transcribes speech with OpenAI Whisper, and speaks responses back. The
optional `face` companion window shows live status and lets you edit `brain`'s
`.env` from a settings panel.

If you have not yet completed the shared setup, start at
[Get Started](./GET_STARTED.md).

## 1. Hardware and permissions

- A working microphone selected as the Windows default input device
- Speakers or headphones for spoken replies
- Microphone access allowed for desktop apps:
  Windows Settings → Privacy & security → Microphone → Allow desktop apps

## 2. Configure the provider

Edit `src\head\brain\.env`. The provider determines whether the agent boots
into voice or text mode by default:

| `LLM_PROVIDER` | Default UI | Notes                                        |
| -------------- | ---------- | -------------------------------------------- |
| `openai-api`   | voice      | Needs `OPENAI_API_KEY`                       |
| `claude-api`   | voice      | Needs `ANTHROPIC_API_KEY` (+ Whisper key)    |
| `openai-codex` | text       | Run `codex login` first; switch with `/mode:voice` |

Voice transcription always uses OpenAI Whisper, so `OPENAI_API_KEY` should be
set even when `LLM_PROVIDER=claude-api`.

Other voice-related settings in `.env`:

- `WHISPER_MODEL` — defaults to `whisper-1`
- `MAX_RECORD_MS` — single-utterance cap; defaults to `30000`
- `ACTIVE_IDLE_TIMEOUT_MS` — return to wake-word listening after silence
- `POST_ACTION_UI_SETTLE_DELAY_MS` — wait before the final post-action UI snapshot
  that feeds the next LLM attempt; defaults to 1000 ms
- `WAKE_WORD` — phrase that re-activates the agent (default `Hello there`)
- `VOICE_LANGUAGES` — comma-separated list, default `American English, Korean`

## 3. Start `brain` only

```powershell
dotnet run --project src\head\brain
```

You should see the agent boot, connect to the MCP servers, and start
listening. Say the wake word, then your command.

In-session text commands (also available after `/mode:text`):

```text
/reset        # clear the conversation
/exit         # quit
/mode:voice   # switch to voice input
/mode:text    # switch to text input
```

## 4. Start `brain` + `face` together

The `face` companion window shows live status and exposes a settings panel
that can edit `brain`'s `.env`. The repo launcher builds and starts both:

```powershell
.\buildandrun.ps1
```

Useful variants:

```powershell
.\buildandrun.ps1 -FaceOnly        # just the companion UI
.\buildandrun.ps1 -BrainOnly       # just the agent
.\buildandrun.ps1 -NoBuild         # skip the build step
```

`face` connects to `brain` over a local named pipe. If `face` shows
"disconnected", confirm `brain` is running and that no other `brain`
instance is holding the pipe.

## 5. Talk to the agent

Default flow:

1. Say the wake word (e.g. *"Hello there"*).
2. Wait for the active-listening cue, then speak your command.
3. The agent transcribes, plans, calls MCP tools (browser, UI automation,
   process control), and replies in voice.
4. After `ACTIVE_IDLE_TIMEOUT_MS` of silence, it returns to wake-word mode.

Try simple commands first:

- *"Open Notepad."*
- *"What windows do I have open?"*
- *"Go to the Netflix website."*

## 6. Debugging a voice session

- Set `DEBUG_TRACE=1` in `.env` to keep the JSONL trace alongside voice runs.
- Set `DEBUG_AUDIO_PLAYBACK=true` to keep saved `.wav` clips.
- Trace artifacts (`.log`, `.jsonl`, `.png`, `.wav`) are written to a `logs`
  folder next to the executable and cleared at the start of each session.
- If voice fails to transcribe, double-check `OPENAI_API_KEY` and the
  selected Windows input device.

## Next steps

- Use [script mode](./get-started-script-mode.md) for reproducible runs.
- See the [brain README](../src/head/brain/README.md) and
  [face README](../src/head/face/README.md) for component-level details.
- Review [Development Guardrails](./DEVELOPMENT_GUARDRAILS.md) before
  changing agent behavior.
