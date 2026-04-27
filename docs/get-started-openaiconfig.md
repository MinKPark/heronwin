# Get Started - OpenAI Configuration

This page covers the OpenAI-related `.env` settings used by `brain`. Start
from [Get Started](./GET_STARTED.md) if you have not copied the template yet.

## Provider routes

`brain` has two OpenAI routes with separate model settings:

| Route | `LLM_PROVIDER` | Model setting | Authentication |
| ----- | -------------- | ------------- | -------------- |
| OpenAI Platform API | `openai-api` | `OPENAI_MODEL` | `OPENAI_API_KEY` |
| ChatGPT / Codex sign-in | `openai-codex` | `OPENAI_CODEX_MODEL` | `codex login` |

You can keep both model settings in `src\head\brain\.env` and switch routes by
changing only `LLM_PROVIDER`.

## Use the OpenAI API route

Set `LLM_PROVIDER=openai-api`, add your API key, and choose the API model:

```dotenv
LLM_PROVIDER=openai-api
OPENAI_API_KEY=sk-...
OPENAI_MODEL=gpt-5.4-mini
```

When this route is active, `brain` calls the OpenAI API directly using
`OPENAI_MODEL`.

## Use the Codex sign-in route

Sign in once with the Codex CLI:

```powershell
codex login
```

Then set the provider and Codex model:

```dotenv
LLM_PROVIDER=openai-codex
OPENAI_CODEX_COMMAND=codex
OPENAI_CODEX_MODEL=gpt-5.5
```

When this route is active, `brain` shells out to `OPENAI_CODEX_COMMAND` and
passes `OPENAI_CODEX_MODEL` to the CLI. Leave `OPENAI_CODEX_MODEL` blank to use
the Codex CLI default model.

## Keep different models ready

This is useful when comparing API and Codex behavior:

```dotenv
# Active route.
LLM_PROVIDER=openai-api

# OpenAI Platform API route.
OPENAI_API_KEY=sk-...
OPENAI_MODEL=gpt-5.4-mini

# ChatGPT / Codex sign-in route.
OPENAI_CODEX_COMMAND=codex
OPENAI_CODEX_MODEL=gpt-5.5
```

Switch `LLM_PROVIDER` between `openai-api` and `openai-codex`; the model for
the inactive route can stay in the file.

## Voice note

Voice transcription uses OpenAI Whisper. Set `OPENAI_API_KEY` when using voice
mode, even if the active LLM provider is `claude-api`. The transcription model
is controlled separately with `WHISPER_MODEL`.
