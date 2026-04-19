# Brain OpenAI + ChatGPT Subscription Plan

Last updated: 2026-04-18
Status: proposed

## Summary

`brain` already supports `openai-api` and `claude-api`, but it does not yet
support an OpenAI route that uses a ChatGPT subscription instead of an API key.
There is also a half-finished placeholder for `chatgpt-web` in config, but the
runtime currently rejects it.

This design proposes adding a second OpenAI route so `brain` can use either:

- direct OpenAI Platform API access with `OPENAI_API_KEY`, or
- ChatGPT/Codex subscription-backed access for users who sign in with their
  ChatGPT account.

The main recommendation is to treat this as a separate provider with separate
capabilities and risks, not as an alias for the existing OpenAI API path.
It also proposes making `voice`, `text`, and `scripted` explicit launch modes
instead of assuming that every non-scripted run is voice-capable.

## Why This Needs A Design Pass

The external picture is now clearer than it was when the original
`chatgpt-web` placeholder was added:

- OpenAI officially documents that ChatGPT billing and API billing are separate
  systems.
- OpenAI officially supports signing into Codex clients with a ChatGPT account.
- OpenClaw appears to support ChatGPT-subscription usage through a separate
  OpenAI route, not by pretending a ChatGPT plan is an API key.

That means the right local mental model is:

- `openai-api` = stable developer API, usage-based billing
- `chatgpt.com` / Codex sign-in = separate auth route with different transport,
  limits, and failure modes

## Goals

- Let `brain` run LLM turns through either OpenAI API or ChatGPT-subscription
  auth.
- Keep the existing `openai-api` and `claude-api` paths working unchanged.
- Make the provider choice explicit in config and in `face`.
- Add an explicit interactive text mode that reuses the normal agent/tool
  pipeline without microphone or TTS requirements.
- Support `openai-api` in voice mode and text mode.
- Support ChatGPT/Codex sign-in in text mode and scripted mode.
- Avoid storing fragile browser cookies in `.env`.

## Non-Goals

- Do not claim that a ChatGPT subscription replaces API billing everywhere.
- Do not require ChatGPT/Codex sign-in to support voice mode.
- Do not bring back generic browser-automation chat scraping as the default
  implementation strategy.
- Do not remove the existing OpenAI API path.
- Do not silently downgrade an unsupported voice launch into text mode.

## External Constraints

Verified on 2026-04-18:

- OpenAI Help says ChatGPT and Platform API billing are separate systems.
- OpenAI Help says a ChatGPT subscription cannot simply be moved onto API
  billing.
- OpenAI Help and OpenAI Developers docs say Codex clients can authenticate
  with a ChatGPT account.
- OpenClaw documents two OpenAI routes:
  - `openai/*` for API-key access
  - `openai-codex/*` for ChatGPT/Codex sign-in

Important inference from those sources:

- There is clear official support for ChatGPT-account sign-in in Codex
  surfaces.
- There is not, in the reviewed official docs, a documented general-purpose
  `chatgpt.com` developer API that can simply replace `https://api.openai.com`
  inside `brain`.

## Current Repo State

Today the repo has partial groundwork but no working subscription route:

- `src/head/brain/AppConfig.cs`
  - `LlmProviderId` already includes `ChatGptWeb`
  - `NormalizeProvider` already accepts `chatgpt` / `chatgpt-web`
  - `Load()` immediately throws if that provider is selected
- `src/head/brain/LlmClients.cs`
  - only instantiates `OpenAiApiClient` and `ClaudeApiClient`
- `src/head/brain/.env.example`
  - still advertises `chatgpt-web`
  - still contains browser-oriented `CHATGPT_*` placeholders
- `src/head/brain/README.md`
  - explicitly says browser-backed ChatGPT mode is not included
- `src/head/brain/ConsoleMode.cs`
  - only distinguishes scripted mode from non-scripted mode
  - has no interactive text mode
- `src/head/brain/Program.cs`
  - currently treats every non-scripted run as voice mode
  - voice mode still requires `OPENAI_API_KEY` for Whisper transcription
- `src/head/face/ViewModels/SettingsViewModel.cs`
  - persists API-key settings
  - has no subscription-auth UX or token-status concept

So the repo is not starting from zero, but the existing placeholder shape is
more misleading than useful.

## Options Considered

### 1. OpenClaw-style direct `chatgpt.com` backend route

Description:

- Add a new `ILlmClient` that talks to a `chatgpt.com` backend with
  ChatGPT/Codex auth, similar to OpenClaw's `openai-codex` route.

Pros:

- Closest match to the user's desired outcome.
- Does not require an API key for the LLM path.
- Most likely to preserve the existing `brain` architecture with minimal
  higher-level changes if the transport supports normal request/response,
  vision, and tool-calling semantics.

Cons:

- Appears to depend on an undocumented transport surface.
- Fragile against Cloudflare, bot detection, header changes, auth drift, and
  session policy changes.
- Headless or server-like environments may be hit harder than normal desktop
  interactive use.
- Carries the highest maintenance burden.

Assessment:

- Viable only as an explicitly experimental provider unless we discover a more
  stable OpenAI-supported transport during the spike.

### 2. Codex-auth-backed route through official Codex surfaces

Description:

- Use officially supported ChatGPT sign-in for Codex-oriented clients, then
  bridge that into `brain`.

Pros:

- Best match to documented OpenAI auth behavior.
- Avoids pretending ChatGPT subscription auth is the same thing as API auth.
- Gives us a cleaner story for setup and support.

Cons:

- The officially documented surfaces are Codex-centric, not a documented
  drop-in transport for an arbitrary long-lived assistant runtime like
  `brain`.
- We need to prove that the surface can support `brain`'s message loop:
  text, images, tool definitions, tool results, retries, and local history
  management.
- If the only practical implementation still ends up hitting an unofficial
  backend, the support story is not actually much better than option 1.

Assessment:

- Best conceptual direction, but it needs a spike before we commit to it.

### 3. Browser automation of ChatGPT web UI

Description:

- Drive `chatgpt.com` in a browser and scrape the page.

Pros:

- Familiar human setup path.

Cons:

- Slowest option.
- Most brittle option.
- Worst fit for structured tool-calling and deterministic testing.
- Hardest option to keep stable in scripted scenarios.

Assessment:

- Reject.

## Recommendation

Treat this feature as a new provider named around subscription auth, not as a
revival of the old browser placeholder.

Recommended naming:

- runtime/provider id: `OpenAiCodex` or `ChatGptSubscription`
- user-facing description: `ChatGPT / Codex sign-in`

Compatibility behavior:

- continue accepting `chatgpt` and `chatgpt-web` as input aliases
- warn in logs that the old browser-backed label is deprecated

Recommended implementation strategy:

1. Do a short transport/auth spike first.
2. If the only workable route is direct `chatgpt.com` backend traffic, ship it
   as experimental.
3. Make launch mode explicit: `voice`, `text`, or `scripted`.
4. Keep the voice/speech pipeline separate from the LLM provider decision.

## Proposed Runtime Shape

### Launch modes

Add an explicit launch-mode concept:

- `Voice`
  - microphone input
  - optional spoken output
  - interactive wake-word flow
- `Text`
  - typed interactive REPL in the console
  - no microphone requirement
  - no Whisper requirement
  - no TTS requirement
- `Scripted`
  - existing `--command`, `--commands-file`, and `--scenario` flows

Recommended CLI shape:

- `brain.exe`
  - start voice mode
- `brain.exe --text`
  - start interactive text mode
- `brain.exe --command "..."`, `--commands-file`, `--scenario`
  - start scripted mode

Recommended validation rule:

- launch mode is chosen first
- provider compatibility is validated second
- unsupported combinations fail fast with a precise message

### Provider model

Support:

- `LLM_PROVIDER=openai-api`
- `LLM_PROVIDER=openai-codex` or `chatgpt-subscription`
- `LLM_PROVIDER=claude-api`

Provider expectations:

- `openai-api`
  - requires `OPENAI_API_KEY`
  - uses existing `OpenAiApiClient`
  - supports `Voice`, `Text`, and `Scripted`
- `openai-codex` / `chatgpt-subscription`
  - requires interactive ChatGPT/Codex sign-in or imported local auth state
  - uses a new `ChatGptSubscriptionClient`
  - supports `Text` and `Scripted`
  - does not support `Voice`
- `claude-api`
  - unchanged

Recommended strict behavior:

- `openai-api` + no flags
  - start voice mode
- `openai-api --text`
  - start text mode
- `openai-codex` / `chatgpt-subscription` + no flags
  - fail with a message such as:
    `This provider supports text mode only. Start brain with --text or use scripted commands.`
- `openai-codex --text`
  - start text mode
- `openai-codex` + scripted flags
  - allowed

### Speech separation

Do not bind speech services to the LLM provider name.

Instead:

- LLM provider selection decides how chat turns are produced.
- launch mode decides whether audio services are needed at all.
- transcription and TTS each decide independently whether they are available.

Initial rule set:

- text mode:
  - should work without `OPENAI_API_KEY`
  - should not initialize microphone capture, Whisper transcription, or TTS
- scripted mode:
  - should work with subscription auth and no `OPENAI_API_KEY`
- voice mode:
  - is supported only for providers that explicitly allow it
  - for `openai-api`, may still require `OPENAI_API_KEY` for Whisper/TTS in
    the first release
  - should fail with a precise message when the selected provider does not
    support voice mode
  - should fail with a separate precise message when the provider supports
    voice mode but speech services are unavailable without API credentials

This avoids the current trap where a user could think ChatGPT-subscription mode
is supported, then still fail at startup because voice mode assumes API-backed
speech.

## Proposed Config Shape

The exact variable names can change during implementation, but the design
should move toward this split:

```dotenv
LLM_PROVIDER=openai-api

# OpenAI Platform route
OPENAI_API_KEY=
OPENAI_MODEL=gpt-5.4-mini

# ChatGPT / Codex subscription route
OPENAI_CODEX_MODEL=
CHATGPT_AUTH_PROFILE=
CHATGPT_AUTH_STORE_PATH=

# Speech services remain separate
WHISPER_MODEL=whisper-1
TTS_MODEL=gpt-4o-mini-tts
TTS_VOICE=marin
```

Notes:

- Do not keep raw cookies or access tokens in `.env`.
- Prefer a secure local token store plus a lightweight `.env` pointer to a
  profile id or auth-store path.
- Model discovery should be dynamic where possible. The official Codex docs say
  model availability depends on the client version and configuration, so we
  should not hard-wire too much volatile model metadata into config defaults.

## Auth Plan

### Preferred direction

Add a local auth flow that can either:

- sign in interactively with ChatGPT/Codex, or
- import an already-authenticated local Codex credential source if present

Credential handling requirements:

- store refreshable auth state outside `.env`
- prefer Windows-local secure storage or DPAPI-backed encryption for any local
  token cache
- log the presence of a profile, not the token content
- support logout / credential reset

### User experience

The happy path should look like this:

1. User chooses `ChatGPT / Codex sign-in` in `face` or `.env`.
2. `brain` detects no local auth profile.
3. User is prompted to complete a local sign-in or import flow.
4. User launches `brain --text` or runs scripted commands.
5. `brain` stores a local profile id and starts using that provider.

Avoid:

- requiring users to paste browser cookies
- burying auth state in a random JSON file with no reset path

## Transport Spike Gate

Before implementation, do a small spike and answer these questions:

1. Can we drive a stable request/response loop for normal `brain` text turns?
2. Can the route support image inputs?
3. Can the route support tool definitions and tool-call round trips in a form
   we can map onto `ILlmClient`?
4. Can we refresh auth without forcing the user to re-login constantly?
5. Does the route survive the normal local Windows desktop environment where
   `brain` runs, or does it mostly fail outside a browser-hosted session?

If the answer to 1-3 is weak, stop and do not start a large implementation.

## Implementation Plan

### Phase 0: Spike and decision record

- Verify whether the intended subscription route can satisfy the `ILlmClient`
  contract.
- Decide whether the implementation is:
  - official Codex-surface-backed,
  - unofficial direct `chatgpt.com` backend,
  - or not viable enough to pursue now.
- Capture the result in this doc before landing user-facing config.

### Phase 1: Config and provider plumbing

- Replace the hard failure in `AppConfig.Load()` with real provider config.
- Add new provider-specific config fields.
- Keep backward-compatible alias parsing for `chatgpt` / `chatgpt-web`.
- Update validation messages so they explain which credential is missing for
  which subsystem.

### Phase 2: Launch-mode split

- Replace the current implicit `scripted` vs `voice` split with an explicit
  launch mode:
  - `Voice`
  - `Text`
  - `Scripted`
- Add `--text` to `ConsoleMode`.
- Split `Program.cs` startup into separate mode entry points instead of treating
  every non-scripted run as voice mode.
- Add provider x launch-mode validation before audio initialization.

### Phase 3: `ChatGptSubscriptionClient`

- Add a new `ILlmClient` implementation.
- Map:
  - `AgentMessage.User`
  - `AgentMessage.Summary`
  - `AgentMessage.VisualContext`
  - `AgentMessage.Assistant`
  - `AgentMessage.ToolResult`
  to the provider payload format.
- Parse assistant text and tool calls back into `ChatResult`.
- Add provider-specific retry/error parsing instead of reusing OpenAI API
  assumptions blindly.

### Phase 4: Auth store and login flow

- Add a local auth-profile abstraction.
- Implement load/save/refresh/logout behavior.
- Add a minimal sign-in bootstrap flow or import path.
- Ensure logs and debug traces redact provider credentials.

### Phase 5: Text-mode interaction loop

- Add an interactive console text loop that reuses the normal agent/tool flow.
- Support at least:
  - one line per turn
  - `/exit` to quit
  - `/reset` to clear local history if that fits the current interaction model
- Make sure text mode still publishes useful status to `face`.

### Phase 6: Voice/speech behavior cleanup

- Split LLM auth availability from speech-service availability.
- Keep text mode and scripted mode working without API speech credentials.
- Make voice-mode startup errors precise and actionable.
- For now, keep voice mode limited to providers that explicitly support it.

### Phase 7: `face` settings support

- Update the provider UI wording.
- Show whether the selected provider supports voice mode, text mode, or both.
- Add subscription-auth status text.
- Add buttons or instructions for sign-in, import, and logout if the UX can
  support them cleanly.
- Avoid showing irrelevant API-key warnings when the selected provider is not
  API-based.

### Phase 8: Docs and cleanup

- Update `src/head/brain/README.md`.
- Update `src/head/brain/.env.example`.
- Update CLI help text to describe `--text`.
- Remove or rename the stale browser-specific config placeholders if they are
  not part of the chosen implementation.
- Update status docs after the provider decision lands.

## Verification

### Unit tests

- launch-mode parsing and validation
- `AppConfig` provider parsing and validation
- provider x launch-mode compatibility checks
- provider-specific missing-credential errors
- auth-profile load/save/redaction behavior
- request/response translation for text, image, and tool-call flows
  - provider-specific retry classification

### Integration tests

- interactive text mode with `openai-api`
- interactive text mode with subscription auth
- scripted mode with `openai-api`
- scripted mode with subscription auth
- clean rejection for subscription auth in voice mode
- fallback behavior when subscription auth is missing or expired
- clear failure behavior when voice mode lacks speech credentials

### Manual checks

- sign in from a clean machine state
- restart `brain` and confirm auth profile reuse
- start `brain --text` and complete a few real turns
- run at least one real scripted scenario with tool calls
- confirm `brain` rejects a default voice launch for the subscription provider
  with a helpful error
- verify that `face` shows the right provider and setup state

## Risks

- The transport may depend on undocumented `chatgpt.com` behavior.
- Cloudflare or anti-bot measures may intermittently break the route.
- Token-refresh or profile-selection logic may be much harder than API-key
  flows.
- Model availability may vary by ChatGPT plan and over time.
- The subscription route may support coding-oriented Codex behavior better than
  generic assistant conversations.
- Adding text mode introduces one more interaction path to keep aligned with
  voice and scripted behavior.

## Open Questions

- Should the final provider name be `openai-codex`, `chatgpt-subscription`, or
  something else?
- Should `brain.exe` with a text-only provider hard-fail, or should it default
  into text mode automatically despite the recommendation above?
- What should text-mode reset behavior be: no reset command, `/reset`, or full
  session restart only?
- Should `face` own the interactive sign-in entry point, or should it remain a
  `brain` CLI helper flow?
- Should we import existing Codex login state if present, or require a fresh
  `brain`-specific auth profile?
- If the transport is unofficial, do we still want to ship it behind an
  explicit experimental flag?

## Rollout Recommendation

1. Review this design and choose the provider naming.
2. Agree on the launch-mode split: `voice`, `text`, and `scripted`.
3. Run the transport/auth spike before any major UI work.
4. If the spike is good enough, land provider plumbing plus text-mode support
   first.
5. Add `face` auth UX after the backend contract is proven.
6. Keep voice-mode promises narrow and explicit.

## Sources

Verified on 2026-04-18:

- OpenAI Help: Billing settings in ChatGPT vs Platform
  - https://help.openai.com/en/articles/9039756
- OpenAI Help: How can I move my ChatGPT subscription to the API?
  - https://help.openai.com/en/articles/8156019
- OpenAI Help: Using Codex with your ChatGPT plan
  - https://help.openai.com/en/articles/11369540-codex-in-chatgpt-faq
- OpenAI Developers: Codex CLI
  - https://developers.openai.com/codex/cli
- OpenClaw provider docs: OpenAI
  - https://github.com/openclaw/openclaw/blob/main/docs/providers/openai.md

Implementation-risk evidence reviewed on 2026-04-18:

- OpenClaw issue showing `openai-codex` routing through `chatgpt.com` and
  hitting Cloudflare challenge failures:
  - https://github.com/openclaw/openclaw/issues/66633
