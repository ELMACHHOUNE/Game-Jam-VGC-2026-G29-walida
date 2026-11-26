using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds a live "Denial Score" test panel to VNDenialEffects: change the real score during Play mode
/// and fire glitch bursts on demand to preview tunnel vision, distortion, audio and music.
/// </summary>
[CustomEditor(typeof(VNDenialEffects))]
public class VNDenialEffectsEditor : Editor
{
    private const int SliderMax = 10;

    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawTestPanel((VNDenialEffects)target);
        EditorGUILayout.Space(6);
        DrawDefaultInspector();
    }

    private static void DrawTestPanel(VNDenialEffects fx)
    {
        EditorGUILayout.LabelField("Denial Score (live testing)", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play mode to change the score live.\n" +
                    "To preview without playing through the choices, tick 'Force Active In Editor' below.",
                    MessageType.Info);
                return;
            }

            int score = fx.DenialScore;
            int threshold = fx.DenialThreshold;

            EditorGUI.BeginChangeCheck();
            int newScore = EditorGUILayout.IntSlider(
                new GUIContent("Score", $"Writes the real story score. The effect turns on above {threshold}."),
                score, 0, Mathf.Max(SliderMax, score));
            if (EditorGUI.EndChangeCheck())
                fx.SetDenialScore(newScore);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("-1")) fx.SetDenialScore(score - 1);
                if (GUILayout.Button("+1")) fx.SetDenialScore(score + 1);
                if (GUILayout.Button($"Reset (0)")) fx.SetDenialScore(0);
                if (GUILayout.Button($"High ({threshold + 1})")) fx.SetDenialScore(threshold + 1);
            }

            string state = fx.IsActive ? "ACTIVE" : "inactive";
            EditorGUILayout.LabelField("State", $"{state}  (blend {fx.Blend:0.00}, turns on above {threshold})");

            if (GUILayout.Button("Trigger Glitch Burst"))
                fx.TriggerGlitchBurstNow();

            if (!fx.HasStory)
                EditorGUILayout.HelpBox("No VNStoryController assigned: the score only drives the effects.", MessageType.Warning);
        }
    }
}
