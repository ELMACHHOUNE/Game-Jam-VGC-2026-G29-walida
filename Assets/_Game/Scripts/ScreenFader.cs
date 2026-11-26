using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

public class ScreenFader : MonoBehaviour
{
    [SerializeField] private PanelRenderer panelRenderer;
    [SerializeField] private bool startOpaqueBlack = true;

    private VisualElement _curtain;
    private float _currentAlpha;
    private bool _curtainReady;
    private CancellationTokenSource _fadeCts;

    public float CurrentAlpha => _currentAlpha;
    public bool IsCurtainReady => _curtainReady;

    private void Awake()
    {
        _currentAlpha = startOpaqueBlack ? 1f : 0f;
    }

    private void OnEnable()
    {
        if (panelRenderer != null)
            panelRenderer.RegisterUIReloadCallback(HandleUIReload);
    }

    private void OnDisable()
    {
        CancelActiveFade();
        _curtainReady = false;
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(HandleUIReload);
    }

    private void OnGUI()
    {
        // Covers Frame 0 before PanelRenderer finishes laying out ScreenFader.uxml
        if (_curtainReady || _currentAlpha <= 0.001f)
            return;

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, _currentAlpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.blackTexture);
        GUI.color = prev;
    }

    private void HandleUIReload(PanelRenderer pr, VisualElement root, int version)
    {
        if (root == null) return;
        _curtain = root.Q<VisualElement>("screen-fade-curtain");
        ApplyAlpha(_currentAlpha);

        if (_curtain != null)
        {
            _curtain.RegisterCallbackOnce<GeometryChangedEvent>(_ => _curtainReady = true);
        }
    }

    public void SetBlackImmediate()
    {
        CancelActiveFade();
        ApplyAlpha(1f);
    }

    public void SetClearImmediate()
    {
        CancelActiveFade();
        ApplyAlpha(0f);
    }

    public void SetAlpha(float alpha)
    {
        CancelActiveFade();
        ApplyAlpha(alpha);
    }

    public async Awaitable FadeOutToBlackAsync(float duration)
    {
        await FadeToAsync(1f, duration);
    }

    public async Awaitable FadeInFromBlackAsync(float duration)
    {
        await FadeToAsync(0f, duration);
    }

    public async Awaitable FadeToAsync(float targetAlpha, float duration)
    {
        CancelActiveFade();
        _fadeCts = new CancellationTokenSource();
        CancellationToken ct = _fadeCts.Token;

        float startAlpha = _currentAlpha;
        targetAlpha = Mathf.Clamp01(targetAlpha);

        if (duration <= 0f)
        {
            ApplyAlpha(targetAlpha);
            return;
        }

        if (_curtain != null && targetAlpha > 0.01f)
        {
            _curtain.style.display = DisplayStyle.Flex;
            _curtain.pickingMode = PickingMode.Position;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            await Awaitable.NextFrameAsync(ct);
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            ApplyAlpha(Mathf.Lerp(startAlpha, targetAlpha, eased));
        }

        ApplyAlpha(targetAlpha);
    }

    private void ApplyAlpha(float alpha)
    {
        _currentAlpha = Mathf.Clamp01(alpha);
        if (_curtain == null) return;

        _curtain.style.opacity = _currentAlpha;
        bool isVisible = _currentAlpha > 0.001f;
        _curtain.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        _curtain.pickingMode = isVisible ? PickingMode.Position : PickingMode.Ignore;
    }

    private void CancelActiveFade()
    {
        if (_fadeCts != null)
        {
            _fadeCts.Cancel();
            _fadeCts.Dispose();
            _fadeCts = null;
        }
    }
}