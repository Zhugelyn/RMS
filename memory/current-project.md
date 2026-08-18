# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `5-research-ui` (Phase 4 closed; next = `phase5-research-ui`; Knowledge/RAG сдвинут на Phase 6)
- Language: русский
- Runtime: Docker Compose (`telegram-gateway` + `assistant-api` + internal `cursor-sdk-bridge` + AgentPacks + `postgres` + shared `research-images` volume)

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime — Cursor SDK (у клиента уже есть подписка).

Домены:

- **salon / Babor (Брянск)**: развивать салон — идеи, удержание, локальный маркетинг ради салона; harness memory фактов о салоне;
- **marketing**: рынок красоты, топ-бренды косметики, тренды, таргет/аудитории; **Phase 4** — Instagram Research своего аккаунта;
- **tasks**: расписание работ и напоминания.

Позже: Research Client UI (Phase 5); RAG + Elasticsearch (Phase 6); MinIO / фото-видео (Phase 7); Яндекс Директ (Phase 8).

## Goal Phase 4 (closed)

Marketing Instagram Research — all slices done:

1. Docs + ADR ✅
2. Postgres durable settings + harness/research memory ✅
3. Instagram Graph API своего аккаунта (без Apify) ✅
4. Research artifacts (snapshot+plan+episodes) + marketing inject ✅
5. 14-дневный scheduler ✅; Mini App + `/research` ✅
6. GenerateImage через local marketing-pack + volume (не OpenAI Images) ✅
7. Hardening ✅ (`phase4-hardening`)

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
- Этот run: `phase4-hardening` closed; **next_slice=none** (не стартовать Phase 5).
