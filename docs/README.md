# HeronWin Docs

Start here:

- [Get Started](./GET_STARTED.md)
- [Scenario Mode](./get-started-script-mode.md)
- [Voice And Text Mode](./get-started-voice-mode.md)
- [OpenAI Configuration](./get-started-openaiconfig.md)
- [Goal And Design](./GOAL_AND_DESIGN.md)
- [History And Todos](./HISTORY_AND_TODOS.md)
- [Development Guardrails](./DEVELOPMENT_GUARDRAILS.md)

Historical docs under `docs/designs`, `docs/bugs`, `docs/perfbase`, and daily summaries may still reference the pre-refactor `src/head/brain` layout, old launcher flags, or the retired companion UI. Live setup docs and component READMEs should use `src/assistants`, `cursor`, and `tars`.

## Run And Verify

```powershell
dotnet build src\heronwin.sln
dotnet test src\heronwin.sln
dotnet run --project src\assistants\cursor
dotnet run --project src\assistants\tars -- --scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

Launcher:

```powershell
.\buildandrun.ps1 -CursorOnly
.\buildandrun.ps1 -TarsOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

## Component Docs

- [brain](../src/assistants/brain/README.md)
- [cursor](../src/assistants/cursor/README.md)
- [tars](../src/assistants/tars/README.md)
- [tools](../src/tools/README.md)
- [agent prompts and skills](../.github/agents/README.md)
