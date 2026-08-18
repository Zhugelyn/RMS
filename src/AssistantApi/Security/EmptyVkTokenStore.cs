namespace AssistantApi.Security;

public sealed class EmptyVkTokenStore : IVkTokenStore
{
    public bool HasToken => false;

    public bool TryGetServiceToken(out string serviceToken)
    {
        serviceToken = string.Empty;
        return false;
    }
}
