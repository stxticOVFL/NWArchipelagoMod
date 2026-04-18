using NeonLite.Modules;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace NWArchipelago.Modules
{
    [Module]
    internal static class SaveHandler
    {
        const bool priority = true;
        static bool active = true;

        internal static bool allowed = false;

        static readonly MethodInfo parseSave = Helpers.Method(typeof(GameDataManager), "OnReadPlayerSaveDataComplete");

        static void Setup() => active = !Settings.testMode;

        static void Activate(bool _)
        {
            Patching.AddPatch(parseSave, ReadSaveDataAP, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(GameDataManager), "DeserializePlayerSaveData", DeserializeAP, Patching.PatchTarget.Prefix);

            Patching.AddPatch(Helpers.Method(typeof(GameData), "GetGoldMedals", []), GetNeonRank, Patching.PatchTarget.Prefix);
            Patching.AddPatch(Helpers.Method(typeof(GameData), "GetGoldMedals", [typeof(string)]), GetNeonRank, Patching.PatchTarget.Prefix);
            Patching.AddPatch(Helpers.Method(typeof(GameData), "GetNeonRank", []), GetNeonRank, Patching.PatchTarget.Prefix);
            Patching.AddPatch(Helpers.Method(typeof(GameData), "GetNeonRank", [typeof(int)]), GetNeonRank, Patching.PatchTarget.Prefix);
            Patching.AddPatch(Helpers.Method(typeof(GameData), "GetNeonRankForDisplay", []), GetNeonRank, Patching.PatchTarget.Prefix);
            Patching.AddPatch(Helpers.Method(typeof(GameData), "GetNeonRankForDisplay", [typeof(int)]), GetNeonRank, Patching.PatchTarget.Prefix);
        }

        static bool DeserializeAP(string result, Action callback)
        {
            ArchipelagoSave save = null;
            try
            {
                if (!string.IsNullOrEmpty(result) && allowed)
                {
                    save = JsonConvert.DeserializeObject<ArchipelagoSave>(result);
                    save.campaignStats.Load(GameDataManager.campaignStats);
                    save.missionStats.Load(GameDataManager.missionStats);
                    save.levelStats.Load(GameDataManager.levelStats);
                    save.cardShowcase.Load(GameDataManager.cardShowcase);
                    save.hubVariables.Load(GameDataManager.hubVariables);
                    save.relationships.Load(GameDataManager.relationships);
                    Singleton<Game>.Instance.GetGameData();
                }
            }
            catch
            {
                save = null;
            }
            parseSave.Invoke(null, [save, callback]);
            return false;
        }

        static bool ReadSaveDataAP(ref PlayerSaveData data, Action callback)
        {
            if (data != null)
            {
                data.currentCampaign = Campaign.CAMPAIGN_ID;
                return true;
            }

            data = new ArchipelagoSave();
            GameDataManager.campaignStats = [];
            GameDataManager.levelStats = [];
            GameDataManager.missionStats = [];
            GameDataManager.cardShowcase = [];
            GameDataManager.hubVariables = [];
            GameDataManager.relationships = new Dictionary<string, RelationshipStats>
            {
                { "RED", new RelationshipStats("RED") },
                { "YELLOW", new RelationshipStats("YELLOW") },
                { "VIOLET", new RelationshipStats("VIOLET") },
                { "RAZ", new RelationshipStats("RAZ") },
                { "MIKEY", new RelationshipStats("MIKEY") },
                { "GREEN", new RelationshipStats("GREEN") }
            };
            GameDataManager.collectibleStats = [];

            return true;
        }

        [Serializable]
        internal class ArchipelagoSave : PlayerSaveData
        {
            [Serializable]
            internal class ArchipelagoData
            {
                public int neonRank;
            }

            public ArchipelagoData apData = new();
        }

        internal static ArchipelagoSave.ArchipelagoData archiSaveData;

        static bool GetNeonRank(ref int __result)
        {
            if (!allowed || archiSaveData == null)
                return true;
            __result = archiSaveData.neonRank;
            return false;
        }
    }
}
