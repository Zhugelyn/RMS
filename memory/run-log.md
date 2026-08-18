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

## Run 2026-08-18d

- Trigger: webhook (empty payload) → first open slice `phase6-vk-docs`
- Slice: phase6-vk-docs
- Status: passed
- Checks: ADR-013 accepted; README Phase 6 table + non-goals; catalog/contracts/security/.env.example `VK__SERVICETOKEN=`; no service code; `dotnet test` green (docs-only); next=`phase6-vk-client`
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
