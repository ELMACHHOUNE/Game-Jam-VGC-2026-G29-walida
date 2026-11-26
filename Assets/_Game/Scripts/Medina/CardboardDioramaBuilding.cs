using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class CardboardDioramaBuilding : MonoBehaviour
{
    [Header("Pop-Up Entrance")]
    [SerializeField] private bool animatePopUpOnStart = true;
    [SerializeField] private float popUpDelay = 0f;
    [SerializeField] private float popUpDuration = 0.6f;
    [SerializeField] private float popUpOvershoot = 1.4f;

    [Header("Cutaway When Player Behind")]
    [SerializeField] private bool enablePlayerCutaway = true;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private BoxCollider boundsCollider;
    [SerializeField] private float cutawayMarginX = 1.8f;
    [SerializeField] private float cutawayDepthBehind = 8.0f;
    [SerializeField] private float fadeSmoothSpeed = 8f;
    [SerializeField, Range(0f, 1f)] private float fadedAlpha = 0f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly Dictionary<Material, Material> TransparentCache = new Dictionary<Material, Material>();

    private MeshRenderer[] _meshRenderers;
    private Material[] _opaqueMaterials;
    private SpriteRenderer[] _spriteRenderers;
    private Color[] _spriteBaseColors;
    private MaterialPropertyBlock _propBlock;
    private float _popUpTimer;
    private bool _isAnimatingPopUp;
    private float _currentFade;
    private float _appliedFade = -1f;

    public bool IsCutawayActive { get; private set; }
    public float CurrentAlpha { get; private set; } = 1f;

    private void OnEnable()
    {
        CacheRenderers();
        UpdateSortingOrders();

        if (!Application.isPlaying)
        {
            ApplyScaleY(1f);
            ApplyFade(0f);
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

    private void Start()
    {
        if (!Application.isPlaying)
        {
            ApplyScaleY(1f);
            return;
        }

        if (animatePopUpOnStart && popUpDuration > 0.01f)
        {
            _popUpTimer = -Mathf.Max(0f, popUpDelay);
            _isAnimatingPopUp = true;
            ApplyScaleY(0.02f);
        }
        else
        {
            ApplyScaleY(1f);
        }
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            if (transform.hasChanged)
            {
                UpdateSortingOrders();
                transform.hasChanged = false;
            }

            return;
        }

        if (_isAnimatingPopUp)
        {
            _popUpTimer += Time.deltaTime;
            if (_popUpTimer < 0f)
            {
                ApplyScaleY(0.02f);
                return;
            }

            float t = Mathf.Clamp01(_popUpTimer / popUpDuration);
            float eased = EaseOutBack(t, popUpOvershoot);
            ApplyScaleY(Mathf.Max(0.02f, eased));

            if (t >= 1f)
            {
                _isAnimatingPopUp = false;
                ApplyScaleY(1f);
            }

            return;
        }

        if (!enablePlayerCutaway || playerTransform == null)
        {
            return;
        }

        bool behind = IsPlayerBehind();
        IsCutawayActive = behind;

        float targetFade = behind ? 1f : 0f;
        float blend = 1f - Mathf.Exp(-fadeSmoothSpeed * Time.deltaTime);
        _currentFade = Mathf.Lerp(_currentFade, targetFade, blend);
        if (Mathf.Abs(_currentFade - targetFade) < 0.002f)
        {
            _currentFade = targetFade;
        }

        ApplyFade(_currentFade);
    }

    public void UpdateSortingOrders()
    {
        if (_meshRenderers == null)
        {
            CacheRenderers();
        }

        int baseOrder = -Mathf.RoundToInt(transform.position.z * 100f);

        for (int i = 0; i < _meshRenderers.Length; i++)
        {
            if (_meshRenderers[i] != null)
            {
                _meshRenderers[i].sortingOrder = baseOrder;
            }
        }

        for (int i = 0; i < _spriteRenderers.Length; i++)
        {
            if (_spriteRenderers[i] != null)
            {
                _spriteRenderers[i].sortingOrder = baseOrder + 5;
            }
        }
    }

    private void CacheRenderers()
    {
        if (boundsCollider == null)
        {
            boundsCollider = GetComponent<BoxCollider>();
        }

        _meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        _opaqueMaterials = new Material[_meshRenderers.Length];
        for (int i = 0; i < _meshRenderers.Length; i++)
        {
            if (_meshRenderers[i] != null)
            {
                _opaqueMaterials[i] = _meshRenderers[i].sharedMaterial;
            }
        }

        _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        _spriteBaseColors = new Color[_spriteRenderers.Length];
        for (int i = 0; i < _spriteRenderers.Length; i++)
        {
            if (_spriteRenderers[i] != null)
            {
                _spriteBaseColors[i] = _spriteRenderers[i].color;
            }
        }
    }

    private void ApplyFade(float fade)
    {
        if (Mathf.Abs(_appliedFade - fade) < 0.0005f)
        {
            return;
        }

        _appliedFade = fade;
        bool useTransparent = Application.isPlaying && fade > 0.005f;
        float alpha = useTransparent
            ? Mathf.Lerp(1f, fadedAlpha, Mathf.SmoothStep(0f, 1f, fade))
            : 1f;

        CurrentAlpha = alpha;
        bool visible = alpha > 0.02f;

        if (_propBlock == null)
        {
            _propBlock = new MaterialPropertyBlock();
        }

        Color tint = new Color(1f, 1f, 1f, alpha);

        for (int i = 0; i < _meshRenderers.Length; i++)
        {
            MeshRenderer mr = _meshRenderers[i];
            if (mr == null)
            {
                continue;
            }

            mr.enabled = visible;
            Material srcMat = _opaqueMaterials[i];
            if (srcMat != null)
            {
                mr.sharedMaterial = useTransparent ? GetOrCreateTransparent(srcMat) : srcMat;
            }

            mr.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId, tint);
            _propBlock.SetColor(ColorId, tint);
            mr.SetPropertyBlock(_propBlock);
        }

        for (int i = 0; i < _spriteRenderers.Length; i++)
        {
            SpriteRenderer sr = _spriteRenderers[i];
            if (sr == null)
            {
                continue;
            }

            sr.enabled = visible;
            Color c = _spriteBaseColors[i];
            c.a *= alpha;
            sr.color = c;
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
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        TransparentCache[source] = mat;
        return mat;
    }

    private bool IsPlayerBehind()
    {
        if (boundsCollider == null)
        {
            return false;
        }

        Bounds b = boundsCollider.bounds;
        Vector3 p = playerTransform.position;
        return p.x >= b.min.x - cutawayMarginX &&
               p.x <= b.max.x + cutawayMarginX &&
               p.z >= b.center.z &&
               p.z <= b.max.z + cutawayDepthBehind;
    }

    private void ApplyScaleY(float y)
    {
        Vector3 s = transform.localScale;
        s.y = y;
        transform.localScale = s;
    }

    private static float EaseOutBack(float t, float overshoot)
    {
        float c3 = overshoot + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + overshoot * x * x;
    }
}
