# Goal And Design

`heronwin` is a Windows-local AI assistant project. The goal is to keep desktop capabilities local, explicit, testable, and usable from both interactive and scenario-driven workflows.

## Current Runtime Pieces

- `src/assistants/brain`: shared library for provider clients, prompt loading, MCP integration, built-in process tools, configuration, debug tracing, trace reports, and desktop session primitives.
- `src/assistants/cursor`: interactive voice/text assistant.
- `src/assistants/tars`: scenario assistant for YAML scenario files and log-based assertions.
- `src/tools/cognition`: MCP server for Windows UI inspection.
- `src/tools/execution`: MCP server for Windows UI interaction.
- `src/tools/desktop-automation`: shared Windows automation library used by the cognition and execution MCP servers.

The retired `face` UI and named-pipe status bridge are no longer part of the active architecture.

## Prompt And Skill Layer

Runtime-loaded prompts live under `.github/agents`:

- `shared`: common desktop automation contract and app/site skills.
- `tars`: scenario execution profile and scenario-only skills.
- `cursor`: interactive profile and voice/text skills.
- `her.agent.*`: compatibility fallbacks.

## Verification

- `dotnet build src\heronwin.sln`
- `dotnet test src\heronwin.sln`
- `dotnet run --project src\assistants\tars -- --scenario <scenario.yml>`
- JSONL traces and Markdown trace reports from either assistant.
