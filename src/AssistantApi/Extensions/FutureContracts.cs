namespace AssistantApi.Extensions;

/// <summary>Extension point for Phase 3 RAG. No implementation in Phase 1.</summary>
public interface IRagRetriever;

/// <summary>Extension point for Phase 4 files. No implementation in Phase 1.</summary>
public interface IFileStore;

/// <summary>Extension point for Phase 5 tools. No implementation in Phase 1.</summary>
public interface IToolProvider;

/// <summary>
/// Placeholder for future encrypt-at-rest of client Cursor API keys.
/// Master key must come from env / secret store, never from chat.
/// </summary>
public interface ISecretProtector
{
    // Intentionally empty in Phase 1 — wiring comes with Cursor SDK.
}
