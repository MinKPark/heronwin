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

Voice transcription uses OpenAI Whisper, so set `OPENAI_API_KEY` for `cursor` voice mode even when the active chat provider is `claude-api`.
