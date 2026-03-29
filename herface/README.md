# herface

AI voice-agent UI for **heronwin**.

Connects to Cloud LLMs (OpenAI GPT and Anthropic Claude) and local MCP servers. The primary input mode is **voice** via the microphone; all output is displayed as text in the terminal.

## Features

- 🎙️ **Voice input** — records from the default microphone and transcribes speech using OpenAI Whisper
- ⌨️ **Keyboard fallback** — type a message if voice is unavailable
- 🤖 **Multi-LLM** — switch between OpenAI GPT or Anthropic Claude via an environment variable
- 🔧 **MCP tool use** — connects to any number of local MCP servers and exposes their tools to the LLM
- 💬 **Multi-turn conversation** — full conversation history is maintained across turns

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| Node.js ≥ 18 | |
| `arecord` (Linux) or SoX `rec` (macOS/Windows) | Required for microphone recording |
| OpenAI API key | Required for GPT and/or Whisper transcription |
| Anthropic API key | Required only when using Claude as the LLM provider |

### Installing SoX on Windows

```powershell
winget install SoX.SoX
```

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

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `LLM_PROVIDER` | `openai` | `openai` or `claude` |
| `OPENAI_API_KEY` | | OpenAI API key |
| `OPENAI_MODEL` | `gpt-4o` | OpenAI model name |
| `ANTHROPIC_API_KEY` | | Anthropic API key |
| `ANTHROPIC_MODEL` | `claude-3-5-sonnet-20241022` | Claude model name |
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

## Usage

Once running, the agent will prompt you each turn:

- **Press Enter** without typing to activate the microphone → speak → audio transcription happens automatically.
- **Type a message** and press Enter to send text directly.
- Type `exit` to quit.

The LLM will use available MCP tools automatically when needed, and the tool calls and results are shown inline in the terminal.
