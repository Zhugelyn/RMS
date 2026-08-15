using AssistantApi.Contracts;

namespace AssistantApi.Providers;

/// <summary>Routes to Cursor SDK when key is sealed; otherwise stub fallback.</summary>
public sealed class FallbackLlmProvider : ILlmProvider
{
    private readonly CursorSdkLlmProvider _cursor;
    private readonly StubLlmProvider _stub;
    private readonly ILogger<FallbackLlmProvider> _logger;

    public FallbackLlmProvider(
        CursorSdkLlmProvider cursor,
        StubLlmProvider stub,
        ILogger<FallbackLlmProvider> logger)
    {
        _cursor = cursor;
        _stub = stub;
        _logger = logger;
    }

    public string Name => _cursor.CanHandle ? _cursor.Name : _stub.Name;

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (!_cursor.CanHandle)
        {
            _logger.LogInformation("LLM fallback to stub (no Cursor API key)");
            return await _stub.CompleteAsync(request, cancellationToken);
        }

        try
        {
            return await _cursor.CompleteAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cursor SDK failed; falling back to stub");
            return await _stub.CompleteAsync(request, cancellationToken);
        }
    }
}
