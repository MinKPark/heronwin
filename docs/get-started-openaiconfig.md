# Get Started - OpenAI Configuration

These `.env` settings are shared by `cursor`, `tars`, and AVA. They can live in an assistant-local `.env` file such as `src\assistants\cursor\.env`, `src\assistants\tars\.env`, or `src\assistants\ava\.env`.

Provider routes:

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

Reasoning effort is optional and provider/model dependent. Use the shared
fallback when you want one setting for the active assistant:

```dotenv
LLM_REASONING_EFFORT=medium
```

AVA also supports assistant-local role overrides:

```dotenv
DRIVER_MODEL=
DRIVER_REASONING_EFFORT=medium
EVALUATOR_MODEL=
EVALUATOR_REASONING_EFFORT=high
REPORTER_MODEL=
REPORTER_REASONING_EFFORT=medium
```

These role variables are intentionally prefixless because they live in
`src/assistants/ava/.env`.

Voice transcription uses OpenAI Whisper, so set `OPENAI_API_KEY` for `cursor` voice mode even when the active chat provider is `claude-api`.
