using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Starhermit;
using Starhermit.Platform;

// The SDK, compiled ahead of time and fully trimmed, doing what a player's build does against a live
// deployment: register through the emailed link, sign in with a key, open a ticketed socket, and keep
// a cloud save under version control. Each step either passes or ends the run with a non-zero exit.

var baseUrl = Environment.GetEnvironmentVariable("STARHERMIT_TEST_BASE_URL");
var mailbox = Environment.GetEnvironmentVariable("STARHERMIT_TEST_MAILBOX");
if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(mailbox))
{
    Console.Error.WriteLine("Set STARHERMIT_TEST_BASE_URL and STARHERMIT_TEST_MAILBOX (tools/live-test.sh does).");
    return 2;
}

var baseUri = new Uri(baseUrl, UriKind.Absolute);
StarhermitClient NewClient() => StarhermitClient.Create(new StarhermitOptions
{
    ApiBaseUri = baseUri,
    Transport = new HttpClientTransport(),
    CallbackDispatcher = ImmediateCallbackDispatcher.Instance,
    AllowInsecureTransport = baseUri.Scheme != "https",
    RequestTimeout = TimeSpan.FromSeconds(20),
});

var checks = 0;
void Check(bool condition, string what)
{
    if (!condition) throw new Exception("FAILED: " + what);
    checks++;
    Console.WriteLine("ok   " + what);
}

try
{
    using var key = RSA.Create(2048);
    var signer = new Signer(key);
    var email = "sdk-aot-" + Guid.NewGuid().ToString("N") + "@sdk-live.test";

    using var client = NewClient();
    var receipt = await client.Auth.BeginPublicKeyRegistrationAsync(email, signer.KeyType, signer.PublicKeyData);
    Check(receipt.EmailSent, "registration mails a verification link");
    var token = await WaitForLinkTokenAsync(mailbox!, email, "auth/public-key/verify");
    var verification = await client.Auth.VerifyPublicKeyRegistrationAsync(token);
    Check(verification.UserId != Guid.Empty, "redeeming the link creates the account and a session");
    var terms = await client.Me.GetTermsAsync();
    await client.Me.AcceptTermsAsync(terms.Hash);
    var profile = await client.Me.GetProfileAsync();
    Check(profile.Id == verification.UserId, "the session reads its own profile");

    using (var other = NewClient())
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var session = await other.Auth.SignInWithPublicKeyAsync(signer);
            Check(session.UserId == verification.UserId, $"public-key sign-in {attempt} of 8 verifies");
        }
    }

    using (var chat = client.CreateChatConnection())
    {
        await chat.ConnectAsync();
        Check(chat.State == StarhermitConnectionState.Connected, "the chat socket connects with a connection ticket");
        await chat.CloseAsync();
        await chat.ConnectAsync();
        Check(chat.State == StarhermitConnectionState.Connected, "a reconnect buys and uses a fresh ticket");
        await chat.CloseAsync();
    }

    var saveKey = "sdk-aot-" + Guid.NewGuid().ToString("N");
    var first = new byte[] { 1, 2, 3, 4 };
    var second = new byte[] { 5, 6, 7, 8, 9 };
    var stored = await client.CloudSaves.UploadAsync(saveKey, first, StarhermitSaveCondition.IfNoSaveExists);
    Check(stored.ETag != null, "a cloud save upload returns its version");
    var unchanged = await client.CloudSaves.DownloadIfChangedAsync(saveKey, stored.ETag);
    Check(unchanged.NotModified && unchanged.Archive == null, "download-if-changed answers not modified for the held version");
    await client.CloudSaves.UploadAsync(saveKey, second, StarhermitSaveCondition.IfMatch(stored.ETag!));
    var changed = await client.CloudSaves.DownloadIfChangedAsync(saveKey, stored.ETag);
    Check(!changed.NotModified && changed.Archive != null && changed.Archive.Length == second.Length,
        "download-if-changed transfers a newer version");
    try
    {
        await client.CloudSaves.UploadAsync(saveKey, first, StarhermitSaveCondition.IfMatch(stored.ETag!));
        Check(false, "a write over a replaced version is refused");
    }
    catch (StarhermitPreconditionFailedException)
    {
        Check(true, "a write over a replaced version is refused");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Console.Error.WriteLine($"AOT smoke failed after {checks} check(s).");
    return 1;
}

Console.WriteLine($"AOT smoke: {checks} checks passed (native AOT, fully trimmed).");
return 0;

static async Task<string> WaitForLinkTokenAsync(string mailbox, string email, string linkPath)
{
    var pattern = new Regex(Regex.Escape(linkPath) + @"\?token=([^\s""'<>&]+)");
    var deadline = DateTime.UtcNow.AddSeconds(20);
    while (DateTime.UtcNow < deadline)
    {
        foreach (var file in Directory.GetFiles(mailbox, "*.txt"))
        {
            var message = File.ReadAllText(file);
            if (message.IndexOf(email, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var match = pattern.Match(message);
            if (match.Success) return Uri.UnescapeDataString(match.Groups[1].Value);
        }
        await Task.Delay(100);
    }
    throw new TimeoutException($"No mail to {email} carrying a {linkPath} link reached {mailbox}.");
}

internal sealed class Signer : IStarhermitSigner
{
    private readonly RSA _key;

    public Signer(RSA key)
    {
        _key = key;
        PublicKeyData = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public string KeyType => StarhermitKeyTypes.RsaPss;

    public string PublicKeyData { get; }

    public Task<byte[]> SignAsync(byte[] data, CancellationToken cancellationToken) =>
        Task.FromResult(_key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
}
