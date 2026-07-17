using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Infrastructure.Security;

/// <summary>
/// AES-GCM encryption for optional prompt/response content at rest.
/// Key material is stored outside the database at the configured key path.
/// </summary>
public sealed class ContentEncryptionService : IContentEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _keyPath;
    private readonly object _gate = new();
    private byte[]? _key;

    public ContentEncryptionService(IOptions<TrackingOptions> options)
    {
        _keyPath = TrackingOptions.ExpandPath(options.Value.EncryptionKeyPath);
    }

    /// <inheritdoc />
    public bool IsConfigured
    {
        get
        {
            EnsureKeyLoaded();
            return _key is { Length: 32 };
        }
    }

    /// <inheritdoc />
    public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        cancellationToken.ThrowIfCancellationRequested();

        var key = EnsureKeyLoaded();
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, cipher, tag);
        }

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);

        return Task.FromResult(Convert.ToBase64String(payload));
    }

    /// <inheritdoc />
    public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        cancellationToken.ThrowIfCancellationRequested();

        var key = EnsureKeyLoaded();
        var payload = Convert.FromBase64String(ciphertext);
        if (payload.Length < NonceSize + TagSize + 1)
        {
            throw new CryptographicException("Ciphertext payload is truncated.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[cipher.Length];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Decrypt(nonce, cipher, tag, plaintext);
        }

        return Task.FromResult(System.Text.Encoding.UTF8.GetString(plaintext));
    }

    private byte[] EnsureKeyLoaded()
    {
        if (_key is not null)
        {
            return _key;
        }

        lock (_gate)
        {
            if (_key is not null)
            {
                return _key;
            }

            var directory = Path.GetDirectoryName(_keyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_keyPath))
            {
                var existing = File.ReadAllBytes(_keyPath);
                if (existing.Length != 32)
                {
                    throw new CryptographicException($"Encryption key at '{_keyPath}' must be 32 bytes.");
                }

                _key = existing;
                return _key;
            }

            var generated = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(_keyPath, generated);
            try
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    File.SetUnixFileMode(_keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch
            {
                // Best-effort permissions; ignore failures on unsupported platforms.
            }

            _key = generated;
            return _key;
        }
    }
}
