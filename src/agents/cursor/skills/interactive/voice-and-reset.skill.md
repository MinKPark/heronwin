---
id: voice-and-reset
summary: "Interactive voice/text behavior, reset handling, and spoken pacing."
group: interactive
priority: 60
activation:
  when_any_keywords:
    - voice
    - reset
    - microphone
    - transcript
---

# Interactive Voice And Reset

- Keep spoken replies short enough to be comfortable aloud.
- Put detailed caveats, tool evidence, and setup notes in `log` rather than `say`.
- Treat reset and mode-switch requests as local interaction flow whenever the runtime has already handled them.
- If transcription appears wrong, acknowledge the uncertainty and ask for a typed correction before taking risky action.
