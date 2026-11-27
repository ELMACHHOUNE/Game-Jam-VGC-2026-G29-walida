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

    [Header("Journal Link")]
    [Tooltip("Optional: Journal entry unlocked when the target sound is recorded (shows the Journal popup and returns " +
             "to the medina). Must match an Entry Id in Medina_HUD > Journal Photographs. Leave empty for none.")]
    [SerializeField] private string journalEntryId = "";

    [Header("Events")]
    [SerializeField] private UnityEvent<RecordableSoundSource> onSoukMerchantsRecorded;

    public bool IsComplete => _targetRecorded;
    public UnityEvent<RecordableSoundSource> OnSoukMerchantsRecorded => onSoukMerchantsRecorded;

    private FMODLoopHandle _fmodStaticLoop;
    private float _currentStaticVolume;
    private AudioSource _staticSource;
    private AudioClip _generatedStaticClip;
    private Image _objectiveIcon;
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
            // Only a screen-space canvas: the phone screen is a world-space canvas and must not host the HUD
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c.isRootCanvas && c.renderMode != RenderMode.WorldSpace)
                {
                    uiCanvas = c;
                    break;
                }
            }
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
                _signalStatusText.color = MedinaUIKit.Olive;
            }
            else if (signal >= required && strongest != null)
            {
                _signalStatusText.text = $"Hearing: {strongest.DisplayName} ({Mathf.RoundToInt(signal * 100f)}%) — Keep searching for {targetLabel}";
                _signalStatusText.color = MedinaUIKit.Terracotta;
            }
            else if (signal > 0.22f && strongest != null)
            {
                _signalStatusText.text = $"Picking up {strongest.DisplayName} ({Mathf.RoundToInt(signal * 100f)}%) — Pan to focus";
                _signalStatusText.color = MedinaUIKit.Terracotta;
            }
            else
            {
                _signalStatusText.text = "Static interference... Pan the phone around the Souk";
                _signalStatusText.color = MedinaUIKit.InkMuted;
            }
        }
    }

    private void HandleSoundRecorded(RecordableSoundSource source)
    {
        if (source == null) return;

        if (targetSoundSource == null || source == targetSoundSource)
        {
            _targetRecorded = true;
            JournalProgress.Unlock(journalEntryId);
            if (_objectiveText != null)
            {
                _objectiveText.text = $"Recorded: {targetLabel}";
                _objectiveText.color = MedinaUIKit.Olive;
            }

            if (_objectiveIcon != null)
            {
                _objectiveIcon.sprite = MedinaUIKit.StarSprite;
                _objectiveIcon.color = MedinaUIKit.Olive;
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
        _subtitleText.text = MedinaUIKit.FormatSpeakerLine(line);
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

        Transform root = uiCanvas.transform;

        // Top-left objective card (storybook die-cut card, like the Medina HUD)
        Image card = MedinaUIKit.CreateCard("SoukObjectivePanel", root, new Vector2(0f, 1f),
            new Vector2(28f, -28f), new Vector2(560f, 128f));

        MedinaUIKit.CreateTopLeftText("ObjectiveLabel", card.transform, MedinaTextRole.Label,
            "SOUND JOURNAL", 15, TextAnchor.MiddleLeft, new Vector2(30f, -18f), new Vector2(500f, 22f));

        _objectiveIcon = MedinaUIKit.CreateImage("ObjectiveStar", card.transform, MedinaUIKit.StarOutlineSprite,
            MedinaUIKit.Navy, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(28f, -46f), new Vector2(26f, 26f), sliced: false);

        _objectiveText = MedinaUIKit.CreateTopLeftText("ObjectiveText", card.transform, MedinaTextRole.Title,
            $"Record: {targetLabel}", 22, TextAnchor.MiddleLeft, new Vector2(64f, -42f), new Vector2(470f, 34f));

        _signalStatusText = MedinaUIKit.CreateTopLeftText("SignalStatusText", card.transform, MedinaTextRole.Caption,
            "Static interference... Pan the phone to locate the sound", 16, TextAnchor.MiddleLeft,
            new Vector2(30f, -80f), new Vector2(504f, 36f));

        // Bottom subtitle card (speaker name highlighted, like the VN dialogue)
        Image subtitle = MedinaUIKit.CreateCard("SoukSubtitlePanel", root, new Vector2(0.5f, 0f),
            new Vector2(0f, 34f), new Vector2(900f, 92f));
        _subtitleObj = subtitle.gameObject;

        _subtitleText = MedinaUIKit.CreateFillText("SubtitleText", subtitle.transform, MedinaTextRole.Body,
            "", 22, TextAnchor.MiddleCenter, new Vector4(36f, 14f, 36f, 14f));
        _subtitleObj.SetActive(false);
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
