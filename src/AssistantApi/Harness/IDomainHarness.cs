using AssistantApi.Contracts;

namespace AssistantApi.Harness;

public interface IDomainHarness
{
    DomainIntent Classify(ChatRequest request);

    string BuildSpecialistPrompt(DomainIntent intent, ChatRequest request);

    HarnessVerifyResult Verify(DomainIntent intent, string specialistOutput);
}

public sealed record HarnessVerifyResult(bool Ok, string Text, string? Reason);
