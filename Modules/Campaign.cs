using System.Text.RegularExpressions;
using NeonLite.Modules;
using NWArchipelago.Objects;
using UnityEngine;
using static NWArchipelago.Modules.APManage;

namespace NWArchipelago.Modules
{
    [Module]
    internal static class Campaign
    {
        const bool priority = true;
        const bool active = true;

        internal const string CAMPAIGN_ID = "C_ARCHIPELAGO";
        internal const string OGCAMPAIGN_ID = "C_MAINQUEST";

        static void Activate(bool _)
        {
            Patching.AddPatch(typeof(MenuResourcesDisplay), "RefreshMainObjective", MainObjectiveOverride, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MainMenu), "SelectCampaign", SelectCorrectCampaign, Patching.PatchTarget.Prefix);
        }

        static bool MainObjectiveOverride(MenuResourcesDisplay __instance)
        {
            __instance.panelMainQuests.SetActive(false);
            __instance.panelOptionalQuests.SetActive(false);

            return false;
        }

        static void SelectCorrectCampaign(ref string campaignID)
            => campaignID = (campaignID == OGCAMPAIGN_ID && !Settings.testMode) ? CAMPAIGN_ID : campaignID;


        internal static readonly Dictionary<string, string> levelKey = [];

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

            foreach (var mission in gd.GetCampaign("C_SIDEQUESTS").missionData)
            {
                var match = Regex.Match(mission.missionID, @"M_.*_(\w)\w*")?.Groups[1]?.Value;
                if (match == "G" || match == null)
                    continue;

                var lm = 0;
                foreach (var level in mission.levels)
                    levelKey.Add(level.levelID, $"{match}-{++lm}");
            }


            gd.campaigns.RemoveAll(x => x.campaignID == "C_SIDEQUESTS");

            // we're gonna hijack mainquest
            campaign = gd.GetCampaign("C_MAINQUEST");
            campaign.name = "Campaign_Archipelago";
            campaign.campaignID = "C_ARCHIPELAGO";
            // campaign.campaignType = CampaignData.CampaignType.Sidequest;
            campaign.campaignDisplayName = "Archipelago";

            campaign.missionData.Clear();

            var hcd = ScriptableObject.CreateInstance<HubContentData>();

            static HubContentLocationData GetFromRepeating(string id)
            {
                var og = campaign.hubContentRepeating.LocationDict[id];
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
            hcd.actionPlaylist.actionPlaylist = [];

            var missionCount = SlotData.missionReqs.Count;
            var levelsNorm = SlotData.levels.Count / missionCount;
            var offset = missionCount - (SlotData.levels.Count % missionCount);

            var lTotal = 0;
            for (int m = 0; m < missionCount; ++m)
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

                for (int l = 0; l < levelCount; ++l)
                {
                    var level = SlotData.levels[lTotal++];
                    Logic.Level(level).ranks = mission.medalsRequired;
                    mission.levels.Add(level);
                }

                campaign.missionData.Add(mission);
            }
        }

        internal static void HandleSaveCData(bool save = false)
        {
            // once again making sure
            GameDataManager.saveData.currentCampaign = CAMPAIGN_ID;

            var cstats = GameDataManager.campaignStats[CAMPAIGN_ID];

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
    }
}
