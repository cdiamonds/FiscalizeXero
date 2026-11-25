using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FiscalizeXero.Api.Security
{
    /// <summary>
    /// Encrypts secrets before they reach the database (Xero refresh/access tokens,
    /// Fiscalize session tokens, device API secrets) and decrypts them on the way back.
    /// </summary>
    public interface ISecretProtector
    {
        string? Protect(string? plaintext);
        string? Unprotect(string? stored);
        bool IsProtected(string? stored);
    }

    /// <summary>
    /// AES-256-GCM with a key derived from <c>AuthSettings:Key</c>, which production already requires to be long and
    /// random and to live only on the server. Deriving from that key means no second secret to manage and no key ring
    /// to persist across deployments; rotating it signs everyone out and makes stored secrets unreadable, so it is
    /// rotated by reconnecting companies, not silently.
    ///
    /// Stored form: <c>enc1:</c> + base64(nonce[12] | tag[16] | ciphertext). A value without the prefix was not written
    /// through this protector (design-time tooling and tests run without one) and is returned as it is.
    /// </summary>
    public sealed class AesGcmSecretProtector : ISecretProtector
    {
        public const string Prefix = "enc1:";
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private readonly byte[] _key;

        public AesGcmSecretProtector(string masterKey)
        {
            if (string.IsNullOrWhiteSpace(masterKey))
                throw new ArgumentException("A master key is required to protect stored secrets.", nameof(masterKey));
            // Domain-separated from the JWT signing use of the same configured value.
            _key = SHA256.HashData(Encoding.UTF8.GetBytes("FiscalizeXero.SecretsAtRest.v1|" + masterKey));
        }

        public bool IsProtected(string? stored) => stored != null && stored.StartsWith(Prefix, StringComparison.Ordinal);

        public string? Protect(string? plaintext)
        {
            if (plaintext == null) return null;
            if (IsProtected(plaintext)) return plaintext; // never double-encrypt

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plain = Encoding.UTF8.GetBytes(plaintext);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(_key, TagSize))
                aes.Encrypt(nonce, plain, cipher, tag);

            var packed = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, packed, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, packed, NonceSize + TagSize, cipher.Length);
            return Prefix + Convert.ToBase64String(packed);
        }

        public string? Unprotect(string? stored)
        {
            if (stored == null) return null;
            if (!IsProtected(stored)) return stored; // legacy plaintext row

            byte[] packed;
            try { packed = Convert.FromBase64String(stored.AsSpan(Prefix.Length).ToString()); }
            catch (FormatException) { throw new CryptographicException("Stored secret is not valid base64."); }
            if (packed.Length < NonceSize + TagSize)
                throw new CryptographicException("Stored secret is too short to be valid.");

            var nonce = packed.AsSpan(0, NonceSize);
            var tag = packed.AsSpan(NonceSize, TagSize);
            var cipher = packed.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(_key, TagSize))
                aes.Decrypt(nonce, cipher, tag, plain); // throws on tampering or a different key
            return Encoding.UTF8.GetString(plain);
        }
    }

    /// <summary>EF Core value converter that applies an <see cref="ISecretProtector"/> to a string column.</summary>
    public sealed class ProtectedStringConverter : ValueConverter<string?, string?>
    {
        public ProtectedStringConverter(ISecretProtector protector)
            : base(v => protector.Protect(v), v => ReadOrNull(protector, v))
        {
        }

        /// <summary>
        /// Called when a stored secret can't be decrypted (the key changed, or the value is damaged). Program.cs points
        /// it at the log. The value is read as missing, so that one connection or session asks to be reconnected or
        /// signed in again, instead of every query that loads the row failing and taking sign-in down with it.
        /// </summary>
        public static Action<Exception>? OnUnreadable { get; set; }

        internal static string? ReadOrNull(ISecretProtector protector, string? stored)
        {
            try
            {
                return protector.Unprotect(stored);
            }
            catch (CryptographicException ex)
            {
                OnUnreadable?.Invoke(ex);
                return null;
            }
        }
    }
}
