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

Runtime-loaded prompts live under `src/agents`:

- `shared`: common desktop automation contract and app/site skills.
- `tars`: scenario execution profile and scenario-only skills.
- `cursor`: interactive profile and voice/text skills.
- `her.agent.*`: compatibility fallbacks.

## Remaining Work Structure

Track open work in two buckets so generic platform improvements stay distinct
from behavior that belongs to a particular assistant host.

### General Improvements

General improvements strengthen the shared runtime, tool servers, docs, and
test surface without being specific to `cursor` or `tars`.

- Finish the `cognition` compact-tree rollout by adding the opt-in
  screenshot-vs-compact evaluation harness, then running parity checks,
  benchmarks, and manual evaluation passes.
- Broaden the prompt and skill intent vocabulary, with focused activation
  tests for the new generic intents.
- Keep repository housekeeping current, including keeping developer indexes
  aligned with new docs.

### Assistant-Specific Features

Assistant-specific features should live as close as practical to the host that
owns the behavior: scenario execution in `tars`, interactive voice/text behavior
in `cursor`, and only shared primitives in `brain`.

- Cut the scripted Netflix smoke runtime below one minute without weakening the
  scenario contract.
- Make scripted scenario pass/fail stricter so incomplete final outcomes cannot
  pass just because required title text appears.
- Decide whether to add separate scripted coverage for the app-first launch
  path now that the current Netflix smoke is website-navigation-based.
- Continue splitting assistant-specific policy out of `brain`: move
  scenario-only runner and context behavior toward `tars`, and move
  interactive voice/text policy toward `cursor`.

## Verification

- `dotnet build src\heronwin.sln`
- `dotnet test src\heronwin.sln`
- `dotnet run --project src\assistants\tars -- --scenario <scenario.yml>`
- JSONL traces and Markdown trace reports from either assistant.
