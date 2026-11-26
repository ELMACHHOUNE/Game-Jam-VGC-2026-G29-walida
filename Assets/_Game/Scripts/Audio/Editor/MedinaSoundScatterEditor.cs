using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Walida.Audio.Editor
{
    /// <summary>
    /// Editor utility to bake or clear the scattered spatial sound emitters in <c>MedinaMainLevel</c>
    /// so designers can inspect gizmo radii and tweak individual element sounds in the Inspector.
    /// </summary>
    public static class MedinaSoundScatterEditor
    {
        [MenuItem("Tools/Audio/Scatter Medina Map Sounds")]
        public static void ScatterSoundsInActiveScene()
        {
            MedinaAmbienceController controller = Object.FindAnyObjectByType<MedinaAmbienceController>();
            if (controller == null)
            {
                MedinaLevelManager manager = Object.FindAnyObjectByType<MedinaLevelManager>();
                if (manager != null)
                {
                    controller = Undo.AddComponent<MedinaAmbienceController>(manager.gameObject);
                }
                else
                {
                    var host = new GameObject("MedinaAmbienceController");
                    Undo.RegisterCreatedObjectUndo(host, "Create MedinaAmbienceController");
                    controller = host.AddComponent<MedinaAmbienceController>();
                }
            }

            GameObject container = MedinaAmbienceController.PopulateMapSoundscape(controller.transform);
            Undo.RegisterCreatedObjectUndo(container, "Scatter Medina Map Sounds");
            EditorSceneManager.MarkSceneDirty(container.scene);
            Selection.activeGameObject = container;

            int count = container.GetComponentsInChildren<MedinaMapSoundEmitter>().Length;
            Debug.Log($"[MedinaAudio] Scattered {count} spatial sound emitters across the map under '{container.name}'.");
        }

        [MenuItem("Tools/Audio/Clear Scattered Medina Sounds")]
        public static void ClearScatteredSounds()
        {
            GameObject existing = GameObject.Find(MedinaAmbienceController.SoundscapeContainerName);
            if (existing == null)
            {
                Debug.Log("[MedinaAudio] No scattered sound container found in the active scene.");
                return;
            }

            var scene = existing.scene;
            Undo.DestroyObjectImmediate(existing);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[MedinaAudio] Cleared scattered Medina map sounds.");
        }
    }
}
