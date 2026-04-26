# body

MCP (Model Context Protocol) servers and shared Windows automation code for **heronwin**.

Each subdirectory is either a standalone MCP server or a shared package used by the `brain` agent.

## Components

| Directory | Language | Description |
|-----------|----------|-------------|
| [`cognition/`](./cognition/) | C# | Stateless Windows UI inspection tools |
| [`execution/`](./execution/) | C# | Stateless Windows UI interaction tools |
| [`desktop-automation/`](./desktop-automation/) | C# | Shared Windows automation library used by cognition and execution |

Process listing, start, and stop tools live inside `brain` rather than in a separate MCP server.

## Adding a New Server

1. Create a new subdirectory such as `src/body/my-server/`.
2. Implement a stdio-based MCP server using the C# MCP SDK.
3. Register the server in the local `src/head/brain/.env` file by adding it to `MCP_SERVERS`. Start from `src/head/brain/.env.example` if you do not have one yet.

## Prerequisites

- .NET 10+ SDK for C# services
