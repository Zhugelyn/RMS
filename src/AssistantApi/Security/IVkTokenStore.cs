namespace AssistantApi.Security;

public interface IVkTokenStore
{
    bool HasToken { get; }

    /// <summary>Decrypted VK service token for outbound calls. Never log or return to clients.</summary>
    bool TryGetServiceToken(out string serviceToken);
}
