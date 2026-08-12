# Dotnet Tooling

## Build and Test

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
```

## EF Core

```powershell
dotnet ef migrations add <Name> --project <InfrastructureProject> --startup-project <ApiProject>
dotnet ef database update --project <InfrastructureProject> --startup-project <ApiProject>
dotnet ef migrations script --idempotent
```

## What To Check

- Build warnings that hide nullable or async mistakes.
- Tests for validation, authorization and failure paths.
- Migrations are deterministic and reviewable.
- Health checks exist for PostgreSQL, brokers and MinIO when used.

