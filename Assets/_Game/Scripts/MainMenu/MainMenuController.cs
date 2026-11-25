using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using LitMotion;

public class MainMenuController : MonoBehaviour
{
    [Serializable]
    public class SplashScreenEntry
    {
        public Sprite sprite;
        [Min(0.05f)] public float fadeInDuration = 0.6f;
        [Min(0f)] public float holdDuration = 1.2f;
        [Min(0.05f)] public float fadeOutDuration = 0.6f;
    }

    [Header("UI Panel & Scene Target")]
    [SerializeField] private PanelRenderer panelRenderer;
    [SerializeField] private string gameplaySceneName = "VNScene";

    [Header("Boot Splash Sequence")]
    [SerializeField] private List<SplashScreenEntry> splashScreens = new();
    [SerializeField] private bool allowSplashSkip = true;
    [SerializeField] private bool playSplashOnlyOnColdBoot = true;
    [SerializeField] private float splashCurtainFadeOut = 0.55f;

    [Header("Main Menu Reveal Timings")]
    [SerializeField] private float backgroundFadeDuration = 0.7f;
    [SerializeField] private float logoFadeDelay = 0.15f;
    [SerializeField] private float logoFadeDuration = 0.65f;
    [SerializeField] private float buttonsFadeDelay = 0.2f;
    [SerializeField] private float buttonsFadeDuration = 0.5f;

    [Header("Main Menu Visuals")]
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite logoSprite;
    [SerializeField] private Vector2 logoSize = new(640f, 260f);

    [Header("Logo Margin (Outer Spacing)")]
    [SerializeField] private float logoMarginTop = 95f;
    [SerializeField] private float logoMarginBottom = 48f;
    [SerializeField] private float logoMarginLeft = 0f;
    [SerializeField] private float logoMarginRight = 0f;

    [Header("Logo Padding (Inner Spacing)")]
    [SerializeField] private float logoPaddingTop = 0f;
    [SerializeField] private float logoPaddingBottom = 0f;
    [SerializeField] private float logoPaddingLeft = 0f;
    [SerializeField] private float logoPaddingRight = 0f;

    [Header("Logo Offset (Pixel Nudge From Center)")]
    [SerializeField] private Vector2 logoOffset = Vector2.zero;

    [Header("Scene Transition Timings")]
    [SerializeField, Min(0f)] private float menuFadeOutDuration = 0.5f;
    [SerializeField, Min(0f)] private float preGameStartDelay = 1.0f;

    // UXML Elements
    private VisualElement _menuBg;
    private VisualElement _logoWrapper;
    private VisualElement _logoImage;
    private VisualElement _buttonContainer;
    private Button _btnContinue;
    private VisualElement _settingsOverlay;
    private VisualElement _splashOverlay;
    private VisualElement _splashImage;
    private VNSettingsView _settingsView;

    // Runtime State
    private CancellationTokenSource _sequenceCts;
    private CancellationTokenSource _splashStepCts;
    private MotionHandle _activeTween;
    private bool _isTransitioningScene;

    private void OnEnable()
    {
        Time.timeScale = 1f;

        if (panelRenderer == null)
        {
            Debug.LogError("[MainMenuController] PanelRenderer reference is missing.");
            return;
        }

        panelRenderer.RegisterUIReloadCallback(HandleUIReload);
    }

    private void OnDisable()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(HandleUIReload);

        CancelAllSequenceWork();
        _settingsView?.Dispose();
        _settingsView = null;
    }

    private void OnValidate()
    {
        ApplyLogoLayout();
    }

    private void Update()
    {
        if (!allowSplashSkip || _splashStepCts == null)
            return;

        bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool pressedAnyKey = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;

        if (clicked || pressedAnyKey)
            SkipCurrentSplashStep();
    }

    private void HandleUIReload(PanelRenderer pr, VisualElement root, int version)
    {
        if (root == null) return;

        BindElements(root);
        ApplyVisualAssets();
        ApplyLogoLayout();
        RefreshContinueButtonVisibility();

        StartBootAndRevealSequence();
    }

    private void BindElements(VisualElement root)
    {
        _menuBg = root.Q<VisualElement>("menu-bg");
        _logoWrapper = root.Q<VisualElement>("logo-wrapper");
        _logoImage = root.Q<VisualElement>("logo-image");
        _buttonContainer = root.Q<VisualElement>("button-container");
        _btnContinue = root.Q<Button>("btn-continue");
        _settingsOverlay = root.Q<VisualElement>("settings-overlay");
        _splashOverlay = root.Q<VisualElement>("splash-overlay");
        _splashImage = root.Q<VisualElement>("splash-image");

        var btnNewGame = root.Q<Button>("btn-new-game");
        var btnSettings = root.Q<Button>("btn-settings");
        var btnExit = root.Q<Button>("btn-exit");
        var btnCloseSettings = root.Q<Button>("btn-close-settings");

        _btnContinue.focusable = false;
        btnNewGame.focusable = false;
        btnSettings.focusable = false;
        btnExit.focusable = false;

        _btnContinue.clicked += HandleContinueClicked;
        btnNewGame.clicked += HandleNewGameClicked;
        btnSettings.clicked += () => _settingsOverlay.style.display = DisplayStyle.Flex;
        btnCloseSettings.clicked += () => _settingsOverlay.style.display = DisplayStyle.None;
        btnExit.clicked += HandleExitClicked;

        _splashOverlay.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (allowSplashSkip && evt.button == 0)
                SkipCurrentSplashStep();
        });

        _settingsView?.Dispose();
        _settingsView = new VNSettingsView(
            root.Q<VisualElement>("settings-panel"),
            () => _settingsOverlay.style.display = DisplayStyle.None
        );

        btnSettings.clicked += () =>
        {
            _settingsView.SyncFromManager();
            _settingsOverlay.style.display = DisplayStyle.Flex;
        };
    }

    private void ApplyVisualAssets()
    {
        if (_menuBg != null)
            _menuBg.style.backgroundImage = backgroundSprite != null
                ? new StyleBackground(backgroundSprite)
                : StyleKeyword.None;

        if (_logoImage != null)
            _logoImage.style.backgroundImage = logoSprite != null
                ? new StyleBackground(logoSprite)
                : StyleKeyword.None;
    }

    public void ApplyLogoLayout()
    {
        if (_logoWrapper == null || _logoImage == null)
            return;

        _logoImage.style.width = Mathf.Max(16f, logoSize.x);
        _logoImage.style.height = Mathf.Max(16f, logoSize.y);

        _logoWrapper.style.marginTop = logoMarginTop;
        _logoWrapper.style.marginBottom = logoMarginBottom;
        _logoWrapper.style.marginLeft = logoMarginLeft;
        _logoWrapper.style.marginRight = logoMarginRight;

        _logoWrapper.style.paddingTop = Mathf.Max(0f, logoPaddingTop);
        _logoWrapper.style.paddingBottom = Mathf.Max(0f, logoPaddingBottom);
        _logoWrapper.style.paddingLeft = Mathf.Max(0f, logoPaddingLeft);
        _logoWrapper.style.paddingRight = Mathf.Max(0f, logoPaddingRight);

        _logoWrapper.style.translate = new Translate(logoOffset.x, logoOffset.y);
    }

    public void RefreshContinueButtonVisibility()
    {
        if (_btnContinue == null) return;
        bool hasSave = VNSessionBridge.HasAnySaveData();
        _btnContinue.style.display = hasSave ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // =========================================================================
    // BOOT SPLASH -> BACKGROUND -> LOGO -> BUTTONS CHOREOGRAPHY
    // =========================================================================

    private async void StartBootAndRevealSequence()
    {
        CancelAllSequenceWork();
        _sequenceCts = new CancellationTokenSource();
        CancellationToken token = _sequenceCts.Token;

        // Reset all menu elements to invisible before starting
        _menuBg.style.opacity = 0f;
        _logoWrapper.style.opacity = 0f;
        _buttonContainer.style.opacity = 0f;
        _buttonContainer.SetEnabled(false);

        bool shouldPlaySplashes = splashScreens != null &&
                                  splashScreens.Count > 0 &&
                                  (!playSplashOnlyOnColdBoot || !VNSessionBridge.HasPlayedBootSplash);

        try
        {
            // 1. Sequential Boot Splash Pictures
            if (shouldPlaySplashes)
            {
                VNSessionBridge.HasPlayedBootSplash = true;
                _splashOverlay.style.display = DisplayStyle.Flex;
                _splashOverlay.style.opacity = 1f;
                _splashImage.style.opacity = 0f;

                foreach (SplashScreenEntry entry in splashScreens)
                {
                    if (entry == null || entry.sprite == null)
                        continue;

                    await PlaySingleSplashAsync(entry, token);
                }

                // Fade black splash curtain out
                await FadeElementAsync(_splashOverlay, 1f, 0f, splashCurtainFadeOut, Ease.OutQuad, token);
                _splashOverlay.style.display = DisplayStyle.None;
            }
            else
            {
                _splashOverlay.style.display = DisplayStyle.None;
            }

            // 2. Fade in Main Menu Background
            await FadeElementAsync(_menuBg, 0f, 1f, backgroundFadeDuration, Ease.OutQuad, token);

            // 3. Fade in Logo separately
            if (logoFadeDelay > 0f)
                await Awaitable.WaitForSecondsAsync(logoFadeDelay, token);

            await FadeElementAsync(_logoWrapper, 0f, 1f, logoFadeDuration, Ease.OutCubic, token);

            // 4. Fade in Buttons
            if (buttonsFadeDelay > 0f)
                await Awaitable.WaitForSecondsAsync(buttonsFadeDelay, token);

            _buttonContainer.SetEnabled(true);
            await FadeElementAsync(_buttonContainer, 0f, 1f, buttonsFadeDuration, Ease.OutQuad, token);
        }
        catch (OperationCanceledException)
        {
            // Expected on scene unload or disable
        }
    }

    private async Awaitable PlaySingleSplashAsync(SplashScreenEntry entry, CancellationToken outerToken)
    {
        _splashStepCts?.Cancel();
        _splashStepCts?.Dispose();
        _splashStepCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        CancellationToken stepToken = _splashStepCts.Token;

        _splashImage.style.backgroundImage = new StyleBackground(entry.sprite);
        _splashImage.style.opacity = 0f;

        try
        {
            await FadeElementAsync(_splashImage, 0f, 1f, entry.fadeInDuration, Ease.OutQuad, stepToken);

            if (entry.holdDuration > 0f)
                await Awaitable.WaitForSecondsAsync(entry.holdDuration, stepToken);

            await FadeElementAsync(_splashImage, 1f, 0f, entry.fadeOutDuration, Ease.InQuad, stepToken);
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            // Player skipped this individual splash slide; reset image opacity and continue to next slide
            if (_activeTween.IsActive())
                _activeTween.Cancel();

            _splashImage.style.opacity = 0f;
        }
        finally
        {
            _splashStepCts?.Dispose();
            _splashStepCts = null;
        }
    }

    private void SkipCurrentSplashStep()
    {
        if (_splashStepCts != null && !_splashStepCts.IsCancellationRequested)
            _splashStepCts.Cancel();
    }

    private async Awaitable FadeElementAsync(VisualElement target, float from, float to, float duration, Ease ease, CancellationToken token)
    {
        if (target == null) return;

        if (_activeTween.IsActive())
            _activeTween.Cancel();

        if (duration <= 0f)
        {
            target.style.opacity = to;
            return;
        }

        target.style.opacity = from;
        var completion = new AwaitableCompletionSource();

        using var reg = token.Register(() =>
        {
            if (_activeTween.IsActive())
                _activeTween.Cancel();
            completion.TrySetCanceled();
        });

        _activeTween = LMotion.Create(from, to, duration)
            .WithEase(ease)
            .WithOnComplete(() =>
            {
                target.style.opacity = to;
                completion.TrySetResult();
            })
            .Bind(a => target.style.opacity = a);

        await completion.Awaitable;
    }

    // =========================================================================
    // MENU BUTTON ACTIONS
    // =========================================================================

    private void HandleContinueClicked()
    {
        if (_isTransitioningScene) return;
        VNSessionBridge.LaunchMode = VNLaunchMode.ContinueLatest;
        LoadGameplaySceneAsync();
    }

    private void HandleNewGameClicked()
    {
        if (_isTransitioningScene) return;
        VNSessionBridge.LaunchMode = VNLaunchMode.NewGame;
        VNSessionBridge.TargetSlotId = string.Empty;
        LoadGameplaySceneAsync();
    }

    private async void LoadGameplaySceneAsync()
    {
        if (string.IsNullOrWhiteSpace(gameplaySceneName))
        {
            Debug.LogError("[MainMenuController] Gameplay Scene Name is empty.");
            return;
        }

        _isTransitioningScene = true;
        _buttonContainer.SetEnabled(false);

        CancelAllSequenceWork();
        _sequenceCts = new CancellationTokenSource();
        CancellationToken token = _sequenceCts.Token;

        try
        {
            // 1. Fade Main Menu out to solid black
            _splashImage.style.backgroundImage = StyleKeyword.None;
            _splashOverlay.style.display = DisplayStyle.Flex;
            _splashOverlay.style.opacity = 0f;

            await FadeElementAsync(_splashOverlay, 0f, 1f, menuFadeOutDuration, Ease.InQuad, token);

            // 2. Hold on black screen before starting the game
            if (preGameStartDelay > 0f)
                await Awaitable.WaitForSecondsAsync(preGameStartDelay, token);

            // 3. Load the VN gameplay scene
            await SceneManager.LoadSceneAsync(gameplaySceneName);
        }
        catch (OperationCanceledException)
        {
            // Expected if disabled or unloaded mid-transition
        }
    }

    private static void HandleExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void CancelAllSequenceWork()
    {
        if (_activeTween.IsActive())
            _activeTween.Cancel();

        if (_splashStepCts != null)
        {
            _splashStepCts.Cancel();
            _splashStepCts.Dispose();
            _splashStepCts = null;
        }

        if (_sequenceCts != null)
        {
            _sequenceCts.Cancel();
            _sequenceCts.Dispose();
            _sequenceCts = null;
        }
    }
}