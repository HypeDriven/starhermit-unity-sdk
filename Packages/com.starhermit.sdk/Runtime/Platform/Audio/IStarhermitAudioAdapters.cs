using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// The PCM format voice falls back to when no codec adapter is installed.
    /// </summary>
    /// <remarks>
    /// 20 ms of 16 kHz mono signed 16-bit PCM per frame is the convention the platform's other clients
    /// already speak, so a Unity game and a desktop client can share a voice room without either
    /// needing a codec.
    /// </remarks>
    public readonly struct StarhermitAudioFormat
    {
        /// <summary>The fallback format: 16 kHz, mono, 16-bit, 20 ms frames.</summary>
        public static readonly StarhermitAudioFormat Fallback = new StarhermitAudioFormat(16000, 1, 20);

        /// <summary>Creates a format.</summary>
        /// <param name="sampleRate">Samples per second.</param>
        /// <param name="channels">Channel count.</param>
        /// <param name="frameMilliseconds">Duration of one frame in milliseconds.</param>
        public StarhermitAudioFormat(int sampleRate, int channels, int frameMilliseconds)
        {
            SampleRate = sampleRate;
            Channels = channels;
            FrameMilliseconds = frameMilliseconds;
        }

        /// <summary>Samples per second.</summary>
        public int SampleRate { get; }

        /// <summary>Channel count.</summary>
        public int Channels { get; }

        /// <summary>Duration of one frame, in milliseconds.</summary>
        public int FrameMilliseconds { get; }

        /// <summary>Samples in one frame, across all channels.</summary>
        public int SamplesPerFrame => SampleRate * Channels * FrameMilliseconds / 1000;
    }

    /// <summary>Captures microphone audio as PCM frames.</summary>
    /// <remarks>
    /// A headless build has no microphone and must still run every other module, so an absent adapter
    /// raises <see cref="StarhermitFeatureUnavailableException"/> at the call rather than failing the
    /// build or the connection.
    /// </remarks>
    public interface IStarhermitAudioCapture : IDisposable
    {
        /// <summary>True while frames are being produced.</summary>
        bool IsCapturing { get; }

        /// <summary>Raised for each captured frame, in the format capture was started with.</summary>
        event Action<StarhermitAudioFrame>? FrameCaptured;

        /// <summary>Starts capturing.</summary>
        /// <param name="format">Format the caller expects frames in.</param>
        /// <param name="cancellationToken">Cancels start-up, including a permission prompt.</param>
        /// <returns>A task that completes once capture is running.</returns>
        Task StartAsync(StarhermitAudioFormat format, CancellationToken cancellationToken = default);

        /// <summary>Stops capturing and releases the device.</summary>
        /// <param name="cancellationToken">Cancels the stop.</param>
        /// <returns>A task that completes once capture has stopped.</returns>
        Task StopAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>Plays received voice audio.</summary>
    public interface IStarhermitAudioPlayback : IDisposable
    {
        /// <summary>Starts playback.</summary>
        /// <param name="format">Format frames will be supplied in.</param>
        /// <param name="cancellationToken">Cancels start-up.</param>
        /// <returns>A task that completes once playback is running.</returns>
        Task StartAsync(StarhermitAudioFormat format, CancellationToken cancellationToken = default);

        /// <summary>Queues one speaker's frame for playback.</summary>
        /// <param name="speakerId">The authenticated sender the platform stamped on the frame.</param>
        /// <param name="frame">The PCM frame.</param>
        void Enqueue(Guid speakerId, StarhermitAudioFrame frame);

        /// <summary>Stops playback and releases resources.</summary>
        /// <param name="cancellationToken">Cancels the stop.</param>
        /// <returns>A task that completes once playback has stopped.</returns>
        Task StopAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>One frame of PCM audio.</summary>
    public readonly struct StarhermitAudioFrame
    {
        /// <summary>Creates a frame.</summary>
        /// <param name="samples">Interleaved 16-bit samples.</param>
        /// <param name="format">Format the samples are in.</param>
        public StarhermitAudioFrame(ArraySegment<short> samples, StarhermitAudioFormat format)
        {
            Samples = samples;
            Format = format;
        }

        /// <summary>Interleaved signed 16-bit samples.</summary>
        public ArraySegment<short> Samples { get; }

        /// <summary>Format the samples are in.</summary>
        public StarhermitAudioFormat Format { get; }
    }
}
