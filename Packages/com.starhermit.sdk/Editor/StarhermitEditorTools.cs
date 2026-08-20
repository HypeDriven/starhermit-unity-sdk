#if UNITY_EDITOR
using System.IO;
using Starhermit.Platform;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Starhermit.Editor
{
    /// <summary>Editor entry points for setting the package up in a project.</summary>
    public static class StarhermitEditorTools
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string SettingsPath = SettingsFolder + "/StarhermitSettings.asset";

        /// <summary>Creates the settings asset, or selects it when it already exists.</summary>
        [MenuItem("Starhermit/Create Settings Asset")]
        public static void CreateSettingsAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<StarhermitSettings>(SettingsPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                Debug.Log("[Starhermit] Settings already exist at " + SettingsPath);
                return;
            }

            if (!Directory.Exists(SettingsFolder)) Directory.CreateDirectory(SettingsFolder);

            var settings = ScriptableObject.CreateInstance<StarhermitSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = settings;

            Debug.Log("[Starhermit] Created " + SettingsPath +
                      ". Addresses and log levels belong here; tokens and keys never do.");
        }
    }

    /// <summary>
    /// Refuses to build a shipping player against a development endpoint.
    /// </summary>
    /// <remarks>
    /// <c>AllowInsecureTransport</c> exists so a team can point at <c>http://starhermit.test</c> while
    /// developing. Shipping with it enabled would send player sessions over plain HTTP, and the kind of
    /// mistake that happens at 2am before a release is exactly the kind a build hook should catch.
    /// </remarks>
    public sealed class StarhermitBuildValidation : IPreprocessBuildWithReport
    {
        /// <inheritdoc />
        public int callbackOrder => 0;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            if (EditorUserBuildSettings.development) return;

            var settings = AssetDatabase.LoadAssetAtPath<StarhermitSettings>("Assets/Settings/StarhermitSettings.asset");
            if (settings == null) return;

            if (settings.AllowInsecureTransport)
            {
                throw new BuildFailedException(
                    "Starhermit: AllowInsecureTransport is enabled in StarhermitSettings, which would send " +
                    "sessions over plain HTTP. Turn it off for a non-development build, or build with " +
                    "Development Build ticked if this really is a test player.");
            }

            if (!string.IsNullOrEmpty(settings.ApiBaseUri) &&
                settings.ApiBaseUri.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    "Starhermit: the configured API address is plain HTTP (" + settings.ApiBaseUri +
                    "). Point a shipping build at an HTTPS endpoint.");
            }
        }
    }
}
#endif
