using AssistantApi.Contracts;

namespace AssistantApi.Providers;

public interface ILlmProvider
{
    string Name { get; }

    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
}
