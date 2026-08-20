#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Starhermit.Editor
{
    /// <summary>
    /// Build entry point used by CI to prove the package survives IL2CPP with high stripping.
    /// </summary>
    /// <remarks>
    /// The package maps JSON by hand precisely so stripping has nothing to break, and this is what
    /// checks that claim on every supported target rather than trusting it.
    /// </remarks>
    public static class StarhermitCiBuild
    {
        /// <summary>Builds a stripped IL2CPP player for the active build target.</summary>
        public static void BuildStripped()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);

            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(group, ManagedStrippingLevel.High);

            var options = new BuildPlayerOptions
            {
                scenes = Array.Empty<string>(),
                target = target,
                locationPathName = "build-output/starhermit-ci",
                options = BuildOptions.StrictMode
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception(
                    $"Starhermit CI build failed for {target}: {report.summary.result}. " +
                    "A failure here usually means managed stripping removed something the SDK needs; " +
                    "check Runtime/link.xml.");
            }

            Debug.Log($"[Starhermit] CI build succeeded for {target}.");
        }
    }
}
#endif
