# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `4-instagram-research` (slice `phase4-postgres-settings` done → next `phase4-ig-graph`)
- Language: русский
- Runtime: Docker Compose (`telegram-gateway` + `assistant-api` + internal `cursor-sdk-bridge` + AgentPacks + `postgres`)

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime — Cursor SDK (у клиента уже есть подписка).

Домены:

- **salon / Babor (Брянск)**: развивать салон — идеи, удержание, локальный маркетинг ради салона; harness memory фактов о салоне;
- **marketing**: рынок красоты, топ-бренды косметики, тренды, таргет/аудитории; **Phase 4** — Instagram Research своего аккаунта;
- **tasks**: расписание работ и напоминания.

Позже: RAG + Elasticsearch (Phase 5); MinIO / фото-видео (Phase 6); Яндекс Директ (Phase 7).

## Goal Phase 4 (current)

Marketing Instagram Research:

1. Docs + ADR ✅
2. Postgres durable settings + harness/research memory ✅
3. Instagram Graph API своего аккаунта (без Apify) ← next
4. 14-дневный scheduler; Mini App + `/research`
5. GenerateImage через local marketing-pack + volume (не OpenAI Images)
6. Hardening

## Phase 4 non-goals

- RAG / embeddings / Elasticsearch
- Apify / чужие crawl-сервисы
- Отдельный OpenAI Images API
- MinIO, Яндекс Директ, Kubernetes / Nginx prod

## Phase 3 leftover

- Packs runtime ✅
- Postgres durable harness memory ✅ (`phase4-postgres-settings`)

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*`, не все playbooks.
- Automation Memories = короткие указатели.
- Не генерировать будущие сервисы «на всякий случай».
- Этот run: только `phase4-postgres-settings`; следующий = `phase4-ig-graph` (не стартовать здесь).
