---
name: build-telegram-ai-phase1
description: Builds Phase 1 Telegram gateway plus assistant-api shell with extensible chat contract and stub LLM provider. Use when implementing the telegram-ai bot, assistant server, or Phase 1 architecture.
---

# Build Telegram AI Phase 1

## Инструкции

1. Прочитай `memory/current-project.md` и `memory/phase-plan.md`.
2. Используй `agents/telegram-bot-agent.md` и `agents/ai-assistant-agent.md`.
3. Держи два сервиса: gateway (bot + Mini App) и assistant-api. Подъём через Docker Compose. Не добавляй RAG/ES/MinIO/Cursor SDK/Директ.
4. Контракт `POST /v1/chat` аддитивно расширяемый, optional `intent`: salon | marketing | tasks | general.
5. Secrets: bot token только в gateway; Cursor key не из Telegram.
6. После slice обнови catalog/contracts/run-log.

## Reference

См. `docs/agent-playbooks/build-telegram-ai-phase1.md`.
