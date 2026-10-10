using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Starhermit.Platform;

namespace Starhermit.Tests
{
    /// <summary>
    /// The deployment the live contract tests read, named by environment variables.
    /// </summary>
    /// <remarks>
    /// <c>STARHERMIT_TEST_BASE_URL</c> names the API (for example <c>http://127.0.0.1:5099/api/v1/</c>).
    /// <c>STARHERMIT_TEST_MAILBOX</c> names a directory the deployment's outbound mail lands in, one file
    /// per message - <c>tools/smtp_sink.py</c> writes exactly that. With both, the authenticated tests
    /// sign in the way a player does: register a key, redeem the link the deployment emails, accept the
    /// terms. They never mint a token from a signing secret, because a test that forges credentials stops
    /// testing the thing it claims to test.
    /// </remarks>
    public sealed class LiveDeployment
    {
        private LiveDeployment(Uri baseUri, string? mailbox)
        {
            BaseUri = baseUri;
            Mailbox = mailbox;
        }

        /// <summary>The API's versioned base address.</summary>
        public Uri BaseUri { get; }

        /// <summary>Directory the deployment's mail is delivered to, when one is named.</summary>
        public string? Mailbox { get; }

        /// <summary>Reads the deployment from the environment, ignoring the test when it is not named.</summary>
        /// <param name="requireMailbox">True for tests that need to read the deployment's mail.</param>
        /// <returns>The deployment.</returns>
        public static LiveDeployment FromEnvironment(bool requireMailbox)
        {
            var baseUrl = Environment.GetEnvironmentVariable("STARHERMIT_TEST_BASE_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
                Assert.Ignore("Set STARHERMIT_TEST_BASE_URL to run the live contract tests.");

            var mailbox = Environment.GetEnvironmentVariable("STARHERMIT_TEST_MAILBOX");
            if (requireMailbox && string.IsNullOrWhiteSpace(mailbox))
                Assert.Ignore("Set STARHERMIT_TEST_MAILBOX to the directory the deployment's mail lands in (tools/smtp_sink.py) to run the signed-in live tests.");

            return new LiveDeployment(new Uri(baseUrl!, UriKind.Absolute), string.IsNullOrWhiteSpace(mailbox) ? null : mailbox);
        }

        /// <summary>Creates a client for this deployment with the real transport and sockets.</summary>
        /// <param name="sockets">Socket factory, when the test needs to see the handshakes.</param>
        /// <returns>A client with no session.</returns>
        public StarhermitClient CreateClient(IStarhermitSocketFactory? sockets = null) =>
            StarhermitClient.Create(new StarhermitOptions
            {
                ApiBaseUri = BaseUri,
                Transport = new HttpClientTransport(),
                SocketFactory = sockets,
                CallbackDispatcher = ImmediateCallbackDispatcher.Instance,

                // A development deployment is served over plain HTTP; production would refuse this.
                AllowInsecureTransport = !string.Equals(BaseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase),
                RequestTimeout = TimeSpan.FromSeconds(20)
            });

        /// <summary>Waits for the emailed link addressed to <paramref name="email"/> and returns its token.</summary>
        /// <param name="email">The address the link was sent to.</param>
        /// <param name="linkPath">Path the link points at, for example <c>auth/public-key/verify</c>.</param>
        /// <returns>The link's token, unescaped.</returns>
        public async Task<string> WaitForLinkTokenAsync(string email, string linkPath)
        {
            var pattern = new Regex(Regex.Escape(linkPath) + @"\?token=([^\s""'<>&]+)");
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var file in Directory.GetFiles(Mailbox!, "*.txt"))
                {
                    var message = File.ReadAllText(file);
                    if (message.IndexOf(email, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var match = pattern.Match(message);
                    if (match.Success) return Uri.UnescapeDataString(match.Groups[1].Value);
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            throw new TimeoutException($"No mail to {email} carrying a {linkPath} link reached {Mailbox}.");
        }
    }

    /// <summary>An account the live tests created through public-key registration, and a client signed in to it.</summary>
    /// <remarks>
    /// The client holds the session the emailed link issued, so creating an account costs no
    /// public-key sign-in: the deployment allows sixty sign-in requests a minute per address, and a
    /// suite that signed every test in separately would spend them in two runs.
    /// </remarks>
    public sealed class LiveAccount : IDisposable
    {
        private LiveAccount(StarhermitClient client, RsaPssSigner signer, string email, Guid userId)
        {
            Client = client;
            Signer = signer;
            Email = email;
            UserId = userId;
        }

        /// <summary>A client signed in to the account.</summary>
        public StarhermitClient Client { get; }

        /// <summary>The key the account signs in with.</summary>
        public RsaPssSigner Signer { get; }

        /// <summary>The account's address.</summary>
        public string Email { get; }

        /// <summary>The account id.</summary>
        public Guid UserId { get; }

        /// <summary>
        /// Registers a key against a fresh address, redeems the emailed link and accepts the terms in
        /// force - the whole of a player's first sign-in.
        /// </summary>
        /// <param name="deployment">Where to create the account.</param>
        /// <param name="sockets">Socket factory for the account's client, when a test needs to see the handshakes.</param>
        /// <returns>The account, signed in.</returns>
        public static async Task<LiveAccount> RegisterAsync(LiveDeployment deployment, IStarhermitSocketFactory? sockets = null)
        {
            var signer = RsaPssSigner.Create();
            var email = "sdk-live-" + Guid.NewGuid().ToString("N") + "@sdk-live.test";
            var client = deployment.CreateClient(sockets);
            try
            {
                var receipt = await client.Auth.BeginPublicKeyRegistrationAsync(email, signer.KeyType, signer.PublicKeyData);
                Assert.IsTrue(receipt.EmailSent, "the verification mail was not sent: " + receipt.DeferralReason);

                var token = await deployment.WaitForLinkTokenAsync(email, "auth/public-key/verify");
                var verification = await client.Auth.VerifyPublicKeyRegistrationAsync(token);

                var terms = await client.Me.GetTermsAsync();
                await client.Me.AcceptTermsAsync(terms.Hash);
                return new LiveAccount(client, signer, email, verification.UserId);
            }
            catch
            {
                client.Dispose();
                signer.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Client.Dispose();
            Signer.Dispose();
        }
    }

    /// <summary>An RSA-PSS key held in memory, for the live tests' own accounts only.</summary>
    public sealed class RsaPssSigner : IStarhermitSigner, IDisposable
    {
        private readonly RSA _key;

        private RsaPssSigner(RSA key)
        {
            _key = key;
            PublicKeyData = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        }

        /// <summary>Generates a 2048-bit key.</summary>
        /// <returns>The signer.</returns>
        public static RsaPssSigner Create() => new RsaPssSigner(RSA.Create(2048));

        /// <inheritdoc />
        public string KeyType => StarhermitKeyTypes.RsaPss;

        /// <inheritdoc />
        public string PublicKeyData { get; }

        /// <inheritdoc />
        public Task<byte[]> SignAsync(byte[] data, CancellationToken cancellationToken) =>
            Task.FromResult(_key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));

        /// <inheritdoc />
        public void Dispose() => _key.Dispose();
    }

    /// <summary>Real sockets, with every handshake address remembered so a test can inspect it.</summary>
    public sealed class RecordingSocketFactory : IStarhermitSocketFactory
    {
        private readonly ClientWebSocketFactory _inner = new ClientWebSocketFactory();
        private readonly object _gate = new object();
        private readonly List<Uri> _handshakes = new List<Uri>();

        /// <summary>Every address a socket was asked to connect to, in order.</summary>
        public IReadOnlyList<Uri> Handshakes
        {
            get { lock (_gate) return _handshakes.ToArray(); }
        }

        /// <inheritdoc />
        public IStarhermitSocket Create() => new Recording(_inner.Create(), this);

        private void Record(Uri uri)
        {
            lock (_gate) _handshakes.Add(uri);
        }

        private sealed class Recording : IStarhermitSocket
        {
            private readonly IStarhermitSocket _inner;
            private readonly RecordingSocketFactory _owner;

            internal Recording(IStarhermitSocket inner, RecordingSocketFactory owner)
            {
                _inner = inner;
                _owner = owner;
            }

            public StarhermitConnectionState State => _inner.State;

            public Task ConnectAsync(Uri uri, IReadOnlyList<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
            {
                _owner.Record(uri);
                return _inner.ConnectAsync(uri, headers, cancellationToken);
            }

            public Task SendAsync(ArraySegment<byte> payload, bool isText, CancellationToken cancellationToken) =>
                _inner.SendAsync(payload, isText, cancellationToken);

            public Task<StarhermitSocketMessage> ReceiveAsync(CancellationToken cancellationToken) =>
                _inner.ReceiveAsync(cancellationToken);

            public Task CloseAsync(int closeStatus, string? description, CancellationToken cancellationToken) =>
                _inner.CloseAsync(closeStatus, description, cancellationToken);

            public void Dispose() => _inner.Dispose();
        }
    }

    /// <summary>Builds the <c>.tar.gz</c> a game upload carries, without a tar library.</summary>
    public static class Tarball
    {
        /// <summary>Packs regular files into a gzipped ustar archive.</summary>
        /// <param name="files">Archive paths and contents.</param>
        /// <returns>The archive bytes.</returns>
        public static byte[] Gzip(params KeyValuePair<string, byte[]>[] files)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    var header = new byte[512];
                    Field(header, 0, 100, file.Key);
                    Field(header, 100, 8, "0000644");
                    Field(header, 108, 8, "0000000");
                    Field(header, 116, 8, "0000000");
                    Field(header, 124, 12, Convert.ToString(file.Value.Length, 8).PadLeft(11, '0'));
                    Field(header, 136, 12, Convert.ToString(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 8).PadLeft(11, '0'));
                    for (var i = 148; i < 156; i++) header[i] = (byte)' ';
                    header[156] = (byte)'0';
                    Field(header, 257, 6, "ustar");
                    Field(header, 263, 2, "00");

                    var checksum = 0;
                    foreach (var b in header) checksum += b;
                    Field(header, 148, 6, Convert.ToString(checksum, 8).PadLeft(6, '0'));
                    header[154] = 0;

                    gzip.Write(header, 0, header.Length);
                    gzip.Write(file.Value, 0, file.Value.Length);
                    var padding = (512 - file.Value.Length % 512) % 512;
                    gzip.Write(new byte[padding], 0, padding);
                }

                gzip.Write(new byte[1024], 0, 1024);
            }

            return output.ToArray();
        }

        private static void Field(byte[] header, int offset, int length, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, header, offset, Math.Min(bytes.Length, length));
        }
    }
}
