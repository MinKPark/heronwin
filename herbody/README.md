# herbody

MCP (Model Context Protocol) servers for **heronwin**.

Each subdirectory is a standalone MCP server that can be consumed by the `herface` AI agent or any other MCP-compatible client.

## Servers

| Directory | Language | Description |
|-----------|----------|-------------|
| [`process-manager/`](./process-manager/) | TypeScript | Start, stop, and list processes on the local machine |
| [`eyesandhands/`](./eyesandhands/) | C# | Inspect visible Windows UI and interact with top-level windows and main menus |

## Adding a New Server

1. Create a new subdirectory (e.g. `herbody/my-server/`).
2. Implement a stdio-based MCP server using the [`@modelcontextprotocol/sdk`](https://github.com/modelcontextprotocol/typescript-sdk) (TypeScript) or the [C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
3. Register the server in `herface/.env` by adding an entry to `MCP_SERVERS`.

## Prerequisites

- Node.js ≥ 18 (for TypeScript servers)
- .NET 8+ SDK (for C# servers)
