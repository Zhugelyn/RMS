# Implement Minio Files Playbook

## Preferred Flow

1. Client requests upload intent from application service.
2. Service checks authz, size, content policy and creates metadata row.
3. Service returns short-lived presigned URL.
4. Client uploads directly to MinIO.
5. Service receives confirmation or polls metadata.
6. Scanner validates content before file becomes active.

## Metadata

- File id.
- Owner/tenant.
- Bucket and object key.
- Original filename.
- Declared and detected content type.
- Size.
- Status: pending, uploaded, scanning, active, rejected, deleted.
- Retention/lifecycle.

## Security

- Private bucket.
- Object key is random and not user controlled.
- TTL for presigned URLs.
- Max upload size.
- Extension/MIME allowlist where possible.
- Malware/content scanning hook.

