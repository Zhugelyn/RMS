# Telegram AI — Phase 5 Research Studio + Phase 4 Instagram Research

Три контейнера (Compose) + domain packs на диске + Postgres:

| Сервис | Порт | Назначение |
| --- | --- | --- |
| `assistant-api` | `5080` | `POST /v1/chat`, research APIs, harness, packs, Postgres |
| `telegram-gateway` | `5081` | Telegram bot + Mini App Research Studio |
| `cursor-sdk-bridge` | internal `:8090` | `@cursor/sdk` Agent.create/resume + local pack cwd |
| `postgres` | internal | durable harness + research settings/artifacts |

Без `CURSOR__APIKEY` chat идёт в **stub fallback**.

## Фазы (кратко)

| Phase | Статус | Суть |
| --- | --- | --- |
| 1 Shell | closed | gateway + assistant stub + Mini App |
| 2 Cursor SDK | closed | bridge + classify→agent→verify (persona) |
| 3 Domain packs | functionally closed | packs + affinity + hard verify; Postgres leftover closed in Phase 4 |
| 4 Marketing Instagram Research | closed | Graph API своего аккаунта, 14d scheduler, artifacts, GenerateImage volume |
| **5 Research Client UI** | **open** (research-ui ✅ → next ui-hardening) | Mini App studio + bot web_app (ADR-012); не RAG |
| 6 Knowledge | later | RAG + embeddings + Elasticsearch |
| 7 Files / media | later | MinIO, фото/видео adapters |
| 8 External tools | later | Яндекс Директ и др. |

## Phase 4 — Marketing Instagram Research (план)

Цель: маркетинговый research по Instagram-ленте **своего** аккаунта (бесплатный Instagram Graph API), план на 14 дней, настройки в Mini App и команда бота `/research`. Артефакты (snapshot + plan + episodes) живут в **Postgres assistant-api** — это harness research memory, **не RAG**.

| Что | Как |
| --- | --- |
| Источник ленты | Только **Instagram Graph API** своего аккаунта (`INSTAGRAM__*`). **Apify нет.** |
| Картинки | **Cursor GenerateImage** через local `marketing` pack + volume `research-images` (`RESEARCH__IMAGEVOLUMEPATH`). Cap 14; soft-fail `image-tool-missing`. **Отдельный OpenAI Images — нет.** ✅ |
| Память research | `snapshot` + `plan` + `episodes` в Postgres (owner = assistant-api). Не embeddings, не Elasticsearch |
| Счедулер | Окно **14 дней**; настройки в Mini App + `/research` в боте |
| Postgres | Durable store для harness memory + research settings/artifacts |

### Non-goals Phase 4

- RAG / embeddings / Elasticsearch (→ Phase 6; Phase 5 = Research UI)
- Apify / scrapers чужих аккаунтов / платные crawl-сервисы
- Отдельный OpenAI Images / DALL·E API
- MinIO, Яндекс Директ, Kubernetes prod

См. `memory/phase-plan.md` (slices `phase4-docs` … `phase4-hardening`), ADR-009/010/011.

## Phase 5 — Research Client UI

Mini App **Research Studio** + bot `web_app` (ADR-012). Additive `GET /v1/research/latest`: `analytics`, `posts[]`, `items[]` (+ `planPreview`). Media: `GET /api/miniapp/research/media` (initData + path guard). Env: `TELEGRAM__WEBAPPURL=https://…` для MenuButton / inline «Открыть студию».

### Non-goals Phase 5

- RAG / ES (→ 6), MinIO (→ 7), Директ (→ 8), Apify, OpenAI Images, отдельный сайт

## Требования

- Docker + Docker Compose v2
- .NET 8 SDK — только для локальных тестов/`dotnet run`
- Telegram Bot Token от [@BotFather](https://t.me/BotFather) — для живых ответов в чат
- Опционально: Cursor API key + master key (≥16) для живого SDK path
- Phase 4 (после impl slices): Postgres + Instagram Graph API token своего аккаунта

## Быстрый старт (Docker)

```bash
cp .env.example .env
```

Заполни `.env` (файл в git не коммитится):

```env
TELEGRAM__BOTTOKEN=<токен от BotFather>
TELEGRAM__WEBHOOKSECRETTOKEN=          # опционально, для webhook
TELEGRAM__USEPOLLING=true              # local/dev: long polling
ASSISTANT__SERVICEKEY=<случайная строка >= 16 символов>
CURSOR__APIKEY=                        # опционально
CURSOR__MASTERKEY=                     # обязателен, если ApiKey задан
CURSOR__MODEL=composer-2.5

# Phase 4 (placeholders; wiring в следующих slices)
# POSTGRES__CONNECTIONSTRING=
# INSTAGRAM__ACCESSTOKEN=
# INSTAGRAM__BUSINESSACCOUNTID=
# RESEARCH__SCHEDULEDAYS=14
```

Подъём:

```bash
# обычный хост
docker compose up --build

# nested/CI Docker (включает ip_forward для bridge DNS)
./scripts/compose-up.sh
```

Проверка, что живы:

```bash
curl -sS http://127.0.0.1:5080/health/ready
curl -sS http://127.0.0.1:5081/health/ready
```

Оба должны вернуть `Healthy`.

Остановка:

```bash
docker compose down
```

## Что куда ходит

```
Telegram update ──► telegram-gateway ──X-Service-Key──► assistant-api
Mini App UI     ──► gateway /api/miniapp/chat ─────────► assistant-api
```

- Bot token живёт **только** в gateway.
- Service key — inter-service auth (`X-Service-Key`).
- Cursor API key из чата/Mini App **не принимается**.
- Instagram Graph token (Phase 4) — только в assistant-api / secret store, не в Telegram.
- Mini App не содержит секретов; ключ на сервере gateway.

## Ручные проверки API

### Health

```bash
curl -i http://127.0.0.1:5080/health/live
curl -i http://127.0.0.1:5080/health/ready
curl -i http://127.0.0.1:5081/health/live
curl -i http://127.0.0.1:5081/health/ready
```

### Chat без ключа → 401

```bash
curl -i -X POST http://127.0.0.1:5080/v1/chat \
  -H 'Content-Type: application/json' \
  -d '{
    "schemaVersion": 1,
    "conversationId": "c1",
    "userId": "u1",
    "text": "привет",
    "traceId": "t1",
    "intent": "salon"
  }'
```

### Chat со service key → stub (без CURSOR__APIKEY)

Подставь тот же ключ, что в `.env` (`ASSISTANT__SERVICEKEY`):

```bash
export SERVICE_KEY='...'

curl -sS -X POST http://127.0.0.1:5080/v1/chat \
  -H 'Content-Type: application/json' \
  -H "X-Service-Key: $SERVICE_KEY" \
  -d '{
    "schemaVersion": 1,
    "conversationId": "c1",
    "userId": "u1",
    "text": "привет",
    "traceId": "t1",
    "intent": "salon"
  }'
```

Ожидаемо без Cursor key: `provider: "stub"`. С валидным `CURSOR__APIKEY`+`CURSOR__MASTERKEY`: `provider: "cursor-sdk"` и `agentId` для resume.

### Mini App proxy

```bash
curl -sS -X POST http://127.0.0.1:5081/api/miniapp/chat \
  -H 'Content-Type: application/json' \
  -d '{"text":"прайс","intent":"marketing","conversationId":"mini-1","userId":"tg-1"}'
```

UI: открой http://127.0.0.1:5081/ — Салон / Маркетинг / Задачи.  
Вкладка **Маркетинг** → блок Instagram Research (enabled, @handle, cadence=14, timezone, last/next, lastError, plan preview, Save / Run now). IG token в UI нет.

### Research API (через gateway proxy)

```bash
# settings (userId обязателен tg-*)
curl -sS 'http://127.0.0.1:5081/api/miniapp/research/settings?userId=tg-1'
curl -sS -X PUT http://127.0.0.1:5081/api/miniapp/research/settings \
  -H 'Content-Type: application/json' \
  -d '{"userId":"tg-1","enabled":true,"instagramHandle":"@mybrand","cadenceDays":14,"timezone":"Europe/Moscow","notifyChatId":"1"}'

curl -sS -X POST http://127.0.0.1:5081/api/miniapp/research/run \
  -H 'Content-Type: application/json' \
  -d '{"userId":"tg-1","notifyChatId":"1"}'

curl -sS 'http://127.0.0.1:5081/api/miniapp/research/latest?userId=tg-1'
```

Assistant (service key): `GET/PUT /v1/research/settings`, `POST /v1/research/run`, `GET /v1/research/latest`.

### Webhook (симуляция update)

```bash
curl -i -X POST http://127.0.0.1:5081/telegram/webhook \
  -H 'Content-Type: application/json' \
  -d '{
    "update_id": 1,
    "message": {
      "message_id": 1,
      "text": "/salon записаться",
      "chat": { "id": 123 },
      "from": { "id": 7 }
    }
  }'
```

С валидным bot token gateway сходит в assistant-api и ответит в Telegram.  
С placeholder-токеном: webhook всё равно `200`, `sendMessage` soft-fail (в логах `status=401`), без утечки токена.

### Секреты из чата → reject

```bash
curl -i -X POST http://127.0.0.1:5081/api/miniapp/chat \
  -H 'Content-Type: application/json' \
  -d '{"text":"CURSOR_API_KEY=sk-abcdefghijklmnopqrstuv","intent":"general"}'
```

Ожидаемо: `400 Secret rejected`.

## Автотесты

```bash
dotnet test
```

Покрытие:

- assistant-api: health, chat, research settings/run/latest (items+analytics), packs/harness/scheduler
- gateway: SecretScanner, `/research` bot web_app, Mini App studio + media proxy + internal notify

## Локальный `dotnet run` (без Docker)

Терминал 1 — assistant-api:

```bash
cd src/AssistantApi
export Assistant__ServiceKey='local-dev-service-key-32chars!!'
dotnet run --urls http://127.0.0.1:5080
```

Терминал 2 — gateway:

```bash
cd src/TelegramGateway
export Telegram__BotToken='<token>'
export Telegram__UsePolling=true
export Assistant__BaseUrl='http://127.0.0.1:5080'
export Assistant__ServiceKey='local-dev-service-key-32chars!!'
dotnet run --urls http://127.0.0.1:5081
```

## Telegram bot

1. Создай бота у BotFather → токен в `.env`.
2. `TELEGRAM__USEPOLLING=true` — для local/dev (default).
3. Напиши боту `/start`, `/salon`, `/marketing`, `/tasks` или обычный текст.
4. `/research` — status; `on` | `off` | `account @handle` | `now` | `plan`. IG token в чат не принимать.
5. Для webhook (позже/prod): выставь публичный URL на `POST /telegram/webhook`, `TELEGRAM__USEPOLLING=false`, опционально `TELEGRAM__WEBHOOKSECRETTOKEN`.

Mini App: в BotFather привяжи Web App URL на `https://<твой-хост>/` (локально нужен tunnel, например Cloudflare/ngrok).

## Структура

```
src/AssistantApi/          # POST /v1/chat, harness, CursorSdkLlmProvider
src/CursorSdkBridge/       # @cursor/sdk HTTP bridge (internal)
src/TelegramGateway/       # bot + wwwroot Mini App
src/AgentPacks/            # salon | marketing | tasks | _router
tests/                     # xUnit + WebApplicationFactory
docker-compose.yml
.env.example
scripts/compose-up.sh
memory/                    # phase-plan, contracts, ADR
```

## Troubleshooting

| Симптом | Что проверить |
| --- | --- |
| gateway не стартует | `.env` есть, `TELEGRAM__BOTTOKEN` и `ASSISTANT__SERVICEKEY` не пустые / длина ≥ требований |
| `assistant-api` 401 | заголовок `X-Service-Key` совпадает с `ASSISTANT__SERVICEKEY` |
| контейнеры не видят друг друга | `./scripts/compose-up.sh` или `sysctl -w net.ipv4.ip_forward=1` |
| бот молчит | валидный token; `docker compose logs telegram-gateway`; polling vs webhook |
| токен в логах | не должен появляться; HttpClient logging для Telegram отключён |

## Security (коротко)

- Не коммить `.env`
- Не слать API keys в чат / Mini App
- Bot token ≠ service key ≠ Instagram token ≠ Cursor API key
- Health без auth; `/v1/chat` только с service key
- Mini App research mutations: Telegram `initData` HMAC (`X-Telegram-Init-Data`); не полагаться только на `tg-*` prefix
- Phase 4: IG token encrypt-at-rest / env only; research artifacts без raw tokens; retention last K snapshots/plans

### Token rotation (не логировать значения)

| Secret | Где | Ротация |
| --- | --- | --- |
| `TELEGRAM__BOTTOKEN` | только gateway | BotFather → новый token → обновить `.env` / secret store → restart `telegram-gateway`. Старый invalidates. |
| `TELEGRAM__WEBHOOKSECRETTOKEN` | gateway webhook header | Сгенерировать новую строку → `setWebhook` secret_token + env → restart. Не путать с bot token. |
| `ASSISTANT__SERVICEKEY` | gateway ↔ assistant-api (`X-Service-Key` / Bearer-like inter-service) | Новое значение ≥16 в обоих сервисах одновременно → restart. Не логировать header. |
| `CURSOR__APIKEY` (+ `CURSOR__MASTERKEY`) | assistant-api (+ bridge) | Новый Cursor key → env; master key только для AES-GCM seal. Без ApiKey = stub. Не из чата. |
| `INSTAGRAM__ACCESSTOKEN` (+ master) | assistant-api | Meta/Graph long-lived refresh → env; encrypt-at-rest на старте. Не из Mini App/chat. |

Правило: secrets не в git, не в OpenAPI examples, не в metrics labels, не в screenshot/логах ошибок Graph/Telegram.

Подробности: `memory/security-baseline.md`, `memory/phase-plan.md`.
