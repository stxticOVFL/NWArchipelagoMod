using MelonLoader;
using NWArchipelago.Modules;
using NWArchipelago.Objects;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using static NeonLite.Helpers;

namespace NWArchipelago
{
    public class NWArchipelago : MelonMod
    {
        internal static NWArchipelago i;

#if DEBUG
        internal static bool DEBUG { get { return Settings.debug.Value; } }
#else
        internal const bool DEBUG = false;
#endif
        internal static MelonLogger.Instance Log => i.LoggerInstance;

        internal static GameObject holder;

        internal static AssetBundle bundle;

        internal static SynchronizationContext mainContext;

        public override void OnInitializeMelon()
        {
            i = this;

            //bundle = AssetBundle.LoadFromMemory(Resources.r.assetbundle);
            Settings.Register();

            if (!Settings.enabled.Value)
            {
                // manually call setup on the modules to register their settings
                // SaveHandler.Setup();
                // Cards.Setup();
                return;
            }
            else
            {
                NeonLite.NeonLite.LoadModules(MelonAssembly);
                NeonLite.Modules.Anticheat.Register(MelonAssembly);
                var abload = AssetBundle.LoadFromMemoryAsync(Resources.r.bundle);
                abload.completed += _ =>
                {
                    bundle = abload.assetBundle;
                };
            }
        }

        public override void OnLateInitializeMelon()
        {
            if (!Settings.enabled.Value || Settings.testMode)
                return;

            mainContext = SynchronizationContext.Current;
            holder = new GameObject("NWArchipelago", typeof(Scheduler));
            UnityEngine.Object.DontDestroyOnLoad(holder);

            MelonCoroutines.Start(SaveHandler.SaveCoro());
        }

        internal static void CheckAnticheat()
        {
            // in most cases this is mod is entirely legal!
            // however we do have a miracle katana gimmick now and potentially other stuff
            // if we have miracle access we have to enable anticheat
            // TODO: save redirection even with anticheat enabled

            bool cheating = Miracle.miracleCount >= 0;

            if (cheating)
                NeonLite.Modules.Anticheat.Register(i.MelonAssembly);
            else
                NeonLite.Modules.Anticheat.Unregister(i.MelonAssembly);
        }

        internal static void CoroTask(Task task)
        {
            IEnumerator Coro()
            {
                yield return new WaitUntil(() => task.IsCompleted);
                if (task.Exception != null)
                {
                    Log.Warning($"Error running async task:");
                    Log.Error(task.Exception.GetBaseException());
                }
            }

            MelonCoroutines.Start(Coro());
        }


    }

    public static class Settings
    {
        public const string h = "Archipelago";
        internal static MelonPreferences_Entry<bool> debug;

        internal static MelonPreferences_Entry<bool> enabled;

        internal static MelonPreferences_Entry<string> ip;
        internal static MelonPreferences_Entry<int> port;
        internal static MelonPreferences_Entry<string> slotname;
        internal static MelonPreferences_Entry<string> password;

        internal static MelonPreferences_Entry<string> gameoverride;


        internal static MelonPreferences_Entry<bool> testing;
        internal static bool testMode;


        public static void Register()
        {
            NeonLite.Settings.AddHolder(h);
#if DEBUG
            debug = NeonLite.Settings.Add(h, "", "debug", "Debug Mode", null, false, true);
#endif

            enabled = NeonLite.Settings.Add(h, "", "enabled", "Enabled", "Requires restart!", false);

            ip = NeonLite.Settings.Add(h, "Connection", "ip", "Server IP/Address", null, "archipelago.gg");
            port = NeonLite.Settings.Add(h, "Connection", "port", "Server Port", null, 38281);
            slotname = NeonLite.Settings.Add(h, "Connection", "slotName", "Player/Slot Name", null, "NeonWhite");
            password = NeonLite.Settings.Add(h, "Connection", "password", "Password", "The password for the Archipelago. Be careful about showing this!", "");

            gameoverride = NeonLite.Settings.Add(h, "", "gameoverride", "Game Override", "FOR TESTING PURPOSES ONLY! Do not modify!", "Neon White", true);

            testing = NeonLite.Settings.Add(h, "Testing", "testing", "Testing Mode", "Requires restart.", false, true);
            testMode = testing.Value;
        }
    }

    public static class Extensions
    {
#pragma warning disable CS0162
        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugMsg(this MelonLogger.Instance log, string msg, [CallerFilePath] string fp = "", [CallerLineNumber] int ln = 0)
        {
            if (NWArchipelago.DEBUG)
            {
                // log.Msg($"{FpLn(fp, ln)} {msg}");
                UnityEngine.Debug.Log($"[NWArchipelago] {FpLn(fp, ln)} {msg}");
            }

        }

        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugMsg(this MelonLogger.Instance log, object obj, [CallerFilePath] string fp = "", [CallerLineNumber] int ln = 0)
            => DebugMsg(log, obj.ToString(), fp, ln);
    }
}
