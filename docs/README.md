# HeronWin User Docs

These docs cover how to configure and run HeronWin.

- [Get Started](./GET_STARTED.md)
- [Scenario Mode](./get-started-script-mode.md)
- [tars Get Started](./tars/getstarted.md)
- [Voice And Text Mode](./get-started-voice-mode.md)
- [cursor Get Started](./cursor/getstarted.md)
- [Environment Configuration](./ENV_CONFIGURATION.md)
- [Agents And Skills](./agentsandskills/README.md)
- [Create App Skills](./agentsandskills/create-app-skills.md)
- [AVA Documentation](./ava/README.md)
- [AVA Get Started](./ava/getstarted.md)
- [AVA Browser And Web Validation Plan](./ava/browser-and-web-validation-plan.md)
- [AVA Rule Catalog](./ava/rules/README.md)
- [AVA Samples](./ava/sample/README.md)

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
- [agent prompts and skills](./agentsandskills/README.md)
