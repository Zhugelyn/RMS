# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `5-research-ui` (slice `phase5-research-ui` ✅; next=`phase5-ui-hardening`)
- Language: русский
- Runtime: Docker Compose (`telegram-gateway` + `assistant-api` + internal `cursor-sdk-bridge` + AgentPacks + `postgres` + shared `research-images` volume)

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime — Cursor SDK (у клиента уже есть подписка).

Домены:

- **salon / Babor (Брянск)**: развивать салон — идеи, удержание, локальный маркетинг ради салона; harness memory фактов о салоне;
- **marketing**: рынок красоты, топ-бренды косметики, тренды, таргет/аудитории; Instagram Research своего аккаунта + **Research Studio** Mini App;
- **tasks**: расписание работ и напоминания.

Позже: RAG + Elasticsearch (Phase 6); MinIO / фото-видео (Phase 7); Яндекс Директ (Phase 8).

## Goal Phase 5 (Research Client UI)

Клиентский UI поверх research API (ADR-012):

1. Additive `latest` DTO: analytics + posts[] + items[] (+ planPreview) ✅
2. Mini App Marketing = Research Studio (не pre-dump) ✅
3. Media proxy initData + path guard; imageUrl = gateway proxy ✅
4. Bot web_app «Открыть студию» + MenuButtonWebApp (`TELEGRAM__WEBAPPURL`) ✅
5. Follow-up: `phase5-ui-hardening`

## Phase 5 non-goals

- RAG / embeddings / Elasticsearch (→ Phase 6)
- Apify / OpenAI Images / MinIO
- Отдельный сайт вне Telegram Mini App

## Phase 4 closed

Marketing Instagram Research — all slices done including hardening.

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*`, не все playbooks.
- Automation Memories = короткие указатели.
- Не генерировать будущие сервисы «на всякий случай».
- Этот run: `phase5-research-ui`; **next_slice=`phase5-ui-hardening`**.
