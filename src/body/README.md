# body

MCP (Model Context Protocol) servers and shared Windows automation code for **heronwin**.

Each subdirectory is either a standalone MCP server or a shared package used by the `brain` agent.

## Components

| Directory | Language | Description |
|-----------|----------|-------------|
| [`cognition/`](./cognition/) | C# | Stateless Windows UI inspection tools |
| [`execution/`](./execution/) | C# | Stateless Windows UI interaction tools |
| [`desktop-automation/`](./desktop-automation/) | C# | Shared Windows automation library used by cognition and execution |
| [`process-manager/`](./process-manager/) | TypeScript | Start, stop, and list local processes |

## Adding a New Server

1. Create a new subdirectory such as `src/body/my-server/`.
2. Implement a stdio-based MCP server using the TypeScript or C# MCP SDK.
3. Register the server in `src/herhead/brain/.env` by adding it to `MCP_SERVERS`.

## Prerequisites

- Node.js 18+ for TypeScript services
- .NET 10+ SDK for C# services
