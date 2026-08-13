---
name: build-telegram-ai-phase1
description: Builds Phase 1 Telegram gateway plus assistant-api shell with extensible chat contract and stub LLM provider. Use when implementing the telegram-ai bot, assistant server, or Phase 1 architecture.
---

# Build Telegram AI Phase 1

## Инструкции

1. Прочитай `memory/current-project.md` и `memory/phase-plan.md`.
2. Используй `agents/telegram-bot-agent.md` и `agents/ai-assistant-agent.md`.
3. Держи два сервиса: gateway и assistant-api. Не добавляй RAG/ES/MinIO/Cursor SDK.
4. Контракт `POST /v1/chat` должен быть аддитивно расширяемым.
5. Secrets: bot token только в gateway; Cursor key не из Telegram.
6. После slice обнови catalog/contracts/run-log.

## Reference

См. `docs/agent-playbooks/build-telegram-ai-phase1.md`.
