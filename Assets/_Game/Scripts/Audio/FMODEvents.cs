using System;
using System.Collections.Generic;

namespace Walida.Audio
{
    /// <summary>
    /// Exact FMOD event paths exported in <c>FMOD/Build/Desktop/Master.bank</c>, plus friendly alias
    /// resolution for Ink narrative tags (<c>#sfx:</c>, <c>#bgm:</c>, <c>#ambience:</c>) and scripts.
    /// </summary>
    public static class FMODEvents
    {
        // UI & Narrative
        public const string MenuClick = "event:/menu_click";
        public const string ResponseClick = "event:/Reponse_click";
        public const string TextStart = "event:/Text_start";
        public const string DetailPick = "event:/Detail_pick";
        public const string QuietRoomTone = "event:/quiet room tone";

        // Photography Minigame & 1960s Memory Glitches
        public const string Shutter = "event:/shutter";
        public const string CameraFocusBeep = "event:/camera focus beep";
        public const string StaticRiser = "event:/static riser";
        public const string Whisper = "event:/whisper";
        public const string SlowHeartbeat = "event:/slow heatbeat";
        public const string Rumble = "event:/Rumble";

        // Audio Recording Minigame (Souk / Medina Sounds)
        public const string CallStatic = "event:/call_static";
        public const string SignalLock = "event:/signal _lock";
        public const string RecorderStartBeep = "event:/Recorder start beep";
        public const string HoldTillTone = "event:/hold_till_tone";
        public const string RecordingSaved = "event:/Recording _saved";
        public const string SoukLoop = "event:/souk_loop";
        public const string MarketVendorShouting = "event:/market vendor shouting";
        public const string TeaKettle = "event:/Tea_kettle";
        public const string Pigeons = "event:/pigeons";

        // Medina Main Level & Environment
        public const string NeighborhoodLoop = "event:/Neighborhood_lop";
        public const string Walking = "event:/walking";
        public const string WoodenDoorCreak = "event:/wooden door creak";
        public const string Quran = "event:/quran";

        // Phone, Foley & Emotional Story Cues
        public const string PhoneRing = "event:/phone_ring";
        public const string PhoneHome = "event:/phone_home";
        public const string GlassCupSetDown = "event:/glass cup set down";
        public const string MeatCleaverWood = "event:/meat cleaver wood";
        public const string Scrape = "event:/scrape";
        public const string WomanCrying = "event:/woman crying";

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Exact names (normalized without spaces/underscores/hyphens)
            ["detailpick"] = DetailPick,
            ["neighborhoodlop"] = NeighborhoodLoop,
            ["neighborhoodloop"] = NeighborhoodLoop,
            ["neighborhood"] = NeighborhoodLoop,
            ["medina"] = NeighborhoodLoop,
            ["medinaloop"] = NeighborhoodLoop,
            ["recorderstartbeep"] = RecorderStartBeep,
            ["recordstart"] = RecorderStartBeep,
            ["recordingsaved"] = RecordingSaved,
            ["recordsaved"] = RecordingSaved,
            ["reponseclick"] = ResponseClick,
            ["responseclick"] = ResponseClick,
            ["choiceclick"] = ResponseClick,
            ["rumble"] = Rumble,
            ["teakettle"] = TeaKettle,
            ["kettle"] = TeaKettle,
            ["tea"] = TeaKettle,
            ["cafemaure"] = TeaKettle,
            ["cafe"] = TeaKettle,
            ["textstart"] = TextStart,
            ["typing"] = TextStart,
            ["typewriter"] = TextStart,
            ["callstatic"] = CallStatic,
            ["phonestatic"] = CallStatic,
            ["static"] = CallStatic,
            ["camerafocusbeep"] = CameraFocusBeep,
            ["focusbeep"] = CameraFocusBeep,
            ["camerafocus"] = CameraFocusBeep,
            ["glasscupsutdown"] = GlassCupSetDown,
            ["glasscupsetdown"] = GlassCupSetDown,
            ["glasscup"] = GlassCupSetDown,
            ["cup"] = GlassCupSetDown,
            ["holdtilltone"] = HoldTillTone,
            ["holdtone"] = HoldTillTone,
            ["marketvendorshouting"] = MarketVendorShouting,
            ["vendorshouting"] = MarketVendorShouting,
            ["soukmerchants"] = MarketVendorShouting,
            ["vendors"] = MarketVendorShouting,
            ["meatcleaverwood"] = MeatCleaverWood,
            ["meatcleaver"] = MeatCleaverWood,
            ["cleaver"] = MeatCleaverWood,
            ["menuclick"] = MenuClick,
            ["buttonclick"] = MenuClick,
            ["click"] = MenuClick,
            ["phonehome"] = PhoneHome,
            ["phonering"] = PhoneRing,
            ["ring"] = PhoneRing,
            ["pigeons"] = Pigeons,
            ["birds"] = Pigeons,
            ["fountain"] = Pigeons,
            ["medinafountain"] = Pigeons,
            ["quietroomtone"] = QuietRoomTone,
            ["roomtone"] = QuietRoomTone,
            ["room"] = QuietRoomTone,
            ["quran"] = Quran,
            ["mosque"] = Quran,
            ["scrape"] = Scrape,
            ["shutter"] = Shutter,
            ["camerashutter"] = Shutter,
            ["photo"] = Shutter,
            ["signallock"] = SignalLock,
            ["lock"] = SignalLock,
            ["slowheatbeat"] = SlowHeartbeat,
            ["slowheartbeat"] = SlowHeartbeat,
            ["heartbeat"] = SlowHeartbeat,
            ["soukloop"] = SoukLoop,
            ["souk"] = SoukLoop,
            ["staticriser"] = StaticRiser,
            ["riser"] = StaticRiser,
            ["glitch"] = StaticRiser,
            ["walking"] = Walking,
            ["footsteps"] = Walking,
            ["steps"] = Walking,
            ["whisper"] = Whisper,
            ["whispers"] = Whisper,
            ["womancrying"] = WomanCrying,
            ["crying"] = WomanCrying,
            ["woodendoorcreak"] = WoodenDoorCreak,
            ["doorcreak"] = WoodenDoorCreak,
            ["door"] = WoodenDoorCreak,
            ["gate"] = WoodenDoorCreak,
        };

        /// <summary>
        /// Resolves either a full FMOD event path (<c>event:/...</c>) or a friendly ID/alias
        /// (e.g. <c>"shutter"</c>, <c>"neighborhood_loop"</c>, <c>"signal_lock"</c>) to the
        /// exact path present in <c>Master.bank</c>.
        /// </summary>
        public static string Resolve(string idOrPath)
        {
            if (string.IsNullOrWhiteSpace(idOrPath))
                return null;

            string trimmed = idOrPath.Trim();
            if (trimmed.StartsWith("event:/", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = trimmed.Substring("event:/".Length);
                string normalizedSuffix = NormalizeKey(suffix);
                if (Aliases.TryGetValue(normalizedSuffix, out string canonical))
                    return canonical;

                return trimmed;
            }

            string key = NormalizeKey(trimmed);
            if (Aliases.TryGetValue(key, out string resolved))
                return resolved;

            return "event:/" + trimmed;
        }

        private static string NormalizeKey(string input)
        {
            char[] buffer = new char[input.Length];
            int count = 0;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsLetterOrDigit(c))
                {
                    buffer[count++] = char.ToLowerInvariant(c);
                }
            }
            return new string(buffer, 0, count);
        }
    }
}
