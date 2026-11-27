using System.Collections.Generic;
using UnityEngine;

public enum CardboardUVMode
{
    FitWrapper,
    AdaptiveAspect
}

public enum CardboardBlockShape
{
    Box,
    Trapezoid,
    CornerChamfer,
    LShape,
    CustomPolygon
}

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class CardboardHouseBlock : MonoBehaviour
{
    [Header("Shape & Dimensions")]
    [SerializeField] private CardboardBlockShape shape = CardboardBlockShape.Box;
    [SerializeField] private Vector3 boxSize = new Vector3(4.5f, 3.8f, 3.6f);
    [SerializeField, Range(0.25f, 1.5f)] private float trapezoidBackWidthRatio = 0.58f;
    [SerializeField, Range(-0.5f, 0.5f)] private float trapezoidSkewX = 0f;
    [SerializeField, Range(0.15f, 0.65f)] private float chamferRatio = 0.42f;
    [SerializeField] private bool chamferRightCorner = true;
    [SerializeField, Range(0.25f, 0.72f)] private float lCutoutWidthRatio = 0.50f;
    [SerializeField, Range(0.25f, 0.72f)] private float lCutoutDepthRatio = 0.50f;
    [SerializeField] private bool mirrorLShape = false;
    [SerializeField] private List<Vector2> customFootprint = new List<Vector2>();

    [Header("3D Cardboard Roof Parapet & Cast Shadow")]
    [SerializeField] private bool enableRoofParapet = true;
    [SerializeField, Range(0.15f, 1.2f)] private float parapetHeight = 0.55f;
    [SerializeField, Range(0.15f, 0.85f)] private float parapetThickness = 0.44f;
    [SerializeField] private bool enableGroundShadow = true;
    [SerializeField] private Vector2 shadowProjectionPerMeter = new Vector2(-0.55f, 0.38f);
    [SerializeField] private float shadowFeatherWidth = 1.05f;

    [Header("UV Wrapper Settings")]
    [SerializeField] private CardboardUVMode uvMode = CardboardUVMode.AdaptiveAspect;
    [SerializeField] private float referenceFaceSize = 4.0f;
    [SerializeField] private float maxWallSegmentLength = 10.5f;
    [SerializeField] private Rect roofUV = new Rect(0f, 0.5f, 1f / 3f, 0.5f);
    [SerializeField] private Rect frontUV = new Rect(1f / 3f, 0.5f, 1f / 3f, 0.5f);
    [SerializeField] private Rect backUV = new Rect(2f / 3f, 0.5f, 1f / 3f, 0.5f);
    [SerializeField] private Rect leftUV = new Rect(0f, 0f, 1f / 3f, 0.5f);
    [SerializeField] private Rect rightUV = new Rect(1f / 3f, 0f, 1f / 3f, 0.5f);

    [Header("Pop-Up Fold Animation")]
    [SerializeField] private bool animatePopUpOnStart = true;
    [SerializeField] private float popUpDelay = 0f;
    [SerializeField] private float popUpDuration = 0.65f;

    [Header("Unwrap Cutaway When Player Behind")]
    [SerializeField] private bool enablePlayerCutaway = true;
    [SerializeField] private Transform playerTransform;
    [SerializeField, Range(20f, 88f)] private float maxUnwrapAngle = 84f;
    [SerializeField, Range(45f, 130f)] private float maxRoofLidAngle = 110f;
    [SerializeField] private bool fadeOnUnwrap = true;
    [SerializeField, Range(0f, 1f)] private float unwrappedAlpha = 0f;
    [SerializeField] private float cutawayMarginX = 1.4f;
    [SerializeField] private float cutawayDepthBehind = 12f;
    [SerializeField] private float cutawayHoldDuration = 0.35f;
    [SerializeField] private float unwrapSmoothSpeed = 4.6f;

    [Header("Cutaway Interior Floor & 3D Furniture")]
    [SerializeField] private bool revealInteriorOnCutaway = true;
    [SerializeField] private MedinaInteriorTheme interiorTheme = MedinaInteriorTheme.BlueSalon;
    [SerializeField] private Material interiorMaterial;
    [SerializeField] private Material furnitureMaterial;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly Dictionary<Material, Material> TransparentMaterialCache = new Dictionary<Material, Material>();
    private static Material SharedShadowMaterial;

    private Mesh _generatedMesh;
    private Mesh _colliderMesh;
    private Mesh _shadowMesh;
    private Mesh _interiorMesh;
    private Mesh _furnitureMesh;
    private MeshRenderer _shadowRenderer;
    private MeshRenderer _interiorRenderer;
    private MeshRenderer _furnitureRenderer;
    private Material _opaqueMaterial;
    private MaterialPropertyBlock _propBlock;
    private MaterialPropertyBlock _shadowPropBlock;
    private MaterialPropertyBlock _interiorPropBlock;
    private MaterialPropertyBlock _furniturePropBlock;
    private readonly List<Vector3> _verts = new List<Vector3>(96);
    private readonly List<Vector3> _norms = new List<Vector3>(96);
    private readonly List<Vector2> _uvs = new List<Vector2>(96);
    private readonly List<Color> _colors = new List<Color>(96);
    private readonly List<int> _tris = new List<int>(160);
    private float _popUpTimer;
    private bool _isAnimatingPopUp;
    private float _currentUnwrap;
    private float _appliedUnwrap = -1f;
    private float _cutawayHoldTimer;
    private bool _needsRebuild;

    public CardboardBlockShape Shape => shape;
    public Vector3 BoxSize => boxSize;
    public bool IsCutawayActive { get; private set; }
    public float CurrentUnwrapAmount => _currentUnwrap;
    public float CurrentAlpha { get; private set; } = 1f;

    private void OnEnable()
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null && _opaqueMaterial == null)
        {
            _opaqueMaterial = mr.sharedMaterial;
        }

        _appliedUnwrap = -1f;
        RebuildMesh();

        if (!Application.isPlaying)
        {
            ApplyUnwrap(0f);
            return;
        }

        if (playerTransform == null)
        {
            MedinaPlayerController player = FindAnyObjectByType<MedinaPlayerController>();
            if (player != null)
            {
                playerTransform = player.transform;
            }
        }
    }

    private void OnValidate()
    {
        boxSize.x = Mathf.Max(0.4f, boxSize.x);
        boxSize.y = Mathf.Max(0.4f, boxSize.y);
        boxSize.z = Mathf.Max(0.4f, boxSize.z);
        maxUnwrapAngle = Mathf.Clamp(maxUnwrapAngle, 20f, 88f);
        maxRoofLidAngle = Mathf.Clamp(maxRoofLidAngle, 45f, 130f);
        _needsRebuild = true;
    }

    private void Start()
    {
        if (!Application.isPlaying)
        {
            ApplyUnwrap(0f);
            return;
        }

        if (animatePopUpOnStart && popUpDuration > 0.01f)
        {
            _popUpTimer = -Mathf.Max(0f, popUpDelay);
            _isAnimatingPopUp = true;
            _currentUnwrap = 1f;
            ApplyUnwrap(1f);
        }
        else
        {
            _currentUnwrap = 0f;
            ApplyUnwrap(0f);
        }
    }

    private void LateUpdate()
    {
        if (_needsRebuild)
        {
            _needsRebuild = false;
            RebuildMesh();
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

        if (_isAnimatingPopUp)
        {
            _popUpTimer += Time.deltaTime;
            if (_popUpTimer < 0f)
            {
                ApplyUnwrap(1f);
                return;
            }

            float t = Mathf.Clamp01(_popUpTimer / popUpDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            _currentUnwrap = 1f - eased;
            ApplyUnwrap(_currentUnwrap);

            if (t >= 1f)
            {
                _isAnimatingPopUp = false;
                _currentUnwrap = 0f;
                ApplyUnwrap(0f);
            }

            return;
        }

        if (!enablePlayerCutaway || playerTransform == null)
        {
            return;
        }

        bool occludedNow = IsPlayerBehindHouse();
        if (occludedNow)
        {
            _cutawayHoldTimer = cutawayHoldDuration;
        }
        else if (_cutawayHoldTimer > 0f)
        {
            _cutawayHoldTimer -= Time.deltaTime;
        }

        bool playerBehind = occludedNow || _cutawayHoldTimer > 0f;
        IsCutawayActive = playerBehind;

        float targetUnwrap = playerBehind ? 1f : 0f;
        float speed = playerBehind ? unwrapSmoothSpeed : unwrapSmoothSpeed * 0.8f;
        float blend = 1f - Mathf.Exp(-speed * Time.deltaTime);
        _currentUnwrap = Mathf.Lerp(_currentUnwrap, targetUnwrap, blend);

        if (Mathf.Abs(_currentUnwrap - targetUnwrap) < 0.002f)
        {
            _currentUnwrap = targetUnwrap;
        }

        ApplyUnwrap(_currentUnwrap);
    }

    public void Configure(Vector3 size, Material material, float delay, CardboardUVMode mode = CardboardUVMode.AdaptiveAspect)
    {
        boxSize = size;
        popUpDelay = delay;
        uvMode = mode;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null && material != null)
        {
            _opaqueMaterial = material;
            mr.sharedMaterial = material;
        }

        RebuildMesh();
    }

    public void SetInteriorSetup(MedinaInteriorTheme theme, Material floorMat, Material furnMat)
    {
        interiorTheme = theme;
        interiorMaterial = floorMat;
        furnitureMaterial = furnMat;
        if (_interiorRenderer != null && interiorMaterial != null)
        {
            _interiorRenderer.sharedMaterial = GetOrCreateInteriorMaterial(interiorMaterial);
        }

        if (_furnitureRenderer != null && furnitureMaterial != null)
        {
            _furnitureRenderer.sharedMaterial = furnitureMaterial;
        }
    }

    public void UpdateSortingOrder()
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null)
        {
            return;
        }

        int baseOrder = -Mathf.RoundToInt(transform.position.z * 100f);
        mr.sortingOrder = baseOrder;

        if (_shadowRenderer != null)
        {
            _shadowRenderer.sortingOrder = -29200;
        }

        if (_interiorRenderer != null)
        {
            _interiorRenderer.sortingOrder = baseOrder - 2;
        }

        if (_furnitureRenderer != null)
        {
            _furnitureRenderer.sortingOrder = baseOrder - 1;
        }
    }

    public void RebuildMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        float w = Mathf.Max(0.4f, boxSize.x);
        float h = Mathf.Max(0.4f, boxSize.y);
        float d = Mathf.Max(0.4f, boxSize.z);

        if (_generatedMesh == null)
        {
            _generatedMesh = new Mesh();
            _generatedMesh.name = "CardboardHouseMesh";
            _generatedMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        PopulateMesh(_generatedMesh, w, h, d, Application.isPlaying ? _currentUnwrap : 0f);

        if (mf != null)
        {
            mf.sharedMesh = _generatedMesh;
        }

        UpdateCollider(w, h, d);
        UpdateGroundShadow(w, h, d);
        UpdateInteriorDiorama(w, h, d);
        ApplyFade(Application.isPlaying ? _currentUnwrap : 0f);
        UpdateSortingOrder();
    }

    public void PopulateMesh(Mesh targetMesh, float w, float h, float d, float unwrap = 0f)
    {
        BuildGeometry(w, h, d, unwrap);

        targetMesh.Clear();
        targetMesh.SetVertices(_verts);
        targetMesh.SetNormals(_norms);
        targetMesh.SetUVs(0, _uvs);
        targetMesh.SetColors(_colors);
        targetMesh.SetTriangles(_tris, 0);
        targetMesh.RecalculateBounds();
    }

    private void UpdateCollider(float w, float h, float d)
    {
        BoxCollider boxCol = GetComponent<BoxCollider>();
        MeshCollider meshCol = GetComponent<MeshCollider>();

        if (shape == CardboardBlockShape.Box)
        {
            if (meshCol != null)
            {
                meshCol.enabled = false;
            }

            if (boxCol == null)
            {
                boxCol = gameObject.AddComponent<BoxCollider>();
            }

            boxCol.enabled = true;
            boxCol.size = new Vector3(w, h, d);
            boxCol.center = new Vector3(0f, h * 0.5f, 0f);
            return;
        }

        if (boxCol != null)
        {
            boxCol.enabled = false;
        }

        if (meshCol == null)
        {
            meshCol = gameObject.AddComponent<MeshCollider>();
        }

        if (_colliderMesh == null)
        {
            _colliderMesh = new Mesh();
            _colliderMesh.name = "CardboardHouseColliderMesh";
            _colliderMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        PopulateMesh(_colliderMesh, w, h, d, 0f);
        meshCol.convex = false;
        meshCol.sharedMesh = null;
        meshCol.sharedMesh = _colliderMesh;
        meshCol.enabled = true;
    }

    private void UpdateGroundShadow(float w, float h, float d)
    {
        Transform existing = transform.Find("GroundCastShadow");
        if (!enableGroundShadow || transform.position.y > 0.5f)
        {
            if (existing != null)
            {
                existing.gameObject.SetActive(false);
            }

            return;
        }

        GameObject shadowObj;
        if (existing == null)
        {
            shadowObj = new GameObject("GroundCastShadow");
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
            _shadowMesh.name = "CardboardShadowMesh";
            _shadowMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        Vector2[] poly = GetFootprint(w, d);
        Vector2 localOffset = ComputeLocalShadowOffset(h + (enableRoofParapet ? parapetHeight : 0f));
        List<Vector2> combined = new List<Vector2>(poly.Length * 2);
        for (int i = 0; i < poly.Length; i++)
        {
            combined.Add(poly[i]);
            combined.Add(poly[i] + localOffset);
        }

        List<Vector2> hull = ComputeConvexHull(combined);
        if (hull.Count < 3)
        {
            return;
        }

        Vector2 center = Vector2.zero;
        for (int i = 0; i < hull.Count; i++) center += hull[i];
        center /= hull.Count;

        int m = hull.Count;
        List<Vector3> sVerts = new List<Vector3>(m * 2 + 1);
        List<Color> sCols = new List<Color>(m * 2 + 1);
        List<int> sTris = new List<int>(m * 9);
        float sy = 0.024f;

        sVerts.Add(new Vector3(center.x, sy, center.y));
        sCols.Add(new Color(1f, 1f, 1f, 1f));

        for (int i = 0; i < m; i++)
        {
            Vector2 p = hull[i];
            sVerts.Add(new Vector3(p.x, sy, p.y));
            sCols.Add(new Color(1f, 1f, 1f, 0.92f));
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
            sVerts.Add(new Vector3(outer.x, sy, outer.y));
            sCols.Add(new Color(1f, 1f, 1f, 0f));
        }

        for (int i = 0; i < m; i++)
        {
            int next = (i + 1) % m;
            sTris.Add(0);
            sTris.Add(1 + next);
            sTris.Add(1 + i);

            int in0 = 1 + i;
            int in1 = 1 + next;
            int out0 = 1 + m + i;
            int out1 = 1 + m + next;
            sTris.Add(in0);
            sTris.Add(in1);
            sTris.Add(out1);
            sTris.Add(in0);
            sTris.Add(out1);
            sTris.Add(out0);
        }

        _shadowMesh.Clear();
        _shadowMesh.SetVertices(sVerts);
        _shadowMesh.SetColors(sCols);
        _shadowMesh.SetTriangles(sTris, 0);
        _shadowMesh.RecalculateBounds();
        smf.sharedMesh = _shadowMesh;

        Material shadowMat = GetSharedShadowMaterial();
        if (shadowMat != null)
        {
            _shadowRenderer.sharedMaterial = shadowMat;
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

    private Vector2 ComputeLocalShadowOffset(float totalH)
    {
        Vector3 worldOffset = new Vector3(shadowProjectionPerMeter.x * totalH, 0f, shadowProjectionPerMeter.y * totalH);
        Vector3 local = transform.InverseTransformVector(worldOffset);
        return new Vector2(local.x, local.z);
    }

    private static Material GetSharedShadowMaterial()
    {
        if (SharedShadowMaterial != null)
        {
            return SharedShadowMaterial;
        }

        // Loaded from Resources so the shader is guaranteed to ship: a shader only reached through
        // Shader.Find is stripped from builds unless a built asset references it.
        Material template = Resources.Load<Material>("Mat_MedinaGroundCastShadow");
        Shader sh = template != null ? template.shader : Shader.Find("Medina/GroundCastShadow");
        if (sh == null || !sh.isSupported)
        {
            Debug.LogWarning("[CardboardHouseBlock] Ground shadow shader 'Medina/GroundCastShadow' is missing from this build; ground shadows disabled.");
            return null;
        }

        SharedShadowMaterial = template != null ? new Material(template) : new Material(sh);
        SharedShadowMaterial.name = "Mat_MedinaGroundCastShadow_Runtime";
        SharedShadowMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        SharedShadowMaterial.SetColor("_ShadowColor", new Color(0.09f, 0.13f, 0.25f, 0.44f));
        return SharedShadowMaterial;
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

    private void ApplyUnwrap(float unwrap)
    {
        unwrap = Mathf.Clamp01(unwrap);
        if (_generatedMesh == null)
        {
            RebuildMesh();
            return;
        }

        if (Mathf.Abs(_appliedUnwrap - unwrap) < 0.0005f)
        {
            return;
        }

        _appliedUnwrap = unwrap;

        float w = Mathf.Max(0.4f, boxSize.x);
        float h = Mathf.Max(0.4f, boxSize.y);
        float d = Mathf.Max(0.4f, boxSize.z);

        BuildGeometry(w, h, d, unwrap);
        if (_generatedMesh.vertexCount != _verts.Count)
        {
            _generatedMesh.Clear();
            _generatedMesh.SetVertices(_verts);
            _generatedMesh.SetNormals(_norms);
            _generatedMesh.SetUVs(0, _uvs);
            _generatedMesh.SetColors(_colors);
            _generatedMesh.SetTriangles(_tris, 0);
        }
        else
        {
            _generatedMesh.SetVertices(_verts);
            _generatedMesh.SetNormals(_norms);
            _generatedMesh.SetColors(_colors);
        }

        _generatedMesh.RecalculateBounds();

        ApplyFade(unwrap);
        UpdateSortingOrder();
    }

    private void ApplyFade(float unwrap)
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

        bool useTransparent = Application.isPlaying && fadeOnUnwrap && unwrap > 0.52f;
        float fadeProgress = Mathf.Clamp01(Mathf.InverseLerp(0.52f, 0.96f, unwrap));
        float alpha = useTransparent
            ? Mathf.Lerp(1f, unwrappedAlpha, Mathf.SmoothStep(0f, 1f, fadeProgress))
            : 1f;

        CurrentAlpha = alpha;
        mr.enabled = alpha > 0.02f;

        if (useTransparent && _opaqueMaterial != null)
        {
            mr.sharedMaterial = GetOrCreateTransparentMaterial(_opaqueMaterial);
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
            _shadowRenderer.enabled = alpha > 0.02f;
            if (_shadowPropBlock == null)
            {
                _shadowPropBlock = new MaterialPropertyBlock();
            }

            _shadowRenderer.GetPropertyBlock(_shadowPropBlock);
            _shadowPropBlock.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
            _shadowRenderer.SetPropertyBlock(_shadowPropBlock);
        }

        if (_interiorRenderer != null)
        {
            bool showInterior = Application.isPlaying && !_isAnimatingPopUp && revealInteriorOnCutaway && interiorMaterial != null && unwrap > 0.03f;
            float intAlpha = showInterior ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.45f, unwrap)) : 0f;
            _interiorRenderer.enabled = intAlpha > 0.02f;

            if (_interiorRenderer.enabled)
            {
                if (_interiorPropBlock == null)
                {
                    _interiorPropBlock = new MaterialPropertyBlock();
                }

                _interiorRenderer.GetPropertyBlock(_interiorPropBlock);
                Color intTint = new Color(1f, 1f, 1f, intAlpha);
                _interiorPropBlock.SetColor(BaseColorId, intTint);
                _interiorPropBlock.SetColor(ColorId, intTint);
                _interiorRenderer.SetPropertyBlock(_interiorPropBlock);
            }
        }

        if (_furnitureRenderer != null)
        {
            bool showFurn = Application.isPlaying && !_isAnimatingPopUp && revealInteriorOnCutaway && furnitureMaterial != null && unwrap > 0.06f;
            float furnAlpha = showFurn ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.52f, unwrap)) : 0f;
            _furnitureRenderer.enabled = furnAlpha > 0.02f;

            if (_furnitureRenderer.enabled)
            {
                float popY = Mathf.Lerp(0.06f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.58f, unwrap)));
                _furnitureRenderer.transform.localScale = new Vector3(1f, popY, 1f);

                bool furnTransparent = furnAlpha < 0.96f;
                if (furnTransparent)
                {
                    _furnitureRenderer.sharedMaterial = GetOrCreateTransparentMaterial(furnitureMaterial);
                }
                else if (_furnitureRenderer.sharedMaterial != furnitureMaterial)
                {
                    _furnitureRenderer.sharedMaterial = furnitureMaterial;
                }

                if (_furniturePropBlock == null)
                {
                    _furniturePropBlock = new MaterialPropertyBlock();
                }

                _furnitureRenderer.GetPropertyBlock(_furniturePropBlock);
                Color fTint = new Color(1f, 1f, 1f, furnAlpha);
                _furniturePropBlock.SetColor(BaseColorId, fTint);
                _furniturePropBlock.SetColor(ColorId, fTint);
                _furnitureRenderer.SetPropertyBlock(_furniturePropBlock);
            }
        }
    }

    private void UpdateInteriorDiorama(float w, float h, float d)
    {
        Transform existingFloor = transform.Find("InteriorDiorama");
        Transform existingFurn = transform.Find("InteriorFurniture");
        if (!revealInteriorOnCutaway || interiorMaterial == null || transform.position.y > 0.5f)
        {
            if (existingFloor != null) existingFloor.gameObject.SetActive(false);
            if (existingFurn != null) existingFurn.gameObject.SetActive(false);
            return;
        }

        GameObject floorObj;
        if (existingFloor == null)
        {
            floorObj = new GameObject("InteriorDiorama");
            floorObj.transform.SetParent(transform, false);
            floorObj.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }
        else
        {
            floorObj = existingFloor.gameObject;
            floorObj.SetActive(true);
        }

        floorObj.transform.localPosition = Vector3.zero;
        floorObj.transform.localRotation = Quaternion.identity;
        floorObj.transform.localScale = Vector3.one;

        MeshFilter imf = floorObj.GetComponent<MeshFilter>();
        if (imf == null) imf = floorObj.AddComponent<MeshFilter>();
        _interiorRenderer = floorObj.GetComponent<MeshRenderer>();
        if (_interiorRenderer == null) _interiorRenderer = floorObj.AddComponent<MeshRenderer>();

        if (_interiorMesh == null)
        {
            _interiorMesh = new Mesh();
            _interiorMesh.name = "CardboardInteriorFloorMesh";
            _interiorMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        Vector2[] poly = GetFootprint(w, d);
        int n = poly.Length;
        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++) centroid += poly[i];
        centroid /= Mathf.Max(1, n);

        Vector2[] insetPoly = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            insetPoly[i] = Vector2.Lerp(centroid, poly[i], 0.95f);
        }

        Vector2 axisU = Vector2.right;
        float bestSq = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 e = insetPoly[(i + 1) % n] - insetPoly[i];
            if (e.sqrMagnitude > bestSq)
            {
                bestSq = e.sqrMagnitude;
                axisU = e.normalized;
            }
        }

        Vector2 axisV = new Vector2(-axisU.y, axisU.x);
        if (transform.TransformDirection(new Vector3(axisV.x, 0f, axisV.y)).z < 0f)
        {
            axisV = -axisV;
            axisU = -axisU;
        }

        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            Vector2 rel = insetPoly[i] - centroid;
            float pu = Vector2.Dot(rel, axisU);
            float pv = Vector2.Dot(rel, axisV);
            if (pu < minU) minU = pu;
            if (pu > maxU) maxU = pu;
            if (pv < minV) minV = pv;
            if (pv > maxV) maxV = pv;
        }

        float spanU = Mathf.Max(0.1f, maxU - minU);
        float spanV = Mathf.Max(0.1f, maxV - minV);

        List<Vector3> iv = new List<Vector3>(n);
        List<Vector3> inorm = new List<Vector3>(n);
        List<Vector2> iuv = new List<Vector2>(n);
        List<Color> icol = new List<Color>(n);
        List<int> itri = new List<int>(n * 3);

        float floorY = 0.06f;
        Rect floorUV = new Rect(0.01f, 0.01f, 0.48f, 0.98f);

        for (int i = 0; i < n; i++)
        {
            Vector2 p = insetPoly[i];
            Vector2 rel = p - centroid;
            iv.Add(new Vector3(p.x, floorY, p.y));
            inorm.Add(Vector3.up);
            icol.Add(Color.white);
            float u = Mathf.Clamp01((Vector2.Dot(rel, axisU) - minU) / spanU);
            float v = Mathf.Clamp01((Vector2.Dot(rel, axisV) - minV) / spanV);
            iuv.Add(new Vector2(Mathf.Lerp(floorUV.xMin, floorUV.xMax, u), Mathf.Lerp(floorUV.yMin, floorUV.yMax, v)));
        }

        List<int> earIdx = new List<int>(n);
        for (int i = 0; i < n; i++) earIdx.Add(i);
        int guard = n * n + 8;
        while (earIdx.Count > 3 && guard-- > 0)
        {
            bool found = false;
            int cnt = earIdx.Count;
            for (int i = 0; i < cnt; i++)
            {
                int prev = earIdx[(i + cnt - 1) % cnt];
                int curr = earIdx[i];
                int next = earIdx[(i + 1) % cnt];
                Vector2 a = insetPoly[prev], b = insetPoly[curr], c = insetPoly[next];
                if (Cross2D(b - a, c - b) <= 0.000001f) continue;
                bool inside = false;
                for (int j = 0; j < cnt; j++)
                {
                    int ti = earIdx[j];
                    if (ti == prev || ti == curr || ti == next) continue;
                    if (PointInTriangle(insetPoly[ti], a, b, c)) { inside = true; break; }
                }

                if (!inside)
                {
                    itri.Add(prev); itri.Add(next); itri.Add(curr);
                    earIdx.RemoveAt(i);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                itri.Add(earIdx[0]); itri.Add(earIdx[2]); itri.Add(earIdx[1]);
                earIdx.RemoveAt(1);
            }
        }

        if (earIdx.Count == 3)
        {
            itri.Add(earIdx[0]); itri.Add(earIdx[2]); itri.Add(earIdx[1]);
        }

        _interiorMesh.Clear();
        _interiorMesh.SetVertices(iv);
        _interiorMesh.SetNormals(inorm);
        _interiorMesh.SetUVs(0, iuv);
        _interiorMesh.SetColors(icol);
        _interiorMesh.SetTriangles(itri, 0);
        _interiorMesh.RecalculateBounds();
        imf.sharedMesh = _interiorMesh;

        int baseOrder = -Mathf.RoundToInt(transform.position.z * 100f);
        _interiorRenderer.sharedMaterial = GetOrCreateInteriorMaterial(interiorMaterial);
        _interiorRenderer.sortingOrder = baseOrder - 2;
        _interiorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _interiorRenderer.receiveShadows = false;
        _interiorRenderer.enabled = false;

        if (furnitureMaterial == null)
        {
            if (existingFurn != null) existingFurn.gameObject.SetActive(false);
            return;
        }

        GameObject furnObj;
        if (existingFurn == null)
        {
            furnObj = new GameObject("InteriorFurniture");
            furnObj.transform.SetParent(transform, false);
            furnObj.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }
        else
        {
            furnObj = existingFurn.gameObject;
            furnObj.SetActive(true);
        }

        furnObj.transform.localPosition = Vector3.zero;
        furnObj.transform.localRotation = Quaternion.identity;
        furnObj.transform.localScale = Vector3.one;

        MeshFilter fmf = furnObj.GetComponent<MeshFilter>();
        if (fmf == null) fmf = furnObj.AddComponent<MeshFilter>();
        _furnitureRenderer = furnObj.GetComponent<MeshRenderer>();
        if (_furnitureRenderer == null) _furnitureRenderer = furnObj.AddComponent<MeshRenderer>();

        if (_furnitureMesh == null)
        {
            _furnitureMesh = new Mesh();
            _furnitureMesh.name = "CardboardInteriorFurnitureMesh";
            _furnitureMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        MedinaInteriorFurnitureBuilder.BuildFurnitureMesh(_furnitureMesh, insetPoly, centroid, axisU, axisV, interiorTheme, floorY);
        fmf.sharedMesh = _furnitureMesh;

        _furnitureRenderer.sharedMaterial = furnitureMaterial;
        _furnitureRenderer.sortingOrder = baseOrder - 1;
        _furnitureRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _furnitureRenderer.receiveShadows = false;
        _furnitureRenderer.enabled = false;
    }

    private static Material GetOrCreateInteriorMaterial(Material source)
    {
        if (TransparentMaterialCache.TryGetValue(source, out Material cached) && cached != null)
        {
            return cached;
        }

        Material mat = new Material(source);
        mat.name = source.name + "_InteriorFade";
        mat.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 2f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        TransparentMaterialCache[source] = mat;
        return mat;
    }

    private static Material GetOrCreateTransparentMaterial(Material source)
    {
        if (TransparentMaterialCache.TryGetValue(source, out Material cached) && cached != null)
        {
            return cached;
        }

        float srcCull = source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2f;
        Material mat = new Material(source);
        mat.name = source.name + "_UnwrapFade";
        mat.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", srcCull);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        TransparentMaterialCache[source] = mat;
        return mat;
    }

    private Vector2[] GetFootprint(float w, float d)
    {
        float hw = w * 0.5f;
        float hd = d * 0.5f;

        switch (shape)
        {
            case CardboardBlockShape.Trapezoid:
            {
                float backHw = hw * Mathf.Clamp(trapezoidBackWidthRatio, 0.25f, 1.5f);
                float skew = w * Mathf.Clamp(trapezoidSkewX, -0.5f, 0.5f);
                return new[]
                {
                    new Vector2(-hw, -hd),
                    new Vector2(hw, -hd),
                    new Vector2(backHw + skew, hd),
                    new Vector2(-backHw + skew, hd)
                };
            }

            case CardboardBlockShape.CornerChamfer:
            {
                float cx = w * Mathf.Clamp(chamferRatio, 0.15f, 0.65f);
                float cz = d * Mathf.Clamp(chamferRatio, 0.15f, 0.65f);
                if (chamferRightCorner)
                {
                    return new[]
                    {
                        new Vector2(-hw, -hd),
                        new Vector2(hw - cx, -hd),
                        new Vector2(hw, -hd + cz),
                        new Vector2(hw, hd),
                        new Vector2(-hw, hd)
                    };
                }

                return new[]
                {
                    new Vector2(-hw + cx, -hd),
                    new Vector2(hw, -hd),
                    new Vector2(hw, hd),
                    new Vector2(-hw, hd),
                    new Vector2(-hw, -hd + cz)
                };
            }

            case CardboardBlockShape.LShape:
            {
                float cutX = Mathf.Lerp(-hw, hw, 1f - Mathf.Clamp(lCutoutWidthRatio, 0.25f, 0.72f));
                float cutZ = Mathf.Lerp(-hd, hd, 1f - Mathf.Clamp(lCutoutDepthRatio, 0.25f, 0.72f));
                if (!mirrorLShape)
                {
                    return new[]
                    {
                        new Vector2(-hw, -hd),
                        new Vector2(hw, -hd),
                        new Vector2(hw, cutZ),
                        new Vector2(cutX, cutZ),
                        new Vector2(cutX, hd),
                        new Vector2(-hw, hd)
                    };
                }

                float cutXMir = Mathf.Lerp(-hw, hw, Mathf.Clamp(lCutoutWidthRatio, 0.25f, 0.72f));
                return new[]
                {
                    new Vector2(-hw, -hd),
                    new Vector2(hw, -hd),
                    new Vector2(hw, hd),
                    new Vector2(cutXMir, hd),
                    new Vector2(cutXMir, cutZ),
                    new Vector2(-hw, cutZ)
                };
            }

            case CardboardBlockShape.CustomPolygon:
                if (customFootprint != null && customFootprint.Count >= 3)
                {
                    Vector2[] pts = customFootprint.ToArray();
                    float area = 0f;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        Vector2 a = pts[i];
                        Vector2 b = pts[(i + 1) % pts.Length];
                        area += a.x * b.y - b.x * a.y;
                    }

                    if (area < 0f)
                    {
                        System.Array.Reverse(pts);
                    }

                    return pts;
                }

                goto default;

            default:
                return new[]
                {
                    new Vector2(-hw, -hd),
                    new Vector2(hw, -hd),
                    new Vector2(hw, hd),
                    new Vector2(-hw, hd)
                };
        }
    }

    private void BuildGeometry(float w, float h, float d, float unwrap)
    {
        _verts.Clear();
        _norms.Clear();
        _uvs.Clear();
        _colors.Clear();
        _tris.Clear();

        Vector2[] poly = GetFootprint(w, d);
        int n = poly.Length;
        float baseY = 0.03f;

        float smoothU = Mathf.SmoothStep(0f, 1f, unwrap);
        float wallU = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.88f, unwrap));
        float wallAngleRad = wallU * Mathf.Clamp(maxUnwrapAngle, 20f, 88f) * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(wallAngleRad);
        float sinA = Mathf.Sin(wallAngleRad);
        float paraH = enableRoofParapet ? parapetHeight * (1f - smoothU * 0.7f) : 0f;
        float wallLengthScale = Mathf.Lerp(1f, 0.78f, wallU);
        float totalWallH = (h + paraH) * wallLengthScale;
        float wallTopY = baseY + totalWallH * cosA;
        float deckY = baseY + (h * wallLengthScale) * cosA;
        Rect innerCardboardUV = new Rect(roofUV.xMin + roofUV.width * 0.04f, roofUV.yMin + roofUV.height * 0.04f, roofUV.width * 0.14f, roofUV.height * 0.14f);

        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            Vector2 edge = b - a;
            float edgeLen = edge.magnitude;
            if (edgeLen < 0.001f)
            {
                continue;
            }

            Vector2 out2D = new Vector2(edge.y / edgeLen, -edge.x / edgeLen);
            Vector3 topOffset = new Vector3(out2D.x * totalWallH * sinA, totalWallH * cosA, out2D.y * totalWallH * sinA);
            Vector3 normal = new Vector3(out2D.x * cosA, sinA, out2D.y * cosA).normalized;
            Vector3 innerShift = -normal * 0.03f;

            int segments = maxWallSegmentLength > 1f
                ? Mathf.Max(1, Mathf.RoundToInt(edgeLen / maxWallSegmentLength))
                : 1;
            float segLen = edgeLen / segments;

            for (int s = 0; s < segments; s++)
            {
                Vector2 sa = Vector2.Lerp(a, b, s / (float)segments);
                Vector2 sb = Vector2.Lerp(a, b, (s + 1) / (float)segments);
                Vector3 bl = new Vector3(sa.x, baseY, sa.y);
                Vector3 br = new Vector3(sb.x, baseY, sb.y);
                Vector3 tl = bl + topOffset;
                Vector3 tr = br + topOffset;

                float aoL = s == 0 ? 0.90f : 0.98f;
                float aoR = s == segments - 1 ? 0.90f : 0.98f;
                Rect uvRect = InsetRect(AdaptUVRect(SelectWallUV(out2D, i + s), segLen, h), 0.005f);
                AddQuadAO(bl, br, tr, tl, normal, uvRect, aoL * 0.86f, aoR * 0.86f, aoR, aoL);
                AddQuadAO(br + innerShift, bl + innerShift, tl + innerShift, tr + innerShift, -normal, innerCardboardUV, 0.72f, 0.72f, 0.80f, 0.80f);
            }
        }

        BuildRoofWithParapet(poly, baseY, h, paraH, wallLengthScale, wallAngleRad, unwrap, smoothU);
    }

    private void BuildRoofWithParapet(Vector2[] poly, float baseY, float h, float paraH, float wallLengthScale, float wallAngleRad, float unwrap, float smoothU)
    {
        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < poly.Length; i++)
        {
            centroid += poly[i];
        }

        centroid /= Mathf.Max(1, poly.Length);

        Vector2 axisU = Vector2.right;
        float bestEdgeSq = 0f;
        float minRadius = float.MaxValue;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 e = poly[(i + 1) % poly.Length] - poly[i];
            float sq = e.sqrMagnitude;
            if (sq > bestEdgeSq)
            {
                bestEdgeSq = sq;
                axisU = e.normalized;
            }

            float distToCenter = (poly[i] - centroid).magnitude;
            if (distToCenter < minRadius) minRadius = distToCenter;
        }

        Vector2 axisV = new Vector2(-axisU.y, axisU.x);

        float minU = float.MaxValue;
        float maxU = float.MinValue;
        float minV = float.MaxValue;
        float maxV = float.MinValue;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 rel = poly[i] - centroid;
            float pu = Vector2.Dot(rel, axisU);
            float pv = Vector2.Dot(rel, axisV);
            if (pu < minU) minU = pu;
            if (pu > maxU) maxU = pu;
            if (pv < minV) minV = pv;
            if (pv > maxV) maxV = pv;
        }

        float spanU = Mathf.Max(0.1f, maxU - minU);
        float spanV = Mathf.Max(0.1f, maxV - minV);
        Rect ruv = InsetRect(roofUV, 0.012f);

        Vector3 localNorth3 = transform.InverseTransformDirection(Vector3.forward);
        Vector2 hingeDir = new Vector2(localNorth3.x, localNorth3.z);
        if (hingeDir.sqrMagnitude < 0.0001f)
        {
            hingeDir = Vector2.up;
        }
        else
        {
            hingeDir.Normalize();
        }

        float maxBackProj = float.MinValue;
        for (int i = 0; i < poly.Length; i++)
        {
            float proj = Vector2.Dot(poly[i] - centroid, hingeDir);
            if (proj > maxBackProj)
            {
                maxBackProj = proj;
            }
        }

        float lidU = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.58f, unwrap));
        float lidAngleRad = lidU * Mathf.Clamp(maxRoofLidAngle, 45f, 130f) * Mathf.Deg2Rad;
        float roofScale = Mathf.Lerp(1f, 0.80f, lidU);
        float cosW = Mathf.Cos(wallAngleRad);
        float sinW = Mathf.Sin(wallAngleRad);
        float totalWallH = (h + paraH) * wallLengthScale;
        float wallTopY = baseY + totalWallH * cosW;
        float deckY = baseY + (h * wallLengthScale) * cosW;

        Vector2 hingeAnchor2D = centroid + hingeDir * (maxBackProj + totalWallH * sinW);
        Vector3 hingePivot = new Vector3(hingeAnchor2D.x, wallTopY, hingeAnchor2D.y);
        Vector3 hingeAxis = new Vector3(-hingeDir.y, 0f, hingeDir.x);
        Quaternion lidRot = Quaternion.AngleAxis(-lidAngleRad * Mathf.Rad2Deg, hingeAxis);
        Vector3 roofNormal = (lidRot * Vector3.up).normalized;
        Vector3 underShift = -roofNormal * 0.03f;

        Vector3 TransformRoofPoint(Vector2 pt2D, float localY)
        {
            Vector2 relFromHinge = (pt2D - (centroid + hingeDir * maxBackProj)) * roofScale;
            Vector3 unrotated = new Vector3(relFromHinge.x, localY - wallTopY, relFromHinge.y);
            Vector3 rotated = hingePivot + lidRot * unrotated;
            if (rotated.y < 0.05f)
            {
                rotated.y = 0.05f;
            }

            return rotated;
        }

        bool buildParapet = enableRoofParapet && minRadius > parapetThickness * 1.8f;

        if (!buildParapet)
        {
            int baseIndex = _verts.Count;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 rel = poly[i] - centroid;
                _verts.Add(TransformRoofPoint(poly[i], deckY));
                _norms.Add(roofNormal);
                _colors.Add(Color.white);

                float u = Mathf.Clamp01((Vector2.Dot(rel, axisU) - minU) / spanU);
                float v = Mathf.Clamp01((Vector2.Dot(rel, axisV) - minV) / spanV);
                _uvs.Add(new Vector2(Mathf.Lerp(ruv.xMin, ruv.xMax, u), Mathf.Lerp(ruv.yMin, ruv.yMax, v)));
            }

            TriangulatePolygonRoof(poly, baseIndex);

            int underBase = _verts.Count;
            Rect underUV = new Rect(roofUV.xMin + roofUV.width * 0.04f, roofUV.yMin + roofUV.height * 0.04f, roofUV.width * 0.14f, roofUV.height * 0.14f);
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 rel = poly[i] - centroid;
                _verts.Add(TransformRoofPoint(poly[i], deckY) + underShift);
                _norms.Add(-roofNormal);
                _colors.Add(new Color(0.74f, 0.74f, 0.74f, 1f));

                float u = Mathf.Clamp01((Vector2.Dot(rel, axisU) - minU) / spanU);
                float v = Mathf.Clamp01((Vector2.Dot(rel, axisV) - minV) / spanV);
                _uvs.Add(new Vector2(Mathf.Lerp(underUV.xMin, underUV.xMax, u), Mathf.Lerp(underUV.yMin, underUV.yMax, v)));
            }

            TriangulatePolygonRoofFlipped(poly, underBase);
            return;
        }

        float insetRatio = Mathf.Clamp(1f - parapetThickness / Mathf.Max(0.5f, minRadius), 0.72f, 0.94f);
        Vector2[] innerPoly = new Vector2[poly.Length];
        for (int i = 0; i < poly.Length; i++)
        {
            innerPoly[i] = centroid + (poly[i] - centroid) * insetRatio;
        }

        Rect rimUV = new Rect(ruv.xMin, ruv.yMax - ruv.height * 0.06f, ruv.width, ruv.height * 0.05f);
        int n = poly.Length;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            Vector3 topOA = TransformRoofPoint(poly[i], wallTopY);
            Vector3 topOB = TransformRoofPoint(poly[next], wallTopY);
            Vector3 topIA = TransformRoofPoint(innerPoly[i], wallTopY);
            Vector3 topIB = TransformRoofPoint(innerPoly[next], wallTopY);
            AddQuadAO(topOA, topOB, topIB, topIA, roofNormal, rimUV, 1.0f, 1.0f, 0.94f, 0.94f);

            Vector2 inEdge = innerPoly[next] - innerPoly[i];
            float inLen = Mathf.Max(0.001f, inEdge.magnitude);
            Vector3 inNorm = (lidRot * new Vector3(-inEdge.y / inLen, 0f, inEdge.x / inLen)).normalized;
            Vector3 botIA = TransformRoofPoint(innerPoly[i], deckY);
            Vector3 botIB = TransformRoofPoint(innerPoly[next], deckY);
            AddQuadAO(botIB, botIA, topIA, topIB, inNorm, rimUV, 0.68f, 0.68f, 0.94f, 0.94f);
        }

        int deckBase = _verts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 rel = innerPoly[i] - centroid;
            _verts.Add(TransformRoofPoint(innerPoly[i], deckY));
            _norms.Add(roofNormal);
            _colors.Add(new Color(0.84f, 0.84f, 0.84f, 1f));

            float u = Mathf.Clamp01((Vector2.Dot(rel, axisU) - minU) / spanU);
            float v = Mathf.Clamp01((Vector2.Dot(rel, axisV) - minV) / spanV);
            _uvs.Add(new Vector2(Mathf.Lerp(ruv.xMin, ruv.xMax, u), Mathf.Lerp(ruv.yMin, ruv.yMax, v)));
        }

        TriangulatePolygonRoof(innerPoly, deckBase);

        int underDeckBase = _verts.Count;
        Rect underDeckUV = new Rect(roofUV.xMin + roofUV.width * 0.04f, roofUV.yMin + roofUV.height * 0.04f, roofUV.width * 0.14f, roofUV.height * 0.14f);
        for (int i = 0; i < n; i++)
        {
            Vector2 rel = poly[i] - centroid;
            _verts.Add(TransformRoofPoint(poly[i], deckY) + underShift);
            _norms.Add(-roofNormal);
            _colors.Add(new Color(0.74f, 0.74f, 0.74f, 1f));

            float u = Mathf.Clamp01((Vector2.Dot(rel, axisU) - minU) / spanU);
            float v = Mathf.Clamp01((Vector2.Dot(rel, axisV) - minV) / spanV);
            _uvs.Add(new Vector2(Mathf.Lerp(underDeckUV.xMin, underDeckUV.xMax, u), Mathf.Lerp(underDeckUV.yMin, underDeckUV.yMax, v)));
        }

        TriangulatePolygonRoofFlipped(poly, underDeckBase);
    }

    private void TriangulatePolygonRoofFlipped(Vector2[] poly, int baseIndex)
    {
        int startTri = _tris.Count;
        TriangulatePolygonRoof(poly, baseIndex);
        for (int i = startTri; i + 2 < _tris.Count; i += 3)
        {
            int tmp = _tris[i + 1];
            _tris[i + 1] = _tris[i + 2];
            _tris[i + 2] = tmp;
        }
    }

    private void TriangulatePolygonRoof(Vector2[] poly, int baseIndex)
    {
        List<int> indices = new List<int>(poly.Length);
        for (int i = 0; i < poly.Length; i++)
        {
            indices.Add(i);
        }

        int guard = poly.Length * poly.Length + 8;
        while (indices.Count > 3 && guard-- > 0)
        {
            bool earFound = false;
            int count = indices.Count;

            for (int i = 0; i < count; i++)
            {
                int prev = indices[(i + count - 1) % count];
                int curr = indices[i];
                int next = indices[(i + 1) % count];

                Vector2 a = poly[prev];
                Vector2 b = poly[curr];
                Vector2 c = poly[next];

                if (Cross2D(b - a, c - b) <= 0.000001f)
                {
                    continue;
                }

                bool containsOther = false;
                for (int j = 0; j < count; j++)
                {
                    int testIdx = indices[j];
                    if (testIdx == prev || testIdx == curr || testIdx == next)
                    {
                        continue;
                    }

                    if (PointInTriangle(poly[testIdx], a, b, c))
                    {
                        containsOther = true;
                        break;
                    }
                }

                if (!containsOther)
                {
                    AddRoofTri(baseIndex, prev, next, curr);
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound)
            {
                int bestIdx = 0;
                float bestCross = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    int prev = indices[(i + count - 1) % count];
                    int curr = indices[i];
                    int next = indices[(i + 1) % count];
                    float cr = Cross2D(poly[curr] - poly[prev], poly[next] - poly[curr]);
                    if (cr > bestCross)
                    {
                        bestCross = cr;
                        bestIdx = i;
                    }
                }

                int p = indices[(bestIdx + count - 1) % count];
                int cur = indices[bestIdx];
                int nx = indices[(bestIdx + 1) % count];
                AddRoofTri(baseIndex, p, nx, cur);
                indices.RemoveAt(bestIdx);
            }
        }

        if (indices.Count == 3)
        {
            AddRoofTri(baseIndex, indices[0], indices[2], indices[1]);
        }
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross2D(b - a, p - a);
        float c2 = Cross2D(c - b, p - b);
        float c3 = Cross2D(a - c, p - c);
        return c1 >= -0.0001f && c2 >= -0.0001f && c3 >= -0.0001f;
    }

    private void AddRoofTri(int baseIndex, int a, int b, int c)
    {
        _tris.Add(baseIndex + a);
        _tris.Add(baseIndex + b);
        _tris.Add(baseIndex + c);
    }

    private Rect SelectWallUV(Vector2 outward2D, int segmentSeed)
    {
        if (Mathf.Abs(outward2D.y) >= Mathf.Abs(outward2D.x))
        {
            if (outward2D.y < 0f)
            {
                return (segmentSeed % 2 == 0) ? frontUV : rightUV;
            }

            return (segmentSeed % 2 == 0) ? backUV : leftUV;
        }

        if (outward2D.x < 0f)
        {
            return (segmentSeed % 2 == 0) ? leftUV : frontUV;
        }

        return (segmentSeed % 2 == 0) ? rightUV : backUV;
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

    private static Rect InsetRect(Rect r, float insetFraction)
    {
        float dx = r.width * insetFraction;
        float dy = r.height * insetFraction;
        return new Rect(r.x + dx, r.y + dy, r.width - dx * 2f, r.height - dy * 2f);
    }

    private Rect AdaptUVRect(Rect baseRect, float faceWidth, float faceHeight)
    {
        if (uvMode != CardboardUVMode.AdaptiveAspect || referenceFaceSize <= 0.01f)
        {
            return baseRect;
        }

        float aspect = faceWidth / Mathf.Max(0.01f, faceHeight);
        float uSpan = baseRect.width;
        float vSpan = baseRect.height;

        if (aspect < 0.85f)
        {
            float keep = Mathf.Clamp(aspect, 0.55f, 1f);
            float trimmedW = uSpan * keep;
            float offsetX = (uSpan - trimmedW) * 0.5f;
            return new Rect(baseRect.x + offsetX, baseRect.y, trimmedW, vSpan);
        }

        if (aspect > 1.35f)
        {
            float keep = Mathf.Clamp(1.25f / aspect, 0.65f, 1f);
            float trimmedH = vSpan * keep;
            return new Rect(baseRect.x, baseRect.y, uSpan, trimmedH);
        }

        return baseRect;
    }

    private bool IsPlayerBehindHouse()
    {
        Collider col = GetComponent<MeshCollider>();
        if (col == null || !col.enabled)
        {
            col = GetComponent<BoxCollider>();
        }

        if (col == null || !col.enabled)
        {
            return false;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return false;
        }

        Bounds bounds = col.bounds;
        Vector3 playerPos = playerTransform.position;
        if (playerPos.z < bounds.min.z - 0.5f || playerPos.z > bounds.max.z + cutawayDepthBehind + 10f)
        {
            return false;
        }

        Vector3 camPos = cam.transform.position;
        Vector3 playerMid = playerPos + Vector3.up * 1.15f;
        Vector3 playerLow = playerPos + Vector3.up * 0.45f;
        Vector3 playerHigh = playerPos + Vector3.up * 1.85f;
        float marginX = IsCutawayActive ? cutawayMarginX + 0.55f : cutawayMarginX;

        if (RayHitsCollider(col, camPos, playerMid))
        {
            return true;
        }

        if (RayHitsCollider(col, camPos, playerLow))
        {
            return true;
        }

        if (RayHitsCollider(col, camPos, playerHigh))
        {
            return true;
        }

        if (RayHitsCollider(col, camPos, playerMid + Vector3.right * marginX))
        {
            return true;
        }

        if (RayHitsCollider(col, camPos, playerMid - Vector3.right * marginX))
        {
            return true;
        }

        return false;
    }

    private static bool RayHitsCollider(Collider col, Vector3 origin, Vector3 target)
    {
        Vector3 dir = target - origin;
        float dist = dir.magnitude;
        if (dist < 0.05f)
        {
            return false;
        }

        Ray ray = new Ray(origin, dir / dist);
        return col.Raycast(ray, out _, dist);
    }

    private void OnDestroy()
    {
        if (_generatedMesh != null)
        {
            if (Application.isPlaying) Destroy(_generatedMesh);
            else DestroyImmediate(_generatedMesh);
        }

        if (_colliderMesh != null)
        {
            if (Application.isPlaying) Destroy(_colliderMesh);
            else DestroyImmediate(_colliderMesh);
        }

        if (_shadowMesh != null)
        {
            if (Application.isPlaying) Destroy(_shadowMesh);
            else DestroyImmediate(_shadowMesh);
        }

        if (_interiorMesh != null)
        {
            if (Application.isPlaying) Destroy(_interiorMesh);
            else DestroyImmediate(_interiorMesh);
        }

        if (_furnitureMesh != null)
        {
            if (Application.isPlaying) Destroy(_furnitureMesh);
            else DestroyImmediate(_furnitureMesh);
        }
    }
}
