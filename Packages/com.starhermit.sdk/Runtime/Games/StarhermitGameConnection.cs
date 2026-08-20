using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// The authoritative-game socket: player commands up, game frames down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SDK carries commands and frames without understanding either. It does not tick game logic,
    /// predict authoritative state, or fabricate an outcome the server has not sent - the platform is
    /// the authority, and a client that guessed would only be wrong convincingly.
    /// </para>
    /// <para>
    /// After an ambiguous disconnect a command is <em>not</em> resent by default: the server may have
    /// applied it already, and replaying a move is worse than dropping one. Supply a deduplication key
    /// in your own command schema if your game can tolerate it.
    /// </para>
    /// </remarks>
    public sealed class StarhermitGameConnection : StarhermitConnection
    {
        private readonly Guid _sessionId;
        private readonly string? _gameSlug;
        private readonly bool _useLaunchToken;

        internal StarhermitGameConnection(StarhermitClient client, Guid sessionId, string? gameSlug, bool useLaunchToken)
            : base(client, "game")
        {
            _sessionId = sessionId;
            _gameSlug = gameSlug;
            _useLaunchToken = useLaunchToken;
        }

        /// <summary>The session this connection is attached to.</summary>
        public Guid SessionId => _sessionId;

        /// <inheritdoc />
        protected override string Path => "games";

        /// <inheritdoc />
        protected override StarhermitCredential Credential =>
            _useLaunchToken ? StarhermitCredential.Launch : StarhermitCredential.Account;

        /// <inheritdoc />
        protected override string? GameSlug => _gameSlug;

        /// <inheritdoc />
        protected override void BuildQuery(IList<KeyValuePair<string, string>> query) =>
            query.Add(new KeyValuePair<string, string>("sessionId", _sessionId.ToString("D")));

        /// <summary>Raised for each authoritative frame the game sends, payload untouched.</summary>
        public event Action<JsonValue>? FrameReceived;

        /// <summary>Raised when the platform reports an achievement unlock during play.</summary>
        public event Action<JsonValue>? AchievementUnlocked;

        /// <summary>Raised when another player in the session connects or disconnects.</summary>
        public event Action<Guid, bool>? PresenceChanged;

        /// <summary>Raised when the server refuses a command or reports a protocol error.</summary>
        public event Action<string>? ErrorReceived;

        /// <summary>Raised for any frame this SDK version does not recognise, payload intact.</summary>
        public event Action<string, JsonValue>? UnknownFrameReceived;

        /// <summary>Sends a command to the game.</summary>
        /// <param name="writeCommand">Writes the command's members. The game defines their shape.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the command is sent.</returns>
        public Task SendCommandAsync(Action<JsonWriter> writeCommand, CancellationToken cancellationToken = default)
        {
            if (writeCommand == null) throw new ArgumentNullException(nameof(writeCommand));
            var json = JsonWriter.SerializeObject(writer =>
            {
                writer.Write("type", "cmd");
                writer.WritePropertyName("data");
                writer.WriteStartObject();
                writeCommand(writer);
                writer.WriteEndObject();
            });

            return SendTextAsync(json, cancellationToken);
        }

        /// <summary>Sends a command that has already been built as a JSON value.</summary>
        /// <param name="command">The command payload.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the command is sent.</returns>
        public Task SendCommandAsync(JsonValue command, CancellationToken cancellationToken = default)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var json = JsonWriter.SerializeObject(writer =>
            {
                writer.Write("type", "cmd");
                writer.Write("data", command);
            });

            return SendTextAsync(json, cancellationToken);
        }

        /// <summary>
        /// Sends a realtime input command, which the server buffers and rate-limits rather than
        /// applying one at a time.
        /// </summary>
        /// <param name="writeInput">Writes the input's members.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the command is sent.</returns>
        public Task SendRealtimeInputAsync(Action<JsonWriter> writeInput, CancellationToken cancellationToken = default)
        {
            if (writeInput == null) throw new ArgumentNullException(nameof(writeInput));
            return SendCommandAsync(writer =>
            {
                writer.Write("type", "input");
                writer.Write("realtime", true);
                writeInput(writer);
            }, cancellationToken);
        }

        /// <inheritdoc />
        protected override void HandleText(string text)
        {
            if (!JsonParser.TryParse(text, out var frame) || !frame.IsObject) return;
            var type = frame["type"].AsStringOrNull();
            if (type == null) return;

            switch (type)
            {
                case "game":
                    Raise(() => FrameReceived?.Invoke(frame["data"]));
                    break;
                case "achievement":
                    Raise(() => AchievementUnlocked?.Invoke(frame["data"]));
                    break;
                case "presence":
                {
                    var userId = frame["userId"].AsGuidOrNull() ?? Guid.Empty;
                    var online = frame["online"].AsBooleanOrDefault();
                    Raise(() => PresenceChanged?.Invoke(userId, online));
                    break;
                }

                case "error":
                {
                    var error = frame["error"].AsStringOrNull() ?? "The game server refused the command.";
                    Raise(() => ErrorReceived?.Invoke(error));
                    break;
                }

                default:
                    Raise(() => UnknownFrameReceived?.Invoke(type, frame));
                    break;
            }
        }
    }
}
