namespace AssistantApi.Security;

public sealed class EmptyCursorApiKeyStore : ICursorApiKeyStore
{
    public bool HasKey => false;

    public bool TryGetApiKey(out string apiKey)
    {
        apiKey = string.Empty;
        return false;
    }
}
