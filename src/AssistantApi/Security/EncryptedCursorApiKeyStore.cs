using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Security;

/// <summary>
/// Seals Cursor API key at startup with AES-GCM. Plaintext env value is not retained.
/// </summary>
public sealed class EncryptedCursorApiKeyStore : ICursorApiKeyStore
{
    private readonly ISecretProtector _protector;
    private readonly string? _encryptedBlob;
    private readonly object _gate = new();

    public EncryptedCursorApiKeyStore(IOptions<CursorOptions> options, ISecretProtector protector)
    {
        _protector = protector;
        var cfg = options.Value;

        if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            _encryptedBlob = _protector.Protect(cfg.ApiKey.Trim());
            // Scrub plaintext bootstrap from options snapshot so later reads don't leak it.
            cfg.ApiKey = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(cfg.EncryptedApiKey))
        {
            // Validate decryptability eagerly.
            _ = _protector.Unprotect(cfg.EncryptedApiKey.Trim());
            _encryptedBlob = cfg.EncryptedApiKey.Trim();
        }
        else
        {
            _encryptedBlob = null;
        }
    }

    public bool HasKey => _encryptedBlob is not null;

    public bool TryGetApiKey(out string apiKey)
    {
        apiKey = string.Empty;
        if (_encryptedBlob is null)
        {
            return false;
        }

        lock (_gate)
        {
            apiKey = _protector.Unprotect(_encryptedBlob);
            return !string.IsNullOrEmpty(apiKey);
        }
    }
}
