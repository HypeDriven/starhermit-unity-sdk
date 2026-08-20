#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Starhermit.Platform
{
    /// <summary>
    /// Captures microphone audio through Unity and hands it to voice as PCM frames.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity's <c>Microphone</c> writes into a ring buffer that the game has to read at the right
    /// rate; this pumps it from a coroutine-free update loop and slices exactly one frame at a time,
    /// so the voice protocol sees a steady 20 ms cadence rather than whatever the OS felt like.
    /// </para>
    /// <para>
    /// A platform with no microphone - a dedicated server, a device that refused permission - raises
    /// <see cref="StarhermitFeatureUnavailableException"/> from <see cref="StartAsync"/> rather than
    /// failing the whole voice connection. The room still works; the player just cannot speak.
    /// </para>
    /// </remarks>
    public sealed class UnityMicrophoneCapture : IStarhermitAudioCapture
    {
        private readonly string? _deviceName;
        private AudioClip? _clip;
        private StarhermitAudioFormat _format;
        private int _readPosition;
        private float[]? _scratch;
        private short[]? _frame;
        private AudioPump? _pump;
        private bool _disposed;

        /// <summary>Creates the adapter.</summary>
        /// <param name="deviceName">Capture device, or null for the system default.</param>
        public UnityMicrophoneCapture(string? deviceName = null)
        {
            _deviceName = deviceName;
        }

        /// <inheritdoc />
        public bool IsCapturing { get; private set; }

        /// <inheritdoc />
        public event Action<StarhermitAudioFrame>? FrameCaptured;

        /// <inheritdoc />
        public Task StartAsync(StarhermitAudioFormat format, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnityMicrophoneCapture));
            if (IsCapturing) return Task.CompletedTask;

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                throw new StarhermitFeatureUnavailableException(
                    "voice.capture",
                    StarhermitFeatureReasons.DeviceUnavailable,
                    "This device reports no microphone, so voice capture cannot start. Everything else on the voice connection still works.");
            }

            _format = format;
            _frame = new short[format.SamplesPerFrame];
            _scratch = new float[format.SamplesPerFrame];
            _readPosition = 0;

            _clip = Microphone.Start(_deviceName, true, 1, format.SampleRate);
            if (_clip == null)
            {
                throw new StarhermitFeatureUnavailableException(
                    "voice.capture",
                    StarhermitFeatureReasons.PermissionDenied,
                    "The microphone could not be started. On mobile and WebGL this usually means the player declined the permission prompt.");
            }

            IsCapturing = true;
            _pump = AudioPump.Create("Starhermit Microphone", Pump);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!IsCapturing) return Task.CompletedTask;

            IsCapturing = false;
            _pump?.Stop();
            _pump = null;
            Microphone.End(_deviceName);
            _clip = null;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAsync().GetAwaiter().GetResult();
        }

        private void Pump()
        {
            var clip = _clip;
            if (!IsCapturing || clip == null || _scratch == null || _frame == null) return;

            var writePosition = Microphone.GetPosition(_deviceName);
            if (writePosition < 0) return;

            var available = writePosition - _readPosition;
            if (available < 0) available += clip.samples;

            while (available >= _frame.Length)
            {
                if (!clip.GetData(_scratch, _readPosition)) return;

                for (var i = 0; i < _frame.Length; i++)
                {
                    var sample = Mathf.Clamp(_scratch[i], -1f, 1f);
                    _frame[i] = (short)(sample * short.MaxValue);
                }

                _readPosition = (_readPosition + _frame.Length) % clip.samples;
                available -= _frame.Length;

                var captured = FrameCaptured;
                captured?.Invoke(new StarhermitAudioFrame(new ArraySegment<short>(_frame), _format));
            }
        }
    }

    /// <summary>
    /// Plays received voice audio, one streaming source per speaker.
    /// </summary>
    /// <remarks>
    /// Each speaker gets an <c>AudioSource</c> fed from its own jitter buffer, so one player's packet
    /// loss cannot stall everyone else's audio. Silence is played when a buffer runs dry, which is
    /// what a listener expects from a dropped packet.
    /// </remarks>
    public sealed class UnityAudioPlayback : IStarhermitAudioPlayback
    {
        private readonly Dictionary<Guid, SpeakerVoice> _speakers = new Dictionary<Guid, SpeakerVoice>();
        private readonly object _gate = new object();
        private StarhermitAudioFormat _format = StarhermitAudioFormat.Fallback;
        private GameObject? _host;
        private bool _running;
        private bool _disposed;

        /// <summary>How many frames to buffer per speaker before dropping the oldest.</summary>
        public int JitterBufferFrames { get; set; } = 10;

        /// <inheritdoc />
        public Task StartAsync(StarhermitAudioFormat format, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnityAudioPlayback));
            if (_running) return Task.CompletedTask;

            _format = format;
            _host = new GameObject("Starhermit Voice Playback");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideInHierarchy;
            _running = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Enqueue(Guid speakerId, StarhermitAudioFrame frame)
        {
            if (!_running || frame.Samples.Array == null) return;

            SpeakerVoice voice;
            lock (_gate)
            {
                if (!_speakers.TryGetValue(speakerId, out voice!))
                {
                    voice = SpeakerVoice.Create(_host!, speakerId, _format, JitterBufferFrames);
                    _speakers[speakerId] = voice;
                }
            }

            voice.Enqueue(frame.Samples);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_running) return Task.CompletedTask;
            _running = false;

            lock (_gate)
            {
                foreach (var speaker in _speakers.Values) speaker.Stop();
                _speakers.Clear();
            }

            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAsync().GetAwaiter().GetResult();
        }

        private sealed class SpeakerVoice
        {
            private readonly Queue<short[]> _queue = new Queue<short[]>();
            private readonly object _gate = new object();
            private readonly int _capacity;
            private AudioSource? _source;
            private short[]? _current;
            private int _offset;

            private SpeakerVoice(int capacity)
            {
                _capacity = capacity;
            }

            internal static SpeakerVoice Create(GameObject host, Guid speakerId, StarhermitAudioFormat format, int capacity)
            {
                var voice = new SpeakerVoice(capacity);
                var child = new GameObject("speaker-" + speakerId.ToString("N"));
                child.transform.SetParent(host.transform, false);

                var source = child.AddComponent<AudioSource>();
                source.loop = true;
                source.spatialBlend = 0f;
                source.clip = AudioClip.Create(
                    "starhermit-voice",
                    format.SampleRate,
                    format.Channels,
                    format.SampleRate,
                    true,
                    voice.Read);
                source.Play();

                voice._source = source;
                return voice;
            }

            internal void Enqueue(ArraySegment<short> samples)
            {
                var copy = new short[samples.Count];
                Buffer.BlockCopy(samples.Array!, samples.Offset * 2, copy, 0, copy.Length * 2);

                lock (_gate)
                {
                    // A buffer that has grown past its window is late audio nobody wants to hear;
                    // dropping the oldest frame keeps the conversation in the present.
                    while (_queue.Count >= _capacity) _queue.Dequeue();
                    _queue.Enqueue(copy);
                }
            }

            internal void Stop()
            {
                if (_source == null) return;
                _source.Stop();
                UnityEngine.Object.Destroy(_source.gameObject);
                _source = null;
            }

            private void Read(float[] data)
            {
                for (var i = 0; i < data.Length; i++)
                {
                    if (_current == null || _offset >= _current.Length)
                    {
                        lock (_gate) _current = _queue.Count > 0 ? _queue.Dequeue() : null;
                        _offset = 0;
                    }

                    data[i] = _current == null ? 0f : _current[_offset++] / (float)short.MaxValue;
                }
            }
        }
    }

    /// <summary>Runs a callback every frame on Unity's main thread.</summary>
    internal sealed class AudioPump : MonoBehaviour
    {
        private Action? _tick;

        internal static AudioPump Create(string name, Action tick)
        {
            var host = new GameObject(name);
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideInHierarchy;
            var pump = host.AddComponent<AudioPump>();
            pump._tick = tick;
            return pump;
        }

        internal void Stop()
        {
            _tick = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private void Update() => _tick?.Invoke();
    }
}
#endif
