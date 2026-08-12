# Deploy Kubernetes Playbook

## Docker

- Multi-stage build for .NET.
- Non-root user.
- Minimal runtime image.
- No secrets in image layers.
- Health endpoint exposed.

## Kubernetes

- Deployment has probes, resources and securityContext.
- Config is split: ConfigMap for ordinary settings, Secret/external provider for sensitive values.
- Service exposes only required port.
- Ingress has TLS, body limits and timeouts.
- Pod supports graceful shutdown.

## Nginx

- Preserve `X-Forwarded-*` headers correctly.
- Set upload body limits per endpoint/file use case.
- Configure upstream timeouts deliberately.
- Avoid exposing internal services directly.

## Verification

```powershell
docker compose config
kubectl diff --server-side -f k8s/
kubectl rollout status deployment/<name>
kubectl logs deployment/<name>
```

