using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit.Tests
{
    /// <summary>A clock the test moves by hand.</summary>
    public sealed class TestClock : IStarhermitClock
    {
        /// <summary>Creates a clock at a fixed instant.</summary>
        /// <param name="start">The starting time.</param>
        public TestClock(DateTimeOffset? start = null)
        {
            UtcNow = start ?? new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        }

        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; private set; }

        /// <summary>Moves the clock forward.</summary>
        /// <param name="delta">How far to advance.</param>
        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    /// <summary>Collects log messages so a test can assert on what was, and was not, written.</summary>
    public sealed class RecordingLogger : IStarhermitLogger
    {
        /// <summary>Every message written.</summary>
        public List<string> Messages { get; } = new List<string>();

        /// <inheritdoc />
        public void Log(StarhermitLogLevel level, string message, Exception? exception = null)
        {
            Messages.Add($"{level}: {message}");
            if (exception != null) Messages.Add($"{level}: {exception.Message}");
        }

        /// <summary>True when no message contains the given text.</summary>
        /// <param name="text">Text that must not appear anywhere in the log.</param>
        /// <returns>True when the text was never logged.</returns>
        public bool NeverLogged(string text)
        {
            foreach (var message in Messages)
                if (message.Contains(text))
                    return false;
            return true;
        }
    }

    /// <summary>Collects telemetry events.</summary>
    public sealed class RecordingTelemetry : IStarhermitTelemetrySink
    {
        /// <summary>Every event recorded.</summary>
        public List<StarhermitTelemetryEvent> Events { get; } = new List<StarhermitTelemetryEvent>();

        /// <inheritdoc />
        public void Record(StarhermitTelemetryEvent telemetryEvent) => Events.Add(telemetryEvent);
    }

    /// <summary>Builds clients wired to test doubles.</summary>
    public static class TestHarness
    {
        /// <summary>A signing key type used by tests that need one.</summary>
        public const string TestKeyType = StarhermitKeyTypes.Ed25519;

        /// <summary>Builds options with every adapter pointed at a test double.</summary>
        /// <param name="transport">Transport to use.</param>
        /// <param name="socketFactory">Socket factory to use.</param>
        /// <param name="clock">Clock to use.</param>
        /// <param name="logger">Logger to use.</param>
        /// <param name="tokenStore">Token store to use.</param>
        /// <returns>Options ready to create a client from.</returns>
        public static StarhermitOptions Options(
            IStarhermitTransport? transport = null,
            IStarhermitSocketFactory? socketFactory = null,
            IStarhermitClock? clock = null,
            IStarhermitLogger? logger = null,
            IStarhermitTokenStore? tokenStore = null) =>
            new StarhermitOptions
            {
                ApiBaseUri = new Uri("https://api.test.starhermit.com/api/v1/"),
                Transport = transport,
                SocketFactory = socketFactory,
                Clock = clock ?? new TestClock(),
                Logger = logger ?? NullStarhermitLogger.Instance,
                LogLevel = logger == null ? StarhermitLogLevel.None : StarhermitLogLevel.Debug,
                TokenStore = tokenStore ?? new InMemoryTokenStore(),
                // Events run inline so a test can assert immediately after the frame that caused them.
                CallbackDispatcher = ImmediateCallbackDispatcher.Instance,
                RetryPolicy = new StarhermitRetryPolicy
                {
                    MaxAttempts = 3,
                    BaseDelay = TimeSpan.FromMilliseconds(1),
                    MaxDelay = TimeSpan.FromMilliseconds(2),
                    JitterFactor = 0,
                    Budget = StarhermitRetryBudget.Unlimited
                }
            };

        /// <summary>Creates a client with a signed-in session already loaded.</summary>
        /// <param name="transport">Transport to use.</param>
        /// <param name="clock">Clock to use.</param>
        /// <param name="socketFactory">Socket factory to use.</param>
        /// <param name="logger">Logger to use.</param>
        /// <returns>A signed-in client.</returns>
        public static async Task<StarhermitClient> SignedInAsync(
            IStarhermitTransport transport,
            IStarhermitClock? clock = null,
            IStarhermitSocketFactory? socketFactory = null,
            IStarhermitLogger? logger = null)
        {
            var testClock = clock ?? new TestClock();
            var store = new InMemoryTokenStore(new StarhermitStoredSession(
                Jwt(testClock.UtcNow.AddMinutes(15)),
                "refresh-token-1",
                TestUserId));

            var client = StarhermitClient.Create(Options(transport, socketFactory, testClock, logger, store));
            await client.InitializeAsync().ConfigureAwait(false);
            return client;
        }

        /// <summary>The account id used by tests.</summary>
        public static readonly Guid TestUserId = new Guid("11111111-2222-3333-4444-555555555555");

        /// <summary>
        /// Builds an unsigned JWT with the claims the SDK reads locally. The SDK never verifies a
        /// signature - the server does - so an unsigned token is enough to exercise expiry handling.
        /// </summary>
        /// <param name="expiresAt">Value for the <c>exp</c> claim.</param>
        /// <param name="userId">Value for the <c>sub</c> claim.</param>
        /// <param name="authMethod">Value for the <c>auth_method</c> claim.</param>
        /// <returns>The encoded token.</returns>
        public static string Jwt(DateTimeOffset expiresAt, Guid? userId = null, string authMethod = "oauth")
        {
            var header = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
            var payload = Base64Url(
                "{\"sub\":\"" + (userId ?? TestUserId).ToString("D") + "\"," +
                "\"exp\":" + expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                "\"iat\":" + expiresAt.AddMinutes(-15).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                "\"auth_method\":\"" + authMethod + "\"," +
                "\"permission\":[\"user.profile.read\",\"user.profile.update\"]}");
            return header + "." + payload + ".signature-not-verified";
        }

        private static string Base64Url(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        /// <summary>Waits for a condition, failing the test rather than hanging forever.</summary>
        /// <param name="condition">The condition to wait for.</param>
        /// <param name="description">What is being waited for, used in the failure message.</param>
        /// <param name="timeoutMilliseconds">How long to wait.</param>
        /// <returns>A task that completes once the condition holds.</returns>
        public static async Task WaitForAsync(Func<bool> condition, string description, int timeoutMilliseconds = 2000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                await Task.Delay(5).ConfigureAwait(false);
            }

            throw new TimeoutException($"Timed out waiting for {description}.");
        }
    }
}
