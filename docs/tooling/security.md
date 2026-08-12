# Security Tooling

## Scans

Use project-approved tools when available. Reasonable defaults:

- Secret scanning before commits.
- Dependency vulnerability scanning for NuGet and container images.
- Dockerfile/container scanning.
- Kubernetes manifest policy checks.

## Manual Review

- Public endpoints have auth policy.
- Object-level authorization exists where data belongs to a user/tenant.
- Logs do not contain secrets or raw PII.
- Error responses do not leak stack traces.
- Upload/download flows defend against SSRF, path traversal, content spoofing and oversized payloads.
- Containers run non-root and manifests include securityContext.

