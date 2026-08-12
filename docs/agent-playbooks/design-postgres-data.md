# Design Postgres Data Playbook

## Modeling

- Start from service-owned aggregates, not UI screens.
- Keep external IDs explicit; do not depend on another service's private schema.
- Use constraints for invariants the database can enforce.
- Model timestamps, status transitions and audit needs deliberately.

## Transactions

- One service transaction should not require another service database.
- For DB + message publish, use outbox.
- For message consume, use inbox/idempotency.
- For concurrency, choose optimistic token or explicit locks.

## Performance

- List primary query patterns.
- Add indexes for filters, joins inside the service, sorting and uniqueness.
- Watch write amplification from excessive indexes.
- For large read models, consider projections.

## Security

- Classify PII.
- Decide retention and deletion behavior.
- Ensure backups and replicas follow access rules.

