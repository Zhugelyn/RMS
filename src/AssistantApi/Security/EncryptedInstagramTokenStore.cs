using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Security;

/// <summary>
/// Seals Instagram Graph access token at startup with AES-GCM (same pattern as Cursor key).
/// Plaintext env value is not retained.
/// </summary>
public sealed class EncryptedInstagramTokenStore : IInstagramTokenStore
{
    private readonly ISecretProtector _protector;
    private readonly string? _encryptedBlob;
    private readonly object _gate = new();

    public EncryptedInstagramTokenStore(IOptions<InstagramOptions> options, ISecretProtector protector)
    {
        _protector = protector;
        var cfg = options.Value;

        if (!string.IsNullOrWhiteSpace(cfg.AccessToken))
        {
            _encryptedBlob = _protector.Protect(cfg.AccessToken.Trim());
            cfg.AccessToken = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(cfg.EncryptedAccessToken))
        {
            _ = _protector.Unprotect(cfg.EncryptedAccessToken.Trim());
            _encryptedBlob = cfg.EncryptedAccessToken.Trim();
        }
        else
        {
            _encryptedBlob = null;
        }
    }

    public bool HasToken => _encryptedBlob is not null;

    public bool TryGetAccessToken(out string accessToken)
    {
        accessToken = string.Empty;
        if (_encryptedBlob is null)
        {
            return false;
        }

        lock (_gate)
        {
            accessToken = _protector.Unprotect(_encryptedBlob);
            return !string.IsNullOrEmpty(accessToken);
        }
    }
}
