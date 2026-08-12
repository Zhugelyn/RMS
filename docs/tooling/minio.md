# MinIO Tooling

## Checks

- Bucket is private unless explicitly public by ADR.
- Presigned URL TTL is short.
- Upload size limit is enforced before issuing upload intent.
- Object key is generated server-side.
- Metadata row and object state stay consistent.

## Manual Flow Test

1. Request upload intent.
2. Upload file with presigned URL.
3. Confirm metadata status transition.
4. Try unauthorized download.
5. Try expired presigned URL.
6. Try wrong content type or oversized file.

