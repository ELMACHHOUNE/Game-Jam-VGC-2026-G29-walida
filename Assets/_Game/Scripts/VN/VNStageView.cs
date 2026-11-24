using LitMotion;
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Animation;
using UnityEngine;
using UnityEngine.UIElements;

public enum StageSlot { Auto, Left, Center, Right }

public readonly struct VNChoiceOption
{
    public readonly string Text;
    public readonly bool IsVisited;

    public VNChoiceOption(string text, bool isVisited)
    {
        Text = text;
        IsVisited = isVisited;
    }
}

public class VNStageView : MonoBehaviour
{
    [Header("UI Panel")]
    [SerializeField] private PanelRenderer panelRenderer;

    [Header("Top-Right Button Icons (Optional - Procedural Fallbacks Used If Empty)")]
    [SerializeField] private Sprite autoplayIconSprite;
    [SerializeField] private Sprite skipIconSprite;
    [SerializeField] private Sprite pauseIconSprite;

    [Header("Character Registry")]
    [SerializeField] private List<VNCharacterData> characterDatabase = new();

    [Header("Timings")]
    [SerializeField] private float defaultCharsPerSecond = 45f;
    [SerializeField] private float bgFadeDuration = 0.45f;
    [SerializeField] private float charMoveDuration = 0.28f;
    [SerializeField] private float charFadeDuration = 0.22f;

    [Header("Typewriter Audio")]
    [SerializeField] private AudioSource uiAudioSource;
    [SerializeField] private AudioClip typingClickClip;
    [SerializeField, Range(0f, 1f)] private float typingClickVolume = 0.45f;
    [SerializeField, Min(1)] private int charsPerClick = 2;
    [SerializeField] private Vector2 pitchJitter = new Vector2(0.96f, 1.04f);

    // Lifecycle & Input Events consumed by VNStoryController
    public event Action OnViewReady;
    public event Action OnAdvanceRequested;
    public event Action<int> OnChoiceSelected;
    public event Action OnLineTypingCompleted;
    public event Action OnAutoClicked;
    public event Action OnSkipClicked;
    public event Action OnPauseClicked;
    public event Action OnResumeClicked;
    public event Action OnSaveClicked;
    public event Action OnLoadClicked;
    public event Action OnQuitClicked;

    public bool IsReady => _boundRoot != null;
    public bool IsTyping => _typewriter != null && _typewriter.IsTyping;

    // UXML Elements
    private VisualElement _boundRoot;
    private int _lastBoundVersion = -1;
    private VisualElement _bgBack;
    private VisualElement _bgFront;
    private VisualElement _stageLayer;
    private VisualElement _cgLayer;
    private VisualElement _advanceSurface;
    private VisualElement _dialogueBox;
    private VisualElement _nameplate;
    private Label _speakerLabel;
    private Label _dialogueLabel;
    private VisualElement _continueIndicator;
    private VisualElement _choiceOverlay;
    private VisualElement _choiceList;
    private VisualElement _modalOverlay;

    private Button _btnAutoplay;
    private VisualElement _autoplayIcon;
    private Button _btnSkip;
    private Button _btnPause;

    // Runtime State

    private int _audibleCharCount;
    private AudioClip _fallbackClickClip;
    private AudioClip _activeVoiceClip;
    private float _activeBasePitch = 1f;
    private float _activePitchVariance = 0.04f;
    private RichTextTypewriter _typewriter;
    private readonly Dictionary<string, VNCharacterData> _charDb = new();
    private readonly Dictionary<string, VisualElement> _activeSlots = new();
    private readonly Dictionary<string, StageSlot> _assignedSlots = new();
    private readonly Dictionary<string, MotionHandle> _charMoveTweens = new();
    private readonly Dictionary<string, MotionHandle> _charFadeTweens = new();
    private readonly List<string> _entranceOrder = new();
    private readonly List<UnityEngine.Object> _proceduralIcons = new();


    private MotionHandle _bgTween;
    private MotionHandle _cgTween;
    private MotionHandle _indicatorTween;
    private MotionHandle _autoplaySpinTween;
    private void Awake()
    {
        foreach (var data in characterDatabase)
        {
            if (data != null && !string.IsNullOrEmpty(data.characterId))
                _charDb[data.characterId.ToLowerInvariant()] = data;
        }

        if (uiAudioSource == null)
            uiAudioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        uiAudioSource.playOnAwake = false;

        if (typingClickClip == null)
            _fallbackClickClip = CreateProceduralClickClip();
    }

    private void OnEnable()
    {
        if (panelRenderer == null)
        {
            Debug.LogError("[VNStageView] PanelRenderer reference is missing.");
            return;
        }

        panelRenderer.RegisterUIReloadCallback(HandleUIReload);
    }

    private void OnDisable()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(HandleUIReload);

        if (_typewriter != null)
        {
            _typewriter.OnCharacterRevealed -= HandleCharacterRevealed;
            _typewriter.OnCompleted -= HandleTypingCompleted;
            _typewriter.Dispose();
            _typewriter = null;
        }

    }

    private void OnDestroy()
    {
        CancelHandle(_bgTween);
        CancelHandle(_cgTween);
        CancelHandle(_indicatorTween);
        CancelHandle(_autoplaySpinTween);

        foreach (var handle in _charMoveTweens.Values) CancelHandle(handle);
        foreach (var handle in _charFadeTweens.Values) CancelHandle(handle);

        foreach (var obj in _proceduralIcons)
        {
            if (obj != null) Destroy(obj);
        }
        _proceduralIcons.Clear();

        if (_fallbackClickClip != null)
            Destroy(_fallbackClickClip);
    }

    private void HandleUIReload(PanelRenderer pr, VisualElement root, int version)
    {
        if (root == null) return;

        if (_boundRoot != root || _lastBoundVersion != version)
        {
            _activeSlots.Clear();
            _assignedSlots.Clear();
            _entranceOrder.Clear();

            BindUxmlElements(root);
            _boundRoot = root;
            _lastBoundVersion = version;
        }

        if (_typewriter != null)
        {
            _typewriter.OnCharacterRevealed -= HandleCharacterRevealed;
            _typewriter.OnCompleted -= HandleTypingCompleted;
            _typewriter.Dispose();
        }

        _typewriter = new RichTextTypewriter(_dialogueLabel);
        _typewriter.OnCharacterRevealed += HandleCharacterRevealed;
        _typewriter.OnCompleted += HandleTypingCompleted;

        OnViewReady?.Invoke();
    }

    private void BindUxmlElements(VisualElement root)
    {
        _bgBack = root.Q<VisualElement>("bg-back");
        _bgFront = root.Q<VisualElement>("bg-front");
        _stageLayer = root.Q<VisualElement>("layer-stage");
        _cgLayer = root.Q<VisualElement>("layer-cg");
        _advanceSurface = root.Q<VisualElement>("advance-surface");

        _dialogueBox = root.Q<VisualElement>("dialogue-box");
        _nameplate = root.Q<VisualElement>("nameplate");
        _speakerLabel = root.Q<Label>("speaker-label");
        _dialogueLabel = root.Q<Label>("dialogue-label");
        _continueIndicator = root.Q<VisualElement>("continue-indicator");

        _choiceOverlay = root.Q<VisualElement>("layer-choices");
        _choiceList = root.Q<VisualElement>("choice-list");
        _modalOverlay = root.Q<VisualElement>("layer-modal");

        _btnAutoplay = root.Q<Button>("btn-autoplay");
        _autoplayIcon = root.Q<VisualElement>("icon-autoplay");
        _btnSkip = root.Q<Button>("btn-skip");
        _btnPause = root.Q<Button>("btn-pause");

        var btnResume = root.Q<Button>("btn-resume");
        var btnSave = root.Q<Button>("btn-pause-save");
        var btnLoad = root.Q<Button>("btn-pause-load");
        var btnQuit = root.Q<Button>("btn-pause-quit");

        // Prevent Space/Enter from re-triggering top icons after clicking them
        _btnAutoplay.focusable = false;
        _btnSkip.focusable = false;
        _btnPause.focusable = false;

        // Assign custom Inspector icons or generate clean procedural icons
        _autoplayIcon.style.backgroundImage = new StyleBackground(
            autoplayIconSprite != null ? autoplayIconSprite : CreateAutoplayIcon());
        root.Q<VisualElement>("icon-skip").style.backgroundImage = new StyleBackground(
            skipIconSprite != null ? skipIconSprite : CreateSkipIcon());
        root.Q<VisualElement>("icon-pause").style.backgroundImage = new StyleBackground(
            pauseIconSprite != null ? pauseIconSprite : CreatePauseIcon());

        _advanceSurface.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0) OnAdvanceRequested?.Invoke();
        });
        _dialogueBox.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0) OnAdvanceRequested?.Invoke();
        });

        _btnAutoplay.clicked += () => OnAutoClicked?.Invoke();
        _btnSkip.clicked += () => OnSkipClicked?.Invoke();
        _btnPause.clicked += () => OnPauseClicked?.Invoke();

        btnResume.clicked += () => OnResumeClicked?.Invoke();
        btnSave.clicked += () => OnSaveClicked?.Invoke();
        btnLoad.clicked += () => OnLoadClicked?.Invoke();
        btnQuit.clicked += () => OnQuitClicked?.Invoke();
    }

    // =========================================================================
    // DIALOGUE & CHOICES
    // =========================================================================

    public void PresentLine(string speakerIdOrName, string text, bool instant = false)
    {
        _audibleCharCount = 0;
        _activeVoiceClip = typingClickClip != null ? typingClickClip : _fallbackClickClip;
        _activeBasePitch = 1f;
        _activePitchVariance = Mathf.Abs(pitchJitter.y - pitchJitter.x) * 0.5f;
        HideChoices();
        StopContinueIndicator();

        if (string.IsNullOrWhiteSpace(speakerIdOrName))
        {
            // Narration line: hide nameplate and dim all active portraits
            _nameplate.style.display = DisplayStyle.None;
            SetActiveSpeaker(null);
        }
        else
        {
            _nameplate.style.display = DisplayStyle.Flex;
            string lookupKey = speakerIdOrName.ToLowerInvariant();

            if (_charDb.TryGetValue(lookupKey, out var charData))
            {
                if (charData.voiceBlip != null)
                {
                    _activeVoiceClip = charData.voiceBlip;
                    _activeBasePitch = charData.basePitch;
                    _activePitchVariance = charData.pitchVariance;
                }
                _speakerLabel.text = charData.displayName;
                _speakerLabel.style.color = charData.nameColor;
                _nameplate.style.borderTopColor = charData.nameColor;
                _nameplate.style.borderBottomColor = charData.nameColor;
                _nameplate.style.borderLeftColor = charData.nameColor;
                _nameplate.style.borderRightColor = charData.nameColor;
                SetActiveSpeaker(lookupKey);
            }
            else
            {
                _speakerLabel.text = speakerIdOrName;
                SetActiveSpeaker(null);
            }
        }

        if (instant)
        {
            _dialogueLabel.text = text;
            HandleTypingCompleted();
        }
        else
        {
            _typewriter.Play(text, defaultCharsPerSecond);
        }
    }

    public void CompleteTyping() => _typewriter?.CompleteImmediately();

    public void ShowChoices(IReadOnlyList<VNChoiceOption> choices)
    {
        StopContinueIndicator();
        _choiceList.Clear();

        for (int i = 0; i < choices.Count; i++)
        {
            int capturedIndex = i;
            VNChoiceOption option = choices[i];

            var btn = new Button(() =>
            {
                HideChoices();
                OnChoiceSelected?.Invoke(capturedIndex);
            })
            {
                text = option.Text
            };

            btn.AddToClassList("choice-btn");

            if (option.IsVisited)
            {
                btn.AddToClassList("choice-btn--visited");
                btn.SetEnabled(false);
                btn.pickingMode = PickingMode.Ignore;
            }

            _choiceList.Add(btn);
        }

        _choiceOverlay.style.display = DisplayStyle.Flex;
    }

    public void HideChoices()
    {
        _choiceOverlay.style.display = DisplayStyle.None;
        _choiceList.Clear();
    }

    public void SetAutoState(bool active)
    {
        if (_btnAutoplay == null || _autoplayIcon == null) return;

        _btnAutoplay.EnableInClassList("icon-btn--active", active);
        CancelHandle(_autoplaySpinTween);

        if (active)
        {
            _autoplaySpinTween = LMotion.Create(0f, 360f, 1.4f)
                .WithEase(Ease.Linear)
                .WithLoops(-1, LoopType.Restart)
                .Bind(deg => _autoplayIcon.style.rotate = new Rotate(deg));
        }
        else
        {
            _autoplayIcon.style.rotate = new Rotate(0f);
        }
    }

    public void ShowPauseMenu(bool show)
    {
        if (_modalOverlay != null)
            _modalOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void SetSkipState(bool active)
    {
        if (_btnSkip == null) return;
        _btnSkip.EnableInClassList("quick-btn--active", active);
        _btnSkip.text = active ? "SKIP: ON" : "SKIP";
    }

    private void HandleTypingCompleted()
    {
        CancelHandle(_indicatorTween);
        _continueIndicator.style.opacity = 0.25f;

        _indicatorTween = LMotion.Create(0.25f, 1f, 0.55f)
            .WithEase(Ease.InOutSine)
            .WithLoops(-1, LoopType.Yoyo)
            .Bind(a => _continueIndicator.style.opacity = a);

        OnLineTypingCompleted?.Invoke();
    }

    private void StopContinueIndicator()
    {
        CancelHandle(_indicatorTween);
        _continueIndicator.style.opacity = 0f;
    }

    // =========================================================================
    // BACKGROUNDS & EVENT CGS
    // =========================================================================

    public void SetBackground(Sprite newSprite, bool instant = false)
    {
        CancelHandle(_bgTween);

        if (newSprite == null)
        {
            _bgBack.style.backgroundImage = StyleKeyword.None;
            _bgFront.style.backgroundImage = StyleKeyword.None;
            return;
        }

        var nextBg = new StyleBackground(newSprite);

        if (instant || bgFadeDuration <= 0f)
        {
            _bgBack.style.backgroundImage = nextBg;
            _bgFront.style.backgroundImage = StyleKeyword.None;
            _bgFront.style.opacity = 0f;
            return;
        }

        _bgFront.style.backgroundImage = nextBg;
        _bgFront.style.opacity = 0f;

        _bgTween = LMotion.Create(0f, 1f, bgFadeDuration)
            .WithEase(Ease.OutQuad)
            .WithOnComplete(() =>
            {
                _bgBack.style.backgroundImage = nextBg;
                _bgFront.style.backgroundImage = StyleKeyword.None;
                _bgFront.style.opacity = 0f;
            })
            .Bind(a => _bgFront.style.opacity = a);
    }

    public void ShowEventCG(Sprite cgSprite, bool instant = false)
    {
        CancelHandle(_cgTween);

        if (cgSprite == null)
        {
            HideEventCG(instant);
            return;
        }

        _cgLayer.style.backgroundImage = new StyleBackground(cgSprite);
        _cgLayer.style.display = DisplayStyle.Flex;

        if (instant)
        {
            _cgLayer.style.opacity = 1f;
            return;
        }

        _cgTween = LMotion.Create(_cgLayer.resolvedStyle.opacity, 1f, bgFadeDuration)
            .WithEase(Ease.OutQuad)
            .Bind(a => _cgLayer.style.opacity = a);
    }

    public void HideEventCG(bool instant = false)
    {
        CancelHandle(_cgTween);

        if (instant)
        {
            _cgLayer.style.opacity = 0f;
            _cgLayer.style.display = DisplayStyle.None;
            _cgLayer.style.backgroundImage = StyleKeyword.None;
            return;
        }

        _cgTween = LMotion.Create(_cgLayer.resolvedStyle.opacity, 0f, bgFadeDuration)
            .WithEase(Ease.OutQuad)
            .WithOnComplete(() =>
            {
                _cgLayer.style.display = DisplayStyle.None;
                _cgLayer.style.backgroundImage = StyleKeyword.None;
            })
            .Bind(a => _cgLayer.style.opacity = a);
    }

    // =========================================================================
    // CHARACTER STAGE
    // =========================================================================

    public void ShowCharacter(string rawId, string expression = "neutral", StageSlot requestedSlot = StageSlot.Auto)
    {
        string id = rawId.ToLowerInvariant();
        if (!_charDb.TryGetValue(id, out var data))
        {
            Debug.LogWarning($"[VNStageView] Unknown character ID: {id}");
            return;
        }

        bool isNewEntrance = !_activeSlots.TryGetValue(id, out var slot);
        if (isNewEntrance)
        {
            slot = new VisualElement();
            slot.AddToClassList("character-slot");
            slot.pickingMode = PickingMode.Ignore;
            slot.style.opacity = 0f;

            _stageLayer.Add(slot);
            _activeSlots[id] = slot;
            _entranceOrder.Add(id);
        }

        Sprite sprite = data.GetExpression(expression);
        if (sprite != null)
            slot.style.backgroundImage = new StyleBackground(sprite);

        _assignedSlots[id] = requestedSlot;
        RecalculateStageLayout(isNewEntrance ? id : null);

        if (isNewEntrance)
            FadeCharacter(id, slot, 0f, 1f, destroyOnComplete: false);
    }

    public void HideCharacter(string rawId)
    {
        string id = rawId.ToLowerInvariant();
        if (!_activeSlots.TryGetValue(id, out var slot))
            return;

        _activeSlots.Remove(id);
        _assignedSlots.Remove(id);
        _entranceOrder.Remove(id);

        RecalculateStageLayout(newlyEnteredId: null);
        FadeCharacter(id, slot, slot.resolvedStyle.opacity, 0f, destroyOnComplete: true);
    }

    public void ClearAllCharacters()
    {
        foreach (var id in new List<string>(_activeSlots.Keys))
            HideCharacter(id);
    }

    public void SetActiveSpeaker(string rawId)
    {
        string speakerId = string.IsNullOrEmpty(rawId) ? string.Empty : rawId.ToLowerInvariant();

        foreach (var (id, slot) in _activeSlots)
        {
            if (id == speakerId)
            {
                slot.RemoveFromClassList("character-slot--dimmed");
                slot.BringToFront();
            }
            else
            {
                slot.AddToClassList("character-slot--dimmed");
            }
        }
    }

    private void RecalculateStageLayout(string newlyEnteredId)
    {
        int count = _entranceOrder.Count;

        for (int i = 0; i < count; i++)
        {
            string id = _entranceOrder[i];
            VisualElement slot = _activeSlots[id];
            StageSlot explicitSlot = _assignedSlots[id];

            float targetX = explicitSlot != StageSlot.Auto
                ? SlotToX(explicitSlot)
                : ResolveAutoX(i, count);

            if (id == newlyEnteredId)
            {
                slot.style.translate = new Translate(targetX, 0f);
                continue;
            }

            MoveCharacter(id, slot, targetX);
        }
    }

    private static float ResolveAutoX(int index, int totalCount)
    {
        if (totalCount <= 1) return 0f;
        if (totalCount == 2) return index == 0 ? -420f : 420f;
        return index switch
        {
            0 => -540f,
            1 => 0f,
            _ => 540f
        };
    }

    private static float SlotToX(StageSlot slot) => slot switch
    {
        StageSlot.Left => -420f,
        StageSlot.Right => 420f,
        _ => 0f
    };

    private void MoveCharacter(string id, VisualElement slot, float targetX)
    {
        if (_charMoveTweens.TryGetValue(id, out var existing))
            CancelHandle(existing);

        float startX = slot.resolvedStyle.translate.x;
        if (Mathf.Approximately(startX, targetX))
            return;

        _charMoveTweens[id] = LMotion.Create(startX, targetX, charMoveDuration)
            .WithEase(Ease.OutCubic)
            .Bind(x => slot.style.translate = new Translate(x, 0f));
    }

    private void FadeCharacter(string id, VisualElement slot, float from, float to, bool destroyOnComplete)
    {
        if (_charFadeTweens.TryGetValue(id, out var existing))
            CancelHandle(existing);

        var builder = LMotion.Create(from, to, charFadeDuration).WithEase(Ease.OutQuad);

        _charFadeTweens[id] = destroyOnComplete
            ? builder.WithOnComplete(() => slot.RemoveFromHierarchy()).Bind(a => slot.style.opacity = a)
            : builder.Bind(a => slot.style.opacity = a);
    }

    private static void CancelHandle(MotionHandle handle)
    {
        if (handle.IsActive()) handle.Cancel();
    }

    public void RegisterCharacter(VNCharacterData data)
    {
        if (data != null && !string.IsNullOrEmpty(data.characterId))
            _charDb[data.characterId.ToLowerInvariant()] = data;
    }

    // =========================================================================
    // PROCEDURAL FALLBACK ICONS (64x64 FullRect Sprites)
    // =========================================================================

    private Sprite CreateAutoplayIcon()
    {
        // Circular ring with gap + arrowhead (spins visibly when active)
        return BuildIconSprite(64, (x, y) =>
        {
            float dx = x - 32f, dy = y - 32f;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // -180 to 180
            bool onRing = dist >= 16f && dist <= 22f && !(angle > 15f && angle < 75f);
            bool onArrow = Mathf.Abs(x - 49f) + Mathf.Abs(y - 36f) <= 7f && y <= 37f;
            return onRing || onArrow;
        });
    }

    private Sprite CreateSkipIcon()
    {
        // Double forward triangles + end bar (>>|)
        return BuildIconSprite(64, (x, y) =>
        {
            float dy = Mathf.Abs(y - 32f);
            bool tri1 = x >= 12 && x <= 28 && dy <= (28 - x) * 0.85f;
            bool tri2 = x >= 28 && x <= 44 && dy <= (44 - x) * 0.85f;
            bool bar = x >= 47 && x <= 52 && dy <= 14f;
            return tri1 || tri2 || bar;
        });
    }

    private Sprite CreatePauseIcon()
    {
        // Two vertical pause bars (||)
        return BuildIconSprite(64, (x, y) =>
        {
            bool inHeight = y >= 16 && y <= 48;
            bool leftBar = x >= 20 && x <= 27;
            bool rightBar = x >= 37 && x <= 44;
            return inHeight && (leftBar || rightBar);
        });
    }

    private Sprite BuildIconSprite(int size, Func<int, int, bool> predicate)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = predicate(x, y) ? Color.white : Color.clear;

        tex.SetPixels(pixels);
        tex.Apply();
        _proceduralIcons.Add(tex);

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _proceduralIcons.Add(sprite);
        return sprite;
    }

    private void HandleCharacterRevealed(char c)
    {
        if (char.IsWhiteSpace(c))
            return;

        if (_audibleCharCount++ % charsPerClick != 0)
            return;

        if (_activeVoiceClip == null || uiAudioSource == null)
            return;

        uiAudioSource.pitch = _activeBasePitch + UnityEngine.Random.Range(-_activePitchVariance, _activePitchVariance);
        uiAudioSource.PlayOneShot(_activeVoiceClip, typingClickVolume);
    }

    private static AudioClip CreateProceduralClickClip()
    {
        const int sampleRate = 44100;
        int sampleCount = (int)(sampleRate * 0.014f); // 14ms crisp mechanical tick
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float envelope = Mathf.Pow(1f - t, 4f);
            float tone = Mathf.Sin(2f * Mathf.PI * 1150f * i / sampleRate);
            samples[i] = tone * envelope * 0.6f;
        }

        var clip = AudioClip.Create("ProceduralTypeClick", sampleCount, 1, sampleRate, stream: false);
        clip.SetData(samples, 0);
        return clip;
    }
}

