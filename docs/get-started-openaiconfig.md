# Get Started - OpenAI Configuration

These `.env` settings are shared by `cursor` and `tars`.

| Route | `LLM_PROVIDER` | Model setting | Authentication |
| --- | --- | --- | --- |
| OpenAI Platform API | `openai-api` | `OPENAI_MODEL` | `OPENAI_API_KEY` |
| ChatGPT / Codex sign-in | `openai-codex` | `OPENAI_CODEX_MODEL` | `codex login` |

Example:

```dotenv
LLM_PROVIDER=openai-api
OPENAI_API_KEY=sk-...
OPENAI_MODEL=gpt-5.4-mini
OPENAI_CODEX_COMMAND=codex
OPENAI_CODEX_MODEL=
```

For the Codex route:

```powershell
codex login
```

Leave `OPENAI_CODEX_MODEL` empty to use the Codex CLI default model, or set it
explicitly. For Codex Spark:

```dotenv
LLM_PROVIDER=openai-codex
OPENAI_CODEX_COMMAND=codex
OPENAI_CODEX_MODEL=gpt-5.3-codex-spark
```

HeronWin treats Codex Spark as text-only and omits screenshot attachments for
that model so `codex exec` does not receive unsupported `--image` inputs.

Voice transcription uses OpenAI Whisper, so set `OPENAI_API_KEY` for `cursor` voice mode even when the active chat provider is `claude-api`.
