using System.Security.Cryptography;
using System.Text;
using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Security;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private readonly byte[] _key;

    public AesGcmSecretProtector(string masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey) || masterKey.Length < 16)
        {
            throw new InvalidOperationException("Secret master key must be at least 16 characters.");
        }

        _key = DeriveKey(masterKey);
    }

    /// <summary>Cursor-path convenience ctor (existing DI / tests).</summary>
    public AesGcmSecretProtector(IOptions<CursorOptions> options)
        : this(options.Value.MasterKey)
    {
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, cipher, tag);

        // payload = version | nonce | tag | cipher
        var payload = new byte[1 + nonce.Length + tag.Length + cipher.Length];
        payload[0] = 1;
        Buffer.BlockCopy(nonce, 0, payload, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, 1 + nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, 1 + nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedPayload)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedPayload);

        var payload = Convert.FromBase64String(protectedPayload);
        if (payload.Length < 1 + 12 + 16 + 1 || payload[0] != 1)
        {
            throw new CryptographicException("Invalid protected payload.");
        }

        var nonce = payload.AsSpan(1, 12);
        var tag = payload.AsSpan(13, 16);
        var cipher = payload.AsSpan(29);
        var plaintextBytes = new byte[cipher.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plaintextBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private static byte[] DeriveKey(string master)
    {
        try
        {
            var raw = Convert.FromBase64String(master);
            if (raw.Length == 32)
            {
                return raw;
            }
        }
        catch (FormatException)
        {
            // treat as passphrase
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(master));
    }
}
