# Environment Configuration

HeronWin uses `.env` files for local assistant configuration: provider credentials, model routing, MCP tool servers, tracing, voice settings, scenario secrets, and AVA validation options.

## Create A Local `.env`

Start with the assistant-local example for the assistant you plan to run:

```powershell
Copy-Item src\assistants\cursor\.env.example src\assistants\cursor\.env
Copy-Item src\assistants\tars\.env.example src\assistants\tars\.env
Copy-Item src\assistants\ava\.env.example src\assistants\ava\.env
```

Use assistant-local files for normal setup:

| Assistant | File | Use it for |
| --- | --- | --- |
| `cursor` | `src\assistants\cursor\.env` | Interactive text/voice sessions. |
| `tars` | `src\assistants\tars\.env` | Scripted YAML scenario runs. |
| `ava` | `src\assistants\ava\.env` | Accessibility validation runs and reports. |

`src\assistants\.env` is also supported as a shared convenience file when all assistants should use the same provider, model, and MCP wiring.

## Loading Rules

HeronWin loads the first `.env` file it finds. From the repository root, the practical lookup order is:

1. `.env`
2. `src\assistants\<assistant>\.env`
3. `src\assistants\.env`
4. Legacy migration paths under `src\head\brain`

Process environment variables take precedence over `.env` values. A `.env` line only fills a setting when that variable is not already set in the launching process.

Relative paths inside `MCP_SERVERS` are resolved from the directory containing the loaded `.env` file. That is why the assistant-local examples use paths like `../../tools/cognition/...`, while a shared `src\assistants\.env` needs paths relative to `src\assistants`.

## Provider Routes

Set `LLM_PROVIDER` to choose the chat route:

| Route | `LLM_PROVIDER` | Model setting | Authentication |
| --- | --- | --- | --- |
| OpenAI Platform API | `openai-api` | `OPENAI_MODEL` | `OPENAI_API_KEY` |
| ChatGPT / Codex sign-in | `openai-codex` | `OPENAI_CODEX_MODEL` | `codex login` |
| Anthropic API | `claude-api` | `ANTHROPIC_MODEL` | `ANTHROPIC_API_KEY` |

OpenAI API example:

```dotenv
LLM_PROVIDER=openai-api
OPENAI_API_KEY=<your-openai-api-key>
OPENAI_MODEL=gpt-5.4-mini
OPENAI_CODEX_COMMAND=codex
OPENAI_CODEX_MODEL=
LLM_REASONING_EFFORT=
```

Anthropic API example:

```dotenv
LLM_PROVIDER=claude-api
ANTHROPIC_API_KEY=<your-anthropic-api-key>
ANTHROPIC_MODEL=claude-3-5-sonnet-20241022
OPENAI_API_KEY=
```

For the Codex route, run:

```powershell
codex login
```

Leave `OPENAI_CODEX_MODEL` empty to use the Codex CLI default model, or set it explicitly. For Codex Spark:

```dotenv
LLM_PROVIDER=openai-codex
OPENAI_CODEX_COMMAND=codex
OPENAI_CODEX_MODEL=gpt-5.3-codex-spark
```

HeronWin treats Codex Spark as text-only and omits screenshot attachments for that model so `codex exec` does not receive unsupported `--image` inputs.

## Shared Settings

| Setting | Applies to | Purpose |
| --- | --- | --- |
| `MCP_SERVERS` | All assistants | JSON list of local MCP servers. The examples register `cognition` and `execution`. |
| `MAX_CONTEXT_TOKENS` | All assistants | Conversation context budget passed to the runtime. |
| `POST_ACTION_UI_SETTLE_DELAY_MS` | All assistants | Delay after UI actions before collecting fresh evidence. |
| `DEBUG_TRACE` | All assistants | Keeps JSONL/text debug traces when true. Scenario and AVA validation modes enable trace logging for the run. |
| `LLM_REASONING_EFFORT` | All assistants | Optional shared reasoning-effort fallback when the provider/model supports it. |
| `LLM_TEMPERATURE` | All assistants | Optional chat temperature override. |
| `MCP_TOOL_TIMEOUT_MS` | All assistants | Optional timeout override for MCP tool calls. |

## `cursor` Settings

`cursor` uses provider selection plus voice and speech settings:

| Setting | Purpose |
| --- | --- |
| `WHISPER_MODEL` | OpenAI Whisper transcription model. |
| `OPENAI_API_KEY` | Required for voice transcription, even when `LLM_PROVIDER=claude-api`. |
| `MAX_RECORD_MS` | Maximum single recording duration. |
| `ACTIVE_IDLE_TIMEOUT_MS` | Voice-mode idle timeout. |
| `WAKE_WORD` | Wake phrase for voice requests. |
| `VOICE_LANGUAGES` | Optional language preferences for transcription. |
| `TTS_MODEL`, `TTS_VOICE`, `TTS_INSTRUCTIONS` | Speech playback configuration. |
| `DEBUG_AUDIO_PLAYBACK` | Keeps extra playback diagnostics when true. |

Default interactive mode depends on `LLM_PROVIDER`: `openai-api` and `claude-api` start in voice mode, while `openai-codex` starts in text mode.

## `tars` Settings

`tars` uses shared provider, MCP, context, and trace settings for non-interactive scenario runs. It does not use microphone or speech playback settings.

Scenario files can reference environment variables with `${NAME}` placeholders. The included Netflix smoke scenario uses:

```dotenv
NETFLIX_PROFILE_PIN=
```

Scenario mode enables JSONL tracing automatically so assertions and failures can be reviewed from the saved run log.

## AVA Settings

AVA uses shared provider, MCP, context, and trace settings for accessibility validation runs. It also supports role-specific model and reasoning overrides in `src\assistants\ava\.env`:

```dotenv
DRIVER_MODEL=
DRIVER_REASONING_EFFORT=medium
EVALUATOR_MODEL=
EVALUATOR_REASONING_EFFORT=high
REPORTER_MODEL=
REPORTER_REASONING_EFFORT=medium
```

Leave role model values empty to use the selected provider's normal model setting. These role variables are intentionally prefixless because they are assistant-local.

AVA can also use Chrome DevTools Protocol evidence for web validation when a browser exposes a debugging endpoint:

```dotenv
AVA_CDP_ENDPOINT=http://127.0.0.1:9222
AVA_CDP_PORT=9222
```

Validation output is written under `artifacts\ava`. Report commands and assistant execution text redact sensitive environment values from keys such as `PIN`, `KEY`, `TOKEN`, `SECRET`, and `PASSWORD`.
