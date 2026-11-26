using FMODUnity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Walida.Audio;

/// <summary>
/// Manages the Souk Sound Segment intro & guidance:
/// - Plays phone static interference when entering the Souk; static fades smoothly as the player
///   aims their phone toward the Souk Merchants sound source.
/// - Displays Abbass's opening line: "Abbass: Hmm… my phone is picking up something."
/// - Guides the player to move the phone to record "Souk Merchants" and fires an event on completion.
/// </summary>
public class SoukAudioSegmentController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpatialAudioPhoneController audioPhoneController;
    [SerializeField] private RecordableSoundSource targetSoundSource;
    [SerializeField] private Canvas uiCanvas;

    [Header("Phone Static SFX (FMOD)")]
    [SerializeField] private EventReference callStaticEvent;
    [SerializeField] private AudioClip staticClipOverride;
    [SerializeField, Range(0f, 1f)] private float maxStaticVolume = 0.28f;
    [SerializeField, Range(0f, 1f)] private float minStaticVolume = 0.02f;

    [Header("Scenario Lines")]
    [SerializeField] private string introDialogueLine = "Abbass: Hmm… my phone is picking up something.";
    [SerializeField] private string targetLabel = "Souk Merchants";
    [SerializeField] private string recordedLine = "Sound recorded: Souk Merchants added to journal.";
    [SerializeField] private float introSubtitleDuration = 5.0f;

    [Header("Events")]
    [SerializeField] private UnityEvent<RecordableSoundSource> onSoukMerchantsRecorded;

    private FMODLoopHandle _fmodStaticLoop;
    private float _currentStaticVolume;
    private AudioSource _staticSource;
    private AudioClip _generatedStaticClip;
    private Font _uiFont;
    private Text _objectiveText;
    private Text _signalStatusText;
    private GameObject _subtitleObj;
    private Text _subtitleText;
    private float _subtitleTimer;
    private bool _targetRecorded;

    private void Awake()
    {
        if (audioPhoneController == null)
        {
            audioPhoneController = FindAnyObjectByType<SpatialAudioPhoneController>();
        }

        if (uiCanvas == null)
        {
            uiCanvas = FindAnyObjectByType<Canvas>();
        }

        SetupStaticAudio();
        BuildHud();
    }

    private void OnEnable()
    {
        if (audioPhoneController != null)
        {
            audioPhoneController.OnSoundRecorded += HandleSoundRecorded;
        }
    }

    private void OnDisable()
    {
        if (audioPhoneController != null)
        {
            audioPhoneController.OnSoundRecorded -= HandleSoundRecorded;
        }

        _fmodStaticLoop?.Stop(0.25f);
    }

    private void Start()
    {
        ShowSubtitle(introDialogueLine, introSubtitleDuration);
    }

    private void Update()
    {
        UpdateStaticAndSignalUi();

        if (_subtitleTimer > 0f)
        {
            _subtitleTimer -= Time.unscaledDeltaTime;
            if (_subtitleTimer <= 0f && _subtitleObj != null)
            {
                _subtitleObj.SetActive(false);
            }
        }
    }

    private void UpdateStaticAndSignalUi()
    {
        if (_targetRecorded)
        {
            _currentStaticVolume = Mathf.MoveTowards(_currentStaticVolume, 0f, Time.deltaTime * 0.6f);
            if (_fmodStaticLoop != null)
            {
                _fmodStaticLoop.SetVolumeImmediate(_currentStaticVolume);
                if (_currentStaticVolume <= 0.001f && _fmodStaticLoop.IsActive)
                {
                    _fmodStaticLoop.Stop(0.15f);
                }
            }

            if (_staticSource != null)
            {
                _staticSource.volume = _currentStaticVolume;
            }
            return;
        }

        float signal = audioPhoneController != null
            ? audioPhoneController.StrongestSignal
            : (targetSoundSource != null ? targetSoundSource.CurrentSignalStrength : 0f);
        RecordableSoundSource strongest = audioPhoneController != null ? audioPhoneController.StrongestSource : targetSoundSource;
        bool isTargetStrongest = strongest != null && (targetSoundSource == null || strongest == targetSoundSource);

        float targetStatic = Mathf.Lerp(maxStaticVolume, minStaticVolume, signal * signal);
        _currentStaticVolume = Mathf.Lerp(_currentStaticVolume, targetStatic, 1f - Mathf.Exp(-8f * Time.deltaTime));

        _fmodStaticLoop?.SetVolumeImmediate(_currentStaticVolume);

        if (_staticSource != null)
        {
            _staticSource.volume = _currentStaticVolume;
        }

        if (_signalStatusText != null)
        {
            float required = strongest != null ? strongest.RequiredSignalStrength : 0.65f;
            if (signal >= required && isTargetStrongest)
            {
                _signalStatusText.text = $"Locked onto {targetLabel} ({Mathf.RoundToInt(signal * 100f)}%) — Hold Left-Click / Space / R";
                _signalStatusText.color = new Color(0.68f, 0.88f, 0.62f, 1f);
            }
            else if (signal >= required && strongest != null)
            {
                _signalStatusText.text = $"Hearing: {strongest.DisplayName} ({Mathf.RoundToInt(signal * 100f)}%) — Keep searching for {targetLabel}";
                _signalStatusText.color = new Color(0.95f, 0.80f, 0.50f, 1f);
            }
            else if (signal > 0.22f && strongest != null)
            {
                _signalStatusText.text = $"Picking up {strongest.DisplayName} ({Mathf.RoundToInt(signal * 100f)}%) — Pan to focus";
                _signalStatusText.color = new Color(0.95f, 0.80f, 0.50f, 1f);
            }
            else
            {
                _signalStatusText.text = "Static interference... Pan the phone around the Souk";
                _signalStatusText.color = new Color(0.949f, 0.929f, 0.890f, 0.88f);
            }
        }
    }

    private void HandleSoundRecorded(RecordableSoundSource source)
    {
        if (source == null) return;

        if (targetSoundSource == null || source == targetSoundSource)
        {
            _targetRecorded = true;
            if (_objectiveText != null)
            {
                _objectiveText.text = $"[✓]  Recorded: {targetLabel}";
                _objectiveText.color = new Color(0.68f, 0.88f, 0.62f, 1f);
            }

            if (_signalStatusText != null)
            {
                _signalStatusText.text = "Souk recording saved.";
            }

            ShowSubtitle(recordedLine, 5.0f);
            onSoukMerchantsRecorded?.Invoke(source);
        }
        else
        {
            ShowSubtitle($"Abbass: That's {source.DisplayName} — I still need to record the {targetLabel}.", 4.0f);
        }
    }

    private void ShowSubtitle(string line, float duration)
    {
        if (_subtitleObj == null || _subtitleText == null) return;
        _subtitleText.text = line;
        _subtitleObj.SetActive(true);
        _subtitleTimer = duration;
    }

    private void SetupStaticAudio()
    {
        _currentStaticVolume = maxStaticVolume;

        if (FMODAudioManager.Instance != null)
        {
            _fmodStaticLoop = FMODAudioManager.Instance.CreateManagedLoop();
            if (_fmodStaticLoop.Start(callStaticEvent, FMODEvents.CallStatic, maxStaticVolume, fadeInSeconds: 0.35f, autoRestartLoop: true))
            {
                return;
            }
        }

        _staticSource = GetComponent<AudioSource>();
        if (_staticSource == null)
        {
            _staticSource = gameObject.AddComponent<AudioSource>();
        }

        _staticSource.loop = true;
        _staticSource.playOnAwake = false;
        _staticSource.spatialBlend = 0f;
        _staticSource.volume = maxStaticVolume;

        if (staticClipOverride != null)
        {
            _staticSource.clip = staticClipOverride;
        }
        else
        {
            _generatedStaticClip = CreatePhoneStaticClip();
            _staticSource.clip = _generatedStaticClip;
        }

        _staticSource.Play();
    }

    private static AudioClip CreatePhoneStaticClip()
    {
        const int sampleRate = 22050;
        const float duration = 2.5f;
        int count = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[count];
        System.Random rand = new System.Random(2026);
        float lp = 0f;

        for (int i = 0; i < count; i++)
        {
            float white = (float)(rand.NextDouble() * 2.0 - 1.0);
            // Soft band-limited radio/phone static with occasional subtle crackle
            lp = lp * 0.72f + white * 0.28f;
            float crackle = rand.NextDouble() < 0.002 ? (float)(rand.NextDouble() * 0.6 - 0.3) : 0f;
            float hum = Mathf.Sin(2f * Mathf.PI * 110f * (i / (float)sampleRate)) * 0.04f;
            samples[i] = Mathf.Clamp((lp * 0.55f + crackle + hum), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("ProceduralPhoneStatic", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void BuildHud()
    {
        if (uiCanvas == null)
        {
            GameObject canvasObj = new GameObject("SoukAudioUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            uiCanvas = canvasObj.GetComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_uiFont == null)
        {
            _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 20);
        }

        // Top-left objective panel
        GameObject panel = new GameObject("SoukObjectivePanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(uiCanvas.transform, false);
        RectTransform pRect = panel.GetComponent<RectTransform>();
        pRect.anchorMin = new Vector2(0f, 1f);
        pRect.anchorMax = new Vector2(0f, 1f);
        pRect.pivot = new Vector2(0f, 1f);
        pRect.anchoredPosition = new Vector2(28f, -28f);
        pRect.sizeDelta = new Vector2(520f, 106f);
        Image pImg = panel.GetComponent<Image>();
        pImg.color = new Color(0.145f, 0.157f, 0.153f, 0.85f);
        pImg.raycastTarget = false;

        _objectiveText = CreateText("ObjectiveText", panel.transform,
            new Vector2(18f, -14f), new Vector2(484f, 34f),
            $"RECORD SOUND:  {targetLabel}", 20, FontStyle.Bold,
            new Color(0.784f, 0.643f, 0.467f, 1f), TextAnchor.MiddleLeft);

        _signalStatusText = CreateText("SignalStatusText", panel.transform,
            new Vector2(18f, -54f), new Vector2(484f, 38f),
            "Static interference... Pan the phone to locate the sound", 15, FontStyle.Normal,
            new Color(0.949f, 0.929f, 0.890f, 0.9f), TextAnchor.MiddleLeft);

        // Bottom Abbass subtitle bar
        _subtitleObj = new GameObject("SoukSubtitlePanel", typeof(RectTransform), typeof(Image));
        _subtitleObj.transform.SetParent(uiCanvas.transform, false);
        RectTransform sRect = _subtitleObj.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(0.5f, 0f);
        sRect.anchorMax = new Vector2(0.5f, 0f);
        sRect.pivot = new Vector2(0.5f, 0f);
        sRect.anchoredPosition = new Vector2(0f, 34f);
        sRect.sizeDelta = new Vector2(820f, 68f);
        Image sImg = _subtitleObj.GetComponent<Image>();
        sImg.color = new Color(0.10f, 0.11f, 0.11f, 0.90f);
        sImg.raycastTarget = false;

        GameObject txtObj = new GameObject("SubtitleText", typeof(RectTransform), typeof(Text));
        txtObj.transform.SetParent(_subtitleObj.transform, false);
        RectTransform tRect = txtObj.GetComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero;
        tRect.anchorMax = Vector2.one;
        tRect.offsetMin = new Vector2(18f, 6f);
        tRect.offsetMax = new Vector2(-18f, -6f);
        _subtitleText = txtObj.GetComponent<Text>();
        _subtitleText.font = _uiFont;
        _subtitleText.fontSize = 22;
        _subtitleText.color = new Color(0.949f, 0.929f, 0.890f, 1f);
        _subtitleText.alignment = TextAnchor.MiddleCenter;
        _subtitleText.raycastTarget = false;
    }

    private Text CreateText(string name, Transform parent, Vector2 anchoredPos, Vector2 size,
        string content, int fontSize, FontStyle style, Color color, TextAnchor alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Text text = obj.GetComponent<Text>();
        text.font = _uiFont;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.raycastTarget = false;
        return text;
    }

    private void OnDestroy()
    {
        if (FMODAudioManager.Instance != null)
            FMODAudioManager.Instance.UnregisterLoop(_fmodStaticLoop);
        else
            _fmodStaticLoop?.ReleaseImmediate();

        if (_generatedStaticClip != null)
        {
            Destroy(_generatedStaticClip);
        }
    }
}
