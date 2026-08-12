# Platform Tooling

## Docker Compose

```powershell
docker compose config
docker compose up --build
docker compose ps
docker compose logs <service>
```

## Kubernetes

```powershell
kubectl diff --server-side -f k8s/
kubectl apply --server-side -f k8s/
kubectl rollout status deployment/<name>
kubectl get pods,svc,ingress
kubectl describe pod <pod>
kubectl logs deployment/<name>
```

## Nginx

Check:

- TLS termination.
- Forwarded headers.
- Body size limits for uploads.
- Upstream timeouts.
- Internal services not publicly exposed.

