using HarmonyLib;
using NeonLite.Modules;
using NWArchipelago.Objects;
using UnityEngine;
using static NWArchipelago.Modules.APManage;

namespace NWArchipelago.Modules
{
    [Module]
    internal static class Campaign
        static bool active = true;

    internal const string CAMPAIGN_ID = "C_ARCHIPELAGO";
    internal const string OGCAMPAIGN_ID = "C_MAINQUEST";

    static void Setup() => active = !Settings.testMode;

    static void Activate(bool _)
    {
        Patching.AddPatch(typeof(MenuResourcesDisplay), "RefreshMainObjective", MainObjectiveOverride, Patching.PatchTarget.Prefix);
        Patching.AddPatch(typeof(MainMenu), "SelectCampaign", SelectCorrectCampaign, Patching.PatchTarget.Prefix);

        Patching.AddPatch(Helpers.Method(typeof(Game), "PlayLevel", [typeof(string), typeof(bool), typeof(Action)]),
            Helpers.HM(PreventPlayString).SetPriority(Priority.First), Patching.PatchTarget.Prefix);
        Patching.AddPatch(Helpers.Method(typeof(Game), "PlayLevel", [typeof(LevelData), typeof(bool), typeof(bool)]),
            Helpers.HM(PreventPlayLD).SetPriority(Priority.First), Patching.PatchTarget.Prefix);
    }

    static bool MainObjectiveOverride(MenuResourcesDisplay __instance)
    {
        __instance.rankDisplay.gameObject.SetActive(SlotData.unlockMethod == UnlockMethod.Ranks);

        return false;
    }

    static void SelectCorrectCampaign(ref string campaignID)
        internal static readonly Regex sidequestLRegex = new(@"M_.*_(\w)\w*");

    internal static readonly Dictionary<string, string> levelKey = [];

    internal static HashSet<string> unlockedLevels = [];
    internal static CampaignData campaign;
    internal static void MakeCampaign()
    {
        NWArchipelago.Log.DebugMsg("make campaign");
        var gd = Singleton<Game>.Instance.GetGameData();

        var km = 0;
        foreach (var mission in gd.GetCampaign("C_MAINQUEST").missionData)
        {
            ++km;
            var lm = 0;
            foreach (var level in mission.levels)
                levelKey.Add(level.levelID, $"{km}-{++lm}");
        }

        var match = sidequestLRegex.Match(mission.missionID)?.Groups[1]?.Value;
        if (match == "G" || match == null)
            continue;

        var lm = 0;
        foreach (var level in mission.levels)
            levelKey.Add(level.levelID, $"{match}-{++lm}");

        // campaign.campaignType = CampaignData.CampaignType.Sidequest;

        return new HubContentLocationData()
        {
            location = og.location,
            state = HubContentLocationData.LocationState.Active,
            locationActions = []
        };
    }

    hcd.locationData = [
        GetFromRepeating("PORTAL"),
        GetFromRepeating("CITYHALLOFFICE"),
    ];
            if (SlotData.unlockMethod != UnlockMethod.Levels)
            {
                gd.campaigns.RemoveAll(x => x.campaignID == "C_SIDEQUESTS");
                // we're gonna hijack mainquest

                campaign.missionData.Clear();

                var missionCount = SlotData.missionReqs.Count;
    var levelsNorm = SlotData.levels.Count / missionCount;
    var offset = missionCount - (SlotData.levels.Count % missionCount);

    var lTotal = 0;
                for (int m = 0; m<missionCount; ++m)
                {
                    var mission = ScriptableObject.CreateInstance<MissionData>();
    mission.missionID = $"M_ARCHI{m}";
                    mission.missionDisplayName = $"Mission {m + 1}";
                    mission.hubContentData = hcd;

                    if (m != 0)
                        mission.medalsRequired = SlotData.missionReqs[m];

                    var levelCount = levelsNorm;
                    if (m >= offset)
                        levelCount++;

                    for (int l = 0; l<levelCount; ++l)
                    {
                        var level = SlotData.levels[lTotal++];
    Logic.Level(level).ranks = mission.medalsRequired;
                        mission.levels.Add(level);
                    }

campaign.missionData.Add(mission);
                }
            }
            else
{
    var sq = gd.GetCampaign("C_SIDEQUESTS");
    campaign.missionData.Add(sq.missionData.First(x => x.missionID.Contains("RED")));
    campaign.missionData.Add(sq.missionData.First(x => x.missionID.Contains("VIOLET")));
    campaign.missionData.Add(sq.missionData.First(x => x.missionID.Contains("YELLOW")));
    gd.campaigns.RemoveAll(x => x.campaignID == "C_SIDEQUESTS");

    int intID = 0;
    foreach (var mission in campaign.missionData)
    {
        mission.missionDialogues.Clear();
        mission.hubContentData = hcd;
        mission.medalsRequired = 0;

        if (mission.missionType == MissionData.MissionType.SideQuest)
            mission.missionDisplayName = "NWArchipelago/" + mission.missionID;

        mission.missionType = MissionData.MissionType.MainQuest;
        mission.hubContentData = hcd;

        foreach (var level in mission.levels)
            level.levelIntegerID = intID++;
    }
}
        }

        internal static void HandleSaveCData(bool save = false)
{
    if (SlotData.unlockMethod != UnlockMethod.Levels)
    {

        int furthest = 0;
        foreach (var mission in campaign.missionData)
        {
            var mstats = GameDataManager.missionStats[mission.missionID];

            if (mission.medalsRequired <= SaveHandler.archiSaveData.neonRank)
            {
                furthest++;
                mstats.UnlockArchive();
                mstats.SetFarthestLevel(mission.levels.Count, false);
            }

            foreach (var level in mission.levels)
            {
                var lstats = GameDataManager.levelStats[level.levelID];
                if (mission.medalsRequired <= SaveHandler.archiSaveData.neonRank)
                    lstats.SetForceUnlocked(true, false);
            }
        }

        cstats.SetFarthestMission(furthest - 1, save);
    }
    else
    {
        foreach (var mission in campaign.missionData)
        {
            var mstats = GameDataManager.missionStats[mission.missionID];
            mstats.UnlockArchive();
            mstats.SetFarthestLevel(mission.levels.Count, false);

            foreach (var level in mission.levels)
            {
                var lstats = GameDataManager.levelStats[level.levelID];
                lstats.SetForceUnlocked(unlockedLevels.Contains(level.levelID), false);
                Logic.Level(level);
            }
        }

        cstats.SetFarthestMission(campaign.missionData.Count - 1, save);
    }
}

static void PreventPlayString(ref string newLevelID, ref bool fromArchive)
{
    var gd = Singleton<Game>.Instance.GetGameData();
    var level = gd.GetLevelData(newLevelID);
    if (Logic.HasLogic(level) && !Logic.Level(level).CanAccessLevel())
    {
        newLevelID = "HUB_HEAVEN";
        fromArchive = false;
    }
}
static void PreventPlayLD(ref LevelData newLevel, ref bool fromArchive)
{
    if (Logic.HasLogic(newLevel) && !Logic.Level(newLevel).CanAccessLevel())
    {
        var gd = Singleton<Game>.Instance.GetGameData();
        newLevel = gd.GetLevelData("HUB_HEAVEN");
        fromArchive = false;
    }
}
    }
}
