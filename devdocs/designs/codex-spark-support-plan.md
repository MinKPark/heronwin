# Codex Spark Support Plan

Last updated: 2026-05-16
Status: implemented

## Summary

Support Codex Spark as a first-class model choice for the existing
`openai-codex` route, using the existing Codex CLI bridge first.

The likely model id is `gpt-5.3-codex-spark`. The current HeronWin Codex path
already shells out to `codex exec --model <OPENAI_CODEX_MODEL>`, so the work is
not a new provider from scratch. The important support gap is that Spark is
documented as text-only, while HeronWin currently treats the Codex route as
vision-capable and can forward screenshot attachments to `codex exec --image`.

## Sources Checked

- OpenAI Codex models: https://developers.openai.com/codex/models
- OpenAI Codex CLI reference: https://developers.openai.com/codex/cli/reference
- OpenAI Codex SDK: https://developers.openai.com/codex/sdk
- Local Codex CLI:
  - `codex-cli 0.131.0-alpha.9`
  - `codex exec --help` supports `-m, --model <MODEL>` and `--image <FILE>`
  - `codex debug models --bundled` did not list Spark on this machine at the
    time of investigation

## Decision

Use the existing Codex CLI path for the first Spark implementation.

The Codex SDK is available, but it would add a Node or experimental Python
bridge to a .NET runtime that already has a working `codex exec` integration.
Defer SDK evaluation until after Spark works through the current CLI bridge.

## Implementation Status - 2026-05-16

Implemented the CLI-first Spark support slice:

- added Codex model metadata and Spark alias normalization
- normalized `OPENAI_CODEX_MODEL=spark` and `OPENAI_CODEX_MODEL=codex-spark`
  to `gpt-5.3-codex-spark`
- added a compact Spark `LlmModelProfile`
- routed Spark through `codex exec --model gpt-5.3-codex-spark`
- omitted `--image` arguments for Spark and added text-only omission context
  to bridge prompts
- preserved image attachment behavior for non-Spark Codex models
- updated `.env.example` files and OpenAI configuration docs
- added focused tests for normalization, CLI arguments, bridge attachment
  behavior, profile behavior, and trace report display

Verified:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~LlmSupportTests|FullyQualifiedName~TraceReportTests|FullyQualifiedName~ProviderModeTests"
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj
dotnet test src\heronwin.sln
```

Manual live Spark smoke is still subject to local Codex CLI/account availability.

## Manual Scenario Measurement - 2026-05-15 PDT

Scenario: `src/scenarios/netflix-boyfriend-on-demand.yml`

### Rerun After MCP Path Fix

Run setup:

- `OPENAI_CODEX_MODEL=gpt-5.3-codex-spark`
- `MCP_SERVERS` was overridden with absolute `src/tools/...` executable paths.

Results:

- Process exit code: `1`.
- Wall-clock command time: `196.292 s`.
- Trace scenario elapsed: `194.688 s`.
- Turns reached: `5`.
- Total LLM responses: `16`.
- Average LLM attempt: `6.357 s`.
- Tool calls: `19`.
- Requested tool time: `40.541 s`.
- Spark bridge telemetry showed `images=0` for every Codex CLI call and omitted
  `7` screenshot attachments as text-only context.

Trace-report highlights:

```text
Provider / model: OpenAiCodex / gpt-5.3-codex-spark
Scenario elapsed: 194.688 s
Turns: 5
Total LLM responses: 16
Average LLM attempt: 6.357 s
```

Outcome: the scenario failed on Turn 5. Spark opened the `Boyfriend on Demand`
title surface and attempted playback, but the freshest evidence still did not
confirm a player/playback state. A final Codex CLI call then exited `1` while
reporting a ChatGPT plugin-sync `403` warning in stderr. This rerun avoided the
stale MCP path issue and produced a more useful measurement: the remaining
problem is final playback completion/confirmation, not Spark model selection.

### Earlier First-Pass Measurement

Run setup:

- `OPENAI_CODEX_MODEL=gpt-5.3-codex-spark`
- `MCP_SERVERS` was overridden with absolute `src/tools/...` executable paths
  because the local untracked Tars `.env` still referenced retired
  `../../body/...` paths.

Results:

- First attempt failed before the first turn in `8.772 s` because MCP startup
  used the stale local `../../body/...` paths.
- Rerun completed with process exit code `0`.
- Harness result: passed.
- Wall-clock command time: `179.669 s`.
- Trace scenario elapsed: `177.948 s`.
- Turns: `5`.
- LLM responses: `14`.
- Average LLM attempt: `6.526 s`.
- Tool calls: `18`.
- Requested tool time: `35.471 s`.
- Spark bridge telemetry showed `images=0` for every Codex CLI call and omitted
  `4` screenshot attachments as text-only context.

Trace-report highlights:

```text
Provider / model: OpenAiCodex / gpt-5.3-codex-spark
Scenario elapsed: 177.948 s
Turns: 5
Total LLM responses: 14
Average LLM attempt: 6.526 s
```

Caveat: this is a harness pass, not a clean end-to-end playback confirmation.
The final turn opened the title and then landed on ambiguous/wrong playback
evidence; the final assistant message explicitly said the Boyfriend on Demand
request was not completed. This reinforces the existing P0 task to make
scripted scenario pass/fail stricter for incomplete final outcomes.

## Current Local State

The Codex route is implemented in `brain`:

- `src/assistants/brain/OpenAiCodexCliClient.cs`
  - builds a bridge prompt
  - writes image attachments to temporary files
  - calls `codex exec`
  - passes `--model` when `OPENAI_CODEX_MODEL` is set
  - passes `--image` for visual context attachments unless the selected model
    is text-only Spark
- `src/assistants/brain/AppConfig.cs`
  - reads `OPENAI_CODEX_COMMAND`, defaulting to `codex`
  - reads `OPENAI_CODEX_MODEL`, defaulting to empty / Codex default
  - normalizes `spark` and `codex-spark` aliases to `gpt-5.3-codex-spark`
- `src/assistants/brain/LlmProviders.cs`
  - exposes `openai-codex` as text-only interactive mode
  - currently marks Codex as supporting vision inputs
- `src/assistants/brain/LlmModelProfile.cs`
  - has a generic `OpenAiCodex` profile and a compact Spark profile
- `src/assistants/cursor/.env.example` and `src/assistants/tars/.env.example`
  - expose `OPENAI_CODEX_MODEL=`
- trace reports already resolve `openAiCodexModel` for Codex runs.

## Goal

Make this configuration work predictably:

```dotenv
LLM_PROVIDER=openai-codex
OPENAI_CODEX_COMMAND=codex
OPENAI_CODEX_MODEL=gpt-5.3-codex-spark
```

Expected behavior:

- HeronWin launches Codex through the existing ChatGPT / Codex sign-in route.
- The trace and console surfaces show Spark clearly.
- Spark uses a model profile tuned for fast, text-only iteration.
- Screenshot/image evidence does not break Spark runs.
- Tests cover the model normalization, CLI argument construction, and trace
  display behavior.

## Non-Goals

- Do not add a new API provider for Spark.
- Do not require OpenAI API credentials for Spark.
- Do not add a Codex SDK sidecar in the first pass.
- Do not build a dynamic model-catalog dependency in the first pass.
- Do not change the default `openai-codex` model for users who leave
  `OPENAI_CODEX_MODEL` empty.
- Do not remove image support for other Codex models unless proven necessary.

## Options

### Option A: Config-only support

Document `OPENAI_CODEX_MODEL=gpt-5.3-codex-spark` and leave runtime behavior as
is.

Pros:

- Very small change.
- Works if Spark accepts the existing `codex exec --model` path and no images
  are attached.

Cons:

- Fragile for any turn that includes screenshot evidence.
- Does not make the text-only behavior explicit.
- Easy to misread trace/config output as fully supported when it is only
  manually configured.

### Option B: First-class Spark model profile

Keep `openai-codex` as the provider, but add Spark-specific model handling:

- normalize `spark`, `codex-spark`, and `gpt-5.3-codex-spark`
- identify Spark as text-only
- suppress or degrade image attachments for Spark
- add model-profile tuning
- update docs and focused tests

Pros:

- Small implementation with meaningful guardrails.
- Matches the current provider architecture.
- Makes Spark safe for normal HeronWin turns that may produce screenshots.

Cons:

- Requires a small Codex model metadata helper.
- Needs careful wording around omitted visual evidence.

### Option C: Separate `openai-codex-spark` provider

Add a new provider id with text-only capability metadata and separate docs.

Pros:

- Very explicit capability boundary.
- Easy for future UI/config surfaces to present as a distinct choice.

Cons:

- Heavier than the current need.
- Duplicates most of the existing Codex provider path.
- Spark is a model selection under Codex, not a separate auth route.

### Option D: Dynamic Codex model catalog

Query `codex debug models` or another Codex model catalog before launch.

Pros:

- Could eventually adapt to model availability changes.
- Might prevent unsupported local/account combinations earlier.

Cons:

- More moving parts.
- The local bundled model list did not include Spark during investigation.
- Account availability and research-preview rollout status may be dynamic.

### Option E: Codex SDK bridge

Use the official Codex SDK instead of calling `codex exec` directly.

Pros:

- The TypeScript SDK is official and more flexible than non-interactive CLI
  mode.
- It supports thread-style control directly, which could eventually fit
  HeronWin's multi-turn assistant shape.

Cons:

- The TypeScript SDK would add a Node sidecar or rewrite layer around the .NET
  runtime.
- The Python SDK is documented as experimental and requires a local Codex repo
  checkout.
- It is larger than needed for first-pass Spark support because `codex exec`
  already accepts `--model`.

## Recommendation

Use Option B for the first implementation pass, implemented through the
existing Codex CLI bridge.

The code should stay centered on the existing `openai-codex` provider, with
Spark represented as a known Codex model profile. This keeps the change small
while still handling the one real behavioral mismatch: Spark is text-only but
HeronWin can attach screenshots.

## Implementation Plan

### Phase 1: Codex model metadata

Add a small internal helper near the Codex client/provider code.

Candidate shape:

```csharp
internal sealed record OpenAiCodexModelInfo(
    string RequestedModel,
    string EffectiveModel,
    bool IsSpark,
    bool SupportsImageInputs);
```

Responsibilities:

- normalize empty model to `codex-default`
- normalize Spark aliases to `gpt-5.3-codex-spark`
- expose `SupportsImageInputs == false` for Spark
- leave unknown Codex models as pass-through and image-capable unless we decide
  to make unknowns conservative

### Phase 2: Spark-safe Codex bridge

Update `OpenAiCodexCliClient` so model metadata drives CLI arguments and
attachment handling.

For Spark:

- pass `--model gpt-5.3-codex-spark`
- do not pass `--image`
- convert visual context into text-only context that says images were omitted
  because the selected Codex model is text-only
- trace how many images were omitted so failures are diagnosable

For non-Spark Codex models:

- preserve the current image attachment behavior
- preserve current bridge schema behavior

### Phase 3: Model profile

Update `LlmModelProfiles.CreateOpenAiCodexProfile`.

Suggested Spark profile:

- `ContextCompressionTriggerRatio`: lower than generic Codex, around `0.52`
- `WindowSnapshotCharBudget`: lower than generic Codex, around `4_800`
- `FocusSnapshotCharBudget`: lower than generic Codex, around `2_400`
- `MaxThrottleRetries`: keep `0` because Codex CLI retries are not handled like
  API throttling

The exact values are tweakable after a real smoke run.

### Phase 4: Config and docs

Update:

- `src/assistants/cursor/.env.example`
- `src/assistants/tars/.env.example`
- `docs/ENV_CONFIGURATION.md`
- `devdocs/HISTORY_AND_TODOS.md`

Docs should show both forms:

```dotenv
OPENAI_CODEX_MODEL=
OPENAI_CODEX_MODEL=gpt-5.3-codex-spark
```

### Phase 5: Tests

Add focused tests for:

- Spark alias normalization
- Spark profile is more compact than generic Codex
- `OpenAiCodexCliClient` builds `--model gpt-5.3-codex-spark`
- Spark does not build `--image` arguments
- visual context degradation text appears for Spark
- non-Spark Codex image behavior remains unchanged
- trace report renders `OpenAiCodex / gpt-5.3-codex-spark`

The CLI argument tests may need a small seam around `OpenAiCodexCliSupport`.
Prefer a narrow internal helper that builds `ProcessStartInfo.ArgumentList`
inputs over a broad process abstraction.

### Phase 6: Verification

Run focused tests first:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~LlmSupportTests|FullyQualifiedName~ProviderModeTests|FullyQualifiedName~TraceReportTests"
```

Then run the full brain test project:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj
```

If shared runtime behavior changes beyond the Codex bridge, run:

```powershell
dotnet test src\heronwin.sln
```

Manual smoke, subject to local account/model availability:

```powershell
$env:LLM_PROVIDER="openai-codex"
$env:OPENAI_CODEX_MODEL="gpt-5.3-codex-spark"
dotnet run --project src\assistants\tars -- --scenario <small-text-only-scenario.yml>
```

## Risks

| Risk | Mitigation |
| --- | --- |
| Local Codex CLI does not expose Spark yet | Keep model id configurable and report CLI failure clearly; implementation can still be ready for account/CLI availability |
| Spark rejects image inputs | Treat Spark as text-only and omit `--image` |
| Omitted screenshot evidence weakens desktop behavior | Include compact text context and clear omission notes; use Spark first for text-heavy or low-visual scenarios |
| Unknown Codex model capability differs | Only special-case Spark in the first pass; leave generic Codex behavior unchanged |
| Model-profile budgets are too tight | Start conservative and tune after a smoke run |

## Open Questions

- Should unknown Codex models continue to be treated as image-capable, or should
  only known image-capable Codex models get `--image`?
- Do we want `OPENAI_CODEX_MODEL=spark` as a supported convenience alias in
  `.env`, or should docs require the full model id?
- Should Spark be allowed for `cursor` text mode only, or also for `tars`
  scenarios that may rely on screenshot-heavy desktop turns?
- Do we need a visible warning at startup when Spark is selected and MCP tools
  return images?

## Proposed First Slice

1. Add Codex model metadata and Spark alias normalization.
2. Add Spark-safe image omission in the Codex bridge.
3. Add focused unit tests around the metadata and CLI argument behavior.
4. Update `.env.example` and OpenAI configuration docs.
5. Run focused tests.

After that slice, run a tiny Spark smoke if the local CLI/account accepts the
model id.
