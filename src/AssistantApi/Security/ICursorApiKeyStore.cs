namespace AssistantApi.Security;

public interface ICursorApiKeyStore
{
    bool HasKey { get; }

    /// <summary>Returns decrypted API key for outbound SDK calls. Never log or return to clients.</summary>
    bool TryGetApiKey(out string apiKey);
}
