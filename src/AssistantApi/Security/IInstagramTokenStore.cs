namespace AssistantApi.Security;

public interface IInstagramTokenStore
{
    bool HasToken { get; }

    /// <summary>Decrypted Graph access token for outbound calls. Never log or return to clients.</summary>
    bool TryGetAccessToken(out string accessToken);
}
