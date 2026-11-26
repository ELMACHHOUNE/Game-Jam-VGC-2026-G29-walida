using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using LitMotion;
using FMOD.Studio;
using FMODUnity;

public class MainMenuController : MonoBehaviour
{
    [Header("UI Panel")]
    [SerializeField] private PanelRenderer panelRenderer;

    [Header("Scene Flow")]
    [SerializeField] private string gameplaySceneName = "MedinaScene";
    [SerializeField, Min(0.1f)] private float sceneTransitionDuration = 0.55f;

    [Header("Optional Artwork (Leave None If Already Rendered by Scene Camera)")]
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite logoSprite;

    [Header("Live Layout & Sizing (Inspector Preview)")]
    [SerializeField, Range(5f, 65f)] private float stackTopOffsetPercent = 37f;
    [SerializeField, Range(0.6f, 2.2f)] private float menuScale = 1.15f;
    [SerializeField, Min(180f)] private float buttonWidth = 310f;
    [SerializeField, Min(34f)] private float buttonHeight = 48f;
    [SerializeField, Min(4f)] private float buttonSpacing = 11f;
    [SerializeField, Min(11f)] private float buttonFontSize = 16f;
    [SerializeField] private Vector2 logoSize = new Vector2(440f, 220f);

    [Header("FMOD Audio (Optional)")]
    [SerializeField] private string menuBgmEventPath = "event:/BGM/MainMenu";
    [SerializeField] private string hoverSfxEventPath = "event:/UI/Hover";
    [SerializeField] private string clickSfxEventPath = "event:/UI/Click";

    private VisualElement _boundRoot;
    private int _lastBoundVersion = -1;

    private VisualElement _bgElement;
    private VisualElement _centerColumn;
    private VisualElement _logoElement;

    private readonly List<VisualElement> _buttonRims = new();
    private readonly List<Button> _buttons = new();
    private readonly List<Label> _buttonLabels = new();

    private VisualElement _continueRim;
    private Button _btnContinue;
    private Button _btnNewGame;
    private Button _btnLoad;
    private Button _btnSettings;
    private Button _btnExit;

    private VisualElement _modalOverlay;
    private VisualElement _loadPanel;
    private ScrollView _loadList;
    private VisualElement _settingsPanel;
    private VisualElement _settingsContent;
    private VNSettingsView _settingsView;

    private VisualElement _fadeCurtain;
    private EventInstance _bgmInstance;
    private bool _isTransitioning;

    private void OnValidate()
    {
        ApplyMenuSizing();
    }

    private void OnEnable()
    {
        if (panelRenderer == null)
        {
            Debug.LogError("[MainMenuController] PanelRenderer reference is missing.");
            return;
        }

        panelRenderer.RegisterUIReloadCallback(HandleUIReload);
    }

    private void Start()
    {
        if (VNSettingsManager.Instance != null)
            VNSettingsManager.Instance.ApplyAllFmodBuses();

        StartMenuBgm();
    }

    private void OnDisable()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(HandleUIReload);

        _settingsView?.Dispose();
        _settingsView = null;
        StopMenuBgm(immediate: true);
    }

    private void Update()
    {
        if (_boundRoot == null || _isTransitioning || Keyboard.current == null)
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame &&
            _modalOverlay != null &&
            _modalOverlay.resolvedStyle.display == DisplayStyle.Flex)
        {
            CloseModals();
        }
    }

    private void HandleUIReload(PanelRenderer pr, VisualElement root, int version)
    {
        if (root == null) return;

        if (_boundRoot != root || _lastBoundVersion != version)
        {
            BindElements(root);
            _boundRoot = root;
            _lastBoundVersion = version;
        }
    }

    private void BindElements(VisualElement root)
    {
        _bgElement = root.Q<VisualElement>("menu-bg");
        _centerColumn = root.Q<VisualElement>("menu-center-column");
        _logoElement = root.Q<VisualElement>("menu-logo");

        _buttonRims.Clear();
        _buttons.Clear();
        _buttonLabels.Clear();

        _continueRim = root.Q<VisualElement>("rim-continue");
        _btnContinue = root.Q<Button>("btn-menu-continue");
        _btnNewGame = root.Q<Button>("btn-menu-newgame");
        _btnLoad = root.Q<Button>("btn-menu-load");
        _btnSettings = root.Q<Button>("btn-menu-settings");
        _btnExit = root.Q<Button>("btn-menu-exit");

        _buttonRims.AddRange(root.Query<VisualElement>(className: "menu-cutout-rim").ToList());
        _buttons.AddRange(root.Query<Button>(className: "menu-btn").ToList());
        _buttonLabels.AddRange(root.Query<Label>(className: "menu-btn__label").ToList());

        _modalOverlay = root.Q<VisualElement>("menu-modal-overlay");
        _loadPanel = root.Q<VisualElement>("menu-load-panel");
        _loadList = root.Q<ScrollView>("menu-load-list");
        _settingsPanel = root.Q<VisualElement>("menu-settings-panel");
        _settingsContent = root.Q<VisualElement>("menu-settings-content");
        _fadeCurtain = root.Q<VisualElement>("menu-fade-curtain");

        _settingsView?.Dispose();
        _settingsView = new VNSettingsView(_settingsContent ?? _settingsPanel, CloseModals);

        foreach (Button btn in _buttons)
        {
            btn.focusable = false;
            btn.RegisterCallback<PointerEnterEvent>(_ => PlayOneShotSafe(hoverSfxEventPath));
        }

        _btnContinue.clicked += OnContinueClicked;
        _btnNewGame.clicked += OnNewGameClicked;
        _btnLoad.clicked += OnLoadClicked;
        _btnSettings.clicked += OnSettingsClicked;
        _btnExit.clicked += OnExitClicked;

        root.Q<Button>("btn-menu-load-back").clicked += CloseModals;

        _modalOverlay.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0 && evt.target == _modalOverlay)
                CloseModals();
        });

        RefreshContinueAvailability();
        ApplyMenuSizing();
        AnimateButtonEntrance();
    }

    public void ApplyMenuSizing()
    {
        float s = Mathf.Max(0.1f, menuScale);

        if (_bgElement != null)
        {
            bool hasBg = backgroundSprite != null;
            _bgElement.style.display = hasBg ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasBg)
                _bgElement.style.backgroundImage = new StyleBackground(backgroundSprite);
        }

        if (_logoElement != null)
        {
            bool hasLogo = logoSprite != null;
            _logoElement.style.display = hasLogo ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasLogo)
            {
                _logoElement.style.backgroundImage = new StyleBackground(logoSprite);
                _logoElement.style.width = logoSize.x * s;
                _logoElement.style.height = logoSize.y * s;
            }
        }

        if (_centerColumn != null)
            _centerColumn.style.top = Length.Percent(stackTopOffsetPercent);

        foreach (VisualElement rim in _buttonRims)
        {
            if (rim != null)
                rim.style.marginBottom = buttonSpacing * s;
        }

        foreach (Button btn in _buttons)
        {
            if (btn == null) continue;
            btn.style.width = buttonWidth * s;
            btn.style.height = buttonHeight * s;
        }

        foreach (Label lbl in _buttonLabels)
        {
            if (lbl != null)
                lbl.style.fontSize = buttonFontSize * s;
        }
    }

    private void RefreshContinueAvailability()
    {
        bool hasSave = VNSaveManager.Instance != null && VNSaveManager.Instance.GetLatestSave() != null;

        if (_btnContinue != null)
        {
            _btnContinue.SetEnabled(hasSave);
            _btnContinue.EnableInClassList("menu-btn--primary", hasSave);
        }

        if (_continueRim != null)
            _continueRim.EnableInClassList("menu-cutout-rim--disabled", !hasSave);
    }

    private void AnimateButtonEntrance()
    {
        for (int i = 0; i < _buttonRims.Count; i++)
        {
            VisualElement rim = _buttonRims[i];
            if (rim == null) continue;

            float targetAlpha = (rim == _continueRim && rim.ClassListContains("menu-cutout-rim--disabled")) ? 0.52f : 1f;
            float delay = i * 0.055f;

            rim.style.opacity = 0f;
            rim.style.translate = new Translate(0f, 16f);

            LMotion.Create(0f, 1f, 0.32f)
                .WithDelay(delay)
                .WithEase(Ease.OutCubic)
                .Bind(t =>
                {
                    rim.style.opacity = Mathf.Lerp(0f, targetAlpha, t);
                    rim.style.translate = new Translate(0f, Mathf.Lerp(16f, 0f, t));
                });
        }
    }

    // =========================================================================
    // BUTTON ACTIONS
    // =========================================================================

    private void OnContinueClicked()
    {
        if (_isTransitioning) return;
        PlayOneShotSafe(clickSfxEventPath);

        VNSessionBridge.CachedInkStateJson = string.Empty;
        VNSessionBridge.LaunchMode = VNLaunchMode.ContinueLatest;
        TransitionToGameScene();
    }

    private void OnNewGameClicked()
    {
        if (_isTransitioning) return;
        PlayOneShotSafe(clickSfxEventPath);

        VNSessionBridge.CachedInkStateJson = string.Empty;
        VNSessionBridge.TargetSlotId = string.Empty;
        VNSessionBridge.LaunchMode = VNLaunchMode.NewGame;
        TransitionToGameScene();
    }

    private void OnLoadClicked()
    {
        if (_isTransitioning || VNSaveManager.Instance == null) return;
        PlayOneShotSafe(clickSfxEventPath);

        IReadOnlyList<VNSaveSlotSummary> slots = VNSaveManager.Instance.GetAllSlotSummaries(includeAutosave: true);
        _loadList.Clear();

        foreach (VNSaveSlotSummary slot in slots)
        {
            string capturedId = slot.SlotId;
            bool isEmpty = slot.IsEmpty;

            var card = new Button(() =>
            {
                PlayOneShotSafe(clickSfxEventPath);
                VNSessionBridge.CachedInkStateJson = string.Empty;
                VNSessionBridge.TargetSlotId = capturedId;
                VNSessionBridge.LaunchMode = VNLaunchMode.LoadSpecificSlot;
                CloseModals();
                TransitionToGameScene();
            });

            card.focusable = false;
            card.AddToClassList("slot-card");
            if (isEmpty)
            {
                card.AddToClassList("slot-card--empty");
                card.SetEnabled(false);
                card.pickingMode = PickingMode.Ignore;
            }

            var header = new VisualElement();
            header.AddToClassList("slot-card__header");
            header.pickingMode = PickingMode.Ignore;

            var titleLbl = new Label(slot.Title);
            titleLbl.AddToClassList("slot-card__title");
            titleLbl.pickingMode = PickingMode.Ignore;

            var timeLbl = new Label(slot.Timestamp);
            timeLbl.AddToClassList("slot-card__time");
            timeLbl.pickingMode = PickingMode.Ignore;

            var previewLbl = new Label(slot.Preview);
            previewLbl.AddToClassList("slot-card__preview");
            previewLbl.pickingMode = PickingMode.Ignore;

            header.Add(titleLbl);
            header.Add(timeLbl);
            card.Add(header);
            card.Add(previewLbl);
            _loadList.Add(card);
        }

        _modalOverlay.style.display = DisplayStyle.Flex;
        _loadPanel.style.display = DisplayStyle.Flex;
        _settingsPanel.style.display = DisplayStyle.None;
    }

    private void OnSettingsClicked()
    {
        if (_isTransitioning) return;
        PlayOneShotSafe(clickSfxEventPath);

        _settingsView?.SyncFromManager();
        _modalOverlay.style.display = DisplayStyle.Flex;
        _settingsPanel.style.display = DisplayStyle.Flex;
        _loadPanel.style.display = DisplayStyle.None;
    }

    private void CloseModals()
    {
        if (_modalOverlay != null) _modalOverlay.style.display = DisplayStyle.None;
        if (_loadPanel != null) _loadPanel.style.display = DisplayStyle.None;
        if (_settingsPanel != null) _settingsPanel.style.display = DisplayStyle.None;
    }

    private void OnExitClicked()
    {
        if (_isTransitioning) return;
        PlayOneShotSafe(clickSfxEventPath);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void TransitionToGameScene()
    {
        _isTransitioning = true;
        StopMenuBgm(immediate: false);

        // A minigame return point from a previous session must not leak into New Game / Load.
        MedinaLevelManager.ClearSavedReturnPosition();

        if (_fadeCurtain == null || sceneTransitionDuration <= 0f)
        {
            SceneManager.LoadSceneAsync(gameplaySceneName);
            return;
        }

        _fadeCurtain.style.opacity = 0f;
        _fadeCurtain.style.display = DisplayStyle.Flex;

        LMotion.Create(0f, 1f, sceneTransitionDuration)
            .WithEase(Ease.InOutQuad)
            .WithOnComplete(() => SceneManager.LoadSceneAsync(gameplaySceneName))
            .Bind(a => _fadeCurtain.style.opacity = a);
    }

    // =========================================================================
    // FMOD AUDIO HELPERS
    // =========================================================================

    private void StartMenuBgm()
    {
        if (string.IsNullOrWhiteSpace(menuBgmEventPath) || !RuntimeManager.IsInitialized)
            return;

        try
        {
            _bgmInstance = RuntimeManager.CreateInstance(menuBgmEventPath);
            _bgmInstance.start();
        }
        catch (EventNotFoundException)
        {
            // Optional BGM event not configured yet
        }
    }

    private void StopMenuBgm(bool immediate)
    {
        if (!_bgmInstance.isValid()) return;

        _bgmInstance.stop(immediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _bgmInstance.release();
        _bgmInstance.clearHandle();
    }

    private static void PlayOneShotSafe(string eventPath)
    {
        if (string.IsNullOrWhiteSpace(eventPath) || !RuntimeManager.IsInitialized)
            return;

        try
        {
            RuntimeManager.PlayOneShot(eventPath);
        }
        catch (EventNotFoundException) { }
    }
}