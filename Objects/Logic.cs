
using System.Runtime.CompilerServices;
using I2.Loc;
using MelonLoader;
using MelonLoader.TinyJSON;
using NeonLite.Modules;
using NWArchipelago.Modules;
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
            Full,
        }

        internal static MelonPreferences_Entry<LogicDisplay> display;
        internal static MelonPreferences_Entry<bool> outOfLogic;

        static void Setup()
        {
            display = NeonLite.Settings.Add(Settings.h, "Tracking", "display", "Display Type",
                """
                How the tracker should display available checks:

                Full - Colors the buttons *and* shows the numeric count for checks
                Colors - Only colors the buttons. The full amount of checks is still shown on the title screen
                TitleOnly - Only give the number on the title screen
                None - Don't show any check indication at all
                """, LogicDisplay.Colors);

            outOfLogic = NeonLite.Settings.Add(Settings.h, "Tracking", "yellowLogic", "Show Out of Logic",
                "Whether to show out of logic (but still possible!) checks as yellow.", false);
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

        internal bool HasSingleRequirement(LevelRequirements single)
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

        internal bool HasRequirements(LevelRequirements requirements)
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
        internal bool CanAccessLevel()
        {
            if (SaveHandler.archiSaveData.neonRank < ranks)
                return false;
            if (APManage.SlotData.unlockMethod == APManage.UnlockMethod.Levels
                && !Campaign.unlockedLevels.Contains(level.levelID))
                return false; // we're using level unlocks and yet we don't have it :broken_heart:
            return true;
        }

        internal bool CanGetMedal(MedalEnum medal)
        {
            if (!CanAccessLevel())
                return false;
            if (level.isSidequest && medal != MedalEnum.Bronze)
                return false; // cheap way to chechk for completion
            if (GetMedalIndex(level.levelID) >= (int)medal)
                return false; // we CAN get it, but we already have it silly
            if (!perMedalLogic.ContainsKey(medal))
                return false;

            return perMedalLogic[medal].Any(HasRequirements);
        }
            if (!CanAccessLevel())
                return false;
            if (level.isSidequest || GIFTLESS.Contains(level.levelID))
                return false; // these don't have one
            if (GameDataManager.GetLevelStats(level.levelID).HasCollectibleBeenFound())
                return false; // we already have it

            return giftLogic.Any(HasRequirements);
        }

        internal int Checks()
        {
            if (SaveHandler.archiSaveData.neonRank < ranks)
                return 0;

            static IEnumerable<MedalEnum> MedalEnumerate()
            {
                for (int i = 0; i <= (int)APManage.SlotData.medalCap; ++i)
                    yield return (MedalEnum)i;
            }

            return MedalEnumerate()
                .Select(CanGetMedal)
                .Append(CanGift())
                .Count(x => x);
        }

        internal static int MissionChecks(MissionData mission)
        {
            if (SaveHandler.archiSaveData.neonRank < mission.medalsRequired)
                return 0;

            return mission.levels
                .Select(logicData.GetOrCreateValue)
                .Sum(x => x.Checks());
        }

        internal static int AllChecks()
        {
            return APManage.SlotData.levels
                .Select(logicData.GetOrCreateValue)
                .Sum(x => x.Checks());
        }


        internal static string ChecksString(out int checks, LevelData level = null, MissionData mission = null, bool force = false)
        {
            checks = 0;
            if (!loaded)
                return "";

            if (level)
                checks = Level(level).Checks();
            else if (mission)
                checks = MissionChecks(mission);
            else
                checks = AllChecks();

            if (display.Value < LogicDisplay.Full && !force)
                return "";

            if (checks == 0)
                return "NWArchipelago/CHECKS_NONE";
            if (checks == 1)
                return "NWArchipelago/CHECKS_ONE";
            return "NWArchipelago/CHECKS_SOME";
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

                    // FIRST PASS: fill the logic
                    foreach (var logic in logics.Select(x => x.Make<LogicProxy>()))
                    {

                        var req = (LevelRequirements)logic.r;
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

                        var cap = Math.Min((int)APManage.SlotData.medalCap, 4 - logic.m);

                        for (int m = 0; m <= cap; ++m)
                            NWArchipelago.Log.DebugMsg($"adding to {medal}");
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
