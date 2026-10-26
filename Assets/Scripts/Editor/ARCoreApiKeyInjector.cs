using System;
using System.IO;
using Google.XR.ARCoreExtensions.Internal;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AuraSync.EditorTools
{
    /// <summary>
    /// Keeps the ARCore Cloud Anchors API key out of version control.
    ///
    /// ARCore Extensions stores the key in <c>ProjectSettings/ARCoreExtensionsProjectSettings.json</c>,
    /// which git and UVCS both track, and that is how the first key leaked to GitHub (GitGuardian
    /// alert, 2026-10-26). The key now lives in <c>UserSettings/ARCoreApiKey.txt</c>, which both
    /// ignore, and is handed to ARCore in memory only for the length of a build, so the APK's
    /// manifest still gets it and the tracked file never does.
    ///
    /// Pasting a key into Project Settings > XR Plug-in Management > ARCore Extensions still works:
    /// the editor notices the settings file change and moves the key out within a couple of seconds.
    /// A build machine can set the <c>ARCORE_API_KEY</c> environment variable instead of the file.
    /// </summary>
    [InitializeOnLoad]
    public class ARCoreApiKeyInjector : IPreprocessBuildWithReport
    {
        private const string KeyFile = "UserSettings/ARCoreApiKey.txt";
        private const string EnvVar = "ARCORE_API_KEY";
        private const string SettingsFile = "ProjectSettings/ARCoreExtensionsProjectSettings.json";
        private const double PollSeconds = 2.0;

        private static DateTime _settingsWriteTime;
        private static double _nextPoll;

        // Before every ARCore build step (they use 0 and 1), so the manifest sees the key.
        public int callbackOrder => -100;

        static ARCoreApiKeyInjector()
        {
            EditorApplication.delayCall += MoveKeyOutOfProjectSettings;
            EditorApplication.update += Poll;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;

            MoveKeyOutOfProjectSettings();
            ARCoreExtensionsProjectSettings settings = ARCoreExtensionsProjectSettings.Instance;
            if (settings.AndroidAuthenticationStrategySetting != AndroidAuthenticationStrategy.ApiKey) return;

            string key = ReadKey();
            if (string.IsNullOrEmpty(key))
            {
                // An APK without the key builds fine and then fails every Cloud Anchor on the phone,
                // which is far harder to trace than stopping here.
                throw new BuildFailedException(
                    $"[ARCoreApiKey] No ARCore API key. Put it in {KeyFile} (one line, never committed) " +
                    $"or set the {EnvVar} environment variable, then build again.");
            }

            settings.AndroidCloudServicesApiKey = key; // memory only - never Save() here
            EditorApplication.delayCall += ForgetKey; // runs once the build has finished or failed
            Debug.Log($"[ARCoreApiKey] Using the ARCore API key from {(Environment.GetEnvironmentVariable(EnvVar) == key ? EnvVar : KeyFile)} for this build.");
        }

        private static void ForgetKey()
        {
            ARCoreExtensionsProjectSettings.Instance.AndroidCloudServicesApiKey = string.Empty;
        }

        private static string ReadKey()
        {
            string env = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            return File.Exists(KeyFile) ? File.ReadAllText(KeyFile).Trim() : null;
        }

        /// <summary>Cheap check (one file time) for the settings page having just saved a key.</summary>
        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            if (!File.Exists(SettingsFile)) return;

            DateTime written = File.GetLastWriteTimeUtc(SettingsFile);
            if (written == _settingsWriteTime) return;
            _settingsWriteTime = written;
            MoveKeyOutOfProjectSettings();
        }

        /// <summary>
        /// If the tracked settings file holds a key, moves it to <see cref="KeyFile"/> and blanks it
        /// there. The strategy stays "API Key", so builds keep asking for one.
        /// </summary>
        private static void MoveKeyOutOfProjectSettings()
        {
            if (BuildPipeline.isBuildingPlayer) return; // the key is in memory on purpose

            ARCoreExtensionsProjectSettings settings = ARCoreExtensionsProjectSettings.Instance;
            string key = settings.AndroidCloudServicesApiKey;
            if (string.IsNullOrWhiteSpace(key)) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KeyFile));
                File.WriteAllText(KeyFile, key.Trim());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ARCoreApiKey] Could not write {KeyFile}, so the key stays in {SettingsFile} - do not commit that file: {ex.Message}");
                return;
            }

            settings.AndroidAuthenticationStrategySetting = AndroidAuthenticationStrategy.ApiKey;
            settings.AndroidCloudServicesApiKey = string.Empty;
            settings.Save();
            _settingsWriteTime = File.GetLastWriteTimeUtc(SettingsFile);
            Debug.Log($"[ARCoreApiKey] Moved the ARCore API key out of {SettingsFile} into {KeyFile} (ignored by git and UVCS).");
        }
    }
}
