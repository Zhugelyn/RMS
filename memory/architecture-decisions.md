# Architecture Decisions

ADR-журнал для решений, которые должны пережить текущий чат.

## Template

```markdown
## ADR-000: <title>

- Status: proposed | accepted | superseded
- Date:
- Context:
- Decision:
- Consequences:
- Alternatives considered:
- Security impact:
- Links:
```

## Current Decisions

## ADR-001: Phase 1 = two services, stub LLM

- Status: accepted
- Date: 2026-08-13
- Context: Нужен Telegram bot + AI assistant. RAG/ES/files/Cursor SDK будут позже. API должно расширяться.
- Decision: Сейчас только `telegram-gateway` и `assistant-api`. LLM через `ILlmProvider`, default stub. Cursor SDK — Phase 2 provider, не отдельный публичный сервис на старте.
- Consequences: Быстрый вертикальный срез. Расширение аддитивными полями `/v1/chat` и новыми providers.
- Alternatives considered: Modular monolith один процесс; сразу multi-agent harness; сразу RAG+ES.
- Security impact: Меньше секретов и attack surface. Bot token и Cursor key разведены по сервисам.
- Links: `memory/current-project.md`

## ADR-002: Secrets never travel through Telegram

- Status: accepted
- Date: 2026-08-13
- Context: Клиентская подписка Cursor. Нужна безопасная работа с токенами.
- Decision: Telegram message не является каналом для API keys. Bot token только в gateway. Cursor API key только в assistant-api / future harness, из secret store. Если ключ надо хранить — envelope encryption (Data Protection / AES-GCM), master key вне git. Transit = TLS.
- Consequences: Пользователь бота не присылает ключ в чат. Ротация через secret provider.
- Alternatives considered: Пользователь шлёт ключ боту; ключ в query string; один shared secret на все сервисы.
- Security impact: Убирает утечку ключей в Telegram history/logs.
- Links: `memory/security-baseline.md`

## ADR-003: Telegram Mini App is the UI

- Status: accepted
- Date: 2026-08-13
- Context: Клиентская часть — Telegram. Нужен красивый интерфейс, не только текстовый echo.
- Decision: UI = Telegram bot + Mini App. Экраны Phase 1: салон, маркетинг, задачи. Кнопки вызывают `POST /v1/chat` с `intent`. Бизнес-логика доменов не реализуется в Phase 1.
- Consequences: Gateway отдаёт WebApp. Дизайн на стороне агента реализации.
- Alternatives considered: Только reply-клавиатура; отдельный web frontend вне Telegram.
- Security impact: Mini App не содержит bot token / service key. Init data проверять, когда появится auth пользователя.
- Links: `memory/current-project.md`

## ADR-004: Docker Compose is the Phase 1 runtime

- Status: accepted
- Date: 2026-08-13
- Context: Клиент должен поднимать сервисы просто.
- Decision: Phase 1 поднимается только через `docker compose up`. Секреты — env / Docker secrets, не файлы в git. K8s/Nginx — не в этой фазе. Helper `scripts/compose-up.sh` для nested Docker (ip_forward).
- Consequences: Один compose на gateway + assistant-api.
- Alternatives considered: Локальный `dotnet run` без Docker; сразу Kubernetes.
- Security impact: `.env` в `.gitignore`. Пример только `.env.example` без реальных значений.
- Links: `memory/phase-plan.md`

## ADR-005: Inter-service auth via shared service key header

- Status: accepted
- Date: 2026-08-13
- Context: gateway вызывает assistant-api; bot token не должен покидать gateway.
- Decision: `X-Service-Key` / `Assistant__ServiceKey` на всех non-health routes assistant-api. Mini App ходит в gateway proxy, не напрямую с ключом.
- Consequences: Один shared secret Phase 1; позже можно mTLS/JWT между сервисами.
- Alternatives considered: mTLS сразу; публичный chat без auth.
- Security impact: bot token и service key разделены; health остаётся без auth для probes.
- Links: `memory/integration-contracts.md`

## ADR-006: Phase 2 Cursor SDK via internal bridge + stub fallback

- Status: accepted
- Date: 2026-08-15
- Context: Нужен живой LLM через клиентский Cursor API key. `@cursor/sdk` — Node/TS. assistant-api — .NET. Без ключа compose должен оставаться зелёным.
- Decision: `CursorSdkLlmProvider` + `DomainHarness` (classify→agent→verify) в assistant-api. Runtime SDK — internal `cursor-sdk-bridge` (`@cursor/sdk`, cloud no-repo). API key encrypt-at-rest AES-GCM. `FallbackLlmProvider` → stub без ключа / при ошибке. Resume через optional `agentId`.
- Consequences: Один compose stack из трёх контейнеров; публичный контракт `/v1/chat` аддитивно расширен `agentId`. RAG/ES/MinIO не трогаем.
- Alternatives considered: Cloud Agents REST напрямую из C#; полный Connect protobuf adapter; отдельный публичный harness-сервис.
- Security impact: ключ не из Telegram; plaintext scrub после seal; bridge без public ports; verify режет secret-like output.
- Links: `memory/phase-plan.md`, `memory/security-baseline.md`

## ADR-007: Domain agent packs, not prompt-switch; not a service per domain

- Status: accepted
- Date: 2026-08-15
- Context: Phase 2 `DomainHarness` — один `@cursor/sdk` agent на диалог, домены = префиксы промпта, verify soft-skip. Это не масштабируется: разные MCP/skills/AGENTS.md/модели нельзя изолировать. Нужны specialist-агенты под салон / маркетинг / задачи.
- Decision: Phase 3 вводит **domain agent packs** на диске (`src/AgentPacks/<domain>/`: `AGENTS.md`, `skills/`, `prompts/`, `mcp.json`, `pack.json`). Router — отдельный pack `_router`. Runtime: отдельный `Agent.create`/resume **на домен**; affinity `conversationId+domain → agentId` в assistant-api. Packs остаются в modular monolith (`assistant-api` + `cursor-sdk-bridge`). Отдельный микросервис на домен — только когда появится независимый data ownership / deploy cadence. RAG/MinIO/Директ подключаются позже **в pack**, не новым shared prompt.
- Consequences: Phase 2 prompt-harness — переходный. `/v1/chat` аддитивно (resolved domain). Bridge контракт расширяется pack runtime. Cross-domain resume запрещён.
- Alternatives considered: оставить persona-switch; отдельный `salon-api`/`marketing-api` сразу; один mega-agent со всеми MCP.
- Security impact: MCP allowlist per pack; секреты MCP не в git; изоляция skills/MCP между доменами; verify обязан резать secret-leak и domain drift.
- Links: `memory/phase-plan.md` Phase 3, `memory/integration-contracts.md`

## ADR-008: Harness memory = profile + domain episodes, not SDK resume and not RAG

- Status: accepted
- Date: 2026-08-15
- Context: Cursor `agentId` помнит только текущий SDK-агент; новый create / смена домена / рестарт контейнера — контекст пользователя пропадает. Нужна короткая память «кто это» и «что уже сделали». Полный транскрипт и Elasticsearch — слишком жирно и смешивает домены.
- Decision: `assistant-api` владеет store. Два слоя: (1) **UserProfile** — короткие shared факты; (2) **HarnessEpisode** `{userId, domain, task, result, at}` — бриф, cap last K. Specialist pack получает profile + свои episodes. Router — profile + last-domain, без чужих эпизодов. Запись эпизода после успешного verify; fail записи не валит HTTP-ответ. Это не RAG (Phase 5) и не `Agent.resume`.
- Consequences: specialist не слепой после нового `Agent.create`. Токен-бюджет: жёсткий лимит символов на инжект. **Impl 2026-08-15:** `IHarnessMemoryStore` + in-process store (isolation OK). **Durable PostgreSQL 2026-08-17:** `phase4-postgres-settings` — `PostgresHarnessMemoryStore` when `ConnectionStrings:AssistantDb` set; in-process fallback otherwise.
- Alternatives considered: тащить всю историю в Cursor agent; один shared log на все домены; отдельный memory-microservice; сразу Elasticsearch.
- Security impact: domain isolation эпизодов; scanner на task/result; PII не в логах; retention/TTL позже явно.
- Links: `memory/phase-plan.md` Phase 3 slice `phase3-harness-memory`, Phase 4 `phase4-postgres-settings`

## ADR-009: Instagram feed source = Graph API of own account only (no Apify)

- Status: accepted
- Date: 2026-08-17
- Context: Phase 4 Marketing Instagram Research нужен источник ленты. Apify и сторонние scrapers дают чужие аккаунты, ToS/риск блокировок и платный crawl. У клиента есть свой бизнес-аккаунт Instagram.
- Decision: Единственный источник ленты/insights — **бесплатный Instagram Graph API** своего аккаунта (`INSTAGRAM__ACCESSTOKEN` + business account id). Apify, произвольные scrapers и «скачать чужую ленту» — non-goals Phase 4.
- Consequences: Ограничение = доступный Graph scope своего аккаунта. Research не про конкурентный crawl. Token — secret store / env, не из Telegram.
- Alternatives considered: Apify Instagram scrapers; неофициальные mobile API; ручной CSV upload only.
- Security impact: IG token encrypt-at-rest / env; не в git/logs/Mini App/chat; least privilege Graph permissions; rate-limit handling.
- Impl 2026-08-17 (`phase4-ig-graph`): `IInstagramGraphClient` / `HttpInstagramGraphClient` + stub; `EncryptedInstagramTokenStore`; media mapper fixtures; SSRF CDN allowlist download; soft errors for rate-limit/expiry.
- Links: `memory/phase-plan.md` Phase 4, `memory/security-baseline.md`

## ADR-010: Research artifacts (snapshot+plan+episodes) ≠ RAG

- Status: accepted
- Date: 2026-08-17
- Context: Нужна память research-цикла: снимок ленты, 14-дневный план, короткие «задача→результат». Легко спутать с RAG/embeddings/ES.
- Decision: Research memory = структурированные артефакты в **Postgres assistant-api**: `snapshot` (срез ленты/метрик), `plan` (14 дней), `episodes` (бриф). Это расширение harness memory, **не** документный индекс, не embeddings, не Elasticsearch. RAG остаётся Phase 5.
- Consequences: Marketing pack инжектит snapshot/plan/episodes по лимиту символов. Retriever/vector search не появляется в Phase 4. Phase 3 packs не «закрываются целиком» — Postgres leftover закрывается здесь.
- Alternatives considered: сразу RAG над постами; хранить полный media blob в ES; отдельный research-microservice.
- Security impact: PII/tokens не в snapshot dump логов; domain isolation (research → marketing); retention/TTL явно в hardening slice.
- Impl 2026-08-17 (`phase4-research-artifacts`): `IResearchArtifactStore` + `InstagramResearchCapture` (Graph→snapshot→14-day plan→marketing episode); `ResearchPackInjector` only for marketing pack; soft-fail persist/inject; fixtures/isolation tests. No embeddings/ES.
- Links: `memory/phase-plan.md` Phase 4, ADR-008

## ADR-011: Image generation = Cursor GenerateImage via local marketing pack + volume

- Status: accepted
- Date: 2026-08-17
- Context: Для research/контент-плана нужны картинки. Отдельный OpenAI Images API = ещё один секрет, биллинг и обход Cursor subscription. Cloud harness уже на Cursor SDK.
- Decision: Генерация картинок — **Cursor GenerateImage** через local `AgentPacks/marketing` (skill/tool) + Docker **volume** для артефактов. Отдельный OpenAI Images / DALL·E API в Phase 4 запрещён. MinIO как object store — Phase 8.
- Consequences: Картинки живут в volume, доступном marketing pack / bridge / gateway. Нет второго image-provider. Failures GenerateImage = soft fail research path с `lastError=image-tool-missing`, план без картинок, job не падает.
- Alternatives considered: OpenAI Images API; внешний Stable Diffusion SaaS; отложить картинки до Phase 8 MinIO.
- Security impact: Cursor key уже в assistant-api; не проксировать image bytes через Telegram without size limits; volume path traversal guard; без Cursor key — skip images.
- Impl 2026-08-17 (`phase4-generate-image`): bridge `collectImages`+`localCwd`; `ResearchImageGenerator` + marketing skill; compose volume; gateway sendPhoto.
- Links: `memory/phase-plan.md` Phase 4 slice `phase4-generate-image`

## ADR-012: Research UI = Telegram Mini App + bot web_app (not a separate site)

- Status: accepted
- Date: 2026-08-18
- Context: Phase 4 закрыла research API/artifacts/scheduler/images. Нужен клиентский UX для плана/аналитики/галереи. Отдельный marketing website = ещё один deploy/auth/surface вне продукта «Telegram AI».
- Decision: Research Client UI живёт в **Telegram Mini App** (вкладка Маркетинг = студия) + bot **InlineKeyboard web_app** / **MenuButtonWebApp** (`TELEGRAM__WEBAPPURL`). Не отдельный публичный сайт. Картинки — только gateway media proxy (`initData` + path guard), не прямой volume и не IG CDN token в клиенте.
- Consequences: Один клиентский surface (bot + Mini App). Additive `GET /v1/research/latest` fields (`analytics`, `posts[]`, `items[]`); `planPreview` остаётся. RAG/ES остаются Phase 7. VK wall — Phase 6.
- Alternatives considered: отдельный SPA/landing; deep-link только без Mini App; отдавать volume paths в браузер.
- Security impact: media proxy требует initData HMAC; path traversal deny; IG token не в URL/Mini App; bot token остаётся в gateway.
- Impl 2026-08-18 (`phase5-research-ui`): studio wwwroot; media endpoint; web_app keyboard + MenuButton hosted service; latest DTO enrichment.
- Links: `memory/phase-plan.md` Phase 5, `memory/integration-contracts.md`

## ADR-013: VK feed source = official API open communities only (no scrape)

- Status: accepted
- Date: 2026-08-18
- Context: Phase 6 Marketing VK Public Research нужен источник чужих открытых пабликов. Instagram Graph (ADR-009) чужие аккаунты не отдаёт. HTML/`m.vk.com`/Apify = scrape, ToS и хрупкость. У VK есть официальный `wall.get` + service token приложения для открытых стен.
- Decision: Единственный источник стены — **официальный VK API** (`utils.resolveScreenName` + `wall.get`) открытых сообществ из **allowlist** (screen_name / owner_id) в research settings. Auth = `VK__SERVICETOKEN` (service key приложения) из env/secret store / AES-GCM. Посты: `text` + photo attachments; closed/Donut → soft skip. Картинки CDN (`*.userapi.com`) → существующий research volume + media proxy (не MinIO). Additive `source=vk` в snapshot; `schemaVersion` не ломаем. Не отдельный `vk-research-api`.
- Consequences: Research расширяется на открытые паблики без scrape. IG path (ADR-009) остаётся. User VK ID OAuth / community token чужих пабликов — non-goals, пока service token достаточен. Impl клиента — slices `phase6-vk-client`+.
- Alternatives considered: HTML scrape / Apify / неофициальный mobile API; сразу user OAuth; отдельный `vk-research-api`.
- Security impact: service token не из chat / Mini App / query; encrypt-at-rest / env; не в logs/git/response; CDN download SSRF allowlist (`*.userapi.com`) ✅; cap постов как у IG (≤50); комментарии/профили авторов не тянем (PII / 152-ФЗ).
- Docs 2026-08-18 (`phase6-vk-docs`): ADR + README/catalog/contracts/security/.env.example. No service code in this slice.
- Client 2026-08-18 (`phase6-vk-client`): `IVkWallClient` / `HttpVkWallClient` / stub+fallback; `EncryptedVkTokenStore`; `VkCdnUrlGuard` (`*.userapi.com`); compose `Vk__ServiceToken`; no artifacts/settings/media persist.
- Artifacts 2026-08-18 (`phase6-vk-artifacts`): `IVkResearchCapture` + `FromVkFetch` → snapshot `source=vk` + plan + marketing episode; inject; cap ≤50; soft-fail; no CDN URLs in payload; no settings/media.
- Settings 2026-08-18 (`phase6-vk-settings`): allowlist `vkCommunities` (screen_name/owner_id) in research_settings; Mini App + `/research vk`; `source=vk` run → CaptureAllowlist; token not from UI/chat.
- Media 2026-08-18 (`phase6-vk-media`): `IVkMediaDownloader` + `VkPhotoStore` → `research-media/*/vk/`; plan MediaPath; gateway media proxy; soft-skip without volume; no durable CDN URLs.
- Hardening 2026-08-18 (`phase6-vk-hardening`): `VkApiHostGuard` (api.vk.com only); `ResearchMediaPathGuard`; caps + non-goals tests; Phase 6 closed.
- Links: `memory/phase-plan.md` Phase 6, `memory/security-baseline.md`, ADR-009, ADR-010
