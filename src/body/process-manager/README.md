# process-manager

An MCP (Model Context Protocol) server that exposes tools for starting and stopping processes on the local machine.

## Tools

| Tool | Description |
|------|-------------|
| `list_processes` | List currently running processes |
| `start_process` | Start a new process by command |
| `stop_process` | Stop a running process by PID |

## Usage

Build and run the server:

```bash
npm install
npm run build
npm start
```

Or run in development mode with hot reload:

```bash
npm run dev
```

The server communicates via **stdio** using the MCP protocol. It is intended to be launched as a subprocess by an MCP client (e.g. `brain`).

## MCP Client Configuration

Add this server to your MCP client's configuration:

```json
{
  "name": "process-manager",
  "command": "node",
  "args": ["path/to/src/body/process-manager/dist/index.js"]
}
```

## Prerequisites

- Node.js >= 18
- For listing processes: `ps` (Linux/macOS) or `tasklist` (Windows) available in PATH
