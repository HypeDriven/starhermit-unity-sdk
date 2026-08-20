#if UNITY_2021_3_OR_NEWER
using System;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Platform;
using UnityEngine;

namespace Starhermit.Samples
{
    /// <summary>
    /// A voice room end to end: permissions, capture, playback, mute and speaking state.
    /// </summary>
    /// <remarks>
    /// Audio is opaque to the SDK. Frames go out as they are captured and arrive stamped with the
    /// sender the platform authenticated, which is what lets playback demux per speaker without
    /// trusting anything a client said about itself.
    /// </remarks>
    public sealed class VoiceSample : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private StarhermitVoiceConnection? _connection;
        private UnityMicrophoneCapture? _capture;
        private UnityAudioPlayback? _playback;

        /// <summary>The signed-in client to use.</summary>
        public StarhermitClient? Client { get; set; }

        /// <summary>The chat conversation to open voice on.</summary>
        public Guid ConversationId { get; set; }

        private void Start()
        {
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            var client = Client;
            if (client == null) return;

            var room = await client.Voice.CreateRoomAsync(ConversationId, cancellationToken: _lifetime.Token);
            await client.Voice.JoinRoomAsync(room.Id, _lifetime.Token);

            _connection = client.CreateVoiceConnection(room.Id);
            _playback = new UnityAudioPlayback();
            await _playback.StartAsync(StarhermitAudioFormat.Fallback, _lifetime.Token);

            _connection.AudioReceived += (speaker, audio) =>
                _playback.Enqueue(speaker, new StarhermitAudioFrame(ToSamples(audio), StarhermitAudioFormat.Fallback));
            _connection.SpeakingChanged += (user, speaking) => Debug.Log($"[Sample] {user} speaking: {speaking}");
            _connection.MuteChanged += (user, muted) => Debug.Log($"[Sample] {user} muted: {muted}");

            await _connection.ConnectAsync(_lifetime.Token);

            try
            {
                _capture = new UnityMicrophoneCapture();
                _capture.FrameCaptured += frame => _ = _connection.SendPcmAsync(frame.Samples, _lifetime.Token);
                await _capture.StartAsync(StarhermitAudioFormat.Fallback, _lifetime.Token);
                await _connection.SetSpeakingAsync(true, _lifetime.Token);
            }
            catch (StarhermitFeatureUnavailableException unavailable)
            {
                // No microphone, or the player declined. Listening still works, which is the whole
                // reason capture is a separate adapter.
                Debug.LogWarning($"[Sample] Voice capture unavailable ({unavailable.Reason}); listening only.");
            }
        }

        private static ArraySegment<short> ToSamples(byte[] pcm)
        {
            var samples = new short[pcm.Length / 2];
            Buffer.BlockCopy(pcm, 0, samples, 0, samples.Length * 2);
            return new ArraySegment<short>(samples);
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _capture?.Dispose();
            _playback?.Dispose();
            _connection?.Dispose();
        }
    }
}
#endif
