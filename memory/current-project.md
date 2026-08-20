# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `8-files-minio` **closed** (`phase8-hardening` ✅ 2026-08-20; all Phase 8 slices done). Next open = Phase 9 External tools (Яндекс Директ) — только по явному запросу.
- Language: русский
- Runtime: Docker Compose (`telegram-gateway` + `assistant-api` + internal `cursor-sdk-bridge` + AgentPacks + `postgres` + `elasticsearch` + `rag-service` + `minio` + `minio-init` + shared `research-images` volume)
  - Phase 8 closed: MinIO + presign/metadata ✅; pack file tools ✅; hardening (TTL + scan stub + non-goals) ✅

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime — Cursor SDK (у клиента уже есть подписка).

Домены:

- **salon / Babor (Брянск)**: развивать салон — идеи, удержание, локальный маркетинг ради салона; harness memory фактов о салоне;
- **marketing**: рынок красоты, топ-бренды косметики, тренды, таргет/аудитории; Instagram Research своего аккаунта + **Research Studio** Mini App; Phase 6 = открытые паблики VK (официальный API);
- **tasks**: расписание работ и напоминания.

Позже: Яндекс Директ (Phase 9).

## Goal Phase 5 (Research Client UI)

Клиентский UI поверх research API (ADR-012):

1. Additive `latest` DTO: analytics + posts[] + items[] (+ planPreview) ✅
2. Mini App Marketing = Research Studio (не pre-dump) ✅
3. Media proxy initData + path guard; imageUrl = gateway proxy ✅
4. Bot web_app «Открыть студию» + MenuButtonWebApp (`TELEGRAM__WEBAPPURL`) ✅
5. Follow-up: `phase5-ui-hardening` ✅ (2026-08-18) — Phase 5 closed

## Goal Phase 6 (VK Public Research)

Официальный VK API открытых пабликов (`wall.get`, service token). Текст + картинки постов → те же snapshot/plan. Не scrape. **Closed** (docs/client/artifacts/settings/media/hardening ✅).

## Phase 5 non-goals

- RAG / embeddings / Elasticsearch (→ Phase 7)
- Apify / OpenAI Images / MinIO
- Отдельный сайт вне Telegram Mini App
- VK (→ Phase 6)

## Goal Phase 7 (Knowledge / RAG)

Документный RAG: `rag-service` + Elasticsearch; индексы salon/marketing раздельно; retriever только в pack MCP. All slices ✅ including `phase7-hardening` (caps, PII-safe logs, ES basic auth, non-goals). Phase 7 closed.

## Goal Phase 8 (Files / MinIO)

Object storage: MinIO private buckets + presigned URLs; metadata в assistant-api (ADR-015). Docs ✅; compose ✅; presign ✅; pack files ✅; hardening ✅. **Phase 8 closed.**

## Phase 4 closed

Marketing Instagram Research — all slices done including hardening.

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*`, не все playbooks.
- Automation Memories = короткие указатели.
- Не генерировать будущие сервисы «на всякий случай».
- Этот run: `phase8-hardening` ✅; **next_slice=none** (Phase 9 Direct — только по явному запросу).
