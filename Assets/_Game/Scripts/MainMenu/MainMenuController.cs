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
    private Button _btnLoadBack;

    private VisualElement _modalOverlay;
    private VisualElement _loadPanel;
    private ScrollView _loadList;
    private VisualElement _settingsPanel;
    private VisualElement _settingsContent;
    private VNSettingsView _settingsView;

    private VisualElement _fadeCurtain;
    private EventInstance _bgmInstance;
    private bool _isTransitioning;

    private readonly CompositeMotionHandle _entranceMotions = new();
    private MotionHandle _transitionMotion;

    private EventCallback<PointerEnterEvent> _onButtonHover;
    private EventCallback<PointerDownEvent> _onOverlayPointerDown;

    private void Awake()
    {
        _onButtonHover = _ => PlayOneShotSafe(hoverSfxEventPath);
        _onOverlayPointerDown = evt =>
        {
            if (evt.button == 0 && evt.target == _modalOverlay)
                CloseModals();
        };
    }

    private void OnValidate()
    {
        // Live Inspector tweaking only applies to the running UI
        if (Application.isPlaying)
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

        UnbindElements();
        StopMenuBgm(immediate: true);
    }

    private void Update()
    {
        if (_boundRoot == null || _boundRoot.panel == null || _isTransitioning || Keyboard.current == null)
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
        if (root == null)
        {
            UnbindElements();
            return;
        }

        if (_boundRoot != root || _lastBoundVersion != version)
        {
            UnbindElements();
            // Set before binding: BindElements -> ApplyMenuSizing needs to see the bound root.
            _boundRoot = root;
            _lastBoundVersion = version;
            BindElements(root);
        }
    }

    private void BindElements(VisualElement root)
    {
        _bgElement = root.Q<VisualElement>("menu-bg");
        _centerColumn = root.Q<VisualElement>("menu-center-column");
        _logoElement = root.Q<VisualElement>("menu-logo");

        _continueRim = root.Q<VisualElement>("rim-continue");
        _btnContinue = root.Q<Button>("btn-menu-continue");
        _btnNewGame = root.Q<Button>("btn-menu-newgame");
        _btnLoad = root.Q<Button>("btn-menu-load");
        _btnSettings = root.Q<Button>("btn-menu-settings");
        _btnExit = root.Q<Button>("btn-menu-exit");
        _btnLoadBack = root.Q<Button>("btn-menu-load-back");

        _buttonRims.AddRange(root.Query<VisualElement>(className: "menu-cutout-rim").ToList());
        _buttons.AddRange(root.Query<Button>(className: "menu-btn").ToList());
        _buttonLabels.AddRange(root.Query<Label>(className: "menu-btn__label").ToList());

        _modalOverlay = root.Q<VisualElement>("menu-modal-overlay");
        _loadPanel = root.Q<VisualElement>("menu-load-panel");
        _loadList = root.Q<ScrollView>("menu-load-list");
        _settingsPanel = root.Q<VisualElement>("menu-settings-panel");
        _settingsContent = root.Q<VisualElement>("menu-settings-content");
        _fadeCurtain = root.Q<VisualElement>("menu-fade-curtain");

        VisualElement settingsHost = _settingsContent ?? _settingsPanel;
        if (settingsHost != null)
            _settingsView = new VNSettingsView(settingsHost, CloseModals);

        foreach (Button btn in _buttons)
        {
            if (btn == null) continue;
            btn.focusable = false;
            btn.RegisterCallback(_onButtonHover);
        }

        if (_btnContinue != null) _btnContinue.clicked += OnContinueClicked;
        if (_btnNewGame != null) _btnNewGame.clicked += OnNewGameClicked;
        if (_btnLoad != null) _btnLoad.clicked += OnLoadClicked;
        if (_btnSettings != null) _btnSettings.clicked += OnSettingsClicked;
        if (_btnExit != null) _btnExit.clicked += OnExitClicked;
        if (_btnLoadBack != null) _btnLoadBack.clicked += CloseModals;

        _modalOverlay?.RegisterCallback(_onOverlayPointerDown);

        RefreshContinueAvailability();
        ApplyMenuSizing();
        AnimateButtonEntrance();
    }

    private void UnbindElements()
    {
        _entranceMotions.Cancel();

        if (_transitionMotion.IsActive())
            _transitionMotion.Cancel();

        _settingsView?.Dispose();
        _settingsView = null;

        foreach (Button btn in _buttons)
        {
            if (btn != null && _onButtonHover != null)
                btn.UnregisterCallback(_onButtonHover);
        }

        if (_btnContinue != null) _btnContinue.clicked -= OnContinueClicked;
        if (_btnNewGame != null) _btnNewGame.clicked -= OnNewGameClicked;
        if (_btnLoad != null) _btnLoad.clicked -= OnLoadClicked;
        if (_btnSettings != null) _btnSettings.clicked -= OnSettingsClicked;
        if (_btnExit != null) _btnExit.clicked -= OnExitClicked;
        if (_btnLoadBack != null) _btnLoadBack.clicked -= CloseModals;

        if (_modalOverlay != null && _onOverlayPointerDown != null)
            _modalOverlay.UnregisterCallback(_onOverlayPointerDown);

        _buttonRims.Clear();
        _buttons.Clear();
        _buttonLabels.Clear();

        _bgElement = null;
        _centerColumn = null;
        _logoElement = null;
        _continueRim = null;
        _btnContinue = null;
        _btnNewGame = null;
        _btnLoad = null;
        _btnSettings = null;
        _btnExit = null;
        _btnLoadBack = null;
        _modalOverlay = null;
        _loadPanel = null;
        _loadList = null;
        _settingsPanel = null;
        _settingsContent = null;
        _fadeCurtain = null;

        _boundRoot = null;
        _lastBoundVersion = -1;
    }

    public void ApplyMenuSizing()
    {
        // Elements may not be attached to the panel yet while the UI is being (re)built; setting inline
        // styles on them is fine. Only skip when nothing is bound.
        if (_boundRoot == null)
            return;

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
        _entranceMotions.Cancel();

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
                    // Stale elements can't reach here: UnbindElements cancels these motions before any
                    // rebind and in OnDisable. Don't skip detached frames, or the final value could be lost.
                    if (rim == null) return;
                    rim.style.opacity = Mathf.Lerp(0f, targetAlpha, t);
                    rim.style.translate = new Translate(0f, Mathf.Lerp(16f, 0f, t));
                })
                .AddTo(_entranceMotions);
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
        if (_isTransitioning || VNSaveManager.Instance == null || _loadList == null) return;
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

        if (_modalOverlay != null) _modalOverlay.style.display = DisplayStyle.Flex;
        if (_loadPanel != null) _loadPanel.style.display = DisplayStyle.Flex;
        if (_settingsPanel != null) _settingsPanel.style.display = DisplayStyle.None;
    }

    private void OnSettingsClicked()
    {
        if (_isTransitioning) return;
        PlayOneShotSafe(clickSfxEventPath);

        _settingsView?.SyncFromManager();
        if (_modalOverlay != null) _modalOverlay.style.display = DisplayStyle.Flex;
        if (_settingsPanel != null) _settingsPanel.style.display = DisplayStyle.Flex;
        if (_loadPanel != null) _loadPanel.style.display = DisplayStyle.None;
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

        if (_transitionMotion.IsActive())
            _transitionMotion.Cancel();

        _fadeCurtain.style.opacity = 0f;
        _fadeCurtain.style.display = DisplayStyle.Flex;

        VisualElement curtain = _fadeCurtain;
        _transitionMotion = LMotion.Create(0f, 1f, sceneTransitionDuration)
            .WithEase(Ease.InOutQuad)
            .WithOnComplete(() => SceneManager.LoadSceneAsync(gameplaySceneName))
            .Bind(a =>
            {
                if (curtain == null) return;
                curtain.style.opacity = a;
            });
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