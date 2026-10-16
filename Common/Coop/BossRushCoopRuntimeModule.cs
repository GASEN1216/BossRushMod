using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed class BossRushCoopRuntimeModule : BossRushRuntimeModuleBase
    {
        private const int MaxPacketBytes = BossRushCoopPacketFrames.MaxMessageBytes;
        private readonly BossRushCoopPacketFrames packetFrames = new BossRushCoopPacketFrames();
        private readonly List<BossRushCoopPlayer> players = new List<BossRushCoopPlayer>();
        private readonly Dictionary<string, List<Action<BossRushCoopMessage>>> handlers = new Dictionary<string, List<Action<BossRushCoopMessage>>>(StringComparer.Ordinal);
        private readonly Dictionary<int, CharacterMainControl> aiCharacters = new Dictionary<int, CharacterMainControl>();
        private readonly BossRushCoopReplayWindow replay = new BossRushCoopReplayWindow();
        private readonly BossRushCoopSceneBarrier barrier = new BossRushCoopSceneBarrier();
        private readonly BossRushCoopAssetIdentity assetIdentities = new BossRushCoopAssetIdentity();
        private string localAssetId;
        private long localAssetRevision;
        private string localReadyEpoch;
        private object welcomeHostPeer;
        private object helloHostPeer;
        private long minimumWelcomeSequence;
        private long readyRevision;
        private readonly Dictionary<string, long> appliedReadyRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
        private bool assetEventsSubscribed;
        private readonly HashSet<string> localReadyKeys = new HashSet<string>(StringComparer.Ordinal);
        private BossRushCoopTransport transport;
        private ModBehaviour owner;
        private Task<string> manifestTask;
        private CancellationTokenSource manifestCancellation;
        private string manifest;
        private string reconnectToken;
        private string officialSceneId;
        private int localSceneHandle = int.MinValue;
        private int generationSceneHandle = int.MinValue;
        private bool generationScenePending;
        private int pendingGeneration;
        private string generationSceneToken;
        private float nextProbe;
        private float nextHello;
        private float nextRoster;
        private float nextRefresh;
        private bool previousHost;
        private long rosterRevision;
        private long helloSequence;
        private long appliedWelcomeSequence;
        private bool roomReady;
        private bool manifestFailed;
        private string lastError;
        private string lastCompatibilityStatus;
        private readonly Dictionary<object, float> peerFirstSeen = new Dictionary<object, float>();
        private readonly HashSet<object> incompatiblePeers = new HashSet<object>();
        private float nextCompatibilityStatus;
        private BossRushCoopWorldLoot worldLoot;
        private BossRushCoopControl control;
        internal bool Connected { get; private set; }
        internal bool NetworkBlocked { get; private set; }
        internal bool Host { get; private set; }
        internal string LocalId { get; private set; } = "local";
        internal string SessionId { get; private set; } = string.Empty;
        internal int Generation { get; private set; }
        internal IReadOnlyList<BossRushCoopPlayer> Players { get { return players; } }
        internal string SceneToken { get { return transport != null ? transport.SceneIdentity.LocalToken : null; } }
        internal string LocalReadyEpoch { get { return localReadyEpoch; } }
        internal object NativeService { get { return transport != null ? transport.Service : null; } }
        internal object NativeHostPeer { get { return transport != null ? transport.HostPeer : null; } }
        internal bool HasHostPeer { get { return Connected && !Host && transport != null && transport.IsPeerConnected(transport.HostPeer); } }
        internal bool HasBoundScene { get { return !NetworkBlocked && Generation > 0 && !generationScenePending && localSceneHandle == generationSceneHandle && HasCurrentSceneCore()
            && transport != null && transport.SceneIdentity.Matches(generationSceneToken); } }
        internal bool Ready
        {
            get
            {
                if (NetworkBlocked || !Connected || manifest == null || string.IsNullOrEmpty(SessionId)) return false;
                if (!Host) return LocalId != "local" && roomReady && !string.IsNullOrEmpty(localReadyEpoch)
                    && ReferenceEquals(welcomeHostPeer, transport.HostPeer) && transport.IsPeerConnected(transport.HostPeer);
                int accepted = 0;
                foreach (var player in players) if (player.IsConnected && !player.IsLocal && !incompatiblePeers.Contains(player.Peer)) accepted++;
                return accepted == transport.PeerCount;
            }
        }
        public override string ModuleName { get { return "Coop"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            BossRushCoop.Runtime = this;
            if (!assetEventsSubscribed)
            {
                Saves.SavesSystem.OnSetFile += OnSlotSelected;
                Saves.SavesSystem.OnSaveDeleted += OnSlotDeleted;
                assetEventsSubscribed = true;
            }
            GetLocalAssetId();
            transport = new BossRushCoopTransport(OnPacket, OnPeerDisconnected, OnNativePlayerSpawned, OnAiSpawned);
            worldLoot = new BossRushCoopWorldLoot(transport);
            control = new BossRushCoopControl();
            OnUpdate(0f, 0f);
        }

        public override void OnStart() { OnUpdate(0f, 0f); }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            float now = Time.unscaledTime;
            bool started, host;
            bool known = transport.TryGetNetworkState(out started, out host);
            if (!transport.Bound && now >= nextProbe)
            {
                nextProbe = now + 1f;
                transport.TryBind();
            }
            NetworkBlocked = started && (!known || !transport.Bound);
            if (NetworkBlocked)
            {
                ShowError(L10n.T("已检测到 COOP 联网，但 BossRush 联机适配不可用。已停止本端刷怪和发奖，请检查 COOP 与 BossRush 文件版本。",
                    "COOP is online, but the BossRush adapter is unavailable. Local spawning and rewards are blocked. Check the COOP and BossRush files."));
                return;
            }
            if (started != Connected || (started && host != previousHost))
            {
                // 原生 Begin 可先于本帧客端更新；welcome 随后只保留与新房间完全匹配的加载记录。
                ResetSession(null, started && !host);
                Connected = started; Host = host; previousHost = host;
                if (started)
                {
                    if (Host)
                    {
                        SessionId = Guid.NewGuid().ToString("N"); LocalId = "host";
                        localReadyEpoch = Guid.NewGuid().ToString("N");
                        players.Add(new BossRushCoopPlayer { Id = LocalId, AssetId = GetLocalAssetId(), AssetRevision = localAssetRevision, ReadyEpoch = localReadyEpoch, IsLocal = true, IsConnected = true, NativeId = transport.SelfId(), Character = CharacterMainControl.Main });
                    }
                    StartManifest();
                }
                BossRushCoop.RaiseSessionChanged();
            }
            if (!transport.Bound) return;
            if (!Connected) return;
            packetFrames.Prune(now);
            if (!Host && !HasHostPeer) InvalidateReadyHandshake();
            GetLocalAssetId();
            RefreshSceneIdentity();
            CompleteManifest();
            StartManifest();
            if (Host && now >= nextRoster)
            {
                nextRoster = now + 1f;
                SendRoster();
                PublishCompatibilityStatus(now);
            }
            if (!Host && now >= nextHello && manifest != null)
            {
                SendHello();
                foreach (string key in localReadyKeys) SendReady(key);
            }
            if (now >= nextRefresh)
            {
                nextRefresh = now + 0.5f;
                // 原版 Direct 断线路径可能跳过 API 的 PeerDisconnected 事件。
                // 按原生实际连接表补齐退场，否则 Ready 与参赛屏障会永久保留离线成员。
                if (Host)
                    foreach (var player in players.ToArray())
                        if (player.IsConnected && !player.IsLocal && !transport.IsPeerConnected(player.Peer)) OnPeerDisconnected(player.Peer);
                transport.RefreshCharacters(OnNativePlayerSpawned);
                // 生成事件负责常态接线；周期扫描只遍历联机自己的已登记表，覆盖先于绑定出生的角色。
                transport.RefreshAi(OnAiSpawned);
            }
            worldLoot.Tick();
            control.Tick();
        }

        private void StartManifest()
        {
            if (manifest != null || manifestTask != null || manifestFailed) return;
            string assemblyPath = typeof(ModBehaviour).Assembly.Location;
            string root = ModBehaviour.GetModPath();
            manifestCancellation = new CancellationTokenSource();
            CancellationToken token = manifestCancellation.Token;
            // 只在实际联机时准备资源清单。文件哈希在线程池计算，不阻塞 Unity 主线程。
            manifestTask = Task.Run(() => BuildManifest(root, assemblyPath, token), token);
        }
        private static string BuildManifest(string root, string assemblyPath, CancellationToken token)
        {
            var files = new List<string>();
            if (!File.Exists(assemblyPath)) throw new IOException("BossRush assembly path is unavailable.");
            files.Add(assemblyPath);
            string assets = Path.Combine(root, "Assets");
            if (Directory.Exists(assets)) files.AddRange(Directory.GetFiles(assets, "*", SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);
            using (var total = SHA256.Create())
            {
                byte[] buffer = new byte[65536];
                foreach (string file in files)
                {
                    token.ThrowIfCancellationRequested();
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = file == assemblyPath ? "BossRush.dll" : file.Substring(assets.Length).Replace('\\', '/');
                    byte[] label = Encoding.UTF8.GetBytes(relative + "\n");
                    total.TransformBlock(label, 0, label.Length, label, 0);
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var content = SHA256.Create())
                    {
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            content.TransformBlock(buffer, 0, read, buffer, 0);
                        }
                        content.TransformFinalBlock(new byte[0], 0, 0);
                        byte[] digest = content.Hash;
                        total.TransformBlock(digest, 0, digest.Length, digest, 0);
                    }
                }
                total.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(total.Hash).Replace("-", string.Empty);
            }
        }
        private void CompleteManifest()
        {
            if (manifestTask == null || !manifestTask.IsCompleted) return;
            if (manifestTask.Status == TaskStatus.RanToCompletion) manifest = manifestTask.Result;
            else if (manifestTask.IsFaulted)
            {
                manifestFailed = true;
                string error = manifestTask.Exception.GetBaseException().Message;
                ModBehaviour.CriticalLog("coop-content-manifest", "[BossRush Coop] Content fingerprint failed: " + error);
                ShowError(L10n.T("联机内容校验失败，请检查 Mod 文件。", "Co-op content validation failed. Check the Mod files."));
            }
            manifestTask = null;
            if (manifestCancellation != null) { manifestCancellation.Dispose(); manifestCancellation = null; }
        }

        internal IDisposable Register(string topic, Action<BossRushCoopMessage> handler)
        {
            if (!ValidTopic(topic) || topic[0] == '$' || handler == null) throw new ArgumentException("Invalid co-op subscription.");
            List<Action<BossRushCoopMessage>> list;
            if (!handlers.TryGetValue(topic, out list)) handlers.Add(topic, list = new List<Action<BossRushCoopMessage>>());
            list.Add(handler);
            return new Subscription(this, topic, handler);
        }
        internal bool SendToHost(string topic, string json)
        {
            if (!Connected || !Ready || !ValidTopic(topic) || topic[0] == '$') return false;
            if (Host) { Dispatch(LocalId, true, topic, json, Guid.NewGuid().ToString("N")); return true; }
            return SendRaw(null, topic, json);
        }
        internal bool SendToPlayer(string id, string topic, string json)
        {
            if (NetworkBlocked || !Connected || !Host || !ValidTopic(topic) || topic[0] == '$') return false;
            BossRushCoopPlayer player = FindPlayer(id);
            if (player == null || !player.IsConnected) return false;
            if (player.IsLocal) { Dispatch(LocalId, true, topic, json, Guid.NewGuid().ToString("N")); return true; }
            return SendRaw(player.Peer, topic, json);
        }
        internal bool Broadcast(string topic, string json)
        {
            if (NetworkBlocked || !Connected || !Host || !ValidTopic(topic) || topic[0] == '$') return false;
            // 不在房主重放自身事件；生产状态已由房主提交，避免二次发奖/施法。
            bool sent = true;
            foreach (var player in players)
                if (player.IsConnected && !player.IsLocal) sent &= SendRaw(player.Peer, topic, json);
            return sent;
        }
        private bool SendRaw(object peer, string topic, string json)
        {
            try
            {
                var envelope = new BossRushCoopEnvelope { session = SessionId, topic = topic, json = json, messageId = Guid.NewGuid().ToString("N") };
                byte[] bytes = Encoding.UTF8.GetBytes(BossRushCoopJson.ToJson(envelope));
                if (bytes.Length > MaxPacketBytes) return false;
                return BossRushCoopPacketFrames.Send(bytes, frame => transport.Send(peer, frame));
            }
            catch (Exception e) { ModBehaviour.CriticalLog("coop-send-" + topic, "[BossRush Coop] Send failed: " + e.Message); return false; }
        }
        private void OnPacket(object peer, byte[] bytes)
        {
            if (NetworkBlocked || !Connected || bytes == null || bytes.Length == 0 || bytes.Length > MaxPacketBytes) return;
            try
            {
                if (BossRushCoopPacketFrames.IsFrame(bytes))
                {
                    BossRushCoopPlayer frameSender = Host ? FindPeer(peer) : null;
                    if (Host ? frameSender == null || !frameSender.IsConnected : !ReferenceEquals(peer, transport.HostPeer)) return;
                    byte[] complete;
                    if (!packetFrames.Receive(peer, bytes, Time.unscaledTime, out complete)) return;
                    bytes = complete;
                }
                var envelope = BossRushCoopJson.FromJson<BossRushCoopEnvelope>(Encoding.UTF8.GetString(bytes));
                if (envelope == null || envelope.version != 1 || !ValidTopic(envelope.topic) || string.IsNullOrEmpty(envelope.messageId)) return;
                if (Host && envelope.topic == "$hello") { OnHello(peer, BossRushCoopJson.FromJson<BossRushCoopHello>(envelope.json)); return; }
                if (!Host && ReferenceEquals(peer, transport.HostPeer) && envelope.topic == "$welcome")
                {
                    OnWelcome(BossRushCoopJson.FromJson<BossRushCoopWelcome>(envelope.json)); return;
                }
                if (!Host && ReferenceEquals(peer, transport.HostPeer) && envelope.topic == "$compatibility"
                    && (string.IsNullOrEmpty(SessionId) || envelope.session == SessionId))
                {
                    ApplyCompatibilityStatus(BossRushCoopJson.FromJson<BossRushCoopCompatibilityStatus>(envelope.json)); return;
                }
                if (string.IsNullOrEmpty(SessionId) || envelope.session != SessionId) return;
                BossRushCoopPlayer sender = Host ? FindPeer(peer) : null;
                if (Host && (sender == null || !sender.IsConnected)) return;
                if (!Host && !ReferenceEquals(peer, transport.HostPeer)) return;
                string senderId = Host ? sender.Id : "host";
                if (!replay.Accept(senderId, envelope.messageId)) return;
                if (envelope.topic == "$roster" && !Host) { OnRoster(BossRushCoopJson.FromJson<BossRushCoopRoster>(envelope.json)); return; }
                if (envelope.topic == "$ready") { OnReady(senderId, BossRushCoopJson.FromJson<BossRushCoopReady>(envelope.json)); return; }
                if (envelope.topic[0] != '$') Dispatch(senderId, !Host, envelope.topic, envelope.json, envelope.messageId);
            }
            catch (Exception e) { ModBehaviour.CriticalLog("coop-receive", "[BossRush Coop] Rejected invalid message: " + e.Message); }
        }
        private void OnHello(object peer, BossRushCoopHello hello)
        {
            if (peer == null || hello == null || manifest == null || hello.sequence <= 0) return;
            Guid asset;
            if (!Guid.TryParseExact(hello.assetId, "N", out asset) || hello.assetRevision <= 0) return;
            if (!string.Equals(hello.manifest, manifest, StringComparison.Ordinal))
            {
                incompatiblePeers.Add(peer);
                PublishCompatibilityStatus(Time.unscaledTime, true);
                SendRaw(peer, "$welcome", BossRushCoopJson.ToJson(new BossRushCoopWelcome { helloSequence = hello.sequence, error = "content_mismatch" })); return;
            }
            incompatiblePeers.Remove(peer);
            BossRushCoopPlayer player = FindPeer(peer);
            if (player != null && hello.sequence <= player.HelloSequence) return;
            if (player == null && !string.IsNullOrEmpty(hello.reconnectToken))
                foreach (var previous in players)
                    if (previous.ReconnectToken == hello.reconnectToken && !transport.IsPeerConnected(previous.Peer))
                    { if (previous.IsConnected) OnPeerDisconnected(previous.Peer); player = previous; break; }
            if (player == null)
            {
                if (players.Count >= 256) return;
                player = new BossRushCoopPlayer { Id = Guid.NewGuid().ToString("N"), ReconnectToken = Guid.NewGuid().ToString("N") };
                players.Add(player);
            }
            if (hello.assetRevision < player.AssetRevision || (hello.assetRevision == player.AssetRevision && player.AssetId != null && player.AssetId != hello.assetId)) return;
            bool identityChanged = player.AssetId != hello.assetId || player.AssetRevision != hello.assetRevision;
            bool connectionChanged = !player.IsConnected || !ReferenceEquals(player.Peer, peer);
            if (identityChanged || connectionChanged)
            {
                barrier.Remove(player.Id);
                player.ReadyEpoch = Guid.NewGuid().ToString("N");
                player.Character = null;
            }
            player.HelloSequence = hello.sequence;
            if (player.AssetId != hello.assetId || player.NativeId != transport.NativeId(peer)) player.Character = null;
            player.Peer = peer; player.IsConnected = true; player.NativeId = transport.NativeId(peer); player.AssetId = hello.assetId; player.AssetRevision = hello.assetRevision;
            SendRaw(peer, "$welcome", BossRushCoopJson.ToJson(new BossRushCoopWelcome { helloSequence = hello.sequence, session = SessionId, playerId = player.Id, reconnectToken = player.ReconnectToken, generation = Generation, assetId = player.AssetId, assetRevision = player.AssetRevision, readyEpoch = player.ReadyEpoch }));
            SendRoster();
            foreach (string key in barrier.Keys()) BroadcastReady(key, identityChanged || connectionChanged ? null : peer);
        }
        private void SendHello()
        {
            if (!Connected || Host || manifest == null) return;
            if (!ReferenceEquals(helloHostPeer, transport.HostPeer))
            {
                InvalidateReadyHandshake();
                helloHostPeer = transport.HostPeer;
            }
            nextHello = Time.unscaledTime + 2f;
            SendRaw(null, "$hello", BossRushCoopJson.ToJson(new BossRushCoopHello { sequence = ++helloSequence, manifest = manifest, reconnectToken = reconnectToken, nativeId = transport.SelfId(), assetId = localAssetId, assetRevision = localAssetRevision }));
        }
        private void OnWelcome(BossRushCoopWelcome welcome)
        {
            if (welcome == null || welcome.helloSequence <= appliedWelcomeSequence || welcome.helloSequence < minimumWelcomeSequence || welcome.helloSequence > helloSequence) return;
            if (!string.IsNullOrEmpty(welcome.error))
            {
                ShowError(L10n.T("BossRush 版本或资源与房主不同，请统一文件后重新加入。", "BossRush version or resources differ from the host. Use matching files and rejoin."));
                return;
            }
            if (string.IsNullOrEmpty(welcome.session) || string.IsNullOrEmpty(welcome.playerId)) return;
            if (welcome.assetId != GetLocalAssetId() || welcome.assetRevision != localAssetRevision || string.IsNullOrEmpty(welcome.readyEpoch)) return;
            appliedWelcomeSequence = welcome.helloSequence;
            bool changed = SessionId != welcome.session || LocalId != welcome.playerId;
            if (changed && !string.IsNullOrEmpty(SessionId))
            {
                // 原生 client service 可不断网切换房主；新房间不能沿用旧 roster 序号、成员或世界对象。
                ResetSession(welcome.session);
                Connected = true; Host = false; previousHost = false;
            }
            else if (changed) transport.SceneIdentity.ResetSession(welcome.session);
            SessionId = welcome.session; LocalId = welcome.playerId; reconnectToken = welcome.reconnectToken;
            if (localReadyEpoch != welcome.readyEpoch) barrier.Remove(LocalId);
            localReadyEpoch = welcome.readyEpoch;
            welcomeHostPeer = transport.HostPeer;
            helloHostPeer = transport.HostPeer;
            foreach (var player in players) if (player.IsLocal) { player.AssetRevision = localAssetRevision; player.ReadyEpoch = localReadyEpoch; }
            SetGeneration(welcome.generation);
            if (changed) { replay.Clear(); BossRushCoop.RaiseSessionChanged(); }
            foreach (string key in localReadyKeys) SendReady(key);
        }
        private void SendRoster()
        {
            if (!Host || string.IsNullOrEmpty(SessionId)) return;
            var entries = new BossRushCoopRosterEntry[players.Count];
            for (int i = 0; i < players.Count; i++) entries[i] = new BossRushCoopRosterEntry { id = players[i].Id, nativeId = players[i].NativeId, assetId = players[i].AssetId, assetRevision = players[i].AssetRevision, readyEpoch = players[i].ReadyEpoch, connected = players[i].IsConnected, alive = players[i].IsAlive };
            string json = BossRushCoopJson.ToJson(new BossRushCoopRoster { generation = Generation, sceneId = officialSceneId, sceneToken = generationSceneToken, revision = ++rosterRevision, ready = Ready, players = entries });
            foreach (var player in players) if (player.IsConnected && !player.IsLocal) SendRaw(player.Peer, "$roster", json);
        }
        private void OnRoster(BossRushCoopRoster roster)
        {
            if (roster == null || roster.players == null || roster.players.Length > 256 || roster.revision <= rosterRevision || roster.generation < Generation || roster.generation < pendingGeneration) return;
            rosterRevision = roster.revision; roomReady = roster.ready;
            SetGeneration(roster.generation);
            ClearRemoteAlive();
            foreach (var entry in roster.players)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                BossRushCoopPlayer player = FindPlayer(entry.id);
                if (player == null) { player = new BossRushCoopPlayer { Id = entry.id }; players.Add(player); }
                bool local = entry.id == LocalId;
                string nextAsset = local ? GetLocalAssetId() : entry.assetId;
                long nextAssetRevision = local ? localAssetRevision : entry.assetRevision;
                string nextEpoch = local ? localReadyEpoch : entry.readyEpoch;
                if (!entry.connected || player.AssetId != nextAsset || player.AssetRevision != nextAssetRevision || player.ReadyEpoch != nextEpoch) barrier.Remove(player.Id);
                if (player.IsConnected && !entry.connected) { player.Character = null; BossRushCoop.RaisePlayerDisconnected(player.Id); }
                player.IsConnected = entry.connected; player.NativeId = entry.nativeId; player.IsLocal = entry.id == LocalId;
                // hello/roster 在途时不能把本机刚切换的资产身份改回旧槽。
                player.AssetId = nextAsset; player.AssetRevision = nextAssetRevision; player.ReadyEpoch = nextEpoch;
            }
            foreach (var player in players)
            {
                bool present = false; foreach (var entry in roster.players) if (entry != null && entry.id == player.Id) { present = true; break; }
                if (!present) { barrier.Remove(player.Id); if (player.IsConnected) BossRushCoop.RaisePlayerDisconnected(player.Id); player.IsConnected = false; player.Character = null; }
            }
            transport.RefreshCharacters(OnNativePlayerSpawned);
            // 先绑定实际身体，再绑定此次完整名单。换体回调不能复用前一个身体的存活事实。
            // 本机先重载、房主尚未推进代次时，旧图的新版本名单仍不能恢复本机远端参与者。
            if (!TryBindGenerationScene(roster.sceneId, roster.sceneToken)) return;
            foreach (var entry in roster.players)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                BossRushCoopPlayer player = FindPlayer(entry.id);
                if (player == null || player.IsLocal) continue;
                player.HostAlive = entry.connected && entry.alive;
                player.HostAliveAssetId = player.AssetId;
                player.HostAliveGeneration = Generation;
                player.HostAliveCharacter = player.Character;
            }
        }
        private void SetGeneration(int generation)
        {
            if (generation <= Generation || generation <= pendingGeneration) return;
            RefreshSceneIdentity();
            // 名单到达不能证明本机已切图；原生旅行 token 匹配实际 Core 后才对玩法暴露新代次。
            if (Generation > 0 && localSceneHandle == generationSceneHandle) localReadyKeys.Clear();
            pendingGeneration = generation;
            generationScenePending = true;
            barrier.Clear(); appliedReadyRevisions.Clear();
            ClearRemoteAlive();
            control.Reset();
        }
        private bool TryBindGenerationScene(string sceneId, string sceneToken)
        {
            RefreshSceneIdentity();
            if (!HasCurrentSceneCore() || string.IsNullOrEmpty(sceneId)
                || !string.Equals(sceneId, officialSceneId, StringComparison.Ordinal)) return false;
            if (Generation == 0) transport.SceneIdentity.TryAdoptInitial(sceneToken, sceneId);
            if (!transport.SceneIdentity.Matches(sceneToken)) return false;
            if (generationScenePending)
            {
                Generation = pendingGeneration;
                pendingGeneration = 0;
                generationSceneHandle = localSceneHandle;
                generationSceneToken = sceneToken;
                generationScenePending = false;
                barrier.Clear(); appliedReadyRevisions.Clear(); aiCharacters.Clear();
                control.Reset(); worldLoot.ChangeGeneration();
                foreach (string key in localReadyKeys) SendReady(key);
                // 仅绑定场景，不复用绑定时在途的存活事实；下一份完整名单再绑定身体。
                return false;
            }
            return HasBoundScene && generationSceneToken == sceneToken;
        }
        private bool HasCurrentSceneCore()
        {
            var core = Duckov.Scenes.MultiSceneCore.Instance;
            if (core == null || core.SceneInfo == null || core.IsLoading) return false;
            Scene scene = core.gameObject.scene;
            return scene.IsValid() && scene.isLoaded && scene.handle == localSceneHandle
                && string.Equals(core.SceneInfo.ID, officialSceneId, StringComparison.Ordinal);
        }
        private void OnNativePlayerSpawned(string nativeId, CharacterMainControl character, bool local)
        {
            if (character == null) return;
            foreach (var player in players)
            {
                if (!player.IsConnected || (local ? !player.IsLocal : player.IsLocal || player.NativeId != nativeId)) continue;
                // 房主只承认联机后端按 NetPeer 保存的角色，不能凭客户端报来的名字认领身体。
                if (Host && !local && transport.CharacterForPeer(player.Peer) != character) continue;
                if (player.Character == character) continue;
                player.HostAlive = false;
                player.Character = character; BossRushCoop.RaisePlayerSpawned(player);
                break;
            }
        }
        private void OnPeerDisconnected(object peer)
        {
            packetFrames.Remove(peer);
            if (!Connected) return;
            if (!Host) { if (ReferenceEquals(peer, transport.HostPeer)) InvalidateReadyHandshake(); return; }
            BossRushCoopPlayer player = FindPeer(peer);
            if (player == null) return;
            player.IsConnected = false; player.HostAlive = false; player.Character = null; player.Peer = null;
            player.ReadyEpoch = null;
            barrier.Remove(player.Id); BossRushCoop.RaisePlayerDisconnected(player.Id);
            SendRoster();
            foreach (string key in barrier.Keys()) BroadcastReady(key);
        }
        private void OnAiSpawned(int id, CharacterMainControl character)
        {
            if (id == 0 || character == null) return;
            CharacterMainControl previous;
            if (aiCharacters.TryGetValue(id, out previous) && previous == character) return;
            aiCharacters[id] = character; BossRushCoop.RaiseAiSpawned(id, character);
        }
        internal int GetAiId(CharacterMainControl character)
        {
            if (character == null) return 0;
            foreach (var entry in aiCharacters) if (entry.Value == character) return entry.Key;
            return 0;
        }
        internal CharacterMainControl FindAi(int id) { CharacterMainControl value; return aiCharacters.TryGetValue(id, out value) ? value : null; }
        internal bool RegisterAi(CharacterMainControl character)
        {
            if (NetworkBlocked || !Connected || !Host || character == null) return false;
            if (GetAiId(character) != 0) return true;
            return transport.RegisterAi(character);
        }
        internal bool RegisterWorldLoot(InteractableLootbox box) { return worldLoot != null && worldLoot.Register(box); }
        internal bool RegisterWorldDestructible(HealthSimpleBase health, string entityKey)
        {
            try { return transport != null && transport.RegisterDestructible(health, entityKey); }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush Coop] Destructible registration failed: " + e.Message); return false; }
        }
        internal string BeginControlLease(string playerId, string entityId) { return control != null ? control.Begin(playerId, entityId) : null; }
        internal void EndControlLease(string leaseId) { if (control != null) control.End(leaseId); }
        internal bool SubmitControlInput(string leaseId, Vector3 move, Vector3 aim, bool fire, bool dash, bool reload, int weapon)
        { return control != null && control.Submit(leaseId, move, aim, fire, dash, reload, weapon); }
        internal bool HasControlLease(string leaseId) { return control != null && control.Contains(leaseId); }
        internal void MarkSceneReady(string key)
        {
            if (!Connected || string.IsNullOrEmpty(key) || key.Length > 256) return;
            RefreshSceneIdentity();
            // 同一准备事实会被玩法每帧确认；仅首次立即发送，丢包仍由 hello 周期重报。
            if (localReadyKeys.Add(key)) SendReady(key);
        }
        private void SendReady(string key)
        {
            if (string.IsNullOrEmpty(SessionId)) return;
            if (!HasBoundScene || string.IsNullOrEmpty(localReadyEpoch)) return;
            if (Host) { if (barrier.Mark(LocalId, key)) BroadcastReady(key); }
            else if (HasHostPeer && ReferenceEquals(welcomeHostPeer, transport.HostPeer)) SendRaw(null, "$ready", BossRushCoopJson.ToJson(new BossRushCoopReady { generation = Generation, key = key, assetId = localAssetId, assetRevision = localAssetRevision, readyEpoch = localReadyEpoch }));
        }
        private void OnReady(string sender, BossRushCoopReady ready)
        {
            if (ready == null || ready.generation != Generation || string.IsNullOrEmpty(ready.key) || ready.key.Length > 256) return;
            if (Host)
            {
                BossRushCoopPlayer player = FindPlayer(sender);
                if (player == null || !player.IsConnected || ready.assetId != player.AssetId || ready.assetRevision != player.AssetRevision || string.IsNullOrEmpty(player.ReadyEpoch) || ready.readyEpoch != player.ReadyEpoch) return;
                bool added = barrier.Mark(sender, ready.key);
                BroadcastReady(ready.key, added ? null : player.Peer);
            }
            else if (ready.members != null && ready.members.Length <= 256 && ready.revision > 0)
            {
                long applied; if (appliedReadyRevisions.TryGetValue(ready.key, out applied) && ready.revision <= applied) return;
                var accepted = new List<string>();
                foreach (var member in ready.members)
                {
                    if (member == null) continue;
                    var player = FindPlayer(member.id);
                    if (player != null && player.IsConnected && member.assetId == player.AssetId && member.assetRevision == player.AssetRevision && !string.IsNullOrEmpty(player.ReadyEpoch) && member.readyEpoch == player.ReadyEpoch) accepted.Add(player.Id);
                }
                if (barrier.Replace(ready.key, accepted)) { appliedReadyRevisions[ready.key] = ready.revision; BossRushCoop.RaiseSceneReadyChanged(ready.key); }
            }
        }
        private void BroadcastReady(string key, object targetPeer = null)
        {
            var members = new List<BossRushCoopReadyMember>();
            foreach (string id in barrier.ReadyPlayers(key))
            {
                var player = FindPlayer(id);
                if (player != null && player.IsConnected && !string.IsNullOrEmpty(player.ReadyEpoch)) members.Add(new BossRushCoopReadyMember { id = id, assetId = player.AssetId, assetRevision = player.AssetRevision, readyEpoch = player.ReadyEpoch });
            }
            string json = BossRushCoopJson.ToJson(new BossRushCoopReady { generation = Generation, key = key, revision = ++readyRevision, members = members.ToArray() });
            foreach (var player in players) if (player.IsConnected && !player.IsLocal && (targetPeer == null || ReferenceEquals(targetPeer, player.Peer))) SendRaw(player.Peer, "$ready", json);
            BossRushCoop.RaiseSceneReadyChanged(key);
        }
        internal bool AllPlayersSceneReady(string key)
        {
            if (!Ready || !HasBoundScene) return false;
            var connected = new List<string>();
            foreach (var player in players) if (player.IsConnected) connected.Add(player.Id);
            return barrier.AllReady(key, connected);
        }
        internal bool ArePlayersSceneReady(string key, IEnumerable<string> participants)
        {
            if (!Ready || !HasBoundScene || participants == null) return false;
            var connected = new List<string>();
            foreach (string id in participants)
            {
                BossRushCoopPlayer player = FindPlayer(id);
                if (player == null || !player.IsConnected) return false;
                connected.Add(id);
            }
            return barrier.AllReady(key, connected);
        }
        private void Dispatch(string sender, bool fromHost, string topic, string json, string id)
        {
            List<Action<BossRushCoopMessage>> list;
            if (!handlers.TryGetValue(topic, out list)) return;
            var message = new BossRushCoopMessage { SenderId = sender, IsFromHost = fromHost, Topic = topic, Json = json, MessageId = id };
            foreach (var handler in list.ToArray())
            {
                try { handler(message); }
                catch (Exception e) { ModBehaviour.CriticalLog("coop-handler-" + topic, "[BossRush Coop] Handler failed: " + e.Message); }
            }
        }
        private BossRushCoopPlayer FindPlayer(string id) { foreach (var player in players) if (player.Id == id) return player; return null; }
        private BossRushCoopPlayer FindPeer(object peer) { if (peer == null) return null; foreach (var player in players) if (ReferenceEquals(player.Peer, peer)) return player; return null; }
        private static bool ValidTopic(string topic) { return !string.IsNullOrEmpty(topic) && topic.Length <= 128; }
        private static string CurrentSceneId()
        {
            var core = Duckov.Scenes.MultiSceneCore.Instance;
            return core != null && core.SceneInfo != null ? core.SceneInfo.ID : null;
        }
        private void RefreshSceneIdentity()
        {
            var core = Duckov.Scenes.MultiSceneCore.Instance;
            transport.SceneIdentity.ObserveCore(core);
            if (core == null || core.SceneInfo == null) return;
            Scene scene = core.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return;
            string id = CurrentSceneId();
            if (string.IsNullOrEmpty(id)) return;
            bool changed = localSceneHandle != scene.handle || officialSceneId != id;
            if (changed)
            {
                localSceneHandle = scene.handle;
                officialSceneId = id;
                localReadyKeys.Clear();
                ClearRemoteAlive();
                control.Reset();
            }
            if (!Host) return;
            string token = transport.SceneIdentity.LocalToken;
            if (string.IsNullOrEmpty(token) || token == generationSceneToken) return;
            // 房主实际加载产生稳定 Core/token 后才能推进代次；同名重载也独立登记。
            Generation++;
            generationSceneHandle = localSceneHandle;
            generationSceneToken = token;
            generationScenePending = false; pendingGeneration = 0;
            barrier.Clear(); appliedReadyRevisions.Clear(); aiCharacters.Clear();
            control.Reset(); worldLoot.ChangeGeneration();
            nextRoster = 0f;
        }
        private void ClearRemoteAlive()
        {
            foreach (BossRushCoopPlayer player in players) if (!player.IsLocal) player.HostAlive = false;
        }
        private void InvalidateReadyHandshake()
        {
            minimumWelcomeSequence = helloSequence + 1;
            welcomeHostPeer = helloHostPeer = null; localReadyEpoch = null;
            barrier.Remove(LocalId); ClearRemoteAlive();
            foreach (var player in players) if (player.IsLocal) player.ReadyEpoch = null;
        }
        private void ShowError(string text)
        {
            if (lastError == text) return;
            lastError = text;
            ModBehaviour.CriticalLog("coop-compatibility-" + text, "[BossRush Coop] " + text);
            if (owner != null) owner.ShowMessage(text);
        }

        private void PublishCompatibilityStatus(float now, bool force = false)
        {
            if (!Host || NetworkBlocked || string.IsNullOrEmpty(SessionId)) return;
            List<object> peers = transport.ConnectedPeers();
            foreach (object previous in new List<object>(peerFirstSeen.Keys))
                if (!peers.Contains(previous)) { peerFirstSeen.Remove(previous); incompatiblePeers.Remove(previous); }
            var issues = new List<BossRushCoopCompatibilityIssue>();
            foreach (object peer in peers)
            {
                float seen;
                if (!peerFirstSeen.TryGetValue(peer, out seen)) peerFirstSeen.Add(peer, seen = now);
                BossRushCoopPlayer player = FindPeer(peer);
                string reason = incompatiblePeers.Contains(peer) ? "content_mismatch"
                    : (player == null || !player.IsConnected) && now - seen >= 10f ? "handshake_missing" : null;
                if (reason != null) issues.Add(new BossRushCoopCompatibilityIssue
                { player = transport.PeerDisplayName(peer), reason = reason });
            }
            issues.Sort((a, b) => string.CompareOrdinal(a.player, b.player));
            var status = new BossRushCoopCompatibilityStatus { issues = issues.ToArray() };
            string json = BossRushCoopJson.ToJson(status);
            bool changed = ApplyCompatibilityStatus(status);
            if (changed && issues.Count != 0)
            {
                var nativeNotice = new StringBuilder();
                foreach (BossRushCoopCompatibilityIssue issue in issues)
                    nativeNotice.Append(issue.player).Append(issue.reason == "content_mismatch"
                        ? "：BossRush 版本或资源与房主不一致 / BossRush version or resources differ from the host.\n"
                        : "：尚未完成 BossRush 校验，请确认已安装并完成加载 / BossRush handshake missing; check installation and loading.\n");
                transport.BroadcastCompatibilityNotice(nativeNotice.ToString());
            }
            if (!force && !changed && now < nextCompatibilityStatus) return;
            nextCompatibilityStatus = now + 5f;
            foreach (object peer in peers) SendRaw(peer, "$compatibility", json);
        }

        private bool ApplyCompatibilityStatus(BossRushCoopCompatibilityStatus status)
        {
            if (status == null || status.issues == null || status.issues.Length > 16) return false;
            var lines = new List<string>();
            foreach (BossRushCoopCompatibilityIssue issue in status.issues)
            {
                if (issue == null || string.IsNullOrEmpty(issue.player) || issue.player.Length > 160) return false;
                if (issue.reason == "content_mismatch") lines.Add(L10n.T("玩家 ", "Player ") + issue.player
                    + L10n.T("：BossRush 版本或资源与房主不一致。", ": BossRush version or resources differ from the host."));
                else if (issue.reason == "handshake_missing") lines.Add(L10n.T("玩家 ", "Player ") + issue.player
                    + L10n.T("：尚未完成 BossRush 校验，请确认已安装并完成加载。", ": BossRush handshake is missing; check installation and loading."));
                else return false;
            }
            string key = string.Join("\n", lines.ToArray());
            if (key == lastCompatibilityStatus) return false;
            bool hadIssue = !string.IsNullOrEmpty(lastCompatibilityStatus);
            lastCompatibilityStatus = key;
            if (key.Length != 0) ShowError(key + L10n.T("\n已确认操作会保留重试；过期战斗上报不会补扣。",
                "\nConfirmed operations remain pending; expired combat reports are not replayed."));
            else if (hadIssue) ShowError(L10n.T("全员 BossRush 版本与加载校验已恢复。", "BossRush compatibility and loading checks have recovered for everyone."));
            return true;
        }
        private static int CurrentSlotMetadata()
        {
            try { return Saves.SavesSystem.CurrentSlot; }
            catch (Exception) { return -1; }
        }
        internal string GetLocalAssetId()
        {
            string value = assetIdentities.Get(CurrentSlotMetadata());
            if (value == localAssetId) return value;
            localAssetId = value;
            localAssetRevision++;
            minimumWelcomeSequence = helloSequence + 1;
            localReadyKeys.Clear(); barrier.Remove(LocalId);
            localReadyEpoch = Host ? Guid.NewGuid().ToString("N") : null;
            foreach (BossRushCoopPlayer player in players) if (player.IsLocal) { player.AssetId = value; player.AssetRevision = localAssetRevision; player.ReadyEpoch = localReadyEpoch; }
            if (Host) foreach (string key in barrier.Keys()) BroadcastReady(key);
            nextHello = nextRoster = 0f;
            // 存档事件与同帧交互之间先发布新身份，避免新槽的第一笔请求被旧身份拒绝。
            if (transport != null) SendHello();
            return value;
        }
        private void OnSlotSelected() { GetLocalAssetId(); }
        private void OnSlotDeleted()
        {
            assetIdentities.Invalidate(CurrentSlotMetadata());
            GetLocalAssetId();
        }
        private void ResetSession(string acceptedSceneSession = null, bool retainNativeScene = false)
        {
            if (transport != null && !retainNativeScene) transport.SceneIdentity.ResetSession(acceptedSceneSession);
            foreach (var player in players) if (player.IsConnected) BossRushCoop.RaisePlayerDisconnected(player.Id);
            Connected = false; Host = false; LocalId = "local"; SessionId = string.Empty; Generation = 0;
            if (control != null) control.Reset();
            if (worldLoot != null) worldLoot.Reset();
            rosterRevision = 0; roomReady = false; manifestFailed = false; lastError = null;
            lastCompatibilityStatus = null; peerFirstSeen.Clear(); incompatiblePeers.Clear(); nextCompatibilityStatus = 0f;
            players.Clear(); aiCharacters.Clear(); replay.Clear(); barrier.Clear(); localReadyKeys.Clear();
            localReadyEpoch = null; welcomeHostPeer = helloHostPeer = null; minimumWelcomeSequence = 0; readyRevision = 0; appliedReadyRevisions.Clear();
            packetFrames.Clear();
            officialSceneId = null; localSceneHandle = generationSceneHandle = int.MinValue;
            generationScenePending = false; pendingGeneration = 0; generationSceneToken = null;
            nextHello = nextRoster = nextRefresh = 0f;
            if (manifestCancellation != null) manifestCancellation.Cancel();
        }
        public override void OnDestroy()
        {
            if (assetEventsSubscribed)
            {
                Saves.SavesSystem.OnSetFile -= OnSlotSelected;
                Saves.SavesSystem.OnSaveDeleted -= OnSlotDeleted;
                assetEventsSubscribed = false;
            }
            ResetSession();
            if (control != null) { control.Dispose(); control = null; }
            if (worldLoot != null) { worldLoot.Dispose(); worldLoot = null; }
            if (transport != null) transport.Dispose();
            if (manifestCancellation != null) { manifestCancellation.Dispose(); manifestCancellation = null; }
            manifestTask = null; manifest = null; reconnectToken = null; handlers.Clear(); owner = null;
            assetIdentities.Clear(); localAssetId = null;
            BossRushCoopJson.ResetStaticCaches();
            BossRushCoop.Shutdown(this);
        }
        private sealed class Subscription : IDisposable
        {
            private BossRushCoopRuntimeModule runtime;
            private readonly string topic;
            private readonly Action<BossRushCoopMessage> handler;
            internal Subscription(BossRushCoopRuntimeModule runtime, string topic, Action<BossRushCoopMessage> handler) { this.runtime = runtime; this.topic = topic; this.handler = handler; }
            public void Dispose()
            {
                if (runtime == null) return;
                List<Action<BossRushCoopMessage>> list;
                if (runtime.handlers.TryGetValue(topic, out list)) { list.Remove(handler); if (list.Count == 0) runtime.handlers.Remove(topic); }
                runtime = null;
            }
        }
    }
}
