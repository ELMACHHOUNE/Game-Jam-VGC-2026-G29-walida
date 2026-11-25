using UnityEngine;

public class StorybookCutout : MonoBehaviour
{
    [Header("Storybook Pose")]
    [SerializeField] private float cameraTiltAngle = 14f;
    [SerializeField] private bool autoSortByDepth = true;
    [SerializeField] private int sortingOrderOffset = 0;

    [Header("Pop-Up Fold Animation")]
    [SerializeField] private bool animatePopUpOnStart = true;
    [SerializeField] private float popUpDelay = 0f;
    [SerializeField] private float popUpDuration = 0.55f;
    [SerializeField] private float foldStartAngle = 86f;
    [SerializeField] private float popUpOvershoot = 1.6f;

    private SpriteRenderer[] _renderers;
    private int[] _baseOffsets;
    private float _popUpTimer;
    private bool _isAnimatingPopUp;

    private void Awake()
    {
        CacheRenderers();
    }

    private void Start()
    {
        if (animatePopUpOnStart && popUpDuration > 0.01f)
        {
            _popUpTimer = -Mathf.Max(0f, popUpDelay);
            _isAnimatingPopUp = true;
            SetCutoutPitch(foldStartAngle);
        }
        else
        {
            SetCutoutPitch(cameraTiltAngle);
        }

        UpdateDepthSorting();
    }

    private void LateUpdate()
    {
        if (_isAnimatingPopUp)
        {
            _popUpTimer += Time.deltaTime;
            if (_popUpTimer < 0f)
            {
                SetCutoutPitch(foldStartAngle);
            }
            else
            {
                float t = Mathf.Clamp01(_popUpTimer / popUpDuration);
                float eased = EaseOutBack(t, popUpOvershoot);
                float pitch = Mathf.LerpUnclamped(foldStartAngle, cameraTiltAngle, eased);
                SetCutoutPitch(pitch);

                if (t >= 1f)
                {
                    _isAnimatingPopUp = false;
                    SetCutoutPitch(cameraTiltAngle);
                }
            }
        }

        if (autoSortByDepth)
        {
            UpdateDepthSorting();
        }
    }

    public void UpdateDepthSorting()
    {
        if (_renderers == null || _renderers.Length == 0)
        {
            CacheRenderers();
        }

        int depthOrder = -Mathf.RoundToInt(transform.position.z * 100f) + sortingOrderOffset;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
            {
                _renderers[i].sortingOrder = depthOrder + _baseOffsets[i];
            }
        }
    }

    public void ApplyStaticPose()
    {
        SetCutoutPitch(cameraTiltAngle);
        UpdateDepthSorting();
    }

    private void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<SpriteRenderer>(true);
        _baseOffsets = new int[_renderers.Length];
        int minOrder = int.MaxValue;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null && _renderers[i].sortingOrder < minOrder)
            {
                minOrder = _renderers[i].sortingOrder;
            }
        }

        if (minOrder == int.MaxValue)
        {
            minOrder = 0;
        }

        for (int i = 0; i < _renderers.Length; i++)
        {
            _baseOffsets[i] = _renderers[i] != null ? _renderers[i].sortingOrder - minOrder : 0;
        }
    }

    private void SetCutoutPitch(float pitchX)
    {
        Vector3 euler = transform.localEulerAngles;
        euler.x = pitchX;
        transform.localEulerAngles = euler;
    }

    private static float EaseOutBack(float t, float overshoot)
    {
        float c3 = overshoot + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + overshoot * x * x;
    }
}
