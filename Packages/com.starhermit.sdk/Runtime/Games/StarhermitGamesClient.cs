using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>Entry point to the authoritative-games API.</summary>
    /// <remarks>
    /// Every game operation is scoped to a slug, so this class hands out <see cref="StarhermitGameClient"/>
    /// instances rather than taking a slug on every call. The only routes that live here are the ones
    /// that genuinely span games.
    /// </remarks>
    public sealed class StarhermitGamesClient : StarhermitServiceClient
    {
        private readonly StarhermitScopedCredentials _credentials;
        private readonly Dictionary<string, StarhermitGameClient> _clients =
            new Dictionary<string, StarhermitGameClient>(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new object();

        internal StarhermitGamesClient(StarhermitRestClient rest, StarhermitScopedCredentials credentials)
            : base(rest)
        {
            _credentials = credentials;
        }

        /// <summary>Gets the client for one game, creating it on first use.</summary>
        /// <param name="slug">The game's slug.</param>
        /// <returns>A client scoped to that game.</returns>
        public StarhermitGameClient ForSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("A game slug is required.", nameof(slug));
            lock (_gate)
            {
                if (_clients.TryGetValue(slug, out var existing)) return existing;
                var created = new StarhermitGameClient(Rest, slug, _credentials, StarhermitCredential.Account);
                _clients[slug] = created;
                return created;
            }
        }

        /// <summary>
        /// The client for the slug configured in options.
        /// </summary>
        /// <exception cref="InvalidOperationException">No default game slug is configured.</exception>
        public StarhermitGameClient Default =>
            ForSlug(Options.GameSlug ?? throw new InvalidOperationException(
                "No default game slug is configured. Set StarhermitOptions.GameSlug, or call ForSlug(slug)."));

        /// <summary>
        /// Lists every pending invite addressed to the caller, across games and realtime rooms.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The pending invitations, each carrying the routes that answer it.</returns>
        public async Task<IReadOnlyList<StarhermitInviteNotification>> GetPendingInvitesAsync(
            CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/game-invites"), "games.getPendingInvites", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitInviteNotification.Read);
        }
    }

    /// <summary>
    /// Everything one game exposes: metadata, launch tokens, sessions, matchmaking, invites, replays,
    /// control bindings and the player's settings document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two credentials reach these routes. The account session can do everything the player can do.
    /// A launch token is game-scoped: the backend fences it to this game's own routes, which is what
    /// makes it safe to hand to a game build. <see cref="WithLaunchToken"/> returns a client that
    /// sends the launch token instead of the session, and the fence - not the SDK - decides what that
    /// client may call.
    /// </para>
    /// <para>
    /// Minting a launch token never replaces the account session; both are held, separately.
    /// </para>
    /// </remarks>
    public sealed class StarhermitGameClient : StarhermitServiceClient
    {
        private readonly StarhermitScopedCredentials _credentials;
        private readonly StarhermitCredential _credential;
        private readonly string _prefix;

        internal StarhermitGameClient(
            StarhermitRestClient rest,
            string slug,
            StarhermitScopedCredentials credentials,
            StarhermitCredential credential)
            : base(rest)
        {
            Slug = slug;
            _credentials = credentials;
            _credential = credential;
            _prefix = $"games/{Escape(slug)}";
        }

        /// <summary>The game's slug.</summary>
        public string Slug { get; }

        /// <summary>True when this client authorises with the game-scoped launch token.</summary>
        public bool IsLaunchScoped => _credential == StarhermitCredential.Launch;

        /// <summary>
        /// Returns a client that authorises with this game's launch token rather than the account
        /// session. Mint one first with <see cref="AcquireLaunchTokenAsync"/>.
        /// </summary>
        /// <returns>A launch-scoped client for the same game.</returns>
        public StarhermitGameClient WithLaunchToken() =>
            IsLaunchScoped ? this : new StarhermitGameClient(Rest, Slug, _credentials, StarhermitCredential.Launch);

        /// <summary>Reads the game's metadata and the caller's standing in it.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The game info.</returns>
        public Task<StarhermitGameInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Request("GET", string.Empty), "games.getInfo", StarhermitGameInfo.Read, cancellationToken);

        /// <summary>
        /// Mints a launch token for this game and stores it in the scoped-credential store.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The token's expiry.</returns>
        public async Task<StarhermitScopedToken> AcquireLaunchTokenAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(
                    Post($"{_prefix}/launch-token").WithCredential(StarhermitCredential.Account),
                    "games.acquireLaunchToken",
                    cancellationToken)
                .ConfigureAwait(false);

            var token = json["token"].AsStringOrNull()
                        ?? throw new StarhermitSerializationException("The launch-token response carried no token.");
            var expiresIn = json["expiresInSeconds"].AsInt32OrNull();
            var scoped = new StarhermitScopedToken(
                token,
                expiresIn.HasValue ? Options.Clock.UtcNow.AddSeconds(expiresIn.Value) : (DateTimeOffset?)null);

            _credentials.SetLaunchToken(Slug, scoped);
            return scoped;
        }

        /// <summary>The launch token currently held for this game, if any.</summary>
        public StarhermitScopedToken? LaunchToken => _credentials.GetLaunchToken(Slug);

        /// <summary>Forgets the launch token held for this game.</summary>
        public void ClearLaunchToken() => _credentials.ClearLaunchToken(Slug);

        /// <summary>Lists the achievements defined for this game and whether the caller holds them.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The achievements.</returns>
        public async Task<IReadOnlyList<StarhermitGameAchievement>> GetAchievementsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Request("GET", "achievements"), "games.getAchievements", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitGameAchievement.Read);
        }

        /// <summary>Lists the caller's sessions in this game.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The sessions.</returns>
        public async Task<IReadOnlyList<StarhermitGameSessionSummary>> GetMySessionsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Request("GET", "sessions/mine"), "games.getMySessions", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitGameSessionSummary.Read);
        }

        /// <summary>Reads one session.</summary>
        /// <param name="sessionId">The session to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The session.</returns>
        public Task<StarhermitGameSession> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("GET", $"sessions/{Escape(sessionId)}"),
                "games.getSession",
                StarhermitGameSession.Read,
                cancellationToken);

        /// <summary>Creates a single-player or AI session.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new session.</returns>
        public Task<StarhermitGameSession> CreateAiSessionAsync(CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("POST", "sessions/ai"),
                "games.createAiSession",
                StarhermitGameSession.Read,
                cancellationToken);

        /// <summary>Enters nearest-rating matchmaking for any shape of match the game accepts.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The ticket.</returns>
        public Task<StarhermitMatchmakingTicket> EnqueueMatchmakingAsync(CancellationToken cancellationToken = default) =>
            EnqueueMatchmakingAsync(null, cancellationToken);

        /// <summary>Enters nearest-rating matchmaking for some of the game's match shapes.</summary>
        /// <param name="queues">Keys from <see cref="GetQueuesAsync"/>, or null for every shape. An unknown
        /// or empty subset is refused rather than read as "any".</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The ticket.</returns>
        public Task<StarhermitMatchmakingTicket> EnqueueMatchmakingAsync(
            IEnumerable<string>? queues,
            CancellationToken cancellationToken = default)
        {
            var request = Request("POST", "matchmaking");
            if (queues != null)
                foreach (var queue in queues)
                    request.WithQuery("queues", queue);
            return SendAsync(request, "games.enqueueMatchmaking", StarhermitMatchmakingTicket.Read, cancellationToken);
        }

        /// <summary>Lists the shapes of match this game accepts - 1v1, 2v2 and so on - so a client can offer them.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The queues; a game that declares none has one implicit 1v1.</returns>
        public async Task<IReadOnlyList<StarhermitGameQueue>> GetQueuesAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Request("GET", "queues"), "games.getQueues", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitGameQueue.Read);
        }

        /// <summary>Reads the caller's matchmaking ticket.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The ticket, or null when there is none.</returns>
        public async Task<StarhermitMatchmakingTicket?> GetMatchmakingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await SendAsync(
                        Request("GET", "matchmaking"),
                        "games.getMatchmaking",
                        StarhermitMatchmakingTicket.Read,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (StarhermitNotFoundException)
            {
                // No ticket is a state, not a failure.
                return null;
            }
        }

        /// <summary>Cancels the caller's matchmaking ticket.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the ticket is cancelled.</returns>
        public Task CancelMatchmakingAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Request("DELETE", "matchmaking"), "games.cancelMatchmaking", cancellationToken);

        /// <summary>Invites another player to a session of this game.</summary>
        /// <param name="toUserId">Who to invite.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The invite.</returns>
        public Task<StarhermitGameInvite> CreateInviteAsync(Guid toUserId, CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Request("POST", "invites"), writer => writer.Write("toUserId", toUserId)),
                "games.createInvite",
                StarhermitGameInvite.Read,
                cancellationToken);

        /// <summary>Lists this game's invites to and from the caller.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>Incoming and outgoing invites.</returns>
        public Task<StarhermitGameInviteLists> GetInvitesAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Request("GET", "invites"), "games.getInvites", StarhermitGameInviteLists.Read, cancellationToken);

        /// <summary>Accepts an invite, which creates the session.</summary>
        /// <param name="inviteId">The invite to accept.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The invite as it stands after acceptance, including the session id.</returns>
        public Task<StarhermitGameInvite> AcceptInviteAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("POST", $"invites/{Escape(inviteId)}/accept"),
                "games.acceptInvite",
                StarhermitGameInvite.Read,
                cancellationToken);

        /// <summary>Declines an invite.</summary>
        /// <param name="inviteId">The invite to decline.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The closed invite.</returns>
        public Task<StarhermitGameInvite> DeclineInviteAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("POST", $"invites/{Escape(inviteId)}/decline"),
                "games.declineInvite",
                StarhermitGameInvite.Read,
                cancellationToken);

        /// <summary>
        /// Lists the caller's replays.
        /// </summary>
        /// <remarks>
        /// A game with replays disabled answers <c>404</c>, which surfaces as
        /// <see cref="StarhermitNotFoundException"/>. That is deliberately not flattened into an empty
        /// list: "this game does not record replays" and "you have none yet" are different answers.
        /// </remarks>
        /// <param name="limit">Maximum replays to return.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The replays.</returns>
        public async Task<IReadOnlyList<StarhermitReplaySummary>> GetMyReplaysAsync(
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            var request = Request("GET", "replays/mine").WithQuery("limit", limit);
            var json = await SendJsonAsync(request, "games.getMyReplays", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitReplaySummary.Read);
        }

        /// <summary>Reads one replay in full.</summary>
        /// <param name="sessionId">The session whose replay to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The replay.</returns>
        public Task<StarhermitReplay> GetReplayAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("GET", $"replays/{Escape(sessionId)}"),
                "games.getReplay",
                StarhermitReplay.Read,
                cancellationToken);

        /// <summary>Reads the caller's control bindings for this game.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The declared actions and their bindings.</returns>
        public Task<StarhermitGameControls> GetControlsAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Request("GET", "controls"), "games.getControls", StarhermitGameControls.Read, cancellationToken);

        /// <summary>Replaces the caller's control bindings.</summary>
        /// <param name="bindings">Action name to bound codes. Pass null to clear overrides.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The bindings as stored.</returns>
        public Task<StarhermitGameControls> PutControlsAsync(
            IReadOnlyDictionary<string, IReadOnlyList<string>>? bindings,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Request("PUT", "controls"), writer =>
            {
                writer.WritePropertyName("bindings");
                if (bindings == null)
                {
                    writer.WriteNull();
                    return;
                }

                writer.WriteStartObject();
                foreach (var binding in bindings)
                {
                    writer.WritePropertyName(binding.Key);
                    writer.WriteStartArray();
                    foreach (var code in binding.Value) writer.WriteString(code);
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            });

            return SendAsync(request, "games.putControls", StarhermitGameControls.Read, cancellationToken);
        }

        /// <summary>Clears the caller's control overrides, restoring the game's defaults.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the overrides are gone.</returns>
        public Task DeleteControlsAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Request("DELETE", "controls"), "games.deleteControls", cancellationToken);

        /// <summary>Reads the caller's settings document for this game.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The document and the server's current budget for it.</returns>
        public Task<StarhermitGameSettings> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Request("GET", "settings"), "games.getSettings", StarhermitGameSettings.Read, cancellationToken);

        /// <summary>Replaces the whole settings document.</summary>
        /// <param name="settings">The new document.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The document as stored.</returns>
        public Task<StarhermitGameSettings> PutSettingsAsync(
            IReadOnlyDictionary<string, JsonValue> settings,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Request("PUT", "settings"), writer => WriteSettings(writer, settings)),
                "games.putSettings",
                StarhermitGameSettings.Read,
                cancellationToken);

        /// <summary>Merges values into the settings document, leaving unmentioned keys alone.</summary>
        /// <param name="settings">The values to merge. A null value deletes that key.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The document as stored.</returns>
        public Task<StarhermitGameSettings> PatchSettingsAsync(
            IReadOnlyDictionary<string, JsonValue> settings,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Request("PATCH", "settings"), writer => WriteSettings(writer, settings)),
                "games.patchSettings",
                StarhermitGameSettings.Read,
                cancellationToken);

        /// <summary>Deletes the whole settings document.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The empty document.</returns>
        public Task<StarhermitGameSettings> DeleteSettingsAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Request("DELETE", "settings"), "games.deleteSettings", StarhermitGameSettings.Read, cancellationToken);

        /// <summary>Reads one settings key.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The stored value.</returns>
        public Task<StarhermitGameSetting> GetSettingAsync(string key, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("GET", $"settings/{Escape(key)}"),
                "games.getSetting",
                StarhermitGameSetting.Read,
                cancellationToken);

        /// <summary>Writes one settings key.</summary>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to store, in whatever shape the game uses.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The stored value.</returns>
        public Task<StarhermitGameSetting> PutSettingAsync(
            string key,
            JsonValue value,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Request("PUT", $"settings/{Escape(key)}"), writer => writer.Write("value", value)),
                "games.putSetting",
                StarhermitGameSetting.Read,
                cancellationToken);

        /// <summary>Deletes one settings key.</summary>
        /// <param name="key">The key to delete.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the key is gone.</returns>
        public Task DeleteSettingAsync(string key, CancellationToken cancellationToken = default) =>
            SendAsync(Request("DELETE", $"settings/{Escape(key)}"), "games.deleteSetting", cancellationToken);

        /// <summary>Lists the game's active leaderboards. Read entries with <see cref="StarhermitClient.Leaderboards"/>.</summary>
        /// <remarks>Scores are submitted only by the game's server logic; a client cannot write to these boards.</remarks>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The leaderboards.</returns>
        public async Task<IReadOnlyList<StarhermitLeaderboard>> GetLeaderboardsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Request("GET", "leaderboards"), "games.getLeaderboards", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitLeaderboard.Read);
        }

        /// <summary>Reads what the caller has unlocked in another game, to open features earned there.</summary>
        /// <param name="otherSlug">The other game's slug.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The other game's unlocked achievements, or a hidden result when the player keeps them private.</returns>
        public Task<StarhermitLinkedAchievements> GetLinkedAchievementsAsync(string otherSlug, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("GET", $"linked-achievements/{Escape(otherSlug)}"),
                "games.getLinkedAchievements",
                StarhermitLinkedAchievements.Read,
                cancellationToken);

        /// <summary>
        /// Reads what it would take to bring a session back: checkpoint size and freshness, restore
        /// limits, transport budgets and how long recovery data is kept. Its players and the game's
        /// owner may read it.
        /// </summary>
        /// <param name="sessionId">The session.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The recovery description.</returns>
        public Task<StarhermitSessionRecovery> GetSessionRecoveryAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("GET", $"sessions/{Escape(sessionId)}/recovery"),
                "games.getSessionRecovery",
                StarhermitSessionRecovery.Read,
                cancellationToken);

        /// <summary>
        /// Gives up the caller's place in a persistent world. A world bound to a room is left through
        /// the room instead. The last member leaving ends the world.
        /// </summary>
        /// <param name="sessionId">The session to leave.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the caller is no longer a member.</returns>
        public Task LeaveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            SendAsync(Request("POST", $"sessions/{Escape(sessionId)}/leave"), "games.leaveSession", cancellationToken);

        /// <summary>Files a crash or bug report with the game's developer.</summary>
        /// <param name="report">The report.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The receipt.</returns>
        /// <exception cref="StarhermitRateLimitException">The player has filed as many reports today as the game allows; see <see cref="StarhermitApiException.RetryAfter"/>.</exception>
        /// <exception cref="StarhermitQuotaExceededException">The description or attachments are larger than the game allows.</exception>
        public Task<StarhermitReportReceipt> FileReportAsync(StarhermitPlayerReport report, CancellationToken cancellationToken = default)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return SendAsync(WithBody(Request("POST", "reports"), report.Write), "games.fileReport", StarhermitReportReceipt.Read, cancellationToken);
        }

        /// <summary>Lists the caller's own reports for this game and where each stands, newest first.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The reports.</returns>
        public async Task<IReadOnlyList<StarhermitMyReport>> GetMyReportsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Request("GET", "reports/mine"), "games.getMyReports", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitMyReport.Read);
        }

        /// <summary>
        /// Exchanges this client's credential for a single-use connection ticket, for a socket
        /// handshake that has to carry its credential in the URL.
        /// </summary>
        /// <remarks>
        /// A ticket carries exactly the authority of the token that bought it - a launch-scoped client
        /// gets a ticket fenced to this game - is valid only on a <c>/ws</c> path, and is spent by the
        /// first handshake that presents it.
        /// </remarks>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The ticket.</returns>
        public Task<StarhermitConnectionTicket> IssueConnectionTicketAsync(CancellationToken cancellationToken = default) =>
            SendAsync(
                Scoped(Post("realtime/connection-tickets")),
                "games.issueConnectionTicket",
                StarhermitConnectionTicket.Read,
                cancellationToken);

        /// <summary>Owner only: what the game is doing right now - sessions, script cost, matchmaking, webhooks and buffered writes.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The diagnostics.</returns>
        public Task<StarhermitGameDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("GET", "diagnostics").WithCredential(StarhermitCredential.Account),
                "games.getDiagnostics",
                StarhermitGameDiagnostics.Read,
                cancellationToken);

        /// <summary>Owner only: lists the game's webhook endpoints.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The endpoints, without their secrets.</returns>
        public async Task<IReadOnlyList<StarhermitWebhookEndpoint>> GetWebhooksAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(
                    Request("GET", "webhooks").WithCredential(StarhermitCredential.Account),
                    "games.getWebhooks",
                    cancellationToken)
                .ConfigureAwait(false);
            return json.AsList(StarhermitWebhookEndpoint.Read);
        }

        /// <summary>Owner only: registers a URL for the platform to call when something happens in the game.</summary>
        /// <remarks>
        /// The URL must be https, carry no credentials and resolve to a public address; it is checked
        /// again before every delivery. Keep the returned <see cref="StarhermitWebhookEndpoint.Secret"/>:
        /// it is shown only now.
        /// </remarks>
        /// <param name="url">Where deliveries go.</param>
        /// <param name="events">Event names to deliver, or null for the platform's default set.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The endpoint, with its signing secret.</returns>
        public Task<StarhermitWebhookEndpoint> CreateWebhookAsync(
            string url,
            IEnumerable<string>? events = null,
            CancellationToken cancellationToken = default)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            var request = WithBody(Request("POST", "webhooks"), writer =>
            {
                writer.Write("url", url);
                if (events != null) writer.WriteArray("events", events, (w, name) => w.WriteString(name));
            }).WithCredential(StarhermitCredential.Account);
            return SendAsync(request, "games.createWebhook", StarhermitWebhookEndpoint.Read, cancellationToken);
        }

        /// <summary>Owner only: removes a webhook endpoint and its pending deliveries.</summary>
        /// <param name="endpointId">The endpoint.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once it is gone.</returns>
        public Task DeleteWebhookAsync(Guid endpointId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("DELETE", $"webhooks/{Escape(endpointId)}").WithCredential(StarhermitCredential.Account),
                "games.deleteWebhook",
                cancellationToken);

        /// <summary>Owner only: re-enables an endpoint the platform disabled for failing.</summary>
        /// <param name="endpointId">The endpoint.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The endpoint.</returns>
        public Task<StarhermitWebhookEndpoint> ResumeWebhookAsync(Guid endpointId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Request("POST", $"webhooks/{Escape(endpointId)}/resume").WithCredential(StarhermitCredential.Account),
                "games.resumeWebhook",
                StarhermitWebhookEndpoint.Read,
                cancellationToken);

        private static void WriteSettings(JsonWriter writer, IReadOnlyDictionary<string, JsonValue> settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            writer.WritePropertyName("settings");
            writer.WriteStartObject();
            foreach (var setting in settings)
            {
                writer.WritePropertyName(setting.Key);
                writer.WriteValue(setting.Value);
            }

            writer.WriteEndObject();
        }

        private StarhermitRequest Request(string method, string suffix)
        {
            var path = string.IsNullOrEmpty(suffix) ? _prefix : $"{_prefix}/{suffix}";
            return Scoped(new StarhermitRequest(method, path));
        }

        private StarhermitRequest Scoped(StarhermitRequest request)
        {
            request.WithCredential(_credential);
            // The pipeline needs the slug to pick this game's launch token; the header never leaves
            // the process.
            if (_credential == StarhermitCredential.Launch) request.WithHeader(StarhermitHeaders.GameSlug, Slug);
            return request;
        }
    }
}
