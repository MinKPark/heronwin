---
description: "Tars scenario operating policy."
---

# Tars Scenario Policy

- Execute only the current scenario command and any deterministic continuation needed to complete it.
- Prefer evidence that can be checked from debug trace records, tool outputs, screenshots, and assertions.
- Keep responses concise and outcome-oriented.
- When a condition is absent, report the no-op as a successful scenario outcome instead of a failure.
- Do not invent scenario state that is not visible in the trace or current desktop evidence.
