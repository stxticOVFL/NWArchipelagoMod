using System.IO.Compression;
using System.Net.WebSockets;
using System.Reflection;
using System.Reflection.Emit;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using HarmonyLib;
using I2.Loc;
using MelonLoader.TinyJSON;
using NeonLite.Modules;
using NWArchipelago.Objects;
using UnityEngine;
using UnityEngine.Networking;
using static System.Text.Encoding;
using static NeonLite.Modules.CommunityMedals;

namespace NWArchipelago.Modules
{
    [Module]
    internal static class APManage
    {
        const bool priority = true;
        static bool active = true;

        internal static class JSONWrap
        {
            public static MethodInfo DeserializeM;
            public static MethodInfo SerializeM;
            public static Type ConverterType;

            public static T Deserialize<T>(string obj, object converters = null)
                => (T)DeserializeM.MakeGenericMethod(typeof(T)).Invoke(null, [obj, converters]);

            public static string Serialize(object obj)
                => (string)SerializeM.Invoke(null, [obj]);
        }

        static void Setup() => active = !Settings.testMode;

        static void Activate(bool _)
        {
            Patching.AddPatch(typeof(BaseArchipelagoSocketHelper<ClientWebSocket>), "OnMessageReceived", FetchTypes, Patching.PatchTarget.Transpiler);
            JSONWrap.SerializeM = typeof(ArchipelagoSession).Assembly
                                .GetType("Newtonsoft.Json.JsonConvert")
                                .GetMethod("SerializeObject", AccessTools.all, null, [typeof(object)], null);

            Patching.AddPatch(typeof(Game), "OnLevelWin", OnWin, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(MainMenu), "OnCompleteSidequest", DontSidequest, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(LevelStats), "SetCollectibleFound", OnCollectible, Patching.PatchTarget.Prefix);
        }

        static IEnumerable<CodeInstruction> FetchTypes(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions)
                .MatchForward(false, new CodeMatch(x => x.opcode == OpCodes.Newarr))
                .Do(x => JSONWrap.ConverterType = (Type)x.Operand)
                .MatchForward(false, new CodeMatch(x => x.opcode == OpCodes.Call))
                .Do(x => JSONWrap.DeserializeM = ((MethodInfo)x.Operand).GetGenericMethodDefinition())
                .InstructionEnumeration();
        }

        static void OnLevelLoad(LevelData level)
        {
            if (ConnectionStatus != ConnectStatus.Connected)
                return;

            if (!level || level.type == LevelData.LevelType.Hub)
                session.SetClientState(ArchipelagoClientState.ClientReady);

            session.SetClientState(ArchipelagoClientState.ClientPlaying);
        }

        internal static ArchipelagoSession session;
        internal static bool deathlink;

        internal enum UnlockMethod
        {
            Ranks = 1,
            Missions,
            Levels
        }

        internal enum Goal
        {
            AllBosses = 1,
            TrueEnding,
        }

        internal static class SlotData
        {
            public static List<LevelData> levels;

            public static List<int> missionReqs;
            public static int neonRanks;
            public static UnlockMethod unlockMethod;

            public static HashSet<MedalEnum> medals;
            public static bool gifts = true;
            public static bool sidequests = true;

            public static int knowledge;
            public static int execution;

            public static Goal winCondition;
            public static MedalEnum bossesCap;
        }

        internal enum ConnectStatus
        {
            Failed = -2,
            Idle,
            Connecting,
            Connected,
        }
        internal static ConnectStatus ConnectionStatus { get; private set; } = ConnectStatus.Idle;
        internal static event Action<ConnectStatus> OnStatusChanged;
        internal static void SetConnectStatus(ConnectStatus status)
        {
            ConnectionStatus = status;
            NWArchipelago.mainContext.Send(s => OnStatusChanged?.Invoke((ConnectStatus)s), status);
        }

        internal static int itemIndex = 0;
        internal static bool doCampaignCheck = true;
        internal static void ItemRecieved(ReceivedItemsHelper helper)
        {
            while (helper.PeekItem() != null)
            {
                var item = helper.DequeueItem();

                if (helper.Index <= itemIndex)
                    return;
                itemIndex++;

                ParseItem(item);
            }

            if (doCampaignCheck)
                Campaign.HandleSaveCData(true);
        }

        internal static void ParseItem(ItemInfo item)
        {
            var type = item.ItemId / 100;

            switch (type)
            {
                case 5: // cards
                    if (!Cards.ParseCard(item.ItemName))
                        NWArchipelago.Log.Warning($"Unknown card-like item {item.ItemName}");
                    return;
                case 4: // progression
                    SaveHandler.archiSaveData.neonRank++;
                    return;
                case 6: // levels
                case 7:
                    Campaign.unlockedLevels.Add((int)(item.ItemId - 600));
                    return;
                case 8: // misc
                    return;
            }
        }

        internal static void SendLevelComplete(LevelData level, long time)
        {
            if (level.isSidequest)
            {
                SendLevelLocationFormatted(level, "{0} Completion");
                return;
            }

            var lname = LocalizationManager.GetTranslation(level.GetLevelDisplayName(), overrideLanguage: "English");
            const string format = "{0} {1} Completion";
            List<string> locs = new((int)MedalEnum.Dev);

            int medal = GetMedalIndex(level.levelID, time);

            for (int i = 0; i <= medal && i <= (int)MedalEnum.Dev; ++i) {
                if (SlotData.medals.Contains((MedalEnum)i))
                    locs.Add(string.Format(format, lname, (MedalEnum)i));
            }

            var ids = locs
                .Select(x => session.Locations.GetLocationIdFromName(session.ConnectionInfo.Game, x))
                .Where(x => x != -1)
                .Where(session.Locations.AllMissingLocations.Contains);

            NWArchipelago.CoroTask(session.Locations.CompleteLocationChecksAsync([.. ids]));
            CheckWinCon();
        }
        internal static void SendGiftComplete(LevelData level) => SendLevelLocationFormatted(level, "{0} Gift");

        internal static void SendLevelLocationFormatted(LevelData level, string format, params object[] form)
        {
            var lname = LocalizationManager.GetTranslation(level.GetLevelDisplayName(), overrideLanguage: "English");
            var locname = string.Format(format, lname, form);
            SendLocation(locname);
        }

        static void SendLocation(params string[] locations)
        {
            if (ConnectionStatus != ConnectStatus.Connected)
                return;

            var iter = locations.Select(x => (x, session.Locations.GetLocationIdFromName(session.ConnectionInfo.Game, x)));

            foreach ((var loc, var id) in iter)
            {
                if (id == -1)
                    continue;

                if (session.Locations.AllLocationsChecked.Contains(id))
                    continue;

                NWArchipelago.CoroTask(session.Locations.CompleteLocationChecksAsync(id));
            }
            CheckWinCon();
        }

        static readonly string[] BOSSES = ["GRID_BOSS_YELLOW", "GRID_BOSS_GODSDEATHTEMPLE", "GRID_BOSS_RAPTURE"];
        static void CheckWinCon()
        {
            // var gd = Singleton<Game>.Instance.GetGameData();

            switch (SlotData.winCondition)
            {
                case Goal.AllBosses:
                    {
                        bool all = true;
                        foreach (var l in BOSSES)
                        {
                            if (CommunityMedals.GetMedalIndex(l) < (int)SlotData.bossesCap)
                                all = false;
                        }
                        if (all)
                            session.SetGoalAchieved(); // WE Did it
                        break;
                    }
            }
        }

        internal static void OnWin(Game __instance)
        {
            if (ConnectionStatus != ConnectStatus.Connected)
                return;

            SendLevelComplete(__instance.GetCurrentLevel(), __instance.GetCurrentLevelTimerMicroseconds());
        }

        static bool DontSidequest() => false;

        /// <summary>
        /// modded version of https://github.com/coding-horror/ascii85/
        /// </summary>
        /// <remarks>
        /// Jeff Atwood
        /// http://www.codinghorror.com/blog/archives/000410.html
        /// </remarks>
        class Ascii85
        {
            private const int _asciiOffset = 33;
            private readonly byte[] _encodedBlock = new byte[5];
            private readonly byte[] _decodedBlock = new byte[4];
            private uint _tuple = 0;

            private readonly uint[] pow85 = [85 * 85 * 85 * 85, 85 * 85 * 85, 85 * 85, 85, 1];

            /// <summary>
            /// Decodes an ASCII85 encoded string into the original binary data
            /// </summary>
            /// <param name="s">ASCII85 encoded string</param>
            /// <returns>byte array of decoded binary data</returns>
            public MemoryStream Decode(string s)
            {
                MemoryStream ms = new();
                int count = 0;
                bool processChar;

                foreach (char c in s)
                {
                    switch (c)
                    {
                        case 'z':
                            if (count != 0)
                                throw new Exception("The character 'z' is invalid inside an ASCII85 block.");
                            _decodedBlock[0] = 0;
                            _decodedBlock[1] = 0;
                            _decodedBlock[2] = 0;
                            _decodedBlock[3] = 0;
                            ms.Write(_decodedBlock, 0, _decodedBlock.Length);
                            processChar = false;
                            break;
                        case '\n':
                        case '\r':
                        case '\t':
                        case '\0':
                        case '\f':
                        case '\b':
                            processChar = false;
                            break;
                        default:
                            if (c < '!' || c > 'u')
                                throw new Exception("Bad character '" + c + "' found. ASCII85 only allows characters '!' to 'u'.");
                            processChar = true;
                            break;
                    }

                    if (processChar)
                    {
                        _tuple += (uint)(c - _asciiOffset) * pow85[count];
                        count++;
                        if (count == _encodedBlock.Length)
                        {
                            DecodeBlock();
                            ms.Write(_decodedBlock, 0, _decodedBlock.Length);
                            _tuple = 0;
                            count = 0;
                        }
                    }
                }

                // if we have some bytes left over at the end..
                if (count != 0)
                {
                    if (count == 1)
                        throw new Exception("The last block of ASCII85 data cannot be a single byte.");
                    count--;
                    _tuple += pow85[count];
                    DecodeBlock(count);
                    for (int i = 0; i < count; i++)
                    {
                        ms.WriteByte(_decodedBlock[i]);
                    }
                }

                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }

            private void DecodeBlock() => DecodeBlock(_decodedBlock.Length);

            private void DecodeBlock(int bytes)
            {
                for (int i = 0; i < bytes; i++)
                {
                    _decodedBlock[i] = (byte)(_tuple >> 24 - (i * 8));
                }
            }
        }


        static void EnableCampaignCheck(ArchipelagoPacketBase p)
            => doCampaignCheck = p is ReceivedItemsPacket;

        internal static async Task PrepareItemChecks()
        {
            doCampaignCheck = false;
            session.Items.ItemReceived -= ItemRecieved;
            session.Items.ItemReceived += ItemRecieved;

            session.Socket.PacketReceived += EnableCampaignCheck;
        }

        internal static async Task OnConnect(RoomInfoPacket info, LoginSuccessful login)
        {
            var variant = JSON.Load(JSONWrap.Serialize(login.SlotData)) as ProxyObject;
            var gd = Singleton<Game>.Instance.GetGameData();

            NWArchipelago.Log.DebugMsg("load slotdata");

            var decoded = new Ascii85().Decode(variant["level_order"] as ProxyString);
            using MemoryStream decompressed = new();
            using (DeflateStream deflate = new(decoded, CompressionMode.Decompress))
                deflate.CopyTo(decompressed);

            Variant list = JSON.Load(UTF8.GetString(decompressed.ToArray()));

            SlotData.levels = [.. (list as ProxyArray).Select(x => gd.GetLevelData(x))];

            SlotData.missionReqs = [.. (variant["mission_costs"] as ProxyArray).Select(x => (int)x)];
            SlotData.neonRanks = SlotData.missionReqs.Last();

            var options = variant["options"] as ProxyObject;
            if (options.Keys.Contains("gifts"))
                SlotData.gifts = options["gifts"];
            if (options.Keys.Contains("sidequests"))
                SlotData.sidequests = options["sidequests"];

            SlotData.unlockMethod = (UnlockMethod)(int)options["unlock_method"];
            SlotData.medals = [.. (options["medal_select"] as ProxyArray)
                .Select(x => (string)x)
                .Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1))
                .Select(x => (MedalEnum)Enum.Parse(typeof(MedalEnum), x))];

            SlotData.knowledge = (int)options["difficulty_knowledge"];
            SlotData.execution = (int)options["difficulty_execution"];

            SlotData.winCondition = (Goal)(int)options["goal"];
            if (SlotData.winCondition == Goal.AllBosses)
                SlotData.bossesCap = (MedalEnum)((int)options["bosses_goal_cap"] - 1);

            NWArchipelago.Log.DebugMsg("download/load logic");

            using (var req = UnityWebRequest.Get(Logic.URL))
            {
                var send = req.SendWebRequest();
                while (!send.isDone)
                    await Task.CompletedTask.ConfigureAwait(false);

                var load = req.result == UnityWebRequest.Result.Success;
                if (load)
                {
                    load = false;
                    try
                    {
                        var toLoad = JSON.Load(req.downloadHandler.text);
                        load = Logic.Load(toLoad);
                    }
                    catch { }
                }

                if (!load)
                {
                    NWArchipelago.Log.Warning("Could not load up to date logic. Loading the backup resource; this could be really outdated!");

                    var resource = Resources.logic.GetUTF8String();
                    load = false;
                    try
                    {
                        var toLoad = JSON.Load(resource);
                        load = Logic.Load(toLoad);
                    }
                    catch { }
                }

                if (!load)
                    NWArchipelago.Log.Error("Failed to load logic. There will not be any tracking present.");
                else
                    NWArchipelago.Log.Msg("Logic loaded!");
            }

            NWArchipelago.mainContext.Send(static info =>
            {
                NWArchipelago.Log.DebugMsg("build campaign");
                Campaign.MakeCampaign();

                NWArchipelago.Log.DebugMsg("save redir");
                Anticheat.EnableSaveRedirection(Path.Combine("Archipelago", ((RoomInfoPacket)info).SeedName, Settings.slotname.Value), true);
                SaveHandler.allowed = true;
                var currRank = SaveHandler.archiSaveData.neonRank;
                GameDataManager.LoadGame(null);

                NWArchipelago.Log.DebugMsg("set archidata");
                SaveHandler.archiSaveData = (GameDataManager.saveData as SaveHandler.ArchipelagoSave).apData;
                Menu.previousRank = SaveHandler.archiSaveData.neonRank;
                SaveHandler.archiSaveData.neonRank = currRank;

                Campaign.HandleSaveCData();
            }, info);

            NWArchipelago.Log.DebugMsg("items recieved checks");

            static async Task WaitForPacket()
            {
                while (!doCampaignCheck)
                    await Task.Delay(1);
            }

            using (CancellationTokenSource delayCancel = new())
            {
                var delay = Task.Delay(TimeSpan.FromSeconds(0.25), delayCancel.Token);
                await Task.WhenAny(WaitForPacket(), delay).ConfigureAwait(false);
                delayCancel.Cancel();
            }

            session.Socket.PacketReceived -= EnableCampaignCheck;
            if (!doCampaignCheck)
                doCampaignCheck = true;

            NWArchipelago.Log.DebugMsg("set status");
            SetConnectStatus(ConnectStatus.Connected);
            OnLevelLoad(LoadManager.currentLevel);

            NWArchipelago.Log.DebugMsg("check current levels");
            foreach (var level in SlotData.levels)
            {
                var lstats = GameDataManager.GetLevelStats(level.levelID);
                if (lstats == null)
                    continue;

                Campaign.checkComplete = true;
                if (lstats.GetCompleted())
                    SendLevelComplete(level, lstats._timeBestMicroseconds);
                if (lstats.HasCollectibleBeenFound())
                    SendGiftComplete(level);
            }

            NWArchipelago.Log.DebugMsg("done!");

            Campaign.HandleSaveCData(true);
        }

        internal static void OnCollectible(LevelStats __instance)
        {
            var levelID = GameDataManager.levelStats.Where(kv => kv.Value == __instance).Select(kv => kv.Key).FirstOrDefault();
            var level = Singleton<Game>.Instance.GetGameData().GetLevelData(levelID);
            if (!level)
                return;

            SendGiftComplete(level);
        }
    }
}
