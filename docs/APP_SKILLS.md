# Create App Skills

App skills are Markdown playbooks that teach HeronWin how to work inside a specific app or website. They do not add new tools. They tell the assistant which surface it is on, which actions are appropriate, what counts as success, and when to stop instead of guessing.

Use app skills for app-specific behavior. Keep generic reliability rules in the core agent, `any-app`, `generic-app`, browser, or runtime code only when the rule is truly cross-app.

## Where Skills Live

Shared app and site skills live under:

```text
src/agents/shared/skills/<group>/*.skill.md
```

Examples:

- [Netflix skills](../src/agents/shared/skills/netflix/)
- [Edge browser skill](../src/agents/shared/skills/edge/browser-navigation-and-web-operations.skill.md)
- [Any-app skills](../src/agents/shared/skills/any-app/)
- [Generic app policy](../src/agents/shared/skills/generic-app/generic-app-policy.skill.md)

Only use assistant-specific skill folders when the guidance is truly assistant-specific:

- `src/agents/tars/skills`: scenario execution and reproducibility.
- `src/agents/cursor/skills`: interactive voice/text behavior.

## When To Create One

Create or update an app skill when the behavior is mainly about app policy:

- app vocabulary, visible surfaces, and state names
- app-specific action ordering
- exact target-selection and disambiguation rules
- app-specific success, no-op, and stop conditions
- app-specific ASR repair hints
- deterministic same-surface batches for stable UI phases

Do not create an app skill to paper over a generic runtime problem. If the fix is deterministic recovery, tool-output interpretation, safety, evidence refresh, or a reusable invariant, start with [Development Guardrails](../devdocs/DEVELOPMENT_GUARDRAILS.md) and [Skill Versus Code Policy](../src/agents/skill-vs-code-policy.md).

## File Shape

Every skill is a `.skill.md` file with YAML frontmatter followed by Markdown instructions:

```markdown
---
id: spotify-surface-and-state
group: spotify
priority: 350
summary: "Shared Spotify surface model and state-verification rules."
preferred_tools:
  - cognition/describe_window
  - cognition/capture_window_screenshot
  - execution/click_window_element
  - execution/invoke_window_element
  - execution/set_window_element_text
  - execution/type_window_text
activation:
  when_any_keywords:
    - spotify
  when_any_tools:
    - describe_window
    - capture_window_screenshot
    - click_window_element
    - invoke_window_element
    - set_window_element_text
    - type_window_text
applies_when:
  - The user is acting inside Spotify and the request depends on interpreting Spotify state correctly.
---

# Skill: Spotify Surface And State

## Cross-Cutting Rules

- Treat Spotify as layered surfaces: host window, navigation chrome, list/detail content, then playback controls.
- Prefer exact visible playlist, album, track, and control names over generic wrappers.
- After any open, search, or playback action, verify the resulting Spotify screen before continuing.
- If the user only asks whether a surface is visible, report that state and stop.
```

## Metadata Reference

`id`: Stable skill key. Usually the filename without `.skill.md`.

`group`: App or site group slug, such as `netflix` or `spotify`. Skills with the same group are composed together under one prompt section.

`priority`: Lower numbers are loaded earlier. Existing bands are roughly `100` for Windows startup, `140-210` for generic app skills, `300` for browser host skills, `350` for app surface/state skills, and `400+` for narrower app workflows.

`summary`: Short human-readable description.

`preferred_tools`: Tool names the skill expects the assistant to prefer. This is guidance, not tool registration.

`activation`: Structured rules that decide when the skill is included in a turn.

`applies_when`: Human-readable scope notes for maintainers and the model.

`affordances`: Optional runtime opt-ins. Use only when the app really needs one of the existing generic primitives.

Current affordances:

- `website_fallback`: lets the runtime treat an active app/site skill as authorizing website fallback confirmation for that app.
- `named_choice_continuation`: lets generic named-choice continuation consider the skill's app surface.
- `discrete_slot_text_entry`: lets generic continuation handle structured one-character-at-a-time entry flows.
- `discrete_slot_text_rewrite`: lets runtime rewrite unsafe bulk text entry into sequential discrete-slot entry when the surface evidence supports it.

## Activation Rules

Prefer structured activation metadata for new skills. If any activation criteria are present, every non-empty criteria group must pass:

- `when_any_intents`: at least one listed request intent must match.
- `when_all_intents`: every listed request intent must match.
- `unless_any_intents`: none of these intents may match.
- `when_any_tools`: at least one listed tool must be available.
- `when_all_tools`: every listed tool must be available.
- `when_any_keywords`: at least one phrase must appear in activation text.
- `when_all_keywords`: every phrase must appear in activation text.
- `unless_any_keywords`: none of these phrases may appear.

Activation text includes the current user request plus recent user messages, summaries, and assistant `say`/`log` text. Keywords are normalized, so use plain phrases like `profile lock`, `audio subtitles`, or `spotify`.

Known request intents:

- `launch_request`
- `browser_request`
- `instruction_lookup_request`
- `direct_browser_navigation_request`
- `search_or_enumeration_request`
- `action_request`

Tool names may be written with or without prefixes in activation. For example, `cognition/describe_window` and `describe_window` both match the normalized tool name `describe_window`.

## How To Split A Skill Group

Split by independently activatable UI surface and distinct decision logic, not by file length.

A good app group often starts with one surface/state skill:

```text
src/agents/shared/skills/spotify/
  spotify-surface-and-state.skill.md
```

Add narrower skills only when a workflow has different activation and rules:

```text
src/agents/shared/skills/spotify/
  spotify-surface-and-state.skill.md
  spotify-search.skill.md
  spotify-playback-controls.skill.md
```

Avoid many tiny skills that always activate together, repeat the same guidance, or share the same failure handling. The Netflix group is the main reference pattern: one shared surface model plus focused skills for profile/PIN, search, browse/play, and playback controls.

## Writing Good Skill Rules

Good app skills are operational. They say what to do from evidence.

- Name the surfaces the assistant must recognize.
- Say which actions are allowed on each surface.
- Separate observation-only turns from action turns.
- Prefer exact visible names, paths, and controls from fresh evidence.
- Define success and stop conditions.
- Say what to verify after UI-changing actions.
- Describe app-specific no-op behavior, such as "if the prompt is absent, report that no action was needed."
- Include ASR repair hints only when there is one obvious visible correction.
- Authorize same-surface batching only for deterministic sequences on one stable surface.
- Stop batches at transition boundaries such as navigation, modal opening, search result updates, or playback start.
- Do not invent tool behavior or mention tools the MCP servers do not expose.
- Do not include secrets, real PINs, passwords, tokens, or account-specific data.

## Manual Workflow

1. Pick the group slug:
   - Use a lowercase app or site slug such as `spotify`, `slack`, or `photoshop`.
   - Keep product-specific vocabulary inside that group.

2. Create the folder and first skill file:
   - `src/agents/shared/skills/<group>/<group>-surface-and-state.skill.md`

3. Draft the frontmatter:
   - Include `id`, `group`, `priority`, `summary`, `preferred_tools`, `activation`, and `applies_when`.
   - Start with keyword activation for the app name plus the tools needed to operate the surface.

4. Draft the body:
   - Start with cross-cutting surface/state rules.
   - Add workflow sections only when they are specific and testable.
   - Keep generic UI rules out of app files unless the app has a specific exception.

5. Add focused workflow skills when needed:
   - Search, playback controls, account setup, import/export, or modal flows are common splits.
   - Give narrower skills a slightly higher priority than the shared surface skill.

6. Verify activation and behavior:
   - Add or update `AgentPromptLoaderTests` when the repository should guarantee the skill exists and keeps important wording.
   - Add or update `AgentPromptComposerTests` when activation behavior matters.
   - For important app flows, run the relevant assistant or scenario and inspect the trace.

Useful commands:

```powershell
dotnet test src\assistants\brain.tests\HeronWin.Brain.Tests.csproj --filter "FullyQualifiedName~AgentPrompt"
dotnet test src\heronwin.sln
```

## Generated Draft Workflow

When the assistant is asked to open an unknown app, it may offer to generate a dedicated app skill group first. If the user approves, it should use the app vendor's official website, help center, or documentation to draft one to three `.skill.md` files.

Treat generated skills as drafts:

- Review the source URL and prefer official documentation.
- Check that the group slug is correct.
- Keep the files under `src/agents/shared/skills/<group>/`.
- Confirm each file has complete YAML frontmatter and a clear body.
- Remove generic filler that belongs in core, `any-app`, or `generic-app`.
- Add tests or a runtime smoke pass before relying on the skill for repeatable work.

## Review Checklist

Before merging a new app skill, ask:

- Does this describe app-specific behavior rather than generic runtime reliability?
- Is app-specific vocabulary confined to the app's own skill group, scenarios, tests, or docs?
- Will the activation metadata include the skill only when it is useful?
- Are observation, action, no-op, and stop conditions explicit?
- Does every UI-changing action have a verification rule?
- Are deterministic batches limited to one stable surface and stopped before transitions?
- Are any `affordances` intentional and backed by tests or traces?
- Would the skill still be understandable from a fresh trace six weeks from now?

## Related References

- [Agent Prompts And Skills](../src/agents/README.md)
- [Skill Versus Code Policy](../src/agents/skill-vs-code-policy.md)
- [Goal And Design](../devdocs/GOAL_AND_DESIGN.md)
- [App-Agnostic Runtime And Skills Plan](../devdocs/designs/app-agnostic-runtime-and-skills-plan.md)
- [Generic Continuations And Discrete Entry Plan](../devdocs/designs/generic-continuations-and-discrete-entry-plan.md)
