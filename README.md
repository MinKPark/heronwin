# heronwin

*Her on Windows platform* — an AI agent system with a voice-driven UI and local MCP tool servers.

## Repository Layout

```
heronwin/
├── herface/          # Node.js/TypeScript AI agent UI (voice input → LLM → text output)
└── herbody/          # MCP servers (TypeScript / C#)
    └── process-manager/   # Start, stop, and list processes on the local machine
```

## Quick Start

### 1. Start the process-manager MCP server (build once)

```bash
cd herbody/process-manager
npm install
npm run build
```

### 2. Configure and start the herface agent

```bash
cd herface
npm install
cp .env.example .env
# Edit .env — set OPENAI_API_KEY and update MCP_SERVERS to point at the built server
npm run build
npm start
```

### Running without a build step

Both packages support `npm run dev` (via [tsx](https://tsx.is)) for hot-reload development.

## Documentation

- [herface README](./herface/README.md)
- [herbody README](./herbody/README.md)
- [process-manager README](./herbody/process-manager/README.md)
