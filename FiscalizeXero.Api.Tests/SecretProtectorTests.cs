using System.Security.Cryptography;
using FiscalizeXero.Api.Security;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class SecretProtectorTests
    {
        private static readonly AesGcmSecretProtector Protector = new("a-long-random-master-key-of-at-least-32-bytes!!");

        [Fact]
        public void Roundtrips_and_never_stores_plaintext()
        {
            const string secret = "AB11701234567890abcdefRefreshToken==";
            var stored = Protector.Protect(secret)!;

            Assert.StartsWith("enc1:", stored);
            Assert.DoesNotContain(secret, stored);
            Assert.Equal(secret, Protector.Unprotect(stored));
        }

        [Fact]
        public void Same_plaintext_encrypts_differently_each_time()
        {
            var a = Protector.Protect("same");
            var b = Protector.Protect("same");
            Assert.NotEqual(a, b);
            Assert.Equal("same", Protector.Unprotect(a));
            Assert.Equal("same", Protector.Unprotect(b));
        }

        [Fact]
        public void Legacy_plaintext_rows_read_through_unchanged()
        {
            Assert.Equal("plain-old-token", Protector.Unprotect("plain-old-token"));
            Assert.False(Protector.IsProtected("plain-old-token"));
            Assert.Null(Protector.Unprotect(null));
            Assert.Null(Protector.Protect(null));
            Assert.Equal("", Protector.Unprotect(Protector.Protect("")));
        }

        [Fact]
        public void Does_not_double_encrypt()
        {
            var once = Protector.Protect("x")!;
            Assert.Equal(once, Protector.Protect(once));
        }

        [Fact]
        public void Tampering_or_a_different_key_is_detected()
        {
            var stored = Protector.Protect("secret")!;
            var tampered = stored[..^2] + (stored[^2] == 'A' ? "BB" : "AA");
            Assert.ThrowsAny<CryptographicException>(() => Protector.Unprotect(tampered));

            var other = new AesGcmSecretProtector("a-different-master-key-that-is-also-long-enough");
            Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(stored));
        }

        [Fact]
        public void Rejects_an_empty_master_key()
        {
            Assert.Throws<ArgumentException>(() => new AesGcmSecretProtector(" "));
        }
    }
}
