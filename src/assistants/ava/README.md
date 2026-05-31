# AVA Assistant

AVA is the HeronWin accessibility validation assistant host. It is the assistant for scenario-backed accessibility validation of Windows apps and browser workflows.

AVA owns the validation run: it reads the UX scenario, drives UI through action
tools, collects UI evidence, runs deterministic accessibility validators, and
writes Markdown/JSON reports.

Use AVA when the goal is validation evidence and findings. Use `tars` for repeatable functional scenario automation, and use `cursor` for live text or voice control.

```powershell
dotnet run --project src/assistants/ava -- --help
```

Run with direct inputs:

```powershell
dotnet run --project src/assistants/ava -- --ux-scenario .\scenario.yml --validation-config .\validation.yml
```

Run with a bundle:

```powershell
dotnet run --project src/assistants/ava -- --run .\bundle.yml
```

Regenerate Markdown/JSON from a saved run without driving the UI again:

```powershell
dotnet run --project src/assistants/ava -- --regenerate-report latest
dotnet run --project src/assistants/ava -- --regenerate-report .\artifacts\ava\<run-id>
```

Capture compact-tree evaluation artifacts for a known window handle:

```powershell
dotnet run --project src/assistants/ava -- --evaluate-compact-tree --window-handle 0x00123456
```

Add `--vision-verdict` to ask the configured AVA evaluator LLM to compare the
real screenshot with the rendered compact tree:

```powershell
dotnet run --project src/assistants/ava -- --evaluate-compact-tree --window-handle 0x00123456 --vision-verdict
```

AVA uses role-specific LLM settings for its logical conversations. The driver
role drives validation scenarios. The evaluator role is also used when
`--vision-verdict` is passed to compact-tree evaluation. Reporter settings are
reserved for future LLM-based reporting passes:

```dotenv
DRIVER_MODEL=
DRIVER_REASONING_EFFORT=medium
EVALUATOR_MODEL=
EVALUATOR_REASONING_EFFORT=high
REPORTER_MODEL=
REPORTER_REASONING_EFFORT=medium
```

Leave role model values empty to use the selected provider's normal model
setting. Reasoning effort is best-effort and depends on provider/model support.
Report commands and assistant execution text redact sensitive environment values
from keys such as `PIN`, `KEY`, `TOKEN`, `SECRET`, and `PASSWORD`.

Local config normally lives in `src/assistants/ava/.env`. Start from `.env.example`; relative MCP paths in that file are resolved from the `ava` folder.
