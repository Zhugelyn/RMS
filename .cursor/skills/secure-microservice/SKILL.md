---
name: secure-microservice
description: Performs security review and threat modeling for microservice APIs, data, messaging, files, containers, Kubernetes, and supply chain. Use when security-sensitive code or architecture changes are discussed.
---

# Secure Microservice

## Инструкции

1. Прочитай `agents/security-agent.md` и `memory/security-baseline.md`.
2. Определи assets, trust boundaries, actors, entry points и abuse cases.
3. Проверь authn/authz, validation, secrets, PII, TLS, event replay, file abuse и container/K8s hardening.
4. Дай findings с concrete fixes, не общие советы.
5. Предложи update для `memory/security-baseline.md`, если найден новый устойчивый риск или control.

## Reference

См. `docs/agent-playbooks/secure-microservice.md`.

