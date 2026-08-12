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

