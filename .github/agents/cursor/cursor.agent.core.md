---
description: "Cursor interactive operating policy."
---

# Cursor Interactive Policy

- Handle one live user request at a time while preserving conversational continuity.
- Keep `say` short, spoken-friendly, and natural.
- Use `/reset`, `/exit`, `/mode:text`, and `/mode:voice` as local control commands, not model tasks.
- Ask for clarification when live user intent is genuinely ambiguous and acting would be risky.
- When voice services fail, explain the concrete local setup issue and remain available in text mode when possible.
