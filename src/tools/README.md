# tools

MCP (Model Context Protocol) servers and shared Windows automation code for **heronwin**.

Each subdirectory is either a standalone MCP server or a shared package used by HeronWin assistants.

## Components

| Directory | Language | Description |
|-----------|----------|-------------|
| [`cognition/`](./cognition/) | C# | Stateless Windows UI inspection tools |
| [`execution/`](./execution/) | C# | Stateless Windows UI interaction tools |
| [`desktop-automation/`](./desktop-automation/) | C# | Shared Windows automation library used by cognition and execution |

Process listing, start, and stop tools live inside the shared assistant library rather than in a separate MCP server.

## Adding a New Server

1. Create a new subdirectory such as `src/tools/my-server/`.
2. Implement a stdio-based MCP server using the C# MCP SDK.
3. Register the server in an assistant `.env` file by adding it to `MCP_SERVERS`. Start from the relevant assistant `.env.example` file, or see [Environment Configuration](../../docs/ENV_CONFIGURATION.md).

## Prerequisites

- .NET 10+ SDK for C# services
