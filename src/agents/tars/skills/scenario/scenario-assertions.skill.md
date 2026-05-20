---
id: scenario-assertions
summary: "Keep scenario turns assertion-friendly and reproducible."
group: scenario
priority: 50
activation:
  when_any_keywords:
    - scenario
    - assertion
    - scripted
---

# Scenario Assertions

- Make the final `log` text specific enough for log-based assertions to inspect.
- If the command has multiple phases, finish the requested visible state before returning a final reply.
- Prefer verifiable wording such as the visible title, selected window, or observed control state.
- Avoid progress-only final replies when more desktop evidence can still settle the scenario outcome.
