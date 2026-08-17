namespace AssistantApi.Security;

public sealed class EmptyInstagramTokenStore : IInstagramTokenStore
{
    public bool HasToken => false;

    public bool TryGetAccessToken(out string accessToken)
    {
        accessToken = string.Empty;
        return false;
    }
}
