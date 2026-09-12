
using System.Runtime.CompilerServices;
using HarmonyLib;
using I2.Loc;
using MelonLoader;
using MelonLoader.TinyJSON;
using NeonLite.Modules;
using NWArchipelago.Modules;
using UnityEngine;
using static NeonLite.Modules.CommunityMedals;

namespace NWArchipelago.Objects
{
    [Module]
    internal class Logic
    {
        const bool priority = true;
        const bool active = true;

        internal enum LogicDisplay
        {
            None,
            TitleOnly,
            Colors,
            ColorsDetailed,
            Full,
        }

        internal enum HintDisplay
        {
            Never,
            WhenAvailable,
            WhenAny,
            Always,
        }

        internal static MelonPreferences_Entry<LogicDisplay> display;
        internal static MelonPreferences_Entry<bool> outOfLogic;

        internal static MelonPreferences_Entry<bool> hints;
        internal static MelonPreferences_Entry<HintDisplay> hintDisplay;
        
        internal static MelonPreferences_Entry<bool> hueShift;

        static void Setup()
        {
            display = NeonLite.Settings.Add(Settings.h, "Tracking", "display", "Display Type",
                """
                How the tracker should display available checks:

                Full - Colors the buttons *and* shows the numeric count for checks
                ColorsDetailed - Colors the level times and gift text if they're available.
                Colors - Only colors the buttons. The full amount of checks is still shown on the title screen
                TitleOnly - Only give the number on the title screen
                None - Don't show any check indication at all
                """, LogicDisplay.ColorsDetailed);

            outOfLogic = NeonLite.Settings.Add(Settings.h, "Tracking", "yellowLogic", "Show Out of Logic",
                "Whether to show out of logic (but still possible!) checks as yellow.", false);

            hints = NeonLite.Settings.Add(Settings.h, "Tracking", "hints", "Show Hinted",
                "Whether to enable any hinted functionality.", false);

            hintDisplay = NeonLite.Settings.Add(Settings.h, "Tracking", "hintDisplay", "Hint Button Display",
                """
                When the hinted levels mission button should show up:

                Always - Always show the hinted button
                WhenAny - Show the hinted button if you have any hinted in your game
                WhenAvailable - Show the hinted button only when any are in logic
                Never - Never show the hints button
                """, HintDisplay.WhenAvailable);
        }

        static readonly ConditionalWeakTable<LevelData, Logic> logicData = new();
        static readonly ConditionalWeakTable<LevelData, Logic> fullData = new();

        static readonly string[] GIFTLESS = ["GRID_BOSS_YELLOW", "GRID_BOSS_GODSDEATHTEMPLE", "TUT_ORIGIN", "GRID_BOSS_RAPTURE"];

        internal static bool HasLogic(LevelData l) => logicData.TryGetValue(l, out var _);
        internal static Logic Level(LevelData l, bool full = false)
        {
            var ret = (full ? fullData : logicData).GetOrCreateValue(l);
            ret.level = l;
            return ret;
        }

        [Flags]
        internal enum LevelRequirements : ushort
        {
            FistOnly = 0,
            Katana = 1 << 0,
            PurifyFire = 1 << 1,
            PurifyDiscard = 1 << 2,
            ElevateFire = 1 << 3,
            ElevateDiscard = 1 << 4,
            GodspeedFire = 1 << 5,
            GodspeedDiscard = 1 << 6,
            StompFire = 1 << 7,
            StompDiscard = 1 << 8,
            FireballFire = 1 << 9,
            FireballDiscard = 1 << 10,
            DominionFire = 1 << 11,
            DominionDiscard = 1 << 12,
            BookOfLife = 1 << 13
        }

        internal static bool HasSingleRequirement(LevelRequirements single)
        {
            if (single == LevelRequirements.FistOnly)
                return true;
            var str = single.ToString();

            HashSet<string> tolook = null;
            if (str.EndsWith("Fire"))
            {
                str = str.Substring(0, str.Length - "Fire".Length);
                tolook = Cards.fires;
            }
            else if (str.EndsWith("Discard"))
            {
                str = str.Substring(0, str.Length - "Discard".Length);
                tolook = Cards.discards;
            }
            else if (single == LevelRequirements.Katana)
                tolook = Cards.fires;
            else if (single == LevelRequirements.BookOfLife)
            {
                str = "Book of Life";
                tolook = Cards.discards;
            }

            return tolook.Contains(Cards.EngToID(str));
        }

        internal static bool HasRequirements(LevelRequirements requirements)
        {
            if (requirements == LevelRequirements.FistOnly)
                return true;

            return Enum.GetValues(typeof(LevelRequirements)).Cast<LevelRequirements>()
                .Where(f => f != LevelRequirements.FistOnly && requirements.HasFlag(f))
                .All(HasSingleRequirement);
        }

        LevelData level;
        internal int ranks;

        internal readonly Dictionary<MedalEnum, HashSet<LevelRequirements>> perMedalLogic = [];
        internal HashSet<LevelRequirements> giftLogic = [];

        internal bool CanAccessLevel()
        {
            if (SaveHandler.archiSaveData.neonRank < ranks)
                return false;
            if (level.isSidequest && !APManage.SlotData.sidequests)
                return false;
            if (APManage.SlotData.unlockMethod == APManage.UnlockMethod.Levels
                && !Campaign.unlockedLevels.Contains(level.levelIntegerID))
                return false; // we're using level unlocks and yet we don't have it :broken_heart:
            return true;
        }

        internal bool CanGetMedal(MedalEnum medal)
        {
            if (!CanAccessLevel())
                return false;
            if (!APManage.SlotData.medals.Contains(medal))
                return false;
            if (level.isSidequest && medal != MedalEnum.Bronze)
                return false; // cheap way to chechk for completion
            if (GetMedalIndex(level.levelID) >= (int)medal)
                return false; // we CAN get it, but we already have it silly
            if (!perMedalLogic.TryGetValue(medal, out var logic))
                return false;

            return logic.Any(HasRequirements);
        }
        internal bool MedalHinted(MedalEnum medal, bool inLogic = true, bool accessible = true) {
            if (inLogic)
            {
                if (!CanGetMedal(medal))
                    return false;
            }
            else
            {
                if (accessible && !CanAccessLevel())
                    return false;
                if (!APManage.SlotData.medals.Contains(medal))
                    return false;
                if (level.isSidequest && medal != MedalEnum.Bronze)
                    return false; // cheap way to *filter* to just check once
            }

            var levelName = LocalizationManager.GetTranslation(level.GetLevelDisplayName(), overrideLanguage: "English");

            string namecheck;
            if (level.isSidequest)
                namecheck = $"{levelName} Completion";
            else
                namecheck = $"{levelName} {medal} Completion";

            return APManage.IsHinted(namecheck);
        }

        internal bool CanGift()
        {
            if (!APManage.SlotData.gifts || level.isSidequest || GIFTLESS.Contains(level.levelID))
                return false;
            if (!CanAccessLevel())
                return false;
            if (GameDataManager.GetLevelStats(level.levelID).HasCollectibleBeenFound())
                return false; // we already have it

            return giftLogic.Any(HasRequirements);
        }
        internal bool GiftHinted(bool inLogic = true, bool accessible = true) {
            if (!APManage.SlotData.gifts || level.isSidequest || GIFTLESS.Contains(level.levelID))
                return false;

            if (inLogic)
            {
                if (!CanGift())
                    return false;
            }
            else
            {
                if (accessible && !CanAccessLevel())
                    return false;
            }

            var levelName = LocalizationManager.GetTranslation(level.GetLevelDisplayName(), overrideLanguage: "English");
            return APManage.IsHinted($"{levelName} Gift");
        }


        internal int Checks()
        {
            if (SaveHandler.archiSaveData.neonRank < ranks)
                return 0;

            static IEnumerable<MedalEnum> MedalEnumerate()
            {
                for (int i = 0; i <= (int)MedalEnum.Dev; ++i) {
                    if (APManage.SlotData.medals.Contains((MedalEnum)i))
                        yield return (MedalEnum)i;
                }
            }

            return MedalEnumerate()
                .Select(CanGetMedal)
                .Append(CanGift())
                .Count(x => x);
        }
        internal int Hinteds(bool inLogic = true, bool accessible = true) {
            static IEnumerable<MedalEnum> MedalEnumerate()
            {
                for (int i = 0; i <= (int)MedalEnum.Dev; ++i)
                {
                    if (APManage.SlotData.medals.Contains((MedalEnum)i))
                        yield return (MedalEnum)i;
                }
            }

            return MedalEnumerate()
                .Select(x => MedalHinted(x, inLogic, accessible))
                .Append(GiftHinted(inLogic, accessible))
                .Count(x => x);
        }

        internal Logic Full(bool full = true) => Level(level, full);

        internal static int MissionChecks(MissionData mission, bool full = false)
        {
            if (SaveHandler.archiSaveData.neonRank < mission.medalsRequired)
                return 0;

            return mission.levels
                .Select(x => Level(x, full))
                .Sum(x => x.Checks());
        }

        internal static int AllChecks(bool full = false)
        {
            return APManage.SlotData.levels
                .Select(x => Level(x, full))
                .Sum(x => x.Checks());
        }

        internal static int MissionHinted(MissionData mission, bool inLogic = true, bool accessible = true, bool full = false)
        {
            if (SaveHandler.archiSaveData.neonRank < mission.medalsRequired)
                return 0;

            return mission.levels
                .Select(x => Level(x, full))
                .Sum(x => x.Hinteds(inLogic, accessible));
        }

        internal static int AllHinted(bool inLogic = true, bool accessible = true, bool full = false)
        {
            return APManage.SlotData.levels
                .Select(x => Level(x, full))
                .Sum(x => x.Hinteds(inLogic, accessible));
        }

        internal struct StringStorage {
            public string cS;
            public int cN;
            public string hS;
            public int hN;

            public readonly AxKReplacementPair[] Replacements => [
                new AxKReplacementPair("{CHK}", cS),
                new AxKReplacementPair("{HNT}", hS),
                new AxKReplacementPair("{CN}", cN),
                new AxKReplacementPair("{HN}", hN),
            ];
        }

        internal static string GetStrings(out StringStorage strings, LevelData level = null, MissionData mission = null, bool force = false, bool hints = true)
        {
            strings = new();
            if (!loaded)
                return "";
            if (display.Value < LogicDisplay.Full && !force)
                return "";

            if (level) {
                var logic = Level(level);
                strings.cN = logic.Checks();
                strings.hN = logic.Hinteds();
            }
            else if (mission) {
                strings.cN = MissionChecks(mission);
                strings.hN = MissionHinted(mission);
            }
            else {
                strings.cN = AllChecks();
                strings.hN = AllHinted();
            }

            if (strings.cN == 0)
                strings.cS = "NWArchipelago/CHECKS_NONE";
            else if (strings.cN == 1)
                strings.cS = "NWArchipelago/CHECKS_ONE";
            else
                strings.cS = "NWArchipelago/CHECKS_SOME";

            if (strings.hN == 0)
                strings.hS = "NWArchipelago/HINTS_NONE";
            else if (strings.hN == 1)
                strings.hS = "NWArchipelago/HINTS_ONE";
            else
                strings.hS = "NWArchipelago/HINTS_SOME";

            // checks should take priority
            if (strings.hN == 0 || !hints)
                return strings.cS;
            if (strings.cN == 0)
                return strings.hS;
            return "NWArchipelago/CHECK_HINT_COMBO";
        }

        static Color lowLight = new(0.8f, 0.8f, 0.8f);
        static Color yellowLight = new Color32(222, 213, 169, 255);
        static Color greenLight = new Color32(230, 255, 230, 255);
        static Color blueLight = new Color32(153, 218, 255, 255);

        internal static Color GetColor(MissionData mission = null, LevelData level = null, MedalEnum medal = MedalEnum.Plus, bool gift = false, bool hints = true)
        {
            bool inl;
            bool outl;
            bool hintl;

            if (level)
            {
                var logic = Level(level);
                if (gift)
                {
                    inl = logic.CanGift();
                    outl = logic.Full().CanGift();
                    hintl = logic.GiftHinted();
                }
                else if (medal <= MedalEnum.Dev)
                {
                    inl = logic.CanGetMedal(medal);
                    outl = logic.Full().CanGetMedal(medal);
                    hintl = logic.MedalHinted(medal);
                }
                else
                {
                    inl = logic.Checks() > 0;
                    outl = logic.Full().Checks() > 0;
                    hintl = logic.Hinteds() > 0;
                }
            }
            else if (mission)
            {
                inl = MissionChecks(mission) > 0;
                outl = MissionChecks(mission, true) > 0;
                hintl = MissionHinted(mission) > 0;
            }
            else
            {
                inl = AllChecks() > 0;
                outl = AllChecks(true) > 0;
                hintl = AllHinted() > 0;
            }

            if (!outOfLogic.Value)
                outl = false;

            if (hintl && hints)
                return blueLight;
            if (inl)
                return greenLight;
            if (outl)
                return yellowLight;
            return lowLight;
        }

        Logic Clear() {
            ranks = 0;
            perMedalLogic.Clear();
            giftLogic.Clear();

            return this;
        }

#pragma warning disable CS0649
        [Serializable]
        class LogicProxy
        {
            public int m;
            public int r;
            public int k;
            public int e;
        }

        internal static bool loaded = false;
        internal const string filename = "nw_cr.json";
        internal const string URL = "https://raw.githubusercontent.com/Badhamknibbs/ArchipelagoNeonWhite/main/worlds/neonwhite/data/" + filename;

        internal static bool Load(Variant json)
        {
            try
            {
                foreach (var level in APManage.SlotData.levels)
                {
                    var lname = LocalizationManager.GetTranslation(level.GetLevelDisplayName(), overrideLanguage: "English");
                    var logics = json[lname] as ProxyArray;

                    var normal = logicData.GetOrCreateValue(level).Clear();
                    var full = fullData.GetOrCreateValue(level).Clear();

                    foreach (var logic in logics.Select(x => x.Make<LogicProxy>()))
                    {
                        var req = (LevelRequirements)logic.r;
                        void AddToLogic(Logic addTo)
                        {
                            void AddToSet(HashSet<LevelRequirements> set)
                            {
                                if (set.Any(x => req.HasFlag(x)))
                                    return;
                                set.Add(req);
                            }

                            if (logic.m == 5) // gift
                            {
                                AddToSet(addTo.giftLogic);
                                return;
                            }

                            var cap = 4 - logic.m;

                            for (int m = 0; m <= cap; ++m)
                            {
                                var medal = (MedalEnum)m;
                                if (!addTo.perMedalLogic.TryGetValue(medal, out var set))
                                {
                                    set = [];
                                    addTo.perMedalLogic.Add(medal, set);
                                }
                                AddToSet(set);
                            }
                        }

                        AddToLogic(full);
                        if (APManage.SlotData.knowledge >= logic.k && APManage.SlotData.execution >= logic.e)
                            AddToLogic(normal);
                    }
                }

                loaded = true;
                return true;

            }
            catch (Exception e)
            {
                NWArchipelago.Log.Warning("Error while parsing logic:");
                NWArchipelago.Log.Error(e);
            }
            return false;
        }
    }
}
