using System.Collections.Generic;
using UnityEngine;

public enum UdayaWallKind
{
    StraightRampart,
    CornerTower,
    ArchedGate,
    BastionPlatform
}

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class UdayaRampartWall : MonoBehaviour
{
    [Header("Fortress Wall Settings")]
    [SerializeField] private UdayaWallKind wallKind = UdayaWallKind.StraightRampart;
    [SerializeField] private float length = 14f;
    [SerializeField] private float height = 7.2f;
    [SerializeField] private float thickness = 2.6f;
    [SerializeField] private float merlonHeight = 1.25f;
    [SerializeField] private float merlonSpacing = 2.2f;
    [SerializeField] private float gateOpeningWidth = 5.4f;
    [SerializeField] private float gateOpeningHeight = 5.2f;

    [Header("Ground Cast Shadow")]
    [SerializeField] private bool enableGroundShadow = true;
    [SerializeField] private Vector2 shadowProjectionPerMeter = new Vector2(-0.55f, 0.38f);
    [SerializeField] private float shadowFeatherWidth = 1.25f;

    [Header("UV Atlas Regions")]
    [SerializeField] private Rect roofUV = new Rect(0.008f, 0.508f, 0.317f, 0.484f);
    [SerializeField] private Rect wallMainUV = new Rect(0.341f, 0.508f, 0.317f, 0.484f);
    [SerializeField] private Rect wallSideUV = new Rect(0.675f, 0.508f, 0.317f, 0.484f);
    [SerializeField] private Rect merlonUV = new Rect(0.008f, 0.008f, 0.317f, 0.484f);
    [SerializeField] private Rect gateFrontUV = new Rect(0.341f, 0.008f, 0.317f, 0.484f);

    [Header("Player Cutaway Fade")]
    [SerializeField] private bool enablePlayerCutaway = true;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float fadeSmoothSpeed = 8f;
    [SerializeField, Range(0f, 1f)] private float cutawayAlpha = 0.14f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly Dictionary<Material, Material> TransparentCache = new Dictionary<Material, Material>();
    private static Material SharedShadowMaterial;

    private Mesh _mesh;
    private Mesh _shadowMesh;
    private MeshRenderer _shadowRenderer;
    private Material _opaqueMaterial;
    private MaterialPropertyBlock _propBlock;
    private MaterialPropertyBlock _shadowPropBlock;
    private readonly List<Vector3> _verts = new List<Vector3>(128);
    private readonly List<Vector3> _norms = new List<Vector3>(128);
    private readonly List<Vector2> _uvs = new List<Vector2>(128);
    private readonly List<Color> _colors = new List<Color>(128);
    private readonly List<int> _tris = new List<int>(192);
    private float _currentFade;
    private float _holdTimer;
    private bool _needsRebuild;

    private void OnEnable()
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null && _opaqueMaterial == null)
        {
            _opaqueMaterial = mr.sharedMaterial;
        }

        Rebuild();

        if (Application.isPlaying && playerTransform == null)
        {
            MedinaPlayerController p = FindAnyObjectByType<MedinaPlayerController>();
            if (p != null)
            {
                playerTransform = p.transform;
            }
        }
    }

    private void OnValidate()
    {
        length = Mathf.Max(1.5f, length);
        height = Mathf.Max(1.5f, height);
        thickness = Mathf.Max(0.8f, thickness);
        merlonHeight = Mathf.Clamp(merlonHeight, 0.4f, 3f);
        merlonSpacing = Mathf.Clamp(merlonSpacing, 1.0f, 5f);
        _needsRebuild = true;
    }

    private void LateUpdate()
    {
        if (_needsRebuild)
        {
            _needsRebuild = false;
            Rebuild();
        }

        if (!Application.isPlaying)
        {
            if (transform.hasChanged)
            {
                UpdateSortingOrder();
                transform.hasChanged = false;
            }

            return;
        }

        if (!enablePlayerCutaway || playerTransform == null)
        {
            return;
        }

        bool occluded = IsBlockingPlayer();
        if (occluded)
        {
            _holdTimer = 0.2f;
        }
        else if (_holdTimer > 0f)
        {
            _holdTimer -= Time.deltaTime;
        }

        float target = (occluded || _holdTimer > 0f) ? 1f : 0f;
        _currentFade = Mathf.Lerp(_currentFade, target, 1f - Mathf.Exp(-fadeSmoothSpeed * Time.deltaTime));
        if (Mathf.Abs(_currentFade - target) < 0.002f)
        {
            _currentFade = target;
        }

        ApplyFade(_currentFade);
        UpdateSortingOrder();
    }

    public void Configure(UdayaWallKind kind, float wallLength, float wallHeight, float wallThickness, Material mat)
    {
        wallKind = kind;
        length = wallLength;
        height = wallHeight;
        thickness = wallThickness;
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null && mat != null)
        {
            _opaqueMaterial = mat;
            mr.sharedMaterial = mat;
        }

        Rebuild();
    }

    public void Rebuild()
    {
        _verts.Clear();
        _norms.Clear();
        _uvs.Clear();
        _colors.Clear();
        _tris.Clear();

        switch (wallKind)
        {
            case UdayaWallKind.CornerTower:
                BuildTower();
                break;
            case UdayaWallKind.ArchedGate:
                BuildGate();
                break;
            case UdayaWallKind.BastionPlatform:
                BuildBastion();
                break;
            default:
                BuildStraight();
                break;
        }

        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "UdayaRampartMesh";
            _mesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        _mesh.Clear();
        _mesh.SetVertices(_verts);
        _mesh.SetNormals(_norms);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetColors(_colors);
        _mesh.SetTriangles(_tris, 0);
        _mesh.RecalculateBounds();

        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null)
        {
            mf.sharedMesh = _mesh;
        }

        UpdateColliders();
        UpdateGroundShadow();
        ApplyFade(Application.isPlaying ? _currentFade : 0f);
        UpdateSortingOrder();
    }

    private void BuildStraight()
    {
        int segs = Mathf.Max(1, Mathf.RoundToInt(length / 8.5f));
        float segLen = length / segs;
        float startX = -length * 0.5f;

        for (int i = 0; i < segs; i++)
        {
            float cx = startX + (i + 0.5f) * segLen;
            AddBox(new Vector3(cx, height * 0.5f, 0f), new Vector3(segLen, height, thickness), wallMainUV, wallSideUV, roofUV, 0.84f, 0.96f);
        }

        AddMerlonRowAlongX(length, height, thickness * 0.5f - 0.34f, 0.68f);
        AddMerlonRowAlongX(length, height, -thickness * 0.5f + 0.34f, 0.68f);
    }

    private void BuildTower()
    {
        float w = length;
        float d = thickness;
        AddBox(new Vector3(0f, height * 0.5f, 0f), new Vector3(w, height, d), wallSideUV, wallSideUV, roofUV, 0.82f, 0.90f);
        AddBox(new Vector3(0f, height + 0.25f, 0f), new Vector3(w + 0.4f, 0.5f, d + 0.4f), merlonUV, merlonUV, roofUV, 0.80f, 1.0f);

        float parapetY = height + 0.5f;
        AddMerlonRowAlongX(w + 0.2f, parapetY, d * 0.5f - 0.22f, 0.62f);
        AddMerlonRowAlongX(w + 0.2f, parapetY, -d * 0.5f + 0.22f, 0.62f);
        AddMerlonRowAlongZ(d - 1.1f, parapetY, -w * 0.5f + 0.22f, 0.62f);
        AddMerlonRowAlongZ(d - 1.1f, parapetY, w * 0.5f - 0.22f, 0.62f);
    }

    private void BuildBastion()
    {
        float w = length;
        float d = thickness;
        AddBox(new Vector3(0f, height * 0.5f, 0f), new Vector3(w, height, d), wallMainUV, wallSideUV, roofUV, 0.82f, 0.96f);
        AddMerlonRowAlongX(w, height, d * 0.5f - 0.32f, 0.66f);
        AddMerlonRowAlongX(w, height, -d * 0.5f + 0.32f, 0.66f);
        AddMerlonRowAlongZ(d - 1.2f, height, -w * 0.5f + 0.32f, 0.66f);
        AddMerlonRowAlongZ(d - 1.2f, height, w * 0.5f - 0.32f, 0.66f);
    }

    private void BuildGate()
    {
        float openW = Mathf.Clamp(gateOpeningWidth, 2.5f, length - 3.0f);
        float openH = Mathf.Clamp(gateOpeningHeight, 2.5f, height - 1.6f);
        float pierW = (length - openW) * 0.5f;

        AddBox(new Vector3(-(openW + pierW) * 0.5f, height * 0.5f, 0f), new Vector3(pierW, height, thickness), wallSideUV, wallSideUV, roofUV, 0.80f, 0.94f);
        AddBox(new Vector3((openW + pierW) * 0.5f, height * 0.5f, 0f), new Vector3(pierW, height, thickness), wallSideUV, wallSideUV, roofUV, 0.80f, 0.94f);

        float lintelH = height - openH;
        AddBox(new Vector3(0f, openH + lintelH * 0.5f, 0f), new Vector3(openW + 0.1f, lintelH, thickness), gateFrontUV, wallSideUV, roofUV, 0.78f, 0.96f);

        float corbelW = 0.85f;
        float corbelH = 0.9f;
        AddBox(new Vector3(-openW * 0.5f + corbelW * 0.45f, openH - corbelH * 0.45f, 0f), new Vector3(corbelW, corbelH, thickness * 0.94f), merlonUV, merlonUV, roofUV, 0.76f, 0.92f);
        AddBox(new Vector3(openW * 0.5f - corbelW * 0.45f, openH - corbelH * 0.45f, 0f), new Vector3(corbelW, corbelH, thickness * 0.94f), merlonUV, merlonUV, roofUV, 0.76f, 0.92f);

        AddBox(new Vector3(0f, height + 0.25f, 0f), new Vector3(length + 0.45f, 0.5f, thickness + 0.45f), merlonUV, merlonUV, roofUV, 0.80f, 1.0f);
        AddMerlonRowAlongX(length + 0.2f, height + 0.5f, thickness * 0.5f - 0.25f, 0.68f);
        AddMerlonRowAlongX(length + 0.2f, height + 0.5f, -thickness * 0.5f + 0.25f, 0.68f);
    }

    private void AddMerlonRowAlongX(float span, float baseY, float zPos, float depth)
    {
        int count = Mathf.Max(2, Mathf.FloorToInt(span / merlonSpacing));
        float step = span / count;
        float baseW = Mathf.Min(step * 0.64f, 1.35f);
        float capW = baseW * 0.52f;
        float lowerH = merlonHeight * 0.56f;
        float upperH = merlonHeight * 0.44f;

        for (int i = 0; i < count; i++)
        {
            float cx = -span * 0.5f + (i + 0.5f) * step;
            AddBox(new Vector3(cx, baseY + lowerH * 0.5f, zPos), new Vector3(baseW, lowerH, depth), merlonUV, merlonUV, roofUV, 0.86f, 1.0f);
            AddBox(new Vector3(cx, baseY + lowerH + upperH * 0.5f, zPos), new Vector3(capW, upperH, depth * 0.85f), merlonUV, merlonUV, roofUV, 0.92f, 1.04f);
        }
    }

    private void AddMerlonRowAlongZ(float span, float baseY, float xPos, float depth)
    {
        int count = Mathf.Max(1, Mathf.FloorToInt(span / merlonSpacing));
        float step = span / count;
        float baseW = Mathf.Min(step * 0.64f, 1.35f);
        float capW = baseW * 0.52f;
        float lowerH = merlonHeight * 0.56f;
        float upperH = merlonHeight * 0.44f;

        for (int i = 0; i < count; i++)
        {
            float cz = -span * 0.5f + (i + 0.5f) * step;
            AddBox(new Vector3(xPos, baseY + lowerH * 0.5f, cz), new Vector3(depth, lowerH, baseW), merlonUV, merlonUV, roofUV, 0.86f, 1.0f);
            AddBox(new Vector3(xPos, baseY + lowerH + upperH * 0.5f, cz), new Vector3(depth * 0.85f, upperH, capW), merlonUV, merlonUV, roofUV, 0.92f, 1.04f);
        }
    }

    private void AddBox(Vector3 center, Vector3 size, Rect frontBackUV, Rect sideUV, Rect topUV, float aoBottom, float aoTop)
    {
        float hx = size.x * 0.5f;
        float hy = size.y * 0.5f;
        float hz = size.z * 0.5f;

        Vector3 p000 = center + new Vector3(-hx, -hy, -hz);
        Vector3 p100 = center + new Vector3(hx, -hy, -hz);
        Vector3 p110 = center + new Vector3(hx, hy, -hz);
        Vector3 p010 = center + new Vector3(-hx, hy, -hz);

        Vector3 p001 = center + new Vector3(-hx, -hy, hz);
        Vector3 p101 = center + new Vector3(hx, -hy, hz);
        Vector3 p111 = center + new Vector3(hx, hy, hz);
        Vector3 p011 = center + new Vector3(-hx, hy, hz);

        AddQuadAO(p000, p100, p110, p010, Vector3.back, frontBackUV, aoBottom, aoBottom, aoTop, aoTop);
        AddQuadAO(p101, p001, p011, p111, Vector3.forward, frontBackUV, aoBottom, aoBottom, aoTop, aoTop);
        AddQuadAO(p001, p000, p010, p011, Vector3.left, sideUV, aoBottom, aoBottom, aoTop, aoTop);
        AddQuadAO(p100, p101, p111, p110, Vector3.right, sideUV, aoBottom, aoBottom, aoTop, aoTop);
        AddQuadAO(p010, p110, p111, p011, Vector3.up, topUV, aoTop, aoTop, aoTop, aoTop);
    }

    private void AddQuadAO(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 normal, Rect uvRect, float aoBL, float aoBR, float aoTR, float aoTL)
    {
        int v = _verts.Count;
        _verts.Add(bl);
        _verts.Add(br);
        _verts.Add(tr);
        _verts.Add(tl);

        _norms.Add(normal);
        _norms.Add(normal);
        _norms.Add(normal);
        _norms.Add(normal);

        _uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
        _uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
        _uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
        _uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));

        _colors.Add(new Color(aoBL, aoBL, aoBL, 1f));
        _colors.Add(new Color(aoBR, aoBR, aoBR, 1f));
        _colors.Add(new Color(aoTR, aoTR, aoTR, 1f));
        _colors.Add(new Color(aoTL, aoTL, aoTL, 1f));

        _tris.Add(v);
        _tris.Add(v + 2);
        _tris.Add(v + 1);
        _tris.Add(v);
        _tris.Add(v + 3);
        _tris.Add(v + 2);
    }

    private void UpdateGroundShadow()
    {
        Transform existing = transform.Find("WallGroundShadow");
        if (!enableGroundShadow)
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }

        GameObject shadowObj;
        if (existing == null)
        {
            shadowObj = new GameObject("WallGroundShadow");
            shadowObj.transform.SetParent(transform, false);
            shadowObj.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }
        else
        {
            shadowObj = existing.gameObject;
            shadowObj.SetActive(true);
        }

        shadowObj.transform.localPosition = Vector3.zero;
        shadowObj.transform.localRotation = Quaternion.identity;
        shadowObj.transform.localScale = Vector3.one;

        MeshFilter smf = shadowObj.GetComponent<MeshFilter>();
        if (smf == null) smf = shadowObj.AddComponent<MeshFilter>();
        _shadowRenderer = shadowObj.GetComponent<MeshRenderer>();
        if (_shadowRenderer == null) _shadowRenderer = shadowObj.AddComponent<MeshRenderer>();

        if (_shadowMesh == null)
        {
            _shadowMesh = new Mesh();
            _shadowMesh.name = "WallShadowMesh";
            _shadowMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        float hx = length * 0.5f;
        float hz = thickness * 0.5f;
        Vector2[] basePoly = new[]
        {
            new Vector2(-hx, -hz),
            new Vector2(hx, -hz),
            new Vector2(hx, hz),
            new Vector2(-hx, hz)
        };

        float totalH = height + merlonHeight * 0.7f;
        Vector3 worldOffset = new Vector3(shadowProjectionPerMeter.x * totalH, 0f, shadowProjectionPerMeter.y * totalH);
        Vector3 localOff3 = transform.InverseTransformVector(worldOffset);
        Vector2 localOffset = new Vector2(localOff3.x, localOff3.z);

        List<Vector2> pts = new List<Vector2>(8);
        for (int i = 0; i < 4; i++)
        {
            pts.Add(basePoly[i]);
            pts.Add(basePoly[i] + localOffset);
        }

        List<Vector2> hull = ComputeConvexHull(pts);
        Vector2 center = Vector2.zero;
        for (int i = 0; i < hull.Count; i++) center += hull[i];
        center /= Mathf.Max(1, hull.Count);

        int m = hull.Count;
        List<Vector3> sv = new List<Vector3>(m * 2 + 1);
        List<Color> sc = new List<Color>(m * 2 + 1);
        List<int> st = new List<int>(m * 9);
        float sy = 0.022f;

        sv.Add(new Vector3(center.x, sy, center.y));
        sc.Add(new Color(1f, 1f, 1f, 1f));

        for (int i = 0; i < m; i++)
        {
            sv.Add(new Vector3(hull[i].x, sy, hull[i].y));
            sc.Add(new Color(1f, 1f, 1f, 0.90f));
        }

        for (int i = 0; i < m; i++)
        {
            Vector2 p = hull[i];
            Vector2 prev = hull[(i - 1 + m) % m];
            Vector2 next = hull[(i + 1) % m];
            Vector2 e1 = (p - prev).normalized;
            Vector2 e2 = (next - p).normalized;
            Vector2 n1 = new Vector2(e1.y, -e1.x);
            Vector2 n2 = new Vector2(e2.y, -e2.x);
            Vector2 outDir = (n1 + n2).sqrMagnitude > 0.001f ? (n1 + n2).normalized : (p - center).normalized;
            Vector2 outer = p + outDir * shadowFeatherWidth;
            sv.Add(new Vector3(outer.x, sy, outer.y));
            sc.Add(new Color(1f, 1f, 1f, 0f));
        }

        for (int i = 0; i < m; i++)
        {
            int next = (i + 1) % m;
            st.Add(0);
            st.Add(1 + next);
            st.Add(1 + i);

            int in0 = 1 + i;
            int in1 = 1 + next;
            int out0 = 1 + m + i;
            int out1 = 1 + m + next;
            st.Add(in0);
            st.Add(in1);
            st.Add(out1);
            st.Add(in0);
            st.Add(out1);
            st.Add(out0);
        }

        _shadowMesh.Clear();
        _shadowMesh.SetVertices(sv);
        _shadowMesh.SetColors(sc);
        _shadowMesh.SetTriangles(st, 0);
        _shadowMesh.RecalculateBounds();
        smf.sharedMesh = _shadowMesh;

        Material mat = GetSharedShadowMaterial();
        if (mat != null)
        {
            _shadowRenderer.sharedMaterial = mat;
        }
        else
        {
            // Never leave a renderer without a material: it would draw as magenta (missing shader)
            _shadowRenderer.enabled = false;
        }

        _shadowRenderer.sortingOrder = -29200;
        _shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _shadowRenderer.receiveShadows = false;
    }

    private static Material GetSharedShadowMaterial()
    {
        if (SharedShadowMaterial != null) return SharedShadowMaterial;
        // Loaded from Resources so the shader is guaranteed to ship (Shader.Find alone gets stripped in builds)
        Material template = Resources.Load<Material>("Mat_MedinaGroundCastShadow");
        Shader sh = template != null ? template.shader : Shader.Find("Medina/GroundCastShadow");
        if (sh == null || !sh.isSupported)
        {
            Debug.LogWarning("[UdayaRampartWall] Ground shadow shader 'Medina/GroundCastShadow' is missing from this build; wall shadows disabled.");
            return null;
        }
        SharedShadowMaterial = template != null ? new Material(template) : new Material(sh);
        SharedShadowMaterial.name = "Mat_UdayaWallShadow_Runtime";
        SharedShadowMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        SharedShadowMaterial.SetColor("_ShadowColor", new Color(0.09f, 0.13f, 0.25f, 0.44f));
        return SharedShadowMaterial;
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static List<Vector2> ComputeConvexHull(List<Vector2> points)
    {
        if (points.Count <= 3) return points;
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        List<Vector2> lower = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            while (lower.Count >= 2 && Cross2D(lower[lower.Count - 1] - lower[lower.Count - 2], points[i] - lower[lower.Count - 2]) <= 0f)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(points[i]);
        }

        List<Vector2> upper = new List<Vector2>();
        for (int i = points.Count - 1; i >= 0; i--)
        {
            while (upper.Count >= 2 && Cross2D(upper[upper.Count - 1] - upper[upper.Count - 2], points[i] - upper[upper.Count - 2]) <= 0f)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(points[i]);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private void UpdateColliders()
    {
        BoxCollider[] existing = GetComponents<BoxCollider>();
        if (wallKind == UdayaWallKind.ArchedGate)
        {
            float openW = Mathf.Clamp(gateOpeningWidth, 2.5f, length - 3.0f);
            float pierW = (length - openW) * 0.5f;

            while (existing.Length < 2)
            {
                gameObject.AddComponent<BoxCollider>();
                existing = GetComponents<BoxCollider>();
            }

            existing[0].enabled = true;
            existing[0].center = new Vector3(-(openW + pierW) * 0.5f, height * 0.5f, 0f);
            existing[0].size = new Vector3(pierW, height, thickness);

            existing[1].enabled = true;
            existing[1].center = new Vector3((openW + pierW) * 0.5f, height * 0.5f, 0f);
            existing[1].size = new Vector3(pierW, height, thickness);
            return;
        }

        BoxCollider box = existing.Length > 0 ? existing[0] : gameObject.AddComponent<BoxCollider>();
        box.enabled = true;
        box.center = new Vector3(0f, height * 0.5f, 0f);
        box.size = new Vector3(length, height, thickness);

        for (int i = 1; i < existing.Length; i++)
        {
            existing[i].enabled = false;
        }
    }

    private bool IsBlockingPlayer()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return false;
        }

        BoxCollider[] cols = GetComponents<BoxCollider>();
        if (cols.Length == 0)
        {
            return false;
        }

        Vector3 camPos = cam.transform.position;
        Vector3 playerPos = playerTransform.position + Vector3.up * 1.1f;
        Vector3 dir = playerPos - camPos;
        float dist = dir.magnitude;
        if (dist < 0.1f)
        {
            return false;
        }

        Ray ray = new Ray(camPos, dir / dist);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i].enabled && cols[i].Raycast(ray, out _, dist))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyFade(float fade)
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null)
        {
            return;
        }

        if (_opaqueMaterial == null)
        {
            _opaqueMaterial = mr.sharedMaterial;
        }

        bool useTransparent = Application.isPlaying && fade > 0.01f;
        float alpha = useTransparent ? Mathf.Lerp(1f, cutawayAlpha, Mathf.SmoothStep(0f, 1f, fade)) : 1f;

        if (useTransparent && _opaqueMaterial != null)
        {
            mr.sharedMaterial = GetOrCreateTransparent(_opaqueMaterial);
        }
        else if (_opaqueMaterial != null && mr.sharedMaterial != _opaqueMaterial)
        {
            mr.sharedMaterial = _opaqueMaterial;
        }

        if (_propBlock == null)
        {
            _propBlock = new MaterialPropertyBlock();
        }

        mr.GetPropertyBlock(_propBlock);
        Color tint = new Color(1f, 1f, 1f, alpha);
        _propBlock.SetColor(BaseColorId, tint);
        _propBlock.SetColor(ColorId, tint);
        mr.SetPropertyBlock(_propBlock);

        if (_shadowRenderer != null)
        {
            if (_shadowPropBlock == null) _shadowPropBlock = new MaterialPropertyBlock();
            _shadowRenderer.GetPropertyBlock(_shadowPropBlock);
            _shadowPropBlock.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
            _shadowRenderer.SetPropertyBlock(_shadowPropBlock);
        }
    }

    private static Material GetOrCreateTransparent(Material source)
    {
        if (TransparentCache.TryGetValue(source, out Material cached) && cached != null)
        {
            return cached;
        }

        Material mat = new Material(source);
        mat.name = source.name + "_Fade";
        mat.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        TransparentCache[source] = mat;
        return mat;
    }

    public void UpdateSortingOrder()
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null)
        {
            return;
        }

        if (Application.isPlaying && _currentFade > 0.04f)
        {
            mr.sortingOrder = -28000;
        }
        else
        {
            mr.sortingOrder = -Mathf.RoundToInt(transform.position.z * 100f);
        }

        if (_shadowRenderer != null)
        {
            _shadowRenderer.sortingOrder = -29200;
        }
    }

    private void OnDestroy()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }

        if (_shadowMesh != null)
        {
            if (Application.isPlaying) Destroy(_shadowMesh);
            else DestroyImmediate(_shadowMesh);
        }
    }
}
