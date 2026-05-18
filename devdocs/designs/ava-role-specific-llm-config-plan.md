# AVA Role-Specific LLM Config Plan

Last updated: 2026-05-17
Status: implemented initial driver-role slice

## Summary

AVA should use separate logical conversations for different jobs in an
accessibility validation run:

- `driver`: persistent UI-driving conversation across UX scenario steps.
- `evaluator`: dedicated accessibility review conversation per checkpoint when
  AVA adds LLM-based review.
- `reporter`: optional report and triage wording conversation over structured
  findings.

Each role should be able to choose a model and reasoning effort independently,
without making AVA a Codex-only assistant. The configuration should be
provider-neutral first, then mapped to OpenAI API, Codex CLI, Claude API, or
other providers where supported.

Deterministic validators remain code-only and should not use a model or
reasoning setting.

## Current State

AVA currently uses the shared provider system from `brain`, so it can run with:

- `openai-api`
- `openai-codex`
- `claude-api`

Current model configuration is assistant-level, not role-level:

- `OPENAI_MODEL`
- `OPENAI_CODEX_MODEL`
- `ANTHROPIC_MODEL`

Current Codex CLI support passes `--model` when `OPENAI_CODEX_MODEL` is set.
Local `codex exec --help` exposes generic `--config <key=value>`, but does not
show a dedicated reasoning-effort flag. The implementation must verify the
stable Codex CLI config key before passing a reasoning-effort override.

Implementation note: local `~/.codex/config.toml` uses
`model_reasoning_effort`, so the initial Codex CLI mapping passes
`--config model_reasoning_effort="<effort>"`.

## Goals

- Keep AVA provider-neutral.
- Let AVA choose different model settings for driver, evaluator, and reporter.
- Preserve current behavior when no role-specific variables are set.
- Avoid adding LLM use to deterministic validators.
- Make unsupported provider/role reasoning settings visible in trace logs
  without breaking runs by default.
- Document the feature in setup docs and AVA docs.

## Non-Goals

- Do not make AVA require Codex.
- Do not implement an LLM accessibility evaluator in this slice.
- Do not change TARS or Cursor runtime behavior beyond shared config parsing
  compatibility.
- Do not claim every provider supports every reasoning-effort value.

## Proposed Runtime Shape

Add a provider-neutral role config model:

```csharp
internal enum LlmRole
{
    Default,
    AvaDriver,
    AvaEvaluator,
    AvaReporter
}

internal sealed record LlmRoleConfig(
    LlmRole Role,
    string? ModelOverride,
    string? ReasoningEffort);
```

Add a role-aware client factory:

```csharp
internal static class LlmRoleClientFactory
{
    public static ILlmClient CreateClient(
        AppConfig config,
        HttpClient httpClient,
        LlmRole role);
}
```

AVA should request clients by role:

- `AvaBrainCommandDriver` gets `LlmRole.AvaDriver`.
- Future LLM accessibility evaluator gets `LlmRole.AvaEvaluator`.
- Future LLM report/triage pass gets `LlmRole.AvaReporter`.

The driver conversation remains persistent across scenario commands. Evaluator
conversations should be short-lived per checkpoint and receive only the frozen
evidence bundle plus structured context. Reporter conversations should receive
only structured findings and report metadata.

## Configuration

Use assistant-local, provider-neutral role variables first. Because AVA normally
loads `src/assistants/ava/.env`, the variable names do not need an `AVA_`
prefix:

```env
DRIVER_MODEL=
DRIVER_REASONING_EFFORT=medium
EVALUATOR_MODEL=
EVALUATOR_REASONING_EFFORT=high
REPORTER_MODEL=
REPORTER_REASONING_EFFORT=medium
```

Also support shared fallback reasoning:

```env
LLM_REASONING_EFFORT=
```

Existing provider-specific model variables remain valid fallbacks:

```env
OPENAI_MODEL=
OPENAI_CODEX_MODEL=
ANTHROPIC_MODEL=
```

Fallback order for AVA roles:

1. `<ROLE>_MODEL`
2. provider-specific model variable, such as `OPENAI_CODEX_MODEL`
3. provider default

Fallback order for reasoning effort:

1. `<ROLE>_REASONING_EFFORT`
2. `LLM_REASONING_EFFORT`
3. provider/model default

Recommended AVA defaults:

- driver: `medium`
- evaluator: `high`
- reporter: `medium`

Example AVA `.env` role config with default values:

```env
# AVA role-specific model/reasoning overrides.
# Leave role model values empty to use the selected provider's assistant-level
# model setting, such as OPENAI_MODEL, OPENAI_CODEX_MODEL, or ANTHROPIC_MODEL.
DRIVER_MODEL=
DRIVER_REASONING_EFFORT=medium

EVALUATOR_MODEL=
EVALUATOR_REASONING_EFFORT=high

REPORTER_MODEL=
REPORTER_REASONING_EFFORT=medium

# Optional shared fallback used when a role-specific reasoning value is empty.
LLM_REASONING_EFFORT=
```

Allowed normalized values should start with:

- `low`
- `medium`
- `high`
- `xhigh`

Provider mappings may support only a subset. Unsupported values should produce
a clear validation error if the provider is known not to support the selected
value. Unknown support should produce a trace warning and fall back unless the
user enables a strict mode later.

## Provider Mapping

### OpenAI API

Add `ReasoningEffort` to the OpenAI API client configuration, but first verify
which API route and selected models support the setting.

The current implementation uses Chat Completions. If the selected OpenAI API
route or model does not support reasoning effort, omit the setting and write a
trace event.

### OpenAI Codex CLI

Add optional reasoning effort to `OpenAiCodexCliClient` and
`OpenAiCodexCliSupport.BuildExecArguments`.

Implementation must verify the stable CLI config key. If Codex supports it
through `--config`, arguments should look conceptually like:

```text
codex exec --config <verified-reasoning-key>="<effort>"
```

Do not hard-code a speculative key without a focused local test or documented
source.

### Claude API

Keep the provider-neutral role config, but do not invent a mapping in this
slice. If Claude-specific thinking controls are added later, map them from the
same role config in a provider-specific implementation.

## Implementation Phases

### Phase 0 - Provider Capability Check

- Verify OpenAI API support for reasoning effort in the route currently used by
  HeronWin.
- Verify Codex CLI reasoning-effort support and exact `--config` key.
- Decide whether unsupported reasoning values should warn or fail per provider.
- Record the decision in this plan before implementation.

### Phase 1 - Shared Role Config Model

- Add `LlmRole` and `LlmRoleConfig`.
- Extend `AppConfig` to parse provider-neutral role variables.
- Add normalization for reasoning-effort values.
- Preserve current config behavior when role variables are empty.
- Add focused tests for parsing, fallback order, and invalid values.
- Do not add `AVA_` aliases unless shared root `.env` collision becomes a
  proven problem.

### Phase 2 - Role-Aware Client Creation

- Add role-aware client creation without changing existing callers.
- Let providers receive the effective model and optional reasoning effort.
- Add tests proving role model overrides are used for AVA roles.
- Keep TARS and Cursor using the default role.

### Phase 3 - AVA Driver Wiring

- Wire `AvaBrainCommandDriver` to use the `driver` role client.
- Add trace fields for role, effective model, and requested reasoning effort.
- Add tests proving AVA driver uses `DRIVER_MODEL` and
  `DRIVER_REASONING_EFFORT` when configured.

### Phase 4 - Provider-Specific Reasoning Mapping

- Implement the verified OpenAI API mapping where supported.
- Implement the verified Codex CLI mapping where supported.
- For unsupported provider/model combinations, omit the setting and emit a
  trace warning.
- Add tests for CLI argument generation and API payload generation.

### Phase 5 - Future Evaluator And Reporter Hooks

- Add factory methods or small services for future evaluator/reporter clients.
- Do not call them yet from deterministic validators.
- Add tests that evaluator/reporter role config can be resolved even before the
  LLM evaluator exists.

## Test Plan

Unit tests:

- `AppConfig` parses `LLM_REASONING_EFFORT`.
- `AppConfig` parses `DRIVER_*`, `EVALUATOR_*`, and `REPORTER_*`.
- Role-specific model overrides provider-specific defaults.
- Role-specific reasoning overrides shared reasoning.
- Empty role values preserve current behavior.
- Invalid reasoning-effort values fail with a clear message.
- `OpenAiCodexCliSupport.BuildExecArguments` includes the verified reasoning
  config only when supported and configured.
- OpenAI API payload includes reasoning effort only when supported and
  configured.
- AVA driver receives the driver-role client.

Integration or smoke tests:

- `dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj`
- `dotnet test src\assistants\ava.tests\HeronWin.Ava.Tests.csproj`
- `dotnet test src\heronwin.sln`
- Manual Codex smoke with `LLM_PROVIDER=openai-codex` and
  `DRIVER_REASONING_EFFORT=medium`, subject to local CLI/account support.

## Documentation Updates

Update:

- `src/assistants/ava/.env.example`
- `src/assistants/ava/README.md`
- `docs/ENV_CONFIGURATION.md`
- `docs/README.md` if a new verification command is added
- `README.md` if quick-start examples mention AVA role config
- `devdocs/designs/ava-accessibility-validation-assistant-plan.md`

Optional, if shared fallback variables are exposed for all assistants:

- `src/assistants/cursor/.env.example`
- `src/assistants/tars/.env.example`
- `src/assistants/cursor/README.md`
- `src/assistants/tars/README.md`

Docs should state:

- AVA supports multiple providers, not only Codex.
- AVA uses separate logical conversations for driver/evaluator/reporter roles.
- Role variables are intentionally prefixless because assistant-local `.env`
  files already provide the namespace.
- Reasoning effort is best-effort and provider/model dependent.
- Deterministic validators do not use LLM reasoning.

## Implementation Status - 2026-05-17

Implemented the initial driver-role slice:

- added provider-neutral `LlmRole`, `LlmRoleConfig`, and reasoning-effort
  normalization.
- parsed prefixless assistant-local role variables:
  `DRIVER_*`, `EVALUATOR_*`, and `REPORTER_*`.
- added shared `LLM_REASONING_EFFORT` fallback.
- added role-aware provider client creation while preserving existing default
  call sites.
- wired AVA to create the active command driver with `LlmRole.AvaDriver`.
- mapped Codex CLI reasoning through
  `--config model_reasoning_effort="<effort>"`.
- mapped OpenAI API reasoning into Chat Completions payloads for reasoning
  model names.
- left Claude reasoning effort as trace-visible but unsupported for this slice.
- updated AVA/shared `.env.example` files and OpenAI/AVA docs.

Evaluator and reporter conversations are still future hooks; deterministic
validators remain code-only.

## Open Questions

- Should unsupported reasoning effort warn and continue, or fail fast?
- Should `xhigh` be accepted globally, or only for providers known to support
  it?
- Should role-specific model variables be AVA-only first, or should the shared
  config support role overrides for TARS/Cursor future use too?
- Do we ever need `AVA_` aliases for users who keep all assistant config in one
  shared root `.env`, or is assistant-local `.env` enough?
- Should evaluator/reporter model defaults be present in `.env.example` before
  those conversations are implemented?

## Recommendation

Build the feature in two layers:

1. Add provider-neutral role config and AVA driver wiring first.
2. Add provider-specific reasoning-effort mappings only after verifying each
   provider's supported mechanism.

This gives AVA the right architecture now without making Codex-specific
assumptions or blocking on future evaluator/reporter implementation.
