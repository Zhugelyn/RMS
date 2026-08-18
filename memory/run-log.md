# Run Log

Короткий журнал automation/agent прогонов. Не писать сюда код, секреты, полные промпты.

## Template

```markdown
## Run YYYY-MM-DD

- Trigger:
- Slice:
- Status: passed | blocked | partial
- Checks:
- Memory updated:
- Next slice:
- Blocker:
```

## Runs

## Run 2026-08-18m

- Trigger: webhook (empty payload) → first open slice `phase7-docs`
- Slice: phase7-docs
- Status: passed
- Checks: ADR-014 accepted (RAG ≠ harness ≠ research; kb-salon/kb-marketing; pack MCP retriever; soft-fail); README Phase 7 + non-goals; catalog/contracts/security/.env.example placeholders (`RAG__*`, ES comments); no service code / no ES container; `dotnet test` 171/171 (132 api + 39 gateway); next=`phase7-es-compose`
- Memory updated: architecture-decisions ADR-014, phase-plan checkbox, current-project, catalog, contracts, security-baseline, README, .env.example, telegram-ai rule, run-log
- Next slice: phase7-es-compose
- Blocker: none

## Run 2026-08-18l

- Trigger: chat — explicit next phase after Phase 5/6 closed → open Phase 7
- Slice: plan-only (`phase7-docs` not implemented here)
- Status: passed
- Checks: Phase 7 slices listed; next=`phase7-docs`; MinIO/Direct still gated
- Memory updated: phase-plan, current-project, catalog, README, telegram-ai rule, run-log
- Next slice: phase7-docs
- Blocker: none

## Run 2026-08-18k

- Trigger: webhook (empty payload) → first open slice `phase5-ui-hardening`
- Slice: phase5-ui-hardening
- Status: passed
- Checks: studio polish empty/error/limits/a11y (skip-link, tabs, aria-busy, reduced-motion); VK allowlist ≤10 + Run VK empty guard; posts feed ≤20 + gallery ≤14 + source badges; additive `latest.source`; `Phase5UiHardeningTests`; `dotnet test` 171/171 (132 api + 39 gateway); Phase 5 closed; no RAG/Apify/MinIO/Direct
- Memory updated: phase-plan checkbox + Phase 5 closed, current-project, catalog, contracts, security-baseline, README, run-log
- Next slice: none (Phase 7 RAG — только по явному запросу)
- Blocker: none

## Run 2026-08-18j

- Trigger: chat — Phase 6 closed; lift deferral, kick `phase5-ui-hardening` (not RAG)
- Slice: plan-pointer only
- Status: passed
- Checks: next_slice=`phase5-ui-hardening`; Phase 7 still gated
- Memory updated: current-project, phase-plan, catalog, README, telegram-ai rule, run-log
- Next slice: phase5-ui-hardening
- Blocker: none

## Run 2026-08-18i

- Trigger: webhook (empty payload) → first open slice `phase6-vk-hardening`
- Slice: phase6-vk-hardening
- Status: passed
- Checks: `VkApiHostGuard` (api.vk.com only); `ResearchMediaPathGuard` + plan serialize/ApplyMediaPaths strip CDN/absolute URLs; caps (communities≤10, wall≤50, photos≤14); README token rotation VK; `Phase6HardeningTests` non-goals (no scrape/Apify/user-OAuth/RAG/MinIO/Direct); `dotnet test` 167/167 (132 api + 35 gateway); Phase 6 closed
- Memory updated: phase-plan checkbox + closed, current-project, catalog, contracts, security-baseline, ADR-013 note, README, run-log
- Next slice: none (Phase 7 RAG / deferred `phase5-ui-hardening` — только по явному запросу)
- Blocker: none

## Run 2026-08-18h

- Trigger: webhook (empty payload) → first open slice `phase6-vk-media`
- Slice: phase6-vk-media
- Status: passed
- Checks: `IVkMediaDownloader` + `VkPhotoStore` → volume `research-media/*/vk/`; plan MediaPath; gateway media proxy reuse; no CDN URLs in snapshot/plan JSON; soft-skip without volume; SSRF `*.userapi.com` + size cap; `dotnet test` 160/160 (125 api + 35 gateway); no RAG/Apify/MinIO/Direct/user-OAuth
- Memory updated: phase-plan checkbox, current-project, catalog, contracts, security-baseline, ADR-013 note, README, run-log
- Next slice: phase6-vk-hardening
- Blocker: none

## Run 2026-08-18g

- Trigger: webhook (empty payload) → first open slice `phase6-vk-settings`
- Slice: phase6-vk-settings
- Status: passed
- Checks: research_settings `VkCommunitiesJson` + DTO allowlist screen_name/owner_id; Mini App textarea + Run VK; `/research vk add|remove|list|now`; `POST /v1/research/run` source=vk → CaptureAllowlist; reject VK token from UI/chat; IG path unbroken; `dotnet test` 151/151 (116 api + 35 gateway); no RAG/Apify/MinIO/Direct/user-OAuth/media download
- Memory updated: phase-plan checkbox, current-project, catalog, contracts, security-baseline, ADR-013 note, README, run-log
- Next slice: phase6-vk-media
- Blocker: none

## Run 2026-08-18f

- Trigger: webhook (empty payload) → first open slice `phase6-vk-artifacts`
- Slice: phase6-vk-artifacts
- Status: passed
- Checks: `IVkResearchCapture` / `FromVkFetch` → snapshot `source=vk` + plan + marketing episode; inject marketing / salon isolation; cap ≤50; soft-fail persist; no CDN URLs in payload; no settings/Mini App/media download; `dotnet test` 135/135 (103 api + 32 gateway); no RAG/Apify/MinIO/Direct/user-OAuth
- Memory updated: phase-plan checkbox, current-project, catalog, contracts, security-baseline, ADR-013 note, README, run-log
- Next slice: phase6-vk-settings
- Blocker: none

## Run 2026-08-18e

- Trigger: webhook (empty payload) → first open slice `phase6-vk-client`
- Slice: phase6-vk-client
- Status: passed
- Checks: `IVkWallClient`/`HttpVkWallClient`/`Stub`/`Fallback`; `EncryptedVkTokenStore` AES-GCM; `utils.resolveScreenName` + `wall.get`; Donut soft-skip; CDN SSRF `*.userapi.com` no IP; chat/gateway reject `VK__SERVICETOKEN`; no artifacts/settings/media persist; `dotnet test` 130/130 (98 api + 32 gateway); no RAG/Apify/MinIO/Direct/user-OAuth
- Memory updated: phase-plan checkbox, current-project, catalog, contracts, security-baseline, ADR-013 note, README, .env.example, run-log
- Next slice: phase6-vk-artifacts
- Blocker: none

## Run 2026-08-18d

- Trigger: webhook (empty payload) → first open slice `phase6-vk-docs`
- Slice: phase6-vk-docs
- Status: passed
- Checks: ADR-013 accepted; README Phase 6 table + non-goals; catalog/contracts/security/.env.example `VK__SERVICETOKEN=`; no service code; `dotnet test` 105/105 (75 api + 30 gateway); next=`phase6-vk-client`
- Memory updated: architecture-decisions ADR-013, phase-plan checkbox, current-project, catalog, contracts, security-baseline, README, .env.example, run-log
- Next slice: phase6-vk-client
- Blocker: none

## Run 2026-08-18c

- Trigger: chat — add Phase 6 VK plan + webhook kick `phase6-vk-docs`
- Slice: plan-only (no VK client code)
- Status: passed
- Checks: Phase 6 inserted (wall.get / service token / open publics / no scrape); RAG→7 Files→8 Direct→9; `phase5-ui-hardening` deferred; next=`phase6-vk-docs`
- Memory updated: phase-plan, current-project, catalog, README, security open-risks, telegram-ai rule, run-log; ADR-013 left for docs slice
- Next slice: phase6-vk-docs
- Blocker: none

## Run 2026-08-18b

- Trigger: webhook slice=phase5-research-ui
- Slice: phase5-research-ui
- Status: passed
- Checks: latest DTO analytics+posts+items (planPreview kept); Mini App Research Studio (not pre); media proxy 401 without initData + traversal deny; imageUrl=gateway proxy; bot web_app «Открыть студию» + MenuButton when TELEGRAM__WEBAPPURL; /research plan ≤3 photos; ADR-012; `dotnet test` 105/105 (75 api + 30 gateway); no RAG/Apify/OpenAI Images/MinIO
- Memory updated: phase-plan Phase 5 UI + RAG→6/Files→7/Direct→8; current-project; ADR-012; catalog; contracts; security; README; run-log; .env.example
- Next slice: phase5-ui-hardening
- Blocker: none

## Run 2026-08-18

- Trigger: webhook slice=phase4-hardening
- Slice: phase4-hardening
- Status: passed
- Checks: Mini App research mutations require Telegram initData HMAC (`X-Telegram-Init-Data` / body); userId must match initData; Graph fetch hard cap 50; image cap 14; volume path traversal guards + tests; snapshot/plan payload size + posts cap; retention SnapshotCap=5 PlanCap=3 explicit; README token rotation (IG+Cursor+webhook+service key); non-goals guard test (no Apify/OpenAI Images/ES/embeddings/MinIO/Direct wiring); `dotnet test` 99/99 (73 api + 26 gateway); compose YAML OK (docker CLI absent); Phase 4 closed; Phase 5 not started
- Memory updated: current-project, phase-plan, catalog, contracts, security-baseline, run-log, README, .env.example
- Next slice: none (Phase 5 Knowledge/RAG — только по явному запросу)
- Blocker: none

## Run 2026-08-17g

- Trigger: webhook slice=phase4-generate-image
- Slice: phase4-generate-image
- Status: passed
- Checks: local marketing-pack GenerateImage + volume `research-media/<runId>`; bridge `/v1/run` collectImages→images[] cap 14; soft-fail `image-tool-missing`; mediaPath on plan; gateway sendPhoto (text survives 0 photos); compose volume RESEARCH__IMAGEVOLUMEPATH; no Cursor key → skip images; `dotnet test` 86/86 (68 api + 18 gateway); no OpenAI Images/RAG/Apify
- Memory updated: current-project, phase-plan, catalog, contracts, security-baseline, run-log, .env.example
- Next slice: `phase4-hardening` (не стартовать в этом run)
- Blocker: none

## Run 2026-08-17f

- Trigger: webhook slice=phase4-miniapp-research
- Slice: phase4-miniapp-research
- Status: passed
- Checks: Mini App marketing research settings (no IG token); bot `/research on|off|account|now|plan|status`; gateway proxy GET/PUT settings, POST run, GET latest; `GatewayResearchNotifyHook` → `/internal/notify` (не NoOp); mutations require `tg-*`; `dotnet test` 79/79 (62 api + 17 gateway); no GenerateImage/RAG/Apify
- Memory updated: current-project, phase-plan, catalog, contracts, security-baseline, run-log, README
- Next slice: `phase4-generate-image` (не стартовать в этом run)
- Blocker: none

## Run 2026-08-17e

- Trigger: webhook slice=phase4-scheduler
- Slice: phase4-scheduler
- Status: passed
- Checks: BackgroundService 14d cadence from research_settings; ListDue; LastError; idempotent userId+period (research_schedule_runs); nextRunAt/lastRunAt after success; Graph fail → lastError, host alive; no token/disabled → no-op; NoOp notify hook; `dotnet test` 68/68 (57 api + 11 gateway); no Mini App /research UI, GenerateImage, Hangfire, RAG/Apify
- Memory updated: current-project, phase-plan, catalog, contracts, security-baseline, run-log
- Next slice: `phase4-miniapp-research` (не стартовать в этом run)
- Blocker: none

## Run 2026-08-17d

- Trigger: webhook slice=phase4-research-artifacts
- Slice: phase4-research-artifacts
- Status: passed
- Checks: snapshot+14-day plan persist (Postgres/in-mem, cap last K, no embeddings/ES); Graph fetch→capture; marketing episode; marketing inject latest snapshot+plan; salon isolation; soft-fail persist/inject не валит chat; `dotnet test` 61/61 (50 api + 11 gateway); no scheduler/Mini App research/GenerateImage/Apify/RAG
- Memory updated: current-project, phase-plan, catalog, contracts, security-baseline, ADR-010 note, run-log
- Next slice: `phase4-scheduler` (не стартовать в этом run)
- Blocker: none

## Run 2026-08-17c

- Trigger: webhook slice=phase4-ig-graph
- Slice: phase4-ig-graph
- Status: passed
- Checks: Graph client own account only (no Apify); AES-GCM token store; media mapper+insights fixtures; SSRF CDN allowlist; stub skip without token; chat/gateway reject IG token; `dotnet test` 53/53; compose YAML OK (docker CLI absent in env); no scheduler/Mini App research/GenerateImage/RAG/artifacts persist
- Memory updated: current-project, phase-plan, catalog, contracts, security-baseline, run-log
- Next slice: `phase4-research-artifacts` (не стартовать в этом run)
- Blocker: none

## Run 2026-08-17b

- Trigger: webhook slice=phase4-postgres-settings
- Slice: phase4-postgres-settings
- Status: passed
- Checks: postgres in compose; EF migrations user_profiles/harness_episodes/research_settings (+ snapshot/plan stubs); PostgresHarnessMemoryStore when ConnectionStrings:AssistantDb set; in-process fallback without CS; ready fails if DB down; domain isolation tests; `dotnet test` 34/34; `docker compose config` OK; no Graph/Apify/Mini App research/scheduler/GenerateImage
- Memory updated: current-project, phase-plan, catalog, contracts, security-baseline, ADR-008 note, run-log
- Next slice: `phase4-ig-graph` (не стартовать в этом run)
- Blocker: none

## Run 2026-08-17

- Trigger: webhook slice=phase4-docs
- Slice: phase4-docs
- Status: passed
- Checks: README Phase 4 + non-goals; phase-plan Phase 4 slices + RAG→5; ADR-009/010/011; catalog/contracts/security/.env.example; `dotnet test` 29/29 (docs-only, no feature code)
- Memory updated: current-project, phase-plan, architecture-decisions, service-catalog, integration-contracts, security-baseline, run-log
- Next slice: `phase4-postgres-settings` (не стартовать в этом run)
- Blocker: none

## Run 2026-08-15e

- Trigger: user follow-up — continue Phase 3 live harness + Babor/domain specs
- Slice: phase3-bridge-pack-runtime + router-affinity + verify-isolation + harness-memory (batched by user request)
- Status: passed (functional); leftover Postgres durable memory
- Checks: `dotnet test` 29/29; pack local bridge runtime; affinity per domain; hard verify drift/secrets; memory isolation; `/v1/chat` + `domainPack`; stub fallback
- Memory updated: phase-plan Phase 3 checkboxes; current-project; contracts/catalog/security; run-log; packs AGENTS for Babor/market/tasks
- Next slice: `phase3-postgres-memory` (optional harden) or Phase 4 Knowledge when explicitly started
- Blocker: none

## Run 2026-08-15d

- Trigger: webhook slice=phase3-pack-layout
- Slice: phase3-pack-layout
- Status: passed
- Checks: packs salon/marketing/tasks/_router + pack.schema.json; PackCatalog load/validate; `_router` answersUser=false resumePolicy=none; mcp allowlist empty; `dotnet test` 26/26; `/v1/chat` Phase 2 path unchanged (no DomainHarness/bridge chat wiring)
- Memory updated: phase-plan slice1 + catalog checkbox; current-project Phase 3; catalog/contracts/security; run-log
- Next slice: `phase3-bridge-pack-runtime`
- Blocker: none (docker CLI absent in this env; compose build context updated for packs)

## Run 2026-08-15c

- Trigger: user — harness agents must remember user facts + task→result
- Slice: phase3-harness-memory (plan only, не impl)
- Status: passed (planning)
- Checks: ADR-008; memory ≠ SDK resume ≠ RAG; isolation per domain
- Memory updated: phase-plan Phase 3 memory + slice 5; ADR-008; contracts; catalog; security-baseline; current-project; run-log
- Next slice: `phase3-pack-layout` (не стартовать без явного запуска)
- Blocker: none

## Run 2026-08-15b

- Trigger: user — Phase 2 prompt-switch недостаточно; нужны specialist packs (AGENTS.md/skills/MCP)
- Slice: phase3-domain-agent-packs (plan only, не impl)
- Status: passed (planning)
- Checks: в старом плане packs не было (Phase 2 notes врали «harness под домены»; Phase 3 был RAG)
- Memory updated: phase-plan Phase 3 Domain agent packs + сдвиг Knowledge/Files/Tools на 4/5/6; ADR-007; current-project; catalog; contracts; run-log
- Next slice: `phase3-pack-layout` (не стартовать без явного запуска)
- Blocker: none

## Run 2026-08-15

- Trigger: webhook slice=phase2-cursor-sdk
- Slice: phase2-cursor-sdk
- Status: passed
- Checks: `dotnet test` 21/21; `docker compose up` healthy без CURSOR key (bridge+assistant+gateway); `/v1/chat` → stub fallback; AES-GCM key store + harness classify/resume unit tests; no key in response/logs
- Memory updated: phase-plan Phase 2 checkboxes, current-project, catalog, contracts, security-baseline, ADR-006, run-log
- Next slice: phase3-knowledge (не стартовать без явного slice)
- Blocker: none (живой Cursor SDK path требует `CURSOR__APIKEY` + `CURSOR__MASTERKEY`)

## Run 2026-08-13

- Trigger: webhook slice=phase1-shell
- Slice: phase1-shell
- Status: passed
- Checks: `dotnet test` 12/12; `docker compose up` healthy (assistant-api:5080, telegram-gateway:5081); `POST /v1/chat` + health; Mini App intents; webhook→assistant stub; service-key auth; no secrets in git/logs
- Memory updated: phase-plan checkboxes, service-catalog, integration-contracts, security-baseline, run-log
- Next slice: phase2-cursor-sdk (не стартовать без явного запуска Phase 2)
- Blocker: none (для реального Telegram reply нужен валидный `TELEGRAM__BOTTOKEN` в `.env`)
