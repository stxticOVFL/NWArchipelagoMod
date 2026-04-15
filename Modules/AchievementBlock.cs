using NeonLite;
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
        const bool active = true;

        internal static void Activate(bool _)
        {
            Patching.AddPatch(typeof(Achievements), "SyncFistsAchievement", Prevent, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(Achievements), "SyncAmmoDeathAchievement", Prevent, Patching.PatchTarget.Prefix);
        }
        static bool Prevent() => false;
    }
}
