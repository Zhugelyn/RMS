using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Security;

/// <summary>
/// Seals VK service token at startup with AES-GCM (same pattern as IG / Cursor key).
/// Plaintext env value is not retained.
/// </summary>
public sealed class EncryptedVkTokenStore : IVkTokenStore
{
    private readonly ISecretProtector _protector;
    private readonly string? _encryptedBlob;
    private readonly object _gate = new();

    public EncryptedVkTokenStore(IOptions<VkOptions> options, ISecretProtector protector)
    {
        _protector = protector;
        var cfg = options.Value;

        if (!string.IsNullOrWhiteSpace(cfg.ServiceToken))
        {
            _encryptedBlob = _protector.Protect(cfg.ServiceToken.Trim());
            cfg.ServiceToken = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(cfg.EncryptedServiceToken))
        {
            _ = _protector.Unprotect(cfg.EncryptedServiceToken.Trim());
            _encryptedBlob = cfg.EncryptedServiceToken.Trim();
        }
        else
        {
            _encryptedBlob = null;
        }
    }

    public bool HasToken => _encryptedBlob is not null;

    public bool TryGetServiceToken(out string serviceToken)
    {
        serviceToken = string.Empty;
        if (_encryptedBlob is null)
        {
            return false;
        }

        lock (_gate)
        {
            serviceToken = _protector.Unprotect(_encryptedBlob);
            return !string.IsNullOrEmpty(serviceToken);
        }
    }
}
