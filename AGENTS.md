# Microservice Harness Agent

Этот workspace содержит общую Cursor-native инфраструктуру для будущих микросервисных веб-приложений.
Рабочий язык по умолчанию: русский. Английские термины допустимы для стандартных названий технологий, протоколов и паттернов.

## Основной цикл

1. Понять тип задачи: обучение, архитектура, код, данные, messaging, security, platform, files, testing/observability, debug или review.
2. Прочитать релевантную память из `memory/` перед существенными архитектурными или кодовыми решениями.
3. Выбрать agent card из `agents/` и project skill из `.cursor/skills/`.
4. Применить постоянные правила из `.cursor/rules/`.
5. Выполнить задачу с коротким security pass, если затронуты API, данные, события, файлы, secrets, сеть или Kubernetes.
6. После решения предложить обновить память: ADR, service catalog, integration contracts, threat model, known issues или learning progress.

## Routing

| Если задача про | Используй agent | Используй skill |
| --- | --- | --- |
| Границы сервисов, bounded contexts, API contracts, ADR | `agents/architecture-agent.md` | `design-microservice-system` |
| ASP.NET Core, REST/gRPC, validation, errors, OpenAPI | `agents/dotnet-service-agent.md` | `build-dotnet-service` |
| PostgreSQL, EF Core, migrations, consistency, indexes | `agents/postgres-agent.md` | `design-postgres-data` |
| RabbitMQ, Kafka, events, retries, DLQ, outbox/inbox | `agents/messaging-agent.md` | `design-eventing` |
| Threat model, auth, secrets, OWASP, hardening | `agents/security-agent.md` | `secure-microservice` |
| Docker, Kubernetes, Nginx, probes, resources, deploy | `agents/platform-agent.md` | `deploy-kubernetes` |
| MinIO, uploads, downloads, presigned URLs, metadata | `agents/files-agent.md` | `implement-minio-files` |
| Unit/integration/contract/e2e tests, logs, metrics, traces | `agents/testing-observability-agent.md` | `test-observe-microservice` |
| Обучение микросервисам и практика | `agents/learning-mentor-agent.md` | `learn-microservices` |
| Telegram bot / gateway | `agents/telegram-bot-agent.md` | `build-telegram-ai-phase1` |
| AI assistant API, LLM provider, Cursor SDK later | `agents/ai-assistant-agent.md` | `build-telegram-ai-phase1` |
| Довести slice до критериев приемки | `agents/harness-agent.md` | `run-acceptance-loop` |
| Память automation / контроль токенов | `agents/harness-agent.md` | `manage-agent-memory` |

## Orchestration Rules

- Не генерируй микросервис "в вакууме": сначала уточни границы ответственности, данные, входы/выходы, sync/async коммуникацию и failure modes.
- Для .NET сервисов предпочитай явные contracts, typed options, health checks, structured logging, cancellation tokens, ProblemDetails и тестируемые application/use-case handlers.
- Для данных по умолчанию выбирай database-per-service. Shared database допускается только как временная миграционная мера с явным ADR.
- Для событий требуй idempotency key, schema version, correlation id, retry policy, DLQ/parking lot и owner контракта.
- Для файлов через MinIO не проксируй большие файлы через application service без причины; предпочитай presigned URL, отдельную metadata model и проверку типа/размера.
- Для Kubernetes требуй readiness/liveness/startup probes, resource requests/limits, non-root containers, secrets через Secret/external secret provider и TLS на ingress.
- Для безопасности проверяй authn/authz, input validation, secrets, transport security, PII в логах, SSRF через file/url flows, supply chain и container hardening.

## Memory Protocol

Перед работой:

- `memory/current-project.md`, `memory/phase-plan.md`, `memory/run-log.md` — текущий проект и slice.
- `memory/automation-memory-protocol.md` — repo memory vs Automation Memories, token budget.
- `memory/project-principles.md` — общие принципы и предпочтения.
- `memory/service-catalog.md` — существующие сервисы, ownership, зависимости.
- `memory/architecture-decisions.md` — ADR и причины прошлых решений.
- `memory/security-baseline.md` — baseline threat model и обязательные controls.
- `memory/integration-contracts.md` — REST/gRPC/event/file contracts.
- `memory/learning-progress.md` — текущий учебный прогресс.

После работы обнови repo memory. В Cursor Automation Memories пиши только короткие указатели, не простыни.

## Tooling

Смотри `docs/tooling/catalog.md`. Не запускай destructive-команды без явного запроса. Для будущих проектов проверяй команды локально: `dotnet test`, `docker compose config`, `kubectl diff --server-side`, линтеры, security scanners и contract tests.

