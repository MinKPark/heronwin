# herface

AI voice-agent UI for **heronwin**.

Connects to multiple LLM backends and local MCP servers. The primary input mode is **voice** via the Windows microphone; all output is displayed as text in the terminal.

## Features

- 🎙️ **Voice input** — records directly from the default Windows microphone and transcribes speech using OpenAI Whisper
- ⌨️ **Keyboard fallback** — type a message if voice is unavailable
- 🤖 **Multi-provider** — switch between OpenAI API, browser-driven ChatGPT, or Anthropic Claude
- 🔧 **MCP tool use** — connects to any number of local MCP servers and exposes their tools to the LLM
- 💬 **Multi-turn conversation** — full conversation history is maintained across turns

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| Windows 10/11 | This build records directly from the Windows microphone |
| Node.js ≥ 18 | |
| OpenAI API key | Required for OpenAI API mode and for Whisper transcription |
| Anthropic API key | Required only when using Claude as the LLM provider |
| Chrome / Edge desktop browser | Required for browser-driven ChatGPT mode |

### Native microphone backend

`herface` no longer depends on SoX on Windows. It records through a native Node addon instead, so there is no external recorder binary to install or add to `PATH`.

If Windows blocks microphone access, enable it in **Settings -> Privacy & security -> Microphone** for desktop apps before starting `herface`.

## Setup

```bash
npm install
cp .env.example .env
# Edit .env with your API keys and settings
npm run build
npm start
```

Or run directly (no build step):

```bash
npm run dev
```

For browser-backed ChatGPT auth bootstrap:

```bash
npm run chatgpt:login
```

## MCP Validation Helpers

When `eyesandhands` is built locally, you can validate it without going through the LLM:

```bash
npm run mcp:eyesandhands:direct
```

That script launches the bundled `eyesandhands.exe` directly and calls `list_windows`.

To validate the MCP configuration currently defined in `herface/.env`:

```bash
npm run mcp:eyesandhands:configured
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `LLM_PROVIDER` | `openai-api` | `openai-api`, `chatgpt-web`, or `claude-api` |
| `OPENAI_API_KEY` | | OpenAI API key |
| `OPENAI_MODEL` | `gpt-5.2-chat-latest` | OpenAI API model name |
| `ANTHROPIC_API_KEY` | | Anthropic API key |
| `ANTHROPIC_MODEL` | `claude-3-5-sonnet-20241022` | Claude model name |
| `CHATGPT_BASE_URL` | `https://chatgpt.com/` | Base URL for browser-driven ChatGPT |
| `CHATGPT_BROWSER_CHANNEL` | `msedge` | `msedge`, `chrome`, or `chromium` |
| `CHATGPT_PROFILE_DIR` | `.chatgpt-profile` | Persistent browser profile directory |
| `CHATGPT_HEADLESS` | `true` | Launch ChatGPT browser headless or visible |
| `CHATGPT_PROJECT_NAME` | `her` | Local project label used by browser mode |
| `CHATGPT_SESSION_RETENTION_DAYS` | `14` | How long local ChatGPT browser session records are kept |
| `CHATGPT_LOGIN_TIMEOUT_MS` | `900000` | Max wait for manual ChatGPT login during bootstrap |
| `CHATGPT_STARTUP_TIMEOUT_MS` | `120000` | Max wait for ChatGPT UI login/composer readiness |
| `CHATGPT_RESPONSE_TIMEOUT_MS` | `120000` | Max wait for ChatGPT browser responses |
| `WHISPER_MODEL` | `whisper-1` | OpenAI Whisper model for STT |
| `MAX_RECORD_MS` | `30000` | Max recording duration (ms) |
| `MCP_SERVERS` | `[]` | JSON array of MCP server configs |

### MCP_SERVERS format

```json
[
  {
    "name": "process-manager",
    "command": "node",
    "args": ["../herbody/process-manager/dist/index.js"]
  }
]
```

### Browser-driven ChatGPT mode

Set `LLM_PROVIDER=chatgpt-web` to run `herface` against a persistent Chromium session instead of a direct API endpoint.

- `herface` will launch a browser profile and reuse it across runs.
- Headless mode is the default, so no visible browser window is shown.
- Run `npm run chatgpt:login` once to sign in and save reusable auth state.
- If the saved ChatGPT session expires, `herface` will open the login bootstrap again and ask you to re-authenticate.
- Browser mode bridges tool use by asking ChatGPT to emit a JSON envelope that `herface` converts back into tool calls.
- Session metadata is kept locally for the configured retention window.

## Usage

Once running, the agent will prompt you each turn:

- **Press Enter** without typing to activate the microphone → speak → audio transcription happens automatically.
- **Type a message** and press Enter to send text directly.
- Type `exit` to quit.

The LLM will use available MCP tools automatically when needed, and the tool calls and results are shown inline in the terminal.
