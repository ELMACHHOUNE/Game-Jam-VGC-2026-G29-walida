using UnityEngine;

namespace Walida.Audio
{
    public enum MedinaSoundSourceKind
    {
        FMODEvent,
        ExternalAudioFile
    }

    public enum MedinaSoundPlaybackMode
    {
        ContinuousLoop,
        PeriodicOneShot
    }

    public enum MedinaSoundElementCategory
    {
        OceanCoast,
        FountainWater,
        TreesAndGardens,
        Mosque,
        SoukBazaar,
        CafeMaure,
        HeritagePalace,
        ArtisanCraft,
        RampartWind,
        HistoricGate,
        AlleyLife,
        MemoryLandmark
    }

    /// <summary>
    /// Spatial sound emitter placed on a map element in <c>MedinaMainLevel</c>.
    /// Supports both FMOD Studio events (<c>event:/...</c>) and external web SFX files in
    /// <c>StreamingAssets/MedinaSFX/</c>, with continuous proximity-mixed loops or periodic
    /// randomized one-shots, stereo panning, and Scene View gizmos.
    /// </summary>
    public sealed class MedinaMapSoundEmitter : MonoBehaviour
    {
        [Header("Element Identity")]
        [SerializeField] private string elementLabel = "Medina Sound";
        [SerializeField] private MedinaSoundElementCategory category = MedinaSoundElementCategory.AlleyLife;

        [Header("Sound Source")]
        [SerializeField] private MedinaSoundSourceKind sourceKind = MedinaSoundSourceKind.ExternalAudioFile;
        [SerializeField] private string soundPathOrFile = "ocean_waves.ogg";
        [SerializeField] private MedinaSoundPlaybackMode playbackMode = MedinaSoundPlaybackMode.ContinuousLoop;

        [Header("Spatial Attenuation")]
        [SerializeField, Range(0f, 1f)] private float maxVolume = 0.30f;
        [SerializeField, Min(0.5f)] private float innerRadius = 5f;
        [SerializeField, Min(1f)] private float outerRadius = 22f;
        [SerializeField, Min(1f)] private float panSpread = 16f;
        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.65f;

        [Header("Pitch & One-Shot Cadence")]
        [SerializeField, Range(0.5f, 1.5f)] private float minPitch = 0.96f;
        [SerializeField, Range(0.5f, 1.5f)] private float maxPitch = 1.04f;
        [SerializeField, Min(1f)] private float minOneShotInterval = 8f;
        [SerializeField, Min(1f)] private float maxOneShotInterval = 18f;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmosAlways = false;

        private FMODLoopHandle _fmodLoop;
        private FMODExternalLoopHandle _externalLoop;
        private float _nextOneShotTimer;
        private float _assignedLoopPitch = 1f;
        private float _lastWeight;

        public string ElementLabel => elementLabel;
        public MedinaSoundElementCategory Category => category;
        public MedinaSoundSourceKind SourceKind => sourceKind;
        public string SoundPathOrFile => soundPathOrFile;
        public MedinaSoundPlaybackMode PlaybackMode => playbackMode;
        public float OuterRadius => outerRadius;
        public float CurrentProximityWeight => _lastWeight;

        public void Configure(
            string label,
            MedinaSoundElementCategory elementCategory,
            MedinaSoundSourceKind kind,
            string pathOrFile,
            MedinaSoundPlaybackMode mode,
            float volume,
            float inner,
            float outer,
            float pan = 16f,
            float minInterval = 8f,
            float maxInterval = 18f,
            float pitchMin = 0.96f,
            float pitchMax = 1.04f)
        {
            elementLabel = label;
            category = elementCategory;
            sourceKind = kind;
            soundPathOrFile = pathOrFile;
            playbackMode = mode;
            maxVolume = Mathf.Clamp01(volume);
            innerRadius = Mathf.Max(0.5f, inner);
            outerRadius = Mathf.Max(innerRadius + 1f, outer);
            panSpread = Mathf.Max(1f, pan);
            minOneShotInterval = Mathf.Max(1f, minInterval);
            maxOneShotInterval = Mathf.Max(minOneShotInterval, maxInterval);
            minPitch = Mathf.Clamp(pitchMin, 0.5f, 1.5f);
            maxPitch = Mathf.Clamp(pitchMax, minPitch, 1.5f);
        }

        private void Awake()
        {
            _assignedLoopPitch = Random.Range(minPitch, maxPitch);
            _nextOneShotTimer = Random.Range(minOneShotInterval * 0.25f, maxOneShotInterval * 0.75f);
        }

        public void TickSpatial(Vector3 listenerPosition, float deltaTime, float masterGain)
        {
            if (string.IsNullOrWhiteSpace(soundPathOrFile))
                return;

            _externalLoop?.Tick(deltaTime);

            Vector3 pos = transform.position;
            float dx = pos.x - listenerPosition.x;
            float dz = pos.z - listenerPosition.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            float safeOuter = Mathf.Max(innerRadius + 0.5f, outerRadius);
            float t = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerRadius, safeOuter, dist));
            _lastWeight = t;

            float vol = Mathf.Clamp01(t * maxVolume * masterGain);
            float pan = panSpread > 0.01f ? Mathf.Clamp(dx / panSpread, -0.88f, 0.88f) : 0f;

            if (playbackMode == MedinaSoundPlaybackMode.ContinuousLoop)
            {
                UpdateContinuousLoop(vol, pan);
            }
            else
            {
                UpdatePeriodicOneShot(vol, pan, deltaTime);
            }
        }

        private void UpdateContinuousLoop(float vol, float pan)
        {
            const float AudibleThreshold = 0.006f;

            if (sourceKind == MedinaSoundSourceKind.FMODEvent)
            {
                if (vol > AudibleThreshold)
                {
                    if (_fmodLoop == null && FMODAudioManager.Instance != null)
                        _fmodLoop = FMODAudioManager.Instance.CreateManagedLoop();

                    if (_fmodLoop == null)
                        return;

                    if (!_fmodLoop.IsActive)
                    {
                        _fmodLoop.SetPitch(_assignedLoopPitch);
                        _fmodLoop.Start(soundPathOrFile, vol, fadeInSeconds: fadeSeconds, autoRestartLoop: true);
                    }
                    else
                    {
                        _fmodLoop.SetVolumeImmediate(vol);
                    }

                    _fmodLoop.SetPan(pan);
                }
                else if (_fmodLoop != null && _fmodLoop.IsActive)
                {
                    _fmodLoop.Stop(fadeSeconds);
                }
            }
            else
            {
                if (vol > AudibleThreshold)
                {
                    if (_externalLoop == null)
                        _externalLoop = new FMODExternalLoopHandle();

                    if (!_externalLoop.IsActive)
                    {
                        _externalLoop.Start(soundPathOrFile, vol, fadeInSeconds: fadeSeconds, pitch: _assignedLoopPitch, randomizeStartOffset: true);
                    }
                    else
                    {
                        _externalLoop.SetVolumeImmediate(vol);
                    }

                    _externalLoop.SetPan(pan);
                }
                else if (_externalLoop != null && _externalLoop.IsActive)
                {
                    _externalLoop.Stop(fadeSeconds);
                }
            }
        }

        private void UpdatePeriodicOneShot(float vol, float pan, float deltaTime)
        {
            if (vol <= 0.012f)
                return;

            _nextOneShotTimer -= deltaTime;
            if (_nextOneShotTimer > 0f)
                return;

            _nextOneShotTimer = Random.Range(minOneShotInterval, maxOneShotInterval);
            float pitch = Random.Range(minPitch, maxPitch);

            if (sourceKind == MedinaSoundSourceKind.FMODEvent)
            {
                if (FMODAudioManager.TryCreateInstance(soundPathOrFile, out FMOD.Studio.EventInstance inst))
                {
                    inst.setVolume(vol);
                    inst.setPitch(pitch);
                    inst.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(transform.position));
                    inst.start();
                    if (inst.getChannelGroup(out FMOD.ChannelGroup grp) == FMOD.RESULT.OK)
                        grp.setPan(pan);
                    inst.release();
                }
            }
            else
            {
                FMODExternalSoundBank.PlayOneShot(soundPathOrFile, vol, pan, pitch);
            }
        }

        public void StopPlayback(float fadeOut = 0.35f)
        {
            _fmodLoop?.Stop(fadeOut);
            _externalLoop?.Stop(fadeOut);
        }

        private void OnDisable()
        {
            StopPlayback(0.25f);
        }

        private void OnDestroy()
        {
            if (_fmodLoop != null)
            {
                if (FMODAudioManager.Instance != null)
                    FMODAudioManager.Instance.UnregisterLoop(_fmodLoop);
                else
                    _fmodLoop.ReleaseImmediate();
                _fmodLoop = null;
            }

            _externalLoop?.StopImmediate();
            _externalLoop = null;
        }

        private void OnDrawGizmos()
        {
            if (drawGizmosAlways)
                DrawSoundGizmo(selected: false);
            else
            {
                Gizmos.color = GetCategoryColor(0.85f);
                Gizmos.DrawSphere(transform.position + Vector3.up * 0.6f, 0.55f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            DrawSoundGizmo(selected: true);
        }

        private void DrawSoundGizmo(bool selected)
        {
            Vector3 center = transform.position + Vector3.up * 0.4f;
            Gizmos.color = GetCategoryColor(selected ? 0.95f : 0.45f);
            Gizmos.DrawWireSphere(center, innerRadius);

            Gizmos.color = GetCategoryColor(selected ? 0.40f : 0.16f);
            Gizmos.DrawWireSphere(center, outerRadius);

            Gizmos.color = GetCategoryColor(0.95f);
            Gizmos.DrawSphere(center, 0.65f);
        }

        private Color GetCategoryColor(float alpha)
        {
            switch (category)
            {
                case MedinaSoundElementCategory.OceanCoast: return new Color(0.12f, 0.58f, 0.95f, alpha);
                case MedinaSoundElementCategory.FountainWater: return new Color(0.20f, 0.85f, 0.92f, alpha);
                case MedinaSoundElementCategory.TreesAndGardens: return new Color(0.26f, 0.78f, 0.36f, alpha);
                case MedinaSoundElementCategory.Mosque: return new Color(0.95f, 0.82f, 0.24f, alpha);
                case MedinaSoundElementCategory.SoukBazaar: return new Color(0.94f, 0.44f, 0.18f, alpha);
                case MedinaSoundElementCategory.CafeMaure: return new Color(0.18f, 0.78f, 0.74f, alpha);
                case MedinaSoundElementCategory.HeritagePalace: return new Color(0.76f, 0.62f, 0.92f, alpha);
                case MedinaSoundElementCategory.ArtisanCraft: return new Color(0.84f, 0.52f, 0.28f, alpha);
                case MedinaSoundElementCategory.RampartWind: return new Color(0.82f, 0.72f, 0.54f, alpha);
                case MedinaSoundElementCategory.HistoricGate: return new Color(0.66f, 0.42f, 0.24f, alpha);
                case MedinaSoundElementCategory.MemoryLandmark: return new Color(0.92f, 0.48f, 0.76f, alpha);
                default: return new Color(0.60f, 0.78f, 0.96f, alpha);
            }
        }
    }
}
