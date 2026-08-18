# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `5-ui-hardening` (Phase 6 VK **closed**; leftover `phase5-ui-hardening` now open; Phase 7 RAG still gated)
- Language: русский
- Runtime: Docker Compose (`telegram-gateway` + `assistant-api` + internal `cursor-sdk-bridge` + AgentPacks + `postgres` + shared `research-images` volume)

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime — Cursor SDK (у клиента уже есть подписка).

Домены:

- **salon / Babor (Брянск)**: развивать салон — идеи, удержание, локальный маркетинг ради салона; harness memory фактов о салоне;
- **marketing**: рынок красоты, топ-бренды косметики, тренды, таргет/аудитории; Instagram Research своего аккаунта + **Research Studio** Mini App; Phase 6 = открытые паблики VK (официальный API);
- **tasks**: расписание работ и напоминания.

Позже: RAG + Elasticsearch (Phase 7); MinIO / фото-видео (Phase 8); Яндекс Директ (Phase 9).

## Goal Phase 5 (Research Client UI)

Клиентский UI поверх research API (ADR-012):

1. Additive `latest` DTO: analytics + posts[] + items[] (+ planPreview) ✅
2. Mini App Marketing = Research Studio (не pre-dump) ✅
3. Media proxy initData + path guard; imageUrl = gateway proxy ✅
4. Bot web_app «Открыть студию» + MenuButtonWebApp (`TELEGRAM__WEBAPPURL`) ✅
5. Follow-up: `phase5-ui-hardening` (**open** — Phase 6 closed 2026-08-18)

## Goal Phase 6 (VK Public Research)

Официальный VK API открытых пабликов (`wall.get`, service token). Текст + картинки постов → те же snapshot/plan. Не scrape. **Closed** (docs/client/artifacts/settings/media/hardening ✅).

## Phase 5 non-goals

- RAG / embeddings / Elasticsearch (→ Phase 7)
- Apify / OpenAI Images / MinIO
- Отдельный сайт вне Telegram Mini App
- VK (→ Phase 6)

## Phase 4 closed

Marketing Instagram Research — all slices done including hardening.

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*`, не все playbooks.
- Automation Memories = короткие указатели.
- Не генерировать будущие сервисы «на всякий случай».
- Этот run: `phase6-vk-hardening` ✅; **next_slice=`phase5-ui-hardening`**. Phase 7 RAG — не открыт.
