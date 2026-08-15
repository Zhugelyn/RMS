# Telegram AI — Phase 2 Cursor SDK

Три контейнера (Compose):

| Сервис | Порт | Назначение |
| --- | --- | --- |
| `assistant-api` | `5080` | `POST /v1/chat`, harness, encrypt-at-rest key |
| `telegram-gateway` | `5081` | Telegram bot + Mini App |
| `cursor-sdk-bridge` | internal `:8090` | `@cursor/sdk` Agent.create/resume |

Без `CURSOR__APIKEY` chat идёт в **stub fallback**. RAG/ES/MinIO/Директ — следующие фазы.

## Требования

- Docker + Docker Compose v2
- .NET 8 SDK — только для локальных тестов/`dotnet run`
- Telegram Bot Token от [@BotFather](https://t.me/BotFather) — для живых ответов в чат
- Опционально: Cursor API key + master key (≥16) для живого SDK path

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

### Chat со service key → stub-ответ

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

Ожидаемо: `provider: "stub"`, текст вида `[stub/salon] Принял: ...`.

### Mini App proxy

```bash
curl -sS -X POST http://127.0.0.1:5081/api/miniapp/chat \
  -H 'Content-Type: application/json' \
  -d '{"text":"прайс","intent":"marketing","conversationId":"mini-1","userId":"tg-1"}'
```

UI: открой http://127.0.0.1:5081/ — экраны Салон / Маркетинг / Задачи.

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

- assistant-api: health public, chat auth, stub response, reject secrets
- gateway: SecretScanner, update→assistant mapping, Mini App HTML/proxy

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
4. Для webhook (позже/prod): выставь публичный URL на `POST /telegram/webhook`, `TELEGRAM__USEPOLLING=false`, опционально `TELEGRAM__WEBHOOKSECRETTOKEN`.

Mini App: в BotFather привяжи Web App URL на `https://<твой-хост>/` (локально нужен tunnel, например Cloudflare/ngrok).

## Структура

```
src/AssistantApi/          # POST /v1/chat, stub ILlmProvider
src/TelegramGateway/       # bot + wwwroot Mini App
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
- Bot token ≠ service key
- Health без auth; `/v1/chat` только с service key

Подробности: `memory/security-baseline.md`, `memory/phase-plan.md`.
