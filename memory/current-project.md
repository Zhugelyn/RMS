# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `1-shell`
- Language: русский
- Runtime Phase 1: Docker Compose only

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime позже — Cursor SDK (у клиента уже есть подписка).

Домены (бизнес-логика не в Phase 1, только UI-оболочка и контракт `intent`):

- салон красоты: записи, расписание, клиенты;
- маркетинг: реклама, мониторинг, монетизация;
- повседневные задачи: планирование, встречи.

Позже: Яндекс Директ и другие внешние сервисы; генерация картинок; работа с фото и видео; RAG + Elasticsearch (можно готовые фреймворки).

## Goal Phase 1

Рабочая оболочка. Не RAG, не Cursor SDK, не Директ, не медиа-пайплайн.

1. `telegram-gateway` — Telegram bot + Mini App (салон / маркетинг / задачи).
2. `assistant-api` — расширяемый AI server.
3. `POST /v1/chat` с `schemaVersion` и optional `intent`.
4. `ILlmProvider` = stub. Интерфейсы `IRagRetriever`, `IFileStore`, `IToolProvider` можно объявить, реализации не писать.
5. Secrets только из env / Docker secrets. Encrypt-at-rest заложить для будущих ключей.
6. `docker compose up` поднимает оба сервиса.

## Phase 1 non-goals

- Elasticsearch, embeddings, RAG frameworks
- Cursor SDK / живой LLM
- MinIO, генерация картинок, фото/видео пайплайн
- Яндекс Директ и прочие ads API
- Kubernetes / Nginx prod

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*`, не все playbooks.
- Automation Memories = короткие указатели.
- Не генерировать будущие сервисы «на всякий случай».
