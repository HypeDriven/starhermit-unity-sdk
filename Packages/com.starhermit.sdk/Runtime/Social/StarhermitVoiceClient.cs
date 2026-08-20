using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>Voice rooms: the REST half of voice, anchored to chat conversations.</summary>
    /// <remarks>
    /// Audio itself travels over the voice socket. Muting through this client changes server state and
    /// stops the caller's audio reaching anyone; turning down local playback is a different thing and
    /// is not what the API calls a mute.
    /// </remarks>
    public sealed class StarhermitVoiceClient : StarhermitServiceClient
    {
        internal StarhermitVoiceClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Opens a voice room on a conversation.</summary>
        /// <param name="conversationId">The conversation to anchor to.</param>
        /// <param name="maxParticipants">Requested participant limit; the deployment may clamp it.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room.</returns>
        public Task<StarhermitVoiceRoom> CreateRoomAsync(
            Guid conversationId,
            int? maxParticipants = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("voice/rooms"), writer =>
            {
                writer.Write("conversationId", conversationId);
                writer.WriteIfPresent("maxParticipants", maxParticipants);
            });

            return SendAsync(request, "voice.createRoom", StarhermitVoiceRoom.Read, cancellationToken);
        }

        /// <summary>Lists the voice rooms on a conversation.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The rooms.</returns>
        public async Task<IReadOnlyList<StarhermitVoiceRoom>> ListRoomsAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default)
        {
            var request = Get("voice/rooms").WithQuery("conversationId", conversationId);
            var json = await SendJsonAsync(request, "voice.listRooms", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitVoiceRoom.Read);
        }

        /// <summary>Reads one voice room.</summary>
        /// <param name="roomId">The room.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room.</returns>
        public Task<StarhermitVoiceRoom> GetRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
            SendAsync(Get($"voice/rooms/{Escape(roomId)}"), "voice.getRoom", StarhermitVoiceRoom.Read, cancellationToken);

        /// <summary>Joins a voice room. Connect the voice socket afterwards to carry audio.</summary>
        /// <param name="roomId">The room to join.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room as it stands after joining.</returns>
        public Task<StarhermitVoiceRoom> JoinRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"voice/rooms/{Escape(roomId)}/join"), "voice.joinRoom", StarhermitVoiceRoom.Read, cancellationToken);

        /// <summary>Leaves a voice room.</summary>
        /// <param name="roomId">The room to leave.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the caller has left.</returns>
        public Task LeaveRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"voice/rooms/{Escape(roomId)}/leave"), "voice.leaveRoom", cancellationToken);

        /// <summary>Mutes or unmutes the caller in a room, server-side.</summary>
        /// <param name="roomId">The room.</param>
        /// <param name="muted">True to mute.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the mute state is stored.</returns>
        public Task SetMuteAsync(Guid roomId, bool muted, CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post($"voice/rooms/{Escape(roomId)}/mute"), writer => writer.Write("muted", muted)),
                "voice.setMute",
                cancellationToken);

        /// <summary>Closes a voice room.</summary>
        /// <param name="roomId">The room to close.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the room is closed.</returns>
        public Task CloseRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"voice/rooms/{Escape(roomId)}/close"), "voice.closeRoom", cancellationToken);
    }
}
