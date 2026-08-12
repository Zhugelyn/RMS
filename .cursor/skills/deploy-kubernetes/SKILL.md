---
name: deploy-kubernetes
description: Reviews and designs Docker, Kubernetes, and Nginx deployment artifacts with probes, resources, config, secrets, ingress TLS, and runtime hardening. Use for deployment, platform, or local stack tasks.
---

# Deploy Kubernetes

## Инструкции

1. Прочитай `agents/platform-agent.md`.
2. Раздели dev, staging и production assumptions.
3. Проверь Docker image, config/secrets, probes, resources, ingress, TLS и graceful shutdown.
4. Для Nginx зафиксируй body limits, timeouts, forwarded headers и TLS behavior.
5. Предложи verification: `docker compose config`, `kubectl diff`, smoke test и health checks.

## Reference

См. `docs/agent-playbooks/deploy-kubernetes.md`.

