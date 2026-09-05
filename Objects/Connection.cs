using System.Net;
using System.Net.WebSockets;
using System.Reflection.Emit;
using System.Text;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Converters;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using HarmonyLib;
using NeonLite.Modules;
using Newtonsoft.Json;
using NWArchipelago.Modules;
using UnityEngine;

namespace NWArchipelago.Objects
{
    internal class Awaiter : MonoBehaviour
    {
        internal static readonly List<Awaiter> instances = [];
        internal static Awaiter i; // current instance

        internal string hostname;
        internal Uri uri;

        ClientWebSocket ws = null;
        readonly CancellationTokenSource canceller = new();

        readonly SemaphoreSlim sendSemaphore = new(1, 1);
        readonly List<ArchipelagoPacketBase> sendQueue = [];
        internal TaskCompletionSource<object> sendComplete = new();

        async Task<Uri> ConnectFlexUri(Uri uri)
        {
            if (uri.Scheme != "unspecified")
            {
                try
                {
                    await ws.ConnectAsync(uri, CancellationToken.None);
                    return uri;
                }
                catch (Exception e)
                {
                    Wrapper.i.OnError(e);
                    throw;
                }
            }

            Uri UriSchema(string schema)
            {
                return new UriBuilder(uri)
                {
                    Scheme = schema
                }.Uri;
            }

            List<Exception> errors = [];
            try
            {
                await ws.ConnectAsync(UriSchema("wss"), CancellationToken.None);
                if (ws.State == WebSocketState.Open)
                    return UriSchema("wss");
            }
            catch (Exception item)
            {
                errors.Add(item);
                ws = new ClientWebSocket();
            }

            try
            {
                await ws.ConnectAsync(UriSchema("ws"), CancellationToken.None);
                return UriSchema("ws");
            }
            catch (Exception item2)
            {
                errors.Add(item2);
                Wrapper.i.OnError(new AggregateException(errors));
                throw;
            }
        }

        internal async Task Connect()
        {
            string text = hostname;
            if (!text.StartsWith("ws://") && !text.StartsWith("wss://"))
                text = "unspecified://" + text;

            NWArchipelago.Log.DebugMsg($"tryconnect {text}");

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Uri is invalid.");

            const int RETRY_MAX = 3;
            int retries = 0;

            Exception lastError = null;

            while (!enabled && ++retries <= RETRY_MAX)
            {
                try
                {
                    ws = new ClientWebSocket();
                    this.uri = await ConnectFlexUri(uri);
                    enabled = true;
                    Wrapper.i.OnOpen();
                    NWArchipelago.Log.DebugMsg($"connected to {this.uri}");
                }
                catch (Exception e)
                {
                    NWArchipelago.Log.Error($"Error connecting to Archipelago (Retry {retries}/{RETRY_MAX}):");
                    NWArchipelago.Log.Error(e);
                    lastError = e;
                    await Task.Delay(1000);
                }
            }

            if (!enabled)
                throw lastError;

            return;
        }

        static readonly ArchipelagoPacketConverter apConv = new();
        static Array jsonConvArr = null;

        const int BUFFER_LENGTH = 2048;

        void Awake()
        {
            i = this;
            instances.Add(this);
        }

        async void Start()
        {
            var sender = Task.Run(Sender);

            ArraySegment<byte> buffer = new(new byte[BUFFER_LENGTH]);
            using var ms = new MemoryStream(BUFFER_LENGTH);
            using var reader = new StreamReader(ms, Encoding.UTF8);

            while (ws.State == WebSocketState.Open && !canceller.IsCancellationRequested)
            {
                ms.SetLength(0);

                WebSocketReceiveResult result = null;

                do
                {
                    try
                    {
                        result = await ws.ReceiveAsync(buffer, canceller.Token);
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                    }
                    catch
                    {
                        break;
                    }
                }
                while (!result.EndOfMessage && !canceller.IsCancellationRequested);

                if (result == null || result.MessageType == WebSocketMessageType.Close)
                {
                    // figure out close stuff
                    break;
                }


                if (canceller.IsCancellationRequested || ws.State != WebSocketState.Open)
                    break;

                reader.BaseStream.Position = 0;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = reader.ReadToEnd();

                    NWArchipelago.Log.DebugMsg($"RAWDATA {message}");

                    try
                    {
                        if (jsonConvArr == null)
                        {
                            jsonConvArr = Array.CreateInstance(APManage.JSONWrap.ConverterType, 1);
                            jsonConvArr.SetValue(apConv, 0);
                        }
                        var list = APManage.JSONWrap.Deserialize<List<ArchipelagoPacketBase>>(message, jsonConvArr);

                        foreach (ArchipelagoPacketBase item in list)
                        {
                            NWArchipelago.Log.DebugMsg($"RECEIVED {item.PacketType} {JsonConvert.SerializeObject(item)}");
                            Wrapper.i.OnPacket(item);
                        }
                    }
                    catch (Exception e)
                    {
                        NWArchipelago.Log.Error($"Error parsing packet:");
                        NWArchipelago.Log.Error(e);
                        NWArchipelago.Log.Warning($"Recieved:\n{message}");

                        Wrapper.i.OnError(e);
                    }
                }
            }

            NWArchipelago.Log.Warning($"Closing... socket state: {ws.State}");
            Cancel();
            await sender;
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
                catch (Exception e)
                {
                    NWArchipelago.Log.Warning($"Error while closing, continuing as normal:");
                    NWArchipelago.Log.Warning(e);
                }
            }
            Wrapper.i.OnClose();

            // AP specific code lets ditch this joint
            if (APManage.ConnectionStatus == APManage.ConnectStatus.Connected) {
                // kick us into the title screen if we aren't already
                APManage.SetConnectStatus(APManage.ConnectStatus.Idle);
            }

            Destroy(this);
        }

        void OnDestroy() => instances.Remove(this);

        async Task Sender()
        {
            while (ws.State == WebSocketState.Open && !canceller.IsCancellationRequested)
            {
                await sendSemaphore.WaitAsync();
                if (sendQueue.Count > 0)
                {
                    var j = APManage.JSONWrap.Serialize(sendQueue.ToArray());
                    NWArchipelago.Log.DebugMsg($"SENDING {j}");
                    var encoded = Encoding.UTF8.GetBytes(j);
                    var buffer = new ArraySegment<byte>(encoded, 0, encoded.Length);

                    try
                    {
                        await ws.SendAsync(buffer, WebSocketMessageType.Text, true, canceller.Token);
                        sendComplete.TrySetResult(null);
                    }
                    catch (Exception e)
                    {
                        NWArchipelago.Log.DebugMsg($"FAILED TO SEND");
                        sendComplete.TrySetException(e);
                    }
                    Wrapper.i.OnPacketsSend([.. sendQueue]);
                    sendQueue.Clear();
                }
                sendSemaphore.Release();
            }

            NWArchipelago.Log.DebugMsg($"Sender task died");
        }

        internal static Task Send(IEnumerable<ArchipelagoPacketBase> packets)
        {

            if (!i)
                return Task.CompletedTask;
            if (i.sendComplete.Task.IsCompleted)
                i.sendComplete = new();

            i.sendSemaphore.Wait();
            i.sendQueue.AddRange(packets.Where(x => x != null));
            i.sendSemaphore.Release();
            return i.sendComplete.Task;
        }

        internal static Task Send(ArchipelagoPacketBase packet) => Send([packet]);

        internal void Cancel()
        {
            if (!enabled)
                Destroy(this);
            NWArchipelago.Log.DebugMsg($"Cancel called");
            if (!canceller.IsCancellationRequested)
                canceller.Cancel();
        }
    }

    internal class Wrapper : IArchipelagoSocketHelper
    {
#pragma warning disable CS0067
        internal static Wrapper i;

        public Wrapper(Uri _) => i = this;

        internal static async Task StartSession()
        {
            NWArchipelago.Log.DebugMsg("starting session");
            APManage.session = ArchipelagoSessionFactory.CreateSession(null);

            NWArchipelago.Log.DebugMsg("connect async");

            APManage.SetConnectStatus(APManage.ConnectStatus.Connecting);

            Cards.Clear();
            Campaign.unlockedLevels.Clear();
            APManage.itemIndex = 0;

            try
            {
                await APManage.PrepareItemChecks();
                var info = await APManage.session.ConnectAsync().ConfigureAwait(false);

                NWArchipelago.Log.DebugMsg($"info {info.Password} {info.SeedName}");

                var pass = info.Password ? Settings.password.Value : null;

                var login = await APManage.session.LoginAsync(Settings.gameoverride.Value,
                    Settings.slotname.Value, ItemsHandlingFlags.AllItems, password: pass,
                    tags: ["DeathLink"], version: new Version(0, 6, 6)).ConfigureAwait(false);

                if (login is LoginSuccessful win)
                {
                    NWArchipelago.Log.Msg($"Logged into Archipelago!");
                    await APManage.OnConnect(info, win);
                }
                else if (login is LoginFailure fail)
                {
                    NWArchipelago.Log.Warning($"Failed to login to Archipelago: {fail.ErrorCodes})");

                    APManage.SetConnectStatus(APManage.ConnectStatus.Failed);
                }
            }
            catch (Exception e)
            {
                NWArchipelago.Log.Warning($"Failed to login to Archipelago: {e}");

                APManage.SetConnectStatus(APManage.ConnectStatus.Failed);
            }
        }

        public Uri Uri => Awaiter.i.uri;
        public bool Connected => Awaiter.i?.enabled ?? false;

        public event ArchipelagoSocketHelperDelagates.PacketReceivedHandler PacketReceived;
        public event ArchipelagoSocketHelperDelagates.PacketsSentHandler PacketsSent;
        public event ArchipelagoSocketHelperDelagates.ErrorReceivedHandler ErrorReceived;
        public event ArchipelagoSocketHelperDelagates.SocketClosedHandler SocketClosed;
        public event ArchipelagoSocketHelperDelagates.SocketOpenedHandler SocketOpened;

        public Task ConnectAsync() => Awaiter.i.Connect();
        public Task DisconnectAsync()
        {
            NWArchipelago.Log.DebugMsg("Wrapper DisconnectAsync");
            Awaiter.i.Cancel();
            return Task.CompletedTask;
        }

        public void SendMultiplePackets(List<ArchipelagoPacketBase> packets) => Awaiter.Send(packets);
        public void SendMultiplePackets(params ArchipelagoPacketBase[] packets) => Awaiter.Send(packets);

        public Task SendMultiplePacketsAsync(List<ArchipelagoPacketBase> packets) => Awaiter.Send(packets);
        public Task SendMultiplePacketsAsync(params ArchipelagoPacketBase[] packets) => Awaiter.Send(packets);

        public void SendPacket(ArchipelagoPacketBase packet) => Awaiter.Send(packet);
        public Task SendPacketAsync(ArchipelagoPacketBase packet) => Awaiter.Send(packet);

        internal void OnError(Exception e) => ErrorReceived?.Invoke(e, e.Message);
        internal void OnPacket(ArchipelagoPacketBase packet) => PacketReceived?.Invoke(packet);
        internal void OnOpen() => SocketOpened?.Invoke();
        internal void OnClose(string reason = "") => SocketClosed?.Invoke(reason);
        internal void OnPacketsSend(ArchipelagoPacketBase[] packets) => PacketsSent?.Invoke(packets);
    }

    [Module]
    internal class Scheduler : MonoBehaviour
    {
        internal static Scheduler i;

        const bool active = true;
        const bool priority = true;

        static string host;

        static void Setup()
        {
            Settings.ip.OnEntryValueChanged.Subscribe((_, after) => host = $"{after}:{Settings.port.Value}");
            Settings.port.OnEntryValueChanged.Subscribe((_, after) => host = $"{Settings.ip.Value}:{after}");
            host = $"{Settings.ip.Value}:{Settings.port.Value}";
        }


        static void Activate(bool _)
        {
            Patching.AddPatch(Helpers.Method(typeof(ArchipelagoSessionFactory), "CreateSession", [typeof(Uri)]), ReplaceWithWrapper, Patching.PatchTarget.Transpiler);
        }

        static IEnumerable<CodeInstruction> ReplaceWithWrapper(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions)
                .MatchForward(false, new CodeMatch(x => x.opcode == OpCodes.Newobj))
                .SetInstruction(new CodeInstruction(OpCodes.Newobj, typeof(Wrapper).GetConstructors()[0]))
                .InstructionEnumeration();
        }

        void Awake()
        {
            i = this;
            i.enabled = active;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        void Update()
        {
            if (!Awaiter.instances.Any(x => x.hostname == host))
            {
                var a = gameObject.AddComponent<Awaiter>();
                a.hostname = host;
                a.enabled = false;
            }

            foreach (var a in Awaiter.instances)
            {
                if (a.hostname != host)
                    a.Cancel();
            }
        }
    }
}
