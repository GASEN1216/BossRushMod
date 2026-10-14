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
        private const int MaxPacketBytes = 262144;
        private readonly List<BossRushCoopPlayer> players = new List<BossRushCoopPlayer>();
        private readonly Dictionary<string, List<Action<BossRushCoopMessage>>> handlers = new Dictionary<string, List<Action<BossRushCoopMessage>>>(StringComparer.Ordinal);
        private readonly Dictionary<int, CharacterMainControl> aiCharacters = new Dictionary<int, CharacterMainControl>();
        private readonly BossRushCoopReplayWindow replay = new BossRushCoopReplayWindow();
        private readonly BossRushCoopSceneBarrier barrier = new BossRushCoopSceneBarrier();
        private readonly BossRushCoopAssetIdentity assetIdentities = new BossRushCoopAssetIdentity();
        private string localAssetId;
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
        private BossRushCoopWorldLoot worldLoot;
        private BossRushCoopControl control;
        internal bool Connected { get; private set; }
        internal bool Host { get; private set; }
        internal string LocalId { get; private set; } = "local";
        internal string SessionId { get; private set; } = string.Empty;
        internal int Generation { get; private set; }
        internal IReadOnlyList<BossRushCoopPlayer> Players { get { return players; } }
        internal bool HasHostPeer { get { return Connected && !Host && transport != null && transport.IsPeerConnected(transport.HostPeer); } }
        internal bool Ready
        {
            get
            {
                if (!Connected || manifest == null || string.IsNullOrEmpty(SessionId)) return false;
                if (!Host) return LocalId != "local" && roomReady && transport.IsPeerConnected(transport.HostPeer);
                int accepted = 0;
                foreach (var player in players) if (player.IsConnected && !player.IsLocal) accepted++;
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
            if (!transport.Bound)
            {
                if (now < nextProbe) return;
                nextProbe = now + 1f;
                if (!transport.TryBind()) return;
            }
            bool started = transport.NetworkStarted;
            bool host = transport.IsServer;
            if (started != Connected || (started && host != previousHost))
            {
                ResetSession();
                Connected = started; Host = host; previousHost = host;
                if (started)
                {
                    if (Host)
                    {
                        SessionId = Guid.NewGuid().ToString("N"); LocalId = "host";
                        players.Add(new BossRushCoopPlayer { Id = LocalId, AssetId = GetLocalAssetId(), IsLocal = true, IsConnected = true, NativeId = transport.SelfId(), Character = CharacterMainControl.Main });
                    }
                    StartManifest();
                }
                BossRushCoop.RaiseSessionChanged();
            }
            if (!Connected) return;
            if (!Host && !HasHostPeer) ClearRemoteAlive();
            GetLocalAssetId();
            RefreshSceneIdentity();
            CompleteManifest();
            StartManifest();
            if (Host && now >= nextRoster)
            {
                nextRoster = now + 1f;
                SendRoster();
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
            if (!Connected || !Host || !ValidTopic(topic) || topic[0] == '$') return false;
            BossRushCoopPlayer player = FindPlayer(id);
            if (player == null || !player.IsConnected) return false;
            if (player.IsLocal) { Dispatch(LocalId, true, topic, json, Guid.NewGuid().ToString("N")); return true; }
            return SendRaw(player.Peer, topic, json);
        }
        internal bool Broadcast(string topic, string json)
        {
            if (!Connected || !Host || !ValidTopic(topic) || topic[0] == '$') return false;
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
                return transport.Send(peer, bytes);
            }
            catch (Exception e) { ModBehaviour.CriticalLog("coop-send-" + topic, "[BossRush Coop] Send failed: " + e.Message); return false; }
        }
        private void OnPacket(object peer, byte[] bytes)
        {
            if (!Connected || bytes == null || bytes.Length == 0 || bytes.Length > MaxPacketBytes) return;
            try
            {
                var envelope = BossRushCoopJson.FromJson<BossRushCoopEnvelope>(Encoding.UTF8.GetString(bytes));
                if (envelope == null || envelope.version != 1 || !ValidTopic(envelope.topic) || string.IsNullOrEmpty(envelope.messageId)) return;
                if (Host && envelope.topic == "$hello") { OnHello(peer, BossRushCoopJson.FromJson<BossRushCoopHello>(envelope.json)); return; }
                if (!Host && ReferenceEquals(peer, transport.HostPeer) && envelope.topic == "$welcome")
                {
                    OnWelcome(BossRushCoopJson.FromJson<BossRushCoopWelcome>(envelope.json)); return;
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
            if (!Guid.TryParseExact(hello.assetId, "N", out asset)) return;
            if (!string.Equals(hello.manifest, manifest, StringComparison.Ordinal))
            {
                SendRaw(peer, "$welcome", BossRushCoopJson.ToJson(new BossRushCoopWelcome { helloSequence = hello.sequence, error = "content_mismatch" })); return;
            }
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
            player.HelloSequence = hello.sequence;
            if (player.AssetId != hello.assetId || player.NativeId != transport.NativeId(peer)) player.Character = null;
            player.Peer = peer; player.IsConnected = true; player.NativeId = transport.NativeId(peer); player.AssetId = hello.assetId;
            SendRaw(peer, "$welcome", BossRushCoopJson.ToJson(new BossRushCoopWelcome { helloSequence = hello.sequence, session = SessionId, playerId = player.Id, reconnectToken = player.ReconnectToken, generation = Generation }));
            SendRoster();
        }
        private void SendHello()
        {
            if (!Connected || Host || manifest == null) return;
            nextHello = Time.unscaledTime + 2f;
            SendRaw(null, "$hello", BossRushCoopJson.ToJson(new BossRushCoopHello { sequence = ++helloSequence, manifest = manifest, reconnectToken = reconnectToken, nativeId = transport.SelfId(), assetId = localAssetId }));
        }
        private void OnWelcome(BossRushCoopWelcome welcome)
        {
            if (welcome == null || welcome.helloSequence <= appliedWelcomeSequence || welcome.helloSequence > helloSequence) return;
            appliedWelcomeSequence = welcome.helloSequence;
            if (!string.IsNullOrEmpty(welcome.error))
            {
                ShowError(L10n.T("BossRush 版本或资源与房主不同，请统一文件后重新加入。", "BossRush version or resources differ from the host. Use matching files and rejoin."));
                return;
            }
            if (string.IsNullOrEmpty(welcome.session) || string.IsNullOrEmpty(welcome.playerId)) return;
            bool changed = SessionId != welcome.session || LocalId != welcome.playerId;
            if (changed && !string.IsNullOrEmpty(SessionId))
            {
                // 原生 client service 可不断网切换房主；新房间不能沿用旧 roster 序号、成员或世界对象。
                ResetSession();
                Connected = true; Host = false; previousHost = false;
            }
            SessionId = welcome.session; LocalId = welcome.playerId; reconnectToken = welcome.reconnectToken;
            SetGeneration(welcome.generation);
            if (changed) { replay.Clear(); BossRushCoop.RaiseSessionChanged(); }
        }
        private void SendRoster()
        {
            if (!Host || string.IsNullOrEmpty(SessionId)) return;
            var entries = new BossRushCoopRosterEntry[players.Count];
            for (int i = 0; i < players.Count; i++) entries[i] = new BossRushCoopRosterEntry { id = players[i].Id, nativeId = players[i].NativeId, assetId = players[i].AssetId, connected = players[i].IsConnected, alive = players[i].IsAlive };
            string json = BossRushCoopJson.ToJson(new BossRushCoopRoster { generation = Generation, revision = ++rosterRevision, ready = Ready, players = entries });
            foreach (var player in players) if (player.IsConnected && !player.IsLocal) SendRaw(player.Peer, "$roster", json);
        }
        private void OnRoster(BossRushCoopRoster roster)
        {
            if (roster == null || roster.players == null || roster.players.Length > 256 || roster.revision <= rosterRevision || roster.generation < Generation) return;
            rosterRevision = roster.revision; roomReady = roster.ready;
            SetGeneration(roster.generation);
            ClearRemoteAlive();
            foreach (var entry in roster.players)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                BossRushCoopPlayer player = FindPlayer(entry.id);
                if (player == null) { player = new BossRushCoopPlayer { Id = entry.id }; players.Add(player); }
                if (player.IsConnected && !entry.connected) { player.Character = null; BossRushCoop.RaisePlayerDisconnected(player.Id); }
                player.IsConnected = entry.connected; player.NativeId = entry.nativeId; player.IsLocal = entry.id == LocalId;
                // hello/roster 在途时不能把本机刚切换的资产身份改回旧槽。
                player.AssetId = player.IsLocal ? GetLocalAssetId() : entry.assetId;
            }
            transport.RefreshCharacters(OnNativePlayerSpawned);
            // 先绑定实际身体，再绑定此次完整名单。换体回调不能复用前一个身体的存活事实。
            // 本机先重载、房主尚未推进代次时，旧图的新版本名单仍不能恢复本机远端参与者。
            if (localSceneHandle != generationSceneHandle) return;
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
            if (generation <= Generation) return;
            RefreshSceneIdentity();
            // 收到代次前已经完成本机加载时，保留该新 Scene 实例的 ready；
            // 仍在旧实例时不得把旧 ready 自动继承到新代次。
            if (localSceneHandle == generationSceneHandle) localReadyKeys.Clear();
            generationSceneHandle = localSceneHandle;
            Generation = generation; barrier.Clear(); aiCharacters.Clear();
            ClearRemoteAlive();
            control.Reset(); worldLoot.ChangeGeneration();
            // 客端可能先完成加载再收到房主 generation，保留已准备的 key 并重报。
            foreach (string key in localReadyKeys) SendReady(key);
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
            if (!Connected) return;
            if (!Host) return;
            BossRushCoopPlayer player = FindPeer(peer);
            if (player == null) return;
            player.IsConnected = false; player.HostAlive = false; player.Character = null; player.Peer = null;
            barrier.Remove(player.Id); BossRushCoop.RaisePlayerDisconnected(player.Id);
            SendRoster();
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
            if (!Connected || !Host || character == null) return false;
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
            if (Host) { barrier.Mark(LocalId, key); BroadcastReady(key); }
            else SendRaw(null, "$ready", BossRushCoopJson.ToJson(new BossRushCoopReady { generation = Generation, key = key }));
        }
        private void OnReady(string sender, BossRushCoopReady ready)
        {
            if (ready == null || ready.generation != Generation || string.IsNullOrEmpty(ready.key) || ready.key.Length > 256) return;
            if (Host) { barrier.Mark(sender, ready.key); BroadcastReady(ready.key); }
            else if (ready.players != null && ready.players.Length <= 256)
            {
                foreach (string id in ready.players) if (FindPlayer(id) != null) barrier.Mark(id, ready.key);
                BossRushCoop.RaiseSceneReadyChanged(ready.key);
            }
        }
        private void BroadcastReady(string key)
        {
            string json = BossRushCoopJson.ToJson(new BossRushCoopReady { generation = Generation, key = key, players = barrier.ReadyPlayers(key) });
            foreach (var player in players) if (player.IsConnected && !player.IsLocal) SendRaw(player.Peer, "$ready", json);
            BossRushCoop.RaiseSceneReadyChanged(key);
        }
        internal bool AllPlayersSceneReady(string key)
        {
            if (!Ready) return false;
            var connected = new List<string>();
            foreach (var player in players) if (player.IsConnected) connected.Add(player.Id);
            return barrier.AllReady(key, connected);
        }
        internal bool ArePlayersSceneReady(string key, IEnumerable<string> participants)
        {
            if (!Ready || participants == null) return false;
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
            if (core == null || core.SceneInfo == null) return;
            Scene scene = core.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return;
            string id = CurrentSceneId();
            if (string.IsNullOrEmpty(id)) return;
            bool changed = localSceneHandle != scene.handle || officialSceneId != id;
            if (!changed) return;
            localSceneHandle = scene.handle;
            officialSceneId = id;
            localReadyKeys.Clear();
            ClearRemoteAlive();
            if (!Host) return;
            // SceneInfo.ID 相同的重载也必须隔离旧命令、AI、租约和屏障。
            Generation++;
            generationSceneHandle = localSceneHandle;
            barrier.Clear(); aiCharacters.Clear();
            control.Reset(); worldLoot.ChangeGeneration();
            nextRoster = 0f;
        }
        private void ClearRemoteAlive()
        {
            foreach (BossRushCoopPlayer player in players) if (!player.IsLocal) player.HostAlive = false;
        }
        private void ShowError(string text)
        {
            if (lastError == text) return;
            lastError = text;
            ModBehaviour.CriticalLog("coop-compatibility-" + text, "[BossRush Coop] " + text);
            if (owner != null) owner.ShowMessage(text);
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
            foreach (BossRushCoopPlayer player in players) if (player.IsLocal) player.AssetId = value;
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
        private void ResetSession()
        {
            foreach (var player in players) if (player.IsConnected) BossRushCoop.RaisePlayerDisconnected(player.Id);
            Connected = false; Host = false; LocalId = "local"; SessionId = string.Empty; Generation = 0;
            if (control != null) control.Reset();
            if (worldLoot != null) worldLoot.Reset();
            rosterRevision = 0; roomReady = false; manifestFailed = false; lastError = null;
            players.Clear(); aiCharacters.Clear(); replay.Clear(); barrier.Clear(); localReadyKeys.Clear();
            officialSceneId = null; localSceneHandle = generationSceneHandle = int.MinValue;
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
