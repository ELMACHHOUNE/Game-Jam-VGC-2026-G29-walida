using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MedinaPlotMarker))]
public class MedinaPlotMarkerEditor : Editor
{
    [MenuItem("Tools/Medina/New Building Plot %#p")]
    public static void CreateNewPlot()
    {
        GameObject root = GameObject.Find("MapPlots");
        if (root == null)
        {
            root = new GameObject("MapPlots");
            Undo.RegisterCreatedObjectUndo(root, "Create MapPlots");
        }

        Vector3 spawnPos = Vector3.zero;
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            spawnPos = new Vector3(sv.pivot.x, 0f, sv.pivot.z);
        }

        int index = root.transform.childCount + 1;
        GameObject go = new GameObject($"Plot_{index:00}");
        Undo.RegisterCreatedObjectUndo(go, "Create Building Plot");
        go.transform.SetParent(root.transform, false);
        go.transform.position = spawnPos;
        go.AddComponent<MedinaPlotMarker>();

        Selection.activeGameObject = go;
    }

    [MenuItem("Tools/Medina/Toggle Existing Buildings Visibility")]
    public static void ToggleExistingBuildings()
    {
        GameObject env = GameObject.Find("KasbahEnvironment");
        if (env == null)
        {
            return;
        }

        Undo.RecordObject(env, "Toggle KasbahEnvironment");
        env.SetActive(!env.activeSelf);
    }

    public override void OnInspectorGUI()
    {
        MedinaPlotMarker plot = (MedinaPlotMarker)target;
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rectangle (4)"))
        {
            Undo.RecordObject(plot, "Reset Plot Rectangle");
            plot.localCorners.Clear();
            plot.localCorners.Add(new Vector2(-5f, -4f));
            plot.localCorners.Add(new Vector2(5f, -4f));
            plot.localCorners.Add(new Vector2(5f, 4f));
            plot.localCorners.Add(new Vector2(-5f, 4f));
            EditorUtility.SetDirty(plot);
        }

        if (GUILayout.Button("Wedge (4)"))
        {
            Undo.RecordObject(plot, "Reset Plot Wedge");
            plot.localCorners.Clear();
            plot.localCorners.Add(new Vector2(-6f, -4f));
            plot.localCorners.Add(new Vector2(6f, -4f));
            plot.localCorners.Add(new Vector2(3f, 4f));
            plot.localCorners.Add(new Vector2(-4f, 4f));
            EditorUtility.SetDirty(plot);
        }

        if (GUILayout.Button("L-Shape (6)"))
        {
            Undo.RecordObject(plot, "Reset Plot L-Shape");
            plot.localCorners.Clear();
            plot.localCorners.Add(new Vector2(-6f, -5f));
            plot.localCorners.Add(new Vector2(6f, -5f));
            plot.localCorners.Add(new Vector2(6f, 0f));
            plot.localCorners.Add(new Vector2(0f, 0f));
            plot.localCorners.Add(new Vector2(0f, 5f));
            plot.localCorners.Add(new Vector2(-6f, 5f));
            EditorUtility.SetDirty(plot);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Remove Last Corner") && plot.localCorners.Count > 3)
        {
            Undo.RecordObject(plot, "Remove Corner");
            plot.localCorners.RemoveAt(plot.localCorners.Count - 1);
            EditorUtility.SetDirty(plot);
        }

        if (GUILayout.Button("Center Pivot"))
        {
            Undo.RecordObject(plot.transform, "Center Pivot Transform");
            Undo.RecordObject(plot, "Center Pivot Corners");
            plot.RecenterPivot();
            EditorUtility.SetDirty(plot);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("Create Another Plot"))
        {
            CreateNewPlot();
        }
    }

    private void OnSceneGUI()
    {
        MedinaPlotMarker plot = (MedinaPlotMarker)target;
        if (plot.localCorners == null || plot.localCorners.Count < 3)
        {
            return;
        }

        Vector3[] worldPts = plot.GetWorldCorners(0.3f);
        Color baseColor = plot.GetKindColor();

        Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.25f);
        Handles.DrawAAConvexPolygon(worldPts);

        Handles.color = baseColor;
        for (int i = 0; i < worldPts.Length; i++)
        {
            Vector3 a = worldPts[i];
            Vector3 b = worldPts[(i + 1) % worldPts.Length];
            Handles.DrawAAPolyLine(4f, a, b);

            Vector3 mid = (a + b) * 0.5f;
            float midSize = HandleUtility.GetHandleSize(mid) * 0.11f;
            Handles.color = Color.white;
            if (Handles.Button(mid, Quaternion.LookRotation(Vector3.up), midSize, midSize * 1.4f, Handles.DotHandleCap))
            {
                Undo.RecordObject(plot, "Insert Plot Corner");
                Vector3 localMid = plot.transform.InverseTransformPoint(mid);
                plot.localCorners.Insert(i + 1, new Vector2(localMid.x, localMid.z));
                EditorUtility.SetDirty(plot);
                return;
            }

            Handles.color = baseColor;
        }

        Event evt = Event.current;
        for (int i = 0; i < worldPts.Length; i++)
        {
            Vector3 pt = worldPts[i];
            float handleSize = HandleUtility.GetHandleSize(pt) * 0.16f;

            if (evt.shift && plot.localCorners.Count > 3)
            {
                Handles.color = Color.red;
                if (Handles.Button(pt, Quaternion.LookRotation(Vector3.up), handleSize, handleSize * 1.3f, Handles.SphereHandleCap))
                {
                    Undo.RecordObject(plot, "Delete Plot Corner");
                    plot.localCorners.RemoveAt(i);
                    EditorUtility.SetDirty(plot);
                    return;
                }

                continue;
            }

            Handles.color = Color.white;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(pt, handleSize, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(plot, "Move Plot Corner");
                moved.y = plot.transform.position.y;
                Vector3 localPt = plot.transform.InverseTransformPoint(moved);
                plot.localCorners[i] = new Vector2(localPt.x, localPt.z);
                EditorUtility.SetDirty(plot);
            }
        }

        Vector3 center = Vector3.zero;
        for (int i = 0; i < worldPts.Length; i++) center += worldPts[i];
        center /= worldPts.Length;
        Handles.Label(center + Vector3.up * 0.5f, $"{plot.name} ({plot.kind})");
    }
}
