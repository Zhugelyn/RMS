# Project Principles

## Язык и стиль

- По умолчанию все агенты отвечают на русском.
- Ответы должны быть конкретными: код, контракты, команды, проверки, риски.
- Не давать "микросервисную архитектуру" как самоцель; сначала проверять, оправдано ли разделение.

## Архитектурные принципы

- Service boundary строится вокруг business capability и ownership данных.
- Database-per-service по умолчанию.
- Межсервисная коммуникация должна иметь явный contract и owner.
- Eventual consistency между сервисами нормальна; strong consistency держится внутри сервиса.
- Security и observability проектируются в начале, а не прикручиваются в конце.

## Текущий стек

- Backend: .NET / ASP.NET Core.
- Data: PostgreSQL.
- Messaging: RabbitMQ и Kafka.
- Platform: Docker, Kubernetes, Nginx.
- Files: MinIO.
- Current product: Telegram bot + assistant-api shell (`telegram-ai`).
- LLM runtime later: Cursor SDK as `ILlmProvider`, not a hard dependency in Phase 1.

## Memory

- Repo `memory/` — source of truth.
- Automation Memories — pointers only. See `memory/automation-memory-protocol.md`.
- One automation run = one phase-plan slice.

