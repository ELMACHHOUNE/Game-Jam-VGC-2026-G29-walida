using System.Collections.Generic;
using UnityEngine;

namespace Walida.Audio
{
    /// <summary>
    /// Scatters and proximity-mixes spatial soundscapes across every element of <c>MedinaMainLevel</c>:
    /// Atlantic Ocean surf &amp; seagulls along the eastern/NE coast, Andalusian fountain &amp; olive trees,
    /// El Atiqa Mosque recitation, East &amp; West Souks, Café Maure kettle/murmur/oud, Heritage Palace chimes,
    /// Dar Fekhara artisan pottery/hammering, Udaya rampart coastal wind, Bab Oudaia wooden gate creaks,
    /// blue-white alley life, NPCs, and memory photo landmarks.
    /// Auto-attached by <see cref="MedinaLevelManager"/>.
    /// </summary>
    public sealed class MedinaAmbienceController : MonoBehaviour
    {
        public const string SoundscapeContainerName = "MedinaScatteredSounds";

        [Header("References")]
        [SerializeField] private Transform playerTransform;

        [Header("Mix Tuning")]
        [SerializeField, Range(0f, 1.5f)] private float masterScatterGain = 1.0f;
        [SerializeField] private float defaultPanSpread = 16f;
        [SerializeField] private bool autoPopulateMissingEmitters = true;

        private readonly List<MedinaMapSoundEmitter> _emitters = new List<MedinaMapSoundEmitter>(48);

        public IReadOnlyList<MedinaMapSoundEmitter> Emitters => _emitters;

        private void Start()
        {
            if (playerTransform == null)
            {
                MedinaPlayerController player = FindAnyObjectByType<MedinaPlayerController>();
                if (player != null)
                    playerTransform = player.transform;
            }

            RefreshEmitters();

            if (_emitters.Count == 0 && autoPopulateMissingEmitters)
            {
                PopulateMapSoundscape(transform, defaultPanSpread);
                RefreshEmitters();
            }
        }

        private void Update()
        {
            if (playerTransform == null || _emitters.Count == 0)
                return;

            Vector3 playerPos = playerTransform.position;
            float dt = Time.unscaledDeltaTime;

            for (int i = 0; i < _emitters.Count; i++)
            {
                MedinaMapSoundEmitter emitter = _emitters[i];
                if (emitter != null && emitter.isActiveAndEnabled)
                {
                    emitter.TickSpatial(playerPos, dt, masterScatterGain);
                }
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _emitters.Count; i++)
            {
                if (_emitters[i] != null)
                    _emitters[i].StopPlayback(0.3f);
            }
        }

        private void OnApplicationQuit()
        {
            FMODExternalSoundBank.ReleaseAll();
        }

        public void RefreshEmitters()
        {
            _emitters.Clear();
            MedinaMapSoundEmitter[] found = FindObjectsByType<MedinaMapSoundEmitter>(FindObjectsInactive.Exclude);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    _emitters.Add(found[i]);
            }
        }

        /// <summary>
        /// Builds the complete element-based spatial sound scatter across <c>MedinaMainLevel</c>.
        /// Callable both at runtime (automatically if not already baked into the scene) and from the
        /// Unity Editor menu (<c>Tools/Audio/Scatter Medina Map Sounds</c>).
        /// </summary>
        public static GameObject PopulateMapSoundscape(Transform parent = null, float panSpread = 16f)
        {
            GameObject existing = GameObject.Find(SoundscapeContainerName);
            if (existing != null)
            {
                if (Application.isPlaying)
                    Destroy(existing);
                else
                    DestroyImmediate(existing);
            }

            var root = new GameObject(SoundscapeContainerName);
            if (parent != null)
                root.transform.SetParent(parent, worldPositionStays: true);
            root.transform.position = Vector3.zero;

            // ------------------------------------------------------------------
            // 1. ATLANTIC OCEAN & COASTLINE (KasbahSea + East / NE Ocean Walls)
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_OceanWaves_NE", new[] { "Wall_NE_Ocean_02", "Wall_NE_Ocean_03" }, new Vector3(42f, 0f, 73f),
                MedinaSoundElementCategory.OceanCoast, MedinaSoundSourceKind.ExternalAudioFile, "ocean_waves.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.32f, inner: 14f, outer: 46f, pan: panSpread + 6f, pitchMin: 0.94f, pitchMax: 1.02f);

            AddEmitterAtObject(root.transform, "SFX_OceanWaves_EastUpper", new[] { "Wall_East_Upper_01", "UdayaTower_02" }, new Vector3(72f, 0f, 33f),
                MedinaSoundElementCategory.OceanCoast, MedinaSoundSourceKind.ExternalAudioFile, "ocean_waves.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.34f, inner: 14f, outer: 46f, pan: panSpread + 6f, pitchMin: 0.95f, pitchMax: 1.04f);

            AddEmitterAtObject(root.transform, "SFX_OceanWaves_EastMid", new[] { "Wall_East_Mid_02", "Wall_East_Mid_03" }, new Vector3(68f, 0f, -13f),
                MedinaSoundElementCategory.OceanCoast, MedinaSoundSourceKind.ExternalAudioFile, "ocean_waves.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.34f, inner: 14f, outer: 46f, pan: panSpread + 6f, pitchMin: 0.92f, pitchMax: 1.00f);

            AddEmitterAtObject(root.transform, "SFX_OceanWaves_EastLower", new[] { "Wall_East_Lower_02", "Wall_East_Lower_01" }, new Vector3(78f, 0f, -55f),
                MedinaSoundElementCategory.OceanCoast, MedinaSoundSourceKind.ExternalAudioFile, "ocean_waves.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.36f, inner: 15f, outer: 48f, pan: panSpread + 6f, pitchMin: 0.96f, pitchMax: 1.04f);

            // Coastal Seagulls hovering near cliffs, towers, and semaphore bastion
            AddEmitterAtObject(root.transform, "SFX_Seagulls_BastionSemaphore", new[] { "UdayaBastion_Semaphore" }, new Vector3(34.3f, 0f, 78.2f),
                MedinaSoundElementCategory.OceanCoast, MedinaSoundSourceKind.ExternalAudioFile, "seagulls.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.28f, inner: 12f, outer: 44f, pan: panSpread + 4f, minInt: 10f, maxInt: 22f, pitchMin: 0.92f, pitchMax: 1.12f);

            AddEmitterAtObject(root.transform, "SFX_Seagulls_NECliff", new[] { "UdayaTower_02", "Plot_12_NECliff" }, new Vector3(76.5f, 0f, 41.6f),
                MedinaSoundElementCategory.OceanCoast, MedinaSoundSourceKind.ExternalAudioFile, "seagulls.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.26f, inner: 12f, outer: 42f, pan: panSpread + 4f, minInt: 12f, maxInt: 25f, pitchMin: 0.90f, pitchMax: 1.10f);

            AddEmitterAtObject(root.transform, "SFX_Seagulls_EastRidge", new[] { "UdayaTower_04", "Wall_East_Mid_03" }, new Vector3(75.8f, 0f, -33.0f),
                MedinaSoundElementCategory.OceanCoast, MedinaSoundSourceKind.ExternalAudioFile, "seagulls.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.26f, inner: 12f, outer: 42f, pan: panSpread + 4f, minInt: 11f, maxInt: 24f, pitchMin: 0.92f, pitchMax: 1.08f);

            AddEmitterAtObject(root.transform, "SFX_Seagulls_SECoast", new[] { "UdayaTower_05", "Wall_SE_Bastion_01" }, new Vector3(80.6f, 0f, -76.9f),
                MedinaSoundElementCategory.OceanCoast, MedinaSoundSourceKind.ExternalAudioFile, "seagulls.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.24f, inner: 10f, outer: 40f, pan: panSpread + 4f, minInt: 13f, maxInt: 26f, pitchMin: 0.94f, pitchMax: 1.10f);

            // ------------------------------------------------------------------
            // 2. ANDALUSIAN GARDENS & FOUNTAIN
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_AndalusianFountain_Water", new[] { "AndalusianFountain" }, new Vector3(33.6f, 0f, -77.8f),
                MedinaSoundElementCategory.FountainWater, MedinaSoundSourceKind.ExternalAudioFile, "fountain_water.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.44f, inner: 6f, outer: 26f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_AndalusianGardens_Pigeons", new[] { "AndalusianFountain", "07_Museum_AndalusianGardens" }, new Vector3(33.6f, 0f, -77.8f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.FMODEvent, FMODEvents.Pigeons,
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.28f, inner: 6f, outer: 24f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_AndalusianGardens_POI_Birds", new[] { "POI_AndalusianGardensAudio" }, new Vector3(21.3f, 0f, -13.9f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.FMODEvent, FMODEvents.Pigeons,
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.24f, inner: 4f, outer: 16f, pan: panSpread);

            // ------------------------------------------------------------------
            // 3. OLIVE TREES & FOLIAGE CLUSTERS SCATTERED ACROSS THE MAP
            // ------------------------------------------------------------------
            AddEmitterAtCluster(root.transform, "SFX_OliveTrees_Gardens_Birds",
                new[] { "OliveTree_GardenE", "OliveTree_GardenS", "OliveTree_GardenW" }, new Vector3(33.0f, 0f, -80.0f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.ExternalAudioFile, "olive_trees_birds.mp3",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.28f, inner: 7f, outer: 26f, pan: panSpread);

            AddEmitterAtCluster(root.transform, "SFX_OliveTrees_PlaceOudaya_Birds",
                new[] { "OliveTree_PlaceOudayaL", "OliveTree_PlaceOudayaR" }, new Vector3(35.5f, 0f, 72.5f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.ExternalAudioFile, "olive_trees_birds.mp3",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.26f, inner: 6f, outer: 24f, pan: panSpread);

            AddEmitterAtCluster(root.transform, "SFX_OliveTrees_PlaceOudaya_Pigeons",
                new[] { "OliveTree_PlaceOudayaL", "OliveTree_PlaceOudayaR" }, new Vector3(35.5f, 0f, 72.5f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.FMODEvent, FMODEvents.Pigeons,
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.22f, inner: 5f, outer: 20f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_OliveTree_NWAlley_Birds", new[] { "OliveTree_NWAlley" }, new Vector3(-14.4f, 0f, 14.4f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.ExternalAudioFile, "olive_trees_birds.mp3",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.22f, inner: 4.5f, outer: 18f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_OliveTree_NWAlley_Leaves", new[] { "OliveTree_NWAlley" }, new Vector3(-14.4f, 0f, 14.4f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.ExternalAudioFile, "leaves_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.14f, inner: 4f, outer: 16f, pan: panSpread);

            AddEmitterAtCluster(root.transform, "SFX_OliveTrees_CafeMaure_Leaves",
                new[] { "OliveTree_CafeMaureL", "OliveTree_CafeMaureR" }, new Vector3(62.4f, 0f, -62.4f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.ExternalAudioFile, "leaves_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.18f, inner: 5f, outer: 22f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_OliveTree_BabOudaya_Leaves", new[] { "OliveTree_BabOudayaL" }, new Vector3(-43.2f, 0f, -31.7f),
                MedinaSoundElementCategory.TreesAndGardens, MedinaSoundSourceKind.ExternalAudioFile, "leaves_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.16f, inner: 5f, outer: 19f, pan: panSpread);

            // ------------------------------------------------------------------
            // 4. EL ATIQA MOSQUE & MINARET (uses true building/minaret centroid!)
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_ElAtiqaMosque_Quran", new[] { "MinaretTower", "Plot_09_ElAtiqaMosque" }, new Vector3(26.0f, 0f, 16.0f),
                MedinaSoundElementCategory.Mosque, MedinaSoundSourceKind.FMODEvent, FMODEvents.Quran,
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.34f, inner: 7f, outer: 28f, pan: panSpread);

            // ------------------------------------------------------------------
            // 5. SOUK BAZAARS (East Souk + West Plot_01 Souk)
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_EastSouk_Bustle", new[] { "Plot_08_EastSouk" }, new Vector3(32.4f, 0f, -17.4f),
                MedinaSoundElementCategory.SoukBazaar, MedinaSoundSourceKind.FMODEvent, FMODEvents.SoukLoop,
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.36f, inner: 7f, outer: 25f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_EastSouk_VendorCalls", new[] { "Plot_08_EastSouk" }, new Vector3(32.4f, 0f, -17.4f),
                MedinaSoundElementCategory.SoukBazaar, MedinaSoundSourceKind.FMODEvent, FMODEvents.MarketVendorShouting,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.32f, inner: 5f, outer: 22f, pan: panSpread, minInt: 8f, maxInt: 18f, pitchMin: 0.94f, pitchMax: 1.06f);

            AddEmitterAtObject(root.transform, "SFX_EastSouk_ButcherCleaver", new[] { "Plot_08_EastSouk_House_03", "Plot_08_EastSouk" }, new Vector3(40.7f, 0f, -14.8f),
                MedinaSoundElementCategory.SoukBazaar, MedinaSoundSourceKind.FMODEvent, FMODEvents.MeatCleaverWood,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.22f, inner: 4f, outer: 17f, pan: panSpread, minInt: 9f, maxInt: 20f, pitchMin: 0.90f, pitchMax: 1.10f);

            AddEmitterAtObject(root.transform, "SFX_WestSouk_Bustle", new[] { "Plot_01" }, new Vector3(-21.6f, 0f, -3.2f),
                MedinaSoundElementCategory.SoukBazaar, MedinaSoundSourceKind.FMODEvent, FMODEvents.SoukLoop,
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.24f, inner: 5f, outer: 19f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_WestSouk_VendorCalls", new[] { "Plot_01" }, new Vector3(-21.6f, 0f, -3.2f),
                MedinaSoundElementCategory.SoukBazaar, MedinaSoundSourceKind.FMODEvent, FMODEvents.MarketVendorShouting,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.22f, inner: 4f, outer: 17f, pan: panSpread, minInt: 11f, maxInt: 24f, pitchMin: 0.94f, pitchMax: 1.06f);

            // ------------------------------------------------------------------
            // 6. CAFÉ MAURE & TERRACE
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_CafeMaure_TeaKettle", new[] { "POI_CafeMaureAudio", "Plot_14_CafeMaure" }, new Vector3(56.3f, 0f, -42.1f),
                MedinaSoundElementCategory.CafeMaure, MedinaSoundSourceKind.FMODEvent, FMODEvents.TeaKettle,
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.34f, inner: 5f, outer: 19f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_CafeMaure_TeaPouring", new[] { "POI_CafeMaureAudio", "Plot_14_CafeMaure" }, new Vector3(56.3f, 0f, -42.1f),
                MedinaSoundElementCategory.CafeMaure, MedinaSoundSourceKind.ExternalAudioFile, "tea_pouring.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.30f, inner: 4.5f, outer: 17f, pan: panSpread, minInt: 9f, maxInt: 19f, pitchMin: 0.96f, pitchMax: 1.05f);

            AddEmitterAtObject(root.transform, "SFX_CafeMaure_TerraceMurmur", new[] { "Plot_14_CafeMaure" }, new Vector3(62.1f, 0f, -49.8f),
                MedinaSoundElementCategory.CafeMaure, MedinaSoundSourceKind.ExternalAudioFile, "cafe_murmur.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.26f, inner: 6f, outer: 24f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_CafeMaure_GlassCups", new[] { "Plot_14_CafeMaure" }, new Vector3(62.1f, 0f, -49.8f),
                MedinaSoundElementCategory.CafeMaure, MedinaSoundSourceKind.FMODEvent, FMODEvents.GlassCupSetDown,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.26f, inner: 4.5f, outer: 17f, pan: panSpread, minInt: 6f, maxInt: 14f, pitchMin: 0.92f, pitchMax: 1.12f);

            AddEmitterAtObject(root.transform, "SFX_CafeSouth_Oud", new[] { "Plot_16_CafeSouth" }, new Vector3(47.2f, 0f, -59.0f),
                MedinaSoundElementCategory.CafeMaure, MedinaSoundSourceKind.ExternalAudioFile, "oud_melody.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.22f, inner: 6f, outer: 24f, pan: panSpread);

            // ------------------------------------------------------------------
            // 7. HERITAGE PALACE & JEWELLERY MUSEUM
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_JewelleryMuseum_CourtyardChimes", new[] { "Plot_15_JewelleryMuseum" }, new Vector3(29.1f, 0f, -46.6f),
                MedinaSoundElementCategory.HeritagePalace, MedinaSoundSourceKind.ExternalAudioFile, "courtyard_chimes.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.20f, inner: 5f, outer: 22f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_SWMuseum_PalaceOud", new[] { "Plot_06_SWMuseum" }, new Vector3(3.9f, 0f, -51.8f),
                MedinaSoundElementCategory.HeritagePalace, MedinaSoundSourceKind.ExternalAudioFile, "oud_melody.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.18f, inner: 5f, outer: 22f, pan: panSpread, pitchMin: 0.98f, pitchMax: 1.02f);

            // ------------------------------------------------------------------
            // 8. DAR FEKHARA ARTISAN WORKSHOPS (Pottery & Brass Hammering)
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_WestInner_PotteryCraft", new[] { "Plot_02_WestInner" }, new Vector3(-24.8f, 0f, 14.9f),
                MedinaSoundElementCategory.ArtisanCraft, MedinaSoundSourceKind.ExternalAudioFile, "pottery_chime.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.22f, inner: 5f, outer: 20f, pan: panSpread, minInt: 10f, maxInt: 22f, pitchMin: 0.90f, pitchMax: 1.12f);

            AddEmitterAtObject(root.transform, "SFX_CentralCorner_ArtisanHammer", new[] { "Plot_07_CentralCorner" }, new Vector3(13.1f, 0f, -4.1f),
                MedinaSoundElementCategory.ArtisanCraft, MedinaSoundSourceKind.ExternalAudioFile, "artisan_hammer.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.22f, inner: 4.5f, outer: 19f, pan: panSpread, minInt: 9f, maxInt: 20f, pitchMin: 0.92f, pitchMax: 1.08f);

            AddEmitterAtObject(root.transform, "SFX_EastAlley_ArtisanWorkshop", new[] { "Plot_10_EastAlley" }, new Vector3(40.8f, 0f, 9.4f),
                MedinaSoundElementCategory.ArtisanCraft, MedinaSoundSourceKind.ExternalAudioFile, "artisan_hammer.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.20f, inner: 4.5f, outer: 19f, pan: panSpread, minInt: 11f, maxInt: 24f, pitchMin: 0.90f, pitchMax: 1.08f);

            // ------------------------------------------------------------------
            // 9. FORTRESS RAMPARTS, WATCHTOWERS & BASTION SEMAPHORE (Coastal Wind)
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_RampartWind_BastionSemaphore", new[] { "UdayaBastion_Semaphore" }, new Vector3(34.3f, 0f, 78.2f),
                MedinaSoundElementCategory.RampartWind, MedinaSoundSourceKind.ExternalAudioFile, "coastal_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.22f, inner: 8f, outer: 34f, pan: panSpread + 4f);

            AddEmitterAtObject(root.transform, "SFX_RampartWind_NorthRampart", new[] { "Plot_17_NorthRampart" }, new Vector3(7.7f, 0f, 61.0f),
                MedinaSoundElementCategory.RampartWind, MedinaSoundSourceKind.ExternalAudioFile, "coastal_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.20f, inner: 8f, outer: 32f, pan: panSpread + 4f);

            AddEmitterAtObject(root.transform, "SFX_RampartWind_WestRampart", new[] { "Plot_18_WestRampart" }, new Vector3(-50.5f, 0f, 5.3f),
                MedinaSoundElementCategory.RampartWind, MedinaSoundSourceKind.ExternalAudioFile, "coastal_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.22f, inner: 8f, outer: 32f, pan: panSpread + 4f);

            AddEmitterAtObject(root.transform, "SFX_RampartWind_NECliff", new[] { "Plot_12_NECliff" }, new Vector3(55.2f, 0f, 39.3f),
                MedinaSoundElementCategory.RampartWind, MedinaSoundSourceKind.ExternalAudioFile, "coastal_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.20f, inner: 8f, outer: 30f, pan: panSpread + 4f);

            AddEmitterAtObject(root.transform, "SFX_RampartWind_SouthTower", new[] { "UdayaTower_08" }, new Vector3(-12.2f, 0f, -77.6f),
                MedinaSoundElementCategory.RampartWind, MedinaSoundSourceKind.ExternalAudioFile, "coastal_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.18f, inner: 6f, outer: 28f, pan: panSpread + 4f);

            // ------------------------------------------------------------------
            // 10. BAB OUDAIA HISTORIC GATE
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_BabOudayaGate_Creak", new[] { "UdayaGate_BabOudaya", "BabOudaya_MainGate" }, new Vector3(-39.4f, 0f, -36.9f),
                MedinaSoundElementCategory.HistoricGate, MedinaSoundSourceKind.FMODEvent, FMODEvents.WoodenDoorCreak,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.22f, inner: 4.5f, outer: 18f, pan: panSpread, minInt: 14f, maxInt: 28f, pitchMin: 0.85f, pitchMax: 0.98f);

            AddEmitterAtObject(root.transform, "SFX_BabOudayaGate_Wind", new[] { "UdayaGate_BabOudaya", "BabOudaya_MainGate" }, new Vector3(-39.4f, 0f, -36.9f),
                MedinaSoundElementCategory.HistoricGate, MedinaSoundSourceKind.ExternalAudioFile, "coastal_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.15f, inner: 5f, outer: 20f, pan: panSpread);

            // ------------------------------------------------------------------
            // 11. BLUE-WHITE RESIDENTIAL ALLEYS & ARCHWAYS (Cats, Chimes, Breeze)
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_AlleyCat_RueJamaaCentral", new[] { "Archway_RueJamaaCentral" }, new Vector3(4.8f, 0f, 12.5f),
                MedinaSoundElementCategory.AlleyLife, MedinaSoundSourceKind.ExternalAudioFile, "alley_cat.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.20f, inner: 4f, outer: 18f, pan: panSpread, minInt: 15f, maxInt: 32f, pitchMin: 0.92f, pitchMax: 1.12f);

            AddEmitterAtObject(root.transform, "SFX_AlleyCat_RueBazoMid", new[] { "Plot_05_RueBazoMid" }, new Vector3(-1.6f, 0f, -40.1f),
                MedinaSoundElementCategory.AlleyLife, MedinaSoundSourceKind.ExternalAudioFile, "alley_cat.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.18f, inner: 4f, outer: 18f, pan: panSpread, minInt: 16f, maxInt: 34f, pitchMin: 0.90f, pitchMax: 1.10f);

            AddEmitterAtObject(root.transform, "SFX_RueJamaaUpper_RiadChimes", new[] { "Plot_11_RueJamaaUpperW" }, new Vector3(6.6f, 0f, 36.3f),
                MedinaSoundElementCategory.AlleyLife, MedinaSoundSourceKind.ExternalAudioFile, "courtyard_chimes.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.14f, inner: 4.5f, outer: 18f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_Archway_RueJamaaSouth_Breeze", new[] { "Archway_RueJamaaSouth" }, new Vector3(-20.2f, 0f, -16.3f),
                MedinaSoundElementCategory.AlleyLife, MedinaSoundSourceKind.ExternalAudioFile, "leaves_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.13f, inner: 4f, outer: 16f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_Archway_RueJamaaNorth_Breeze", new[] { "Archway_RueJamaaNorth" }, new Vector3(33.6f, 0f, 49.0f),
                MedinaSoundElementCategory.AlleyLife, MedinaSoundSourceKind.ExternalAudioFile, "coastal_wind.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.15f, inner: 5f, outer: 19f, pan: panSpread);

            AddEmitterAtObject(root.transform, "SFX_EastRidge_Life", new[] { "Plot_13_EastRidge" }, new Vector3(53.1f, 0f, -25.1f),
                MedinaSoundElementCategory.AlleyLife, MedinaSoundSourceKind.ExternalAudioFile, "cafe_murmur.ogg",
                MedinaSoundPlaybackMode.ContinuousLoop, volume: 0.15f, inner: 5f, outer: 20f, pan: panSpread);

            // ------------------------------------------------------------------
            // 12. MEMORY PHOTO POIs & NPC NEIGHBORHOOD FOLEY
            // ------------------------------------------------------------------
            AddEmitterAtObject(root.transform, "SFX_MemoryWhisper_PlaceOudaya", new[] { "POI_PlaceOudayaPhoto" }, new Vector3(-12.5f, 0f, -24.5f),
                MedinaSoundElementCategory.MemoryLandmark, MedinaSoundSourceKind.FMODEvent, FMODEvents.Whisper,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.15f, inner: 3.5f, outer: 14f, pan: panSpread, minInt: 14f, maxInt: 26f, pitchMin: 0.94f, pitchMax: 1.04f);

            AddEmitterAtObject(root.transform, "SFX_MemoryWhisper_JewelleryMuseum", new[] { "POI_JewelleryMuseumPhoto" }, new Vector3(19.1f, 0f, -44.2f),
                MedinaSoundElementCategory.MemoryLandmark, MedinaSoundSourceKind.FMODEvent, FMODEvents.Whisper,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.15f, inner: 3.5f, outer: 14f, pan: panSpread, minInt: 14f, maxInt: 26f, pitchMin: 0.94f, pitchMax: 1.04f);

            AddEmitterAtObject(root.transform, "SFX_NPC_LallaKhadija_TeaCup", new[] { "NPC_LallaKhadija" }, new Vector3(1.0f, 0f, -2.9f),
                MedinaSoundElementCategory.AlleyLife, MedinaSoundSourceKind.FMODEvent, FMODEvents.GlassCupSetDown,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.18f, inner: 3f, outer: 13f, pan: panSpread, minInt: 12f, maxInt: 25f, pitchMin: 0.95f, pitchMax: 1.08f);

            AddEmitterAtObject(root.transform, "SFX_NPC_Allal_WorkshopScrape", new[] { "NPC_Allal" }, new Vector3(27.5f, 0f, -4.5f),
                MedinaSoundElementCategory.ArtisanCraft, MedinaSoundSourceKind.FMODEvent, FMODEvents.Scrape,
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.16f, inner: 3f, outer: 13f, pan: panSpread, minInt: 11f, maxInt: 24f, pitchMin: 0.90f, pitchMax: 1.08f);

            AddEmitterAtObject(root.transform, "SFX_NPC_Hicham_AlleyChime", new[] { "NPC_Hicham" }, new Vector3(-4.5f, 0f, 21.5f),
                MedinaSoundElementCategory.AlleyLife, MedinaSoundSourceKind.ExternalAudioFile, "pottery_chime.ogg",
                MedinaSoundPlaybackMode.PeriodicOneShot, volume: 0.16f, inner: 3f, outer: 14f, pan: panSpread, minInt: 14f, maxInt: 28f, pitchMin: 0.92f, pitchMax: 1.08f);

            return root;
        }

        private static MedinaMapSoundEmitter AddEmitterAtObject(
            Transform parent,
            string emitterName,
            string[] candidateObjectNames,
            Vector3 fallbackPos,
            MedinaSoundElementCategory category,
            MedinaSoundSourceKind kind,
            string pathOrFile,
            MedinaSoundPlaybackMode mode,
            float volume,
            float inner,
            float outer,
            float pan = 16f,
            float minInt = 10f,
            float maxInt = 22f,
            float pitchMin = 0.96f,
            float pitchMax = 1.04f)
        {
            Vector3 worldPos = ResolveWorldCenter(candidateObjectNames, fallbackPos);
            return CreateEmitter(parent, emitterName, worldPos, category, kind, pathOrFile, mode, volume, inner, outer, pan, minInt, maxInt, pitchMin, pitchMax);
        }

        private static MedinaMapSoundEmitter AddEmitterAtCluster(
            Transform parent,
            string emitterName,
            string[] clusterObjectNames,
            Vector3 fallbackPos,
            MedinaSoundElementCategory category,
            MedinaSoundSourceKind kind,
            string pathOrFile,
            MedinaSoundPlaybackMode mode,
            float volume,
            float inner,
            float outer,
            float pan = 16f,
            float minInt = 10f,
            float maxInt = 22f,
            float pitchMin = 0.96f,
            float pitchMax = 1.04f)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int i = 0; i < clusterObjectNames.Length; i++)
            {
                GameObject go = GameObject.Find(clusterObjectNames[i]);
                if (go != null)
                {
                    sum += GetEffectiveWorldCenter(go.transform);
                    count++;
                }
            }

            Vector3 worldPos = count > 0 ? sum / count : fallbackPos;
            worldPos.y = 0f;
            return CreateEmitter(parent, emitterName, worldPos, category, kind, pathOrFile, mode, volume, inner, outer, pan, minInt, maxInt, pitchMin, pitchMax);
        }

        private static MedinaMapSoundEmitter CreateEmitter(
            Transform parent,
            string emitterName,
            Vector3 worldPos,
            MedinaSoundElementCategory category,
            MedinaSoundSourceKind kind,
            string pathOrFile,
            MedinaSoundPlaybackMode mode,
            float volume,
            float inner,
            float outer,
            float pan,
            float minInt,
            float maxInt,
            float pitchMin,
            float pitchMax)
        {
            var go = new GameObject(emitterName);
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = new Vector3(worldPos.x, 0f, worldPos.z);

            MedinaMapSoundEmitter emitter = go.AddComponent<MedinaMapSoundEmitter>();
            emitter.Configure(emitterName, category, kind, pathOrFile, mode, volume, inner, outer, pan, minInt, maxInt, pitchMin, pitchMax);
            return emitter;
        }

        private static Vector3 ResolveWorldCenter(string[] candidateNames, Vector3 fallbackPos)
        {
            if (candidateNames != null)
            {
                for (int i = 0; i < candidateNames.Length; i++)
                {
                    GameObject go = GameObject.Find(candidateNames[i]);
                    if (go != null)
                    {
                        return GetEffectiveWorldCenter(go.transform);
                    }
                }
            }

            return fallbackPos;
        }

        /// <summary>
        /// Computes the real world center for a GameObject. Many plot containers in <c>MedinaMainLevel</c>
        /// (such as <c>Plot_09_ElAtiqaMosque</c> or <c>Plot_08_EastSouk</c>) sit at origin (0,0,0) while
        /// their direct child buildings hold the real world coordinates; this method averages child positions
        /// when the parent itself sits at origin.
        /// </summary>
        private static Vector3 GetEffectiveWorldCenter(Transform target)
        {
            Vector3 selfPos = target.position;
            bool nearOrigin = selfPos.sqrMagnitude < 0.01f;

            if (nearOrigin && target.childCount > 0)
            {
                Vector3 sum = Vector3.zero;
                int valid = 0;
                for (int i = 0; i < target.childCount; i++)
                {
                    Transform child = target.GetChild(i);
                    if (child.position.sqrMagnitude >= 0.01f)
                    {
                        sum += child.position;
                        valid++;
                    }
                }

                if (valid > 0)
                {
                    Vector3 center = sum / valid;
                    center.y = 0f;
                    return center;
                }
            }

            return new Vector3(selfPos.x, 0f, selfPos.z);
        }
    }
}
