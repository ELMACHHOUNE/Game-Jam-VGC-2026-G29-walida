using UnityEngine;

/// <summary>
/// Animates a cropped paper-collage sea layer beneath the Kasbah ground with
/// organic multi-harmonic tidal surge, coastal sway, and gentle buoyant bobbing.
/// </summary>
public class MedinaSeaAnimator : MonoBehaviour
{
    [Header("Tidal Surge (Toward / Away From Coast)")]
    [SerializeField] private float surgeAmplitudeX = 0.85f;
    [SerializeField] private float surgeSpeed = 0.95f;

    [Header("Coastal Sway (Along Shoreline)")]
    [SerializeField] private float swayAmplitudeZ = 0.55f;
    [SerializeField] private float swaySpeed = 0.62f;

    [Header("Gentle Buoyant Bob (Vertical)")]
    [SerializeField] private float bobAmplitudeY = 0.01f;
    [SerializeField] private float bobSpeed = 1.25f;

    [Header("Phase & Timing")]
    [SerializeField] private float phaseOffset = 0f;
    [SerializeField] private bool useUnscaledTime = false;

    private Vector3 _baseLocalPosition;
    private bool _hasCachedBase;

    private void Awake()
    {
        CacheBasePosition();
    }

    private void OnEnable()
    {
        CacheBasePosition();
    }

    private void OnDisable()
    {
        if (_hasCachedBase)
        {
            transform.localPosition = _baseLocalPosition;
        }
    }

    private void Update()
    {
        if (!_hasCachedBase)
        {
            CacheBasePosition();
        }

        float t = useUnscaledTime ? Time.unscaledTime : Time.time;

        // Dual-harmonic tidal surge so the paper sea breathes organically instead of metronomically
        float primarySurge = Mathf.Sin(t * surgeSpeed + phaseOffset);
        float secondarySurge = Mathf.Sin(t * surgeSpeed * 0.47f + phaseOffset * 1.73f);
        float offsetX = (primarySurge * 0.72f + secondarySurge * 0.28f) * surgeAmplitudeX;

        float primarySway = Mathf.Cos(t * swaySpeed + phaseOffset * 0.82f);
        float secondarySway = Mathf.Sin(t * swaySpeed * 0.53f + phaseOffset * 1.41f);
        float offsetZ = (primarySway * 0.75f + secondarySway * 0.25f) * swayAmplitudeZ;

        float offsetY = Mathf.Sin(t * bobSpeed + phaseOffset * 1.31f) * bobAmplitudeY;

        transform.localPosition = _baseLocalPosition + new Vector3(offsetX, offsetY, offsetZ);
    }

    public void RecaptureBasePosition()
    {
        _baseLocalPosition = transform.localPosition;
        _hasCachedBase = true;
    }

    private void CacheBasePosition()
    {
        if (_hasCachedBase)
        {
            return;
        }

        _baseLocalPosition = transform.localPosition;
        _hasCachedBase = true;
    }
}
