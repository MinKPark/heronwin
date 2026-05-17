# HeronWin User Docs

These docs cover how to configure and run HeronWin.

- [Get Started](./GET_STARTED.md)
- [Scenario Mode](./get-started-script-mode.md)
- [Voice And Text Mode](./get-started-voice-mode.md)
- [OpenAI Configuration](./get-started-openaiconfig.md)
- [Create App Skills](./APP_SKILLS.md)
- [AVA Rule Catalog](./ava/rules/README.md)

Developer-facing design notes, bug writeups, performance baselines, and project history live in [devdocs](../devdocs/README.md).

## Run And Verify

```powershell
dotnet build src\heronwin.sln
dotnet test src\heronwin.sln
dotnet run --project src\assistants\cursor
dotnet run --project src\assistants\tars -- --scenario src\scenarios\netflix-boyfriend-on-demand.yml
dotnet run --project src\assistants\ava -- --help
dotnet run --project src\assistants\ava -- --run src\scenarios\accessibility\active-window-smoke.bundle.yml
```

Launcher:

```powershell
.\buildandrun.ps1 -CursorOnly
.\buildandrun.ps1 -TarsOnly -Scenario src\scenarios\netflix-boyfriend-on-demand.yml
```

## Component Docs

- [brain](../src/assistants/brain/README.md)
- [ava](../src/assistants/ava/README.md)
- [cursor](../src/assistants/cursor/README.md)
- [tars](../src/assistants/tars/README.md)
- [tools](../src/tools/README.md)
- [agent prompts and skills](../.github/agents/README.md)
