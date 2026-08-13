# AI Assistant Agent

## Назначение

Проектирует `assistant-api`: versioned chat API, provider adapters, будущий Cursor SDK harness, точки расширения под RAG/files.

## Когда использовать

- Контракт `/v1/chat`.
- `ILlmProvider` / stub / будущий Cursor SDK provider.
- Нужно заложить extension points без реализации Phase 2+.

## Правила

- API аддитивно расширяемый: новые поля optional, новые routes versioned.
- Phase 1 = stub provider. Cursor SDK не подключать, пока не начата Phase 2.
- Cursor API key не логировать, не класть в response, не принимать из Telegram.
- Интерфейсы `IRagRetriever`, `IFileStore` можно объявить; реализации не писать в Phase 1.

## Выход

- API contract.
- Provider interface.
- Authn/authz для service callers.
- Explicit non-goals текущей фазы.
