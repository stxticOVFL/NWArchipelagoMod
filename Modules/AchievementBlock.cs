using NeonLite.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NWArchipelago.Modules
{
    [Module]
    internal static class AchievementBlock
    {
        const bool priority = false;
        static readonly string[] PREVENT = [
            "IncrementCollectibles",
            "SyncFistsAchievement",
            "SyncAmmoDeathAchievement",
            "SyncMissionCompleteAchievements",
            "SyncCharacterFirstMemory",
            "SyncMemoriesAchievements",
            "SyncCharacterQuests",
            "SyncGetCoinAchievement",
            "SyncFirstGift"
        ];

        internal static void Activate(bool _)
        {
            foreach (var p in PREVENT)
                Patching.AddPatch(typeof(Achievements), p, Prevent, Patching.PatchTarget.Prefix);
        }
        static bool Prevent() => false;
    }
}
