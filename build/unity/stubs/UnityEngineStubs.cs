// Minimal stand-ins for the Unity APIs this package uses.
//
// They exist so the editor-only half of the SDK - the UnityWebRequest transport, the WebGL socket
// bridge, the settings asset, the audio adapters - is compiled and type-checked on machines that have
// no Unity licence. Unity itself never sees this file: it is referenced only by the two compile-check
// projects under build/unity.
//
// If a stub here disagrees with the real Unity API, the check is worthless. Keep the surface tiny and
// keep the signatures identical to the ones Unity documents.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) => throw new NotImplementedException();

        public static void LogWarning(object message) => throw new NotImplementedException();

        public static void LogError(object message) => throw new NotImplementedException();
    }

    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
    }

    public static class Application
    {
        public static string productName => throw new NotImplementedException();

        public static string version => throw new NotImplementedException();

        public static void OpenURL(string url) => throw new NotImplementedException();
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName { get; set; } = string.Empty;

        public string menuName { get; set; } = string.Empty;

        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute
    {
    }

    [Flags]
    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
    }

    public class Object
    {
        public HideFlags hideFlags { get; set; }

        public string name { get; set; } = string.Empty;

        public static void Destroy(Object target) => throw new NotImplementedException();

        public static void DontDestroyOnLoad(Object target) => throw new NotImplementedException();

        public static bool operator ==(Object? left, Object? right) => ReferenceEquals(left, right);

        public static bool operator !=(Object? left, Object? right) => !ReferenceEquals(left, right);

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);

        public override int GetHashCode() => base.GetHashCode();
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => throw new NotImplementedException();
    }

    public class Transform : Component
    {
        public void SetParent(Transform parent, bool worldPositionStays) => throw new NotImplementedException();
    }

    public class Component : Object
    {
        public GameObject gameObject => throw new NotImplementedException();

        public Transform transform => throw new NotImplementedException();
    }

    public class Behaviour : Component
    {
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public sealed class GameObject : Object
    {
        public GameObject()
        {
        }

        public GameObject(string name)
        {
        }

        public Transform transform => throw new NotImplementedException();

        public T AddComponent<T>() where T : Component => throw new NotImplementedException();
    }

    public static class Microphone
    {
        public static string[] devices => throw new NotImplementedException();

        public static AudioClip? Start(string? deviceName, bool loop, int lengthSec, int frequency) =>
            throw new NotImplementedException();

        public static void End(string? deviceName) => throw new NotImplementedException();

        public static int GetPosition(string? deviceName) => throw new NotImplementedException();
    }

    public sealed class AudioClip : Object
    {
        public delegate void PCMReaderCallback(float[] data);

        public int samples => throw new NotImplementedException();

        public static AudioClip Create(
            string name,
            int lengthSamples,
            int channels,
            int frequency,
            bool stream,
            PCMReaderCallback pcmReaderCallback) => throw new NotImplementedException();

        public bool GetData(float[] data, int offsetSamples) => throw new NotImplementedException();
    }

    public enum TextureFormat
    {
        RGBA32 = 4,
    }

    public class Texture : Object
    {
        public int width => throw new NotImplementedException();

        public int height => throw new NotImplementedException();
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain)
        {
        }

        public bool LoadImage(byte[] data, bool markNonReadable) => throw new NotImplementedException();
    }

    public readonly struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
        }
    }

    public readonly struct Vector2
    {
        public Vector2(float x, float y)
        {
        }
    }

    public sealed class Sprite : Object
    {
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit) =>
            throw new NotImplementedException();
    }

    public sealed class AudioSource : Behaviour
    {
        public AudioClip? clip { get; set; }

        public bool loop { get; set; }

        public float spatialBlend { get; set; }

        public void Play() => throw new NotImplementedException();

        public void Stop() => throw new NotImplementedException();
    }

    public class AsyncOperation
    {
        public event Action<AsyncOperation>? completed
        {
            add => throw new NotImplementedException();
            remove => throw new NotImplementedException();
        }
    }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler : IDisposable
    {
        public byte[]? data => throw new NotImplementedException();

        public void Dispose() => throw new NotImplementedException();
    }

    public sealed class DownloadHandlerBuffer : DownloadHandler
    {
    }

    public class UploadHandler : IDisposable
    {
        public string contentType { get; set; } = string.Empty;

        public void Dispose() => throw new NotImplementedException();
    }

    public sealed class UploadHandlerRaw : UploadHandler
    {
        public UploadHandlerRaw(byte[] data)
        {
        }
    }

    public sealed class UnityWebRequestAsyncOperation : AsyncOperation
    {
    }

    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result
        {
            InProgress = 0,
            Success = 1,
            ConnectionError = 2,
            ProtocolError = 3,
            DataProcessingError = 4,
        }

        public UnityWebRequest(Uri uri, string method)
        {
        }

        public DownloadHandler? downloadHandler { get; set; }

        public UploadHandler? uploadHandler { get; set; }

        public int timeout { get; set; }

        public long responseCode => throw new NotImplementedException();

        public string error => throw new NotImplementedException();

        public Result result => throw new NotImplementedException();

        public void SetRequestHeader(string name, string value) => throw new NotImplementedException();

        public Dictionary<string, string>? GetResponseHeaders() => throw new NotImplementedException();

        public UnityWebRequestAsyncOperation SendWebRequest() => throw new NotImplementedException();

        public void Abort() => throw new NotImplementedException();

        public void Dispose() => throw new NotImplementedException();
    }
}
