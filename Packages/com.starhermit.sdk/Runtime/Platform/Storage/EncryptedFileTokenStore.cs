using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit.Platform
{
    /// <summary>
    /// Persists the session to a file, encrypted with a key the application supplies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the opt-in fallback for platforms with no secure store, and it is described honestly:
    /// it stops the token pair being readable in a text editor, and it does not stop anyone who can
    /// read the process memory or extract a key that shipped inside the build. Where the platform has
    /// a keychain, a console secure store, or an OS credential vault, inject that instead.
    /// </para>
    /// <para>
    /// The SDK never generates or manages the key. Supplying one is a deliberate act by the
    /// application, which is what keeps the decision - and the responsibility - where it belongs.
    /// </para>
    /// <para>
    /// Content is encrypted with AES-CBC and authenticated with HMAC-SHA256 over the ciphertext, so a
    /// tampered file is rejected rather than decrypted into nonsense. Writes are atomic: a crash
    /// mid-save leaves the previous pair intact rather than a truncated one.
    /// </para>
    /// </remarks>
    public sealed class EncryptedFileTokenStore : IStarhermitTokenStore
    {
        private const int KeyLength = 32;
        private const int IvLength = 16;
        private const int MacLength = 32;

        private readonly string _path;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _signingKey;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>Creates the store.</summary>
        /// <param name="filePath">Where to write the encrypted session.</param>
        /// <param name="key">
        /// A 32-byte key the application owns. Deriving it from something the platform protects is
        /// the point; hard-coding it in the build is not.
        /// </param>
        public EncryptedFileTokenStore(string filePath, byte[] key)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A file path is required.", nameof(filePath));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != KeyLength)
                throw new ArgumentException($"The key must be exactly {KeyLength} bytes.", nameof(key));

            _path = filePath;
            _encryptionKey = Derive(key, "starhermit-encryption");
            _signingKey = Derive(key, "starhermit-signing");
        }

        /// <inheritdoc />
        public async Task<StarhermitStoredSession?> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_path)) return null;

                var stored = File.ReadAllBytes(_path);
                if (stored.Length <= IvLength + MacLength) return null;

                var iv = new byte[IvLength];
                Buffer.BlockCopy(stored, 0, iv, 0, IvLength);

                var cipherLength = stored.Length - IvLength - MacLength;
                var cipher = new byte[cipherLength];
                Buffer.BlockCopy(stored, IvLength, cipher, 0, cipherLength);

                var mac = new byte[MacLength];
                Buffer.BlockCopy(stored, IvLength + cipherLength, mac, 0, MacLength);

                using var hmac = new HMACSHA256(_signingKey);
                var expected = hmac.ComputeHash(Combine(iv, cipher));
                if (!FixedTimeEquals(expected, mac))
                {
                    // Tampered, or written with a different key. Either way it is not a session this
                    // application stored, and decrypting it would be inventing one.
                    return null;
                }

                using var aes = CreateAes(iv);
                using var decryptor = aes.CreateDecryptor();
                var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                return StarhermitStoredSession.FromJson(Encoding.UTF8.GetString(plain));
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <inheritdoc />
        public async Task SaveAsync(StarhermitStoredSession session, CancellationToken cancellationToken = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var iv = new byte[IvLength];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(iv);

                using var aes = CreateAes(iv);
                using var encryptor = aes.CreateEncryptor();
                var plain = Encoding.UTF8.GetBytes(session.ToJson());
                var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

                using var hmac = new HMACSHA256(_signingKey);
                var mac = hmac.ComputeHash(Combine(iv, cipher));

                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory!);

                // Atomic: the rotated pair either replaces the old one whole, or not at all.
                var temporary = _path + ".partial";
                File.WriteAllBytes(temporary, Combine(iv, cipher, mac));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temporary, _path);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <inheritdoc />
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (IOException)
            {
                // The session is already unusable to this process; a locked file is not worth throwing.
            }
            finally
            {
                _gate.Release();
            }
        }

        private Aes CreateAes(byte[] iv)
        {
            var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        private static byte[] Derive(byte[] key, string label)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(label));
        }

        private static byte[] Combine(params byte[][] parts)
        {
            var length = 0;
            foreach (var part in parts) length += part.Length;

            var result = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            var difference = 0;
            for (var i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
    }
}
