// Stand-ins for the UnityEditor APIs the package's editor tooling uses. See UnityEngineStubs.cs.
using System;
using UnityEngine;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName)
        {
        }
    }

    public static class AssetDatabase
    {
        public static T? LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => throw new NotImplementedException();

        public static void CreateAsset(UnityEngine.Object asset, string path) => throw new NotImplementedException();

        public static void SaveAssets() => throw new NotImplementedException();

        public static void Refresh() => throw new NotImplementedException();
    }

    public static class Selection
    {
        public static UnityEngine.Object? activeObject { get; set; }
    }

    public static class EditorUserBuildSettings
    {
        public static bool development => throw new NotImplementedException();

        public static BuildTarget activeBuildTarget => throw new NotImplementedException();
    }

    public enum BuildTarget
    {
        StandaloneLinux64 = 24,
    }

    public enum BuildTargetGroup
    {
        Standalone = 1,
    }

    public enum ScriptingImplementation
    {
        Mono2x = 0,
        IL2CPP = 1,
    }

    public enum ManagedStrippingLevel
    {
        Disabled = 0,
        Low = 1,
        Medium = 2,
        High = 3,
    }

    [Flags]
    public enum BuildOptions
    {
        None = 0,
        StrictMode = 512,
    }

    public sealed class BuildPlayerOptions
    {
        public string[] scenes { get; set; } = Array.Empty<string>();

        public BuildTarget target { get; set; }

        public string locationPathName { get; set; } = string.Empty;

        public BuildOptions options { get; set; }
    }

    public static class PlayerSettings
    {
        public static void SetScriptingBackend(BuildTargetGroup group, ScriptingImplementation backend) =>
            throw new NotImplementedException();

        public static void SetManagedStrippingLevel(BuildTargetGroup group, ManagedStrippingLevel level) =>
            throw new NotImplementedException();
    }

    public static class BuildPipeline
    {
        public static BuildTargetGroup GetBuildTargetGroup(BuildTarget target) => throw new NotImplementedException();

        public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) =>
            throw new NotImplementedException();
    }
}

namespace UnityEditor.Build
{
    public interface IOrderedCallback
    {
        int callbackOrder { get; }
    }

    public interface IPreprocessBuildWithReport : IOrderedCallback
    {
        void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report);
    }

    public sealed class BuildFailedException : Exception
    {
        public BuildFailedException(string message) : base(message)
        {
        }
    }
}

namespace UnityEditor.Build.Reporting
{
    public sealed class BuildReport
    {
        public BuildSummary summary => throw new NotImplementedException();
    }

    public sealed class BuildSummary
    {
        public BuildResult result => throw new NotImplementedException();
    }

    public enum BuildResult
    {
        Unknown = 0,
        Succeeded = 1,
        Failed = 2,
        Cancelled = 3,
    }
}
