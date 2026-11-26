using FMODUnity;
using UnityEditor;
using UnityEngine;

namespace Walida.Audio.Editor
{
    /// <summary>
    /// Links the FMOD for Unity integration to the exported FMOD Studio banks that live in
    /// <c>FMOD/Build/Desktop</c> (project root, next to Assets/). Runs once automatically on a fresh
    /// checkout (when no bank source has been configured yet) and can be re-run from the menu.
    /// </summary>
    [InitializeOnLoad]
    internal static class FMODBankAutoSetup
    {
        // Relative to the Unity project root. Drop newly exported Master.bank / Master.strings.bank here.
        public const string BankFolder = "FMOD/Build/Desktop";

        static FMODBankAutoSetup()
        {
            // Settings.Instance must not be touched from an InitializeOnLoad constructor (FMOD warns about it),
            // so defer until the editor has finished loading.
            EditorApplication.delayCall += () => Configure(force: false);
        }

        [MenuItem("Tools/Audio/Link FMOD Banks (FMOD/Build/Desktop)")]
        private static void ConfigureFromMenu() => Configure(force: true);

        private static void Configure(bool force)
        {
            FMODUnity.Settings settings = FMODUnity.Settings.Instance;
            if (settings == null)
                return;

            bool alreadyLinked = !settings.HasSourceProject
                                 && !settings.HasPlatforms
                                 && settings.SourceBankPath == BankFolder;
            if (alreadyLinked && !force)
                return;

            // Respect a bank source someone configured manually in FMOD > Edit Settings.
            if (!force && !string.IsNullOrEmpty(settings.SourceBankPath))
                return;

            if (!System.IO.File.Exists(System.IO.Path.Combine(BankFolder, "Master.bank")))
            {
                Debug.LogWarning($"[FMOD Setup] No Master.bank found in '{BankFolder}'. Export banks from FMOD Studio there.");
                return;
            }

            settings.HasSourceProject = false;   // "Single Platform Build" source type
            settings.HasPlatforms = false;
            settings.SourceBankPath = BankFolder;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            FMODUnity.EventManager.RefreshBanks();
            Debug.Log($"[FMOD Setup] Linked FMOD banks from '{BankFolder}'. Browse events via FMOD > Event Browser.");
        }
    }
}
