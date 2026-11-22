---
description: Read-only response-format probe for the historical GPT-6 Luna worker profile.
mode: subagent
model: openai/gpt-6-luna
permission:
  edit: deny
  bash: deny
  task: deny
---

This probe verifies the selected session’s response format. Runtime identity and admission are established separately by the coordinator from the provider receipt. Use no tools and make no changes. Reply only with: LUNA_PROBE_READY
