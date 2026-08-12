---
name: build-dotnet-service
description: Builds and modifies ASP.NET Core microservices with APIs, handlers, validation, configuration, health checks, tests, and integration boundaries. Use when working on .NET service code or service scaffolding decisions.
---

# Build Dotnet Service

## Инструкции

1. Прочитай `agents/dotnet-service-agent.md` и релевантные contracts из `memory/integration-contracts.md`.
2. Уточни endpoint/use case, auth policy, data access, external dependencies и failure modes.
3. Используй DTO contracts, validation, ProblemDetails, typed options, cancellation tokens и structured logging.
4. Если есть DB/broker/MinIO dependency, добавь health check и integration test plan.
5. Проведи security pass перед итогом.

## Reference

См. `docs/agent-playbooks/build-dotnet-service.md`.

