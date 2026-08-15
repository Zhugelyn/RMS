namespace AssistantApi.Security;

/// <summary>Encrypt-at-rest for client Cursor API keys. Master key from env / secret store only.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedPayload);
}
