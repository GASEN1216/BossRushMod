using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class PetNestCoopPacket
    {
        public string run, sceneToken;
        public int scene;
        public string owner;
        public string asset;
        public int ai;
        public long stateSequence;
        public long requestSequence;
        public bool atBase;
        public PetNestPetRecord pet;
        public float scale;
        public int experience;
        public bool downed;
        public string place;
        public string killer;
        public string rewardId;
    }

    /// <summary>一位玩家一只出战崽。房主维护全部实体，个人背包与成长只在主人自己的服务事务中提交。</summary>
    internal sealed class PetNestCoopRuntimeModule : BossRushRuntimeModuleBase
    {
        private sealed class Entry
        {
            internal string Owner;
            internal string Asset, SceneToken;
            internal PetNestPetRecord Pet;
            internal PetNestCompanionHandle Handle;
            internal bool Owned;
            internal bool Base;
            internal bool Spawning;
            internal bool Downed;
            internal string Killer;
            internal int KillExp;
            internal int Ai;
            internal string Projection;
        }
        private const string RequestTopic = "pet.deploy";
        private const string StateTopic = "pet.state";
        private const string AwardTopic = "pet.award";
        private const string ReleaseTopic = "pet.release";
        private const string AckTopic = "pet.ack";
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, PetNestCoopPacket> pending = new Dictionary<string, PetNestCoopPacket>(StringComparer.Ordinal);
        private readonly HashSet<Health> victims = new HashSet<Health>();
        private readonly Dictionary<string, PetNestCoopPacket> outstanding = new Dictionary<string, PetNestCoopPacket>(StringComparer.Ordinal);
        private readonly HashSet<string> received = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> killBudgets = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> stateSequences = new Dictionary<string, long>(StringComparer.Ordinal);
        private long nextStateSequence;
        private long nextRequestSequence;
        private string rewardRun;
        private readonly Dictionary<string, long> requestSequences = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly List<IDisposable> channels = new List<IDisposable>();
        private ModBehaviour owner;
        private float nextTick;
        private bool subscribed;
        private static PetNestCoopRuntimeModule instance;
        public override string ModuleName { get { return "PetNestCoop"; } }

        public override void OnAwake(ModBehaviour runtimeOwner)
        {
            owner = runtimeOwner;
            instance = this;
            rewardRun = BossRushCoop.RunId;
            channels.Add(BossRushCoop.Register(RequestTopic, OnRequest));
            channels.Add(BossRushCoop.Register(StateTopic, OnState));
            channels.Add(BossRushCoop.Register(AwardTopic, OnAward));
            channels.Add(BossRushCoop.Register(ReleaseTopic, OnRelease));
            channels.Add(BossRushCoop.Register(AckTopic, OnAck));
            if (!subscribed)
            {
                BossRushCoop.SessionChanged += Clear;
                BossRushCoop.PlayerDisconnected += OnDisconnected;
                BossRushCoop.AiSpawned += OnAiSpawned;
                Health.OnDead += OnDead;
                Health.OnHurt += OnHurt;
                subscribed = true;
            }
        }

        private static PetNestCoopPacket Packet(PetNestPetRecord pet)
        {
            PetNestPetRecord copy = pet != null ? pet.Clone() : null;
            if (copy != null) copy.backpackJson = null;
            return new PetNestCoopPacket { run = BossRushCoop.RunId, scene = BossRushCoop.SceneGeneration, sceneToken = BossRushCoop.SceneToken,
                owner = BossRushCoop.LocalPlayerId, asset = BossRushCoop.LocalAssetId, pet = copy,
                atBase = LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel };
        }
        private static bool Current(PetNestCoopPacket packet)
        {
            return packet != null && packet.run == BossRushCoop.RunId && packet.scene == BossRushCoop.SceneGeneration;
        }
        private static bool CurrentWorld(PetNestCoopPacket packet)
        { return Current(packet) && !string.IsNullOrEmpty(packet.sceneToken) && packet.sceneToken == BossRushCoop.SceneToken; }
        internal static void RequestLocalDeployment(PetNestPetRecord pet)
        {
            if (instance == null || !BossRushCoop.IsClient || pet == null) return;
            PetNestCoopPacket packet = Packet(pet); packet.requestSequence = ++instance.nextRequestSequence;
            BossRushCoop.SendToHost(RequestTopic, packet);
        }
        internal static void PublishLocalCompanion(PetNestCompanionHandle handle, PetNestPetRecord pet)
        {
            if (instance == null || !BossRushCoop.IsHost || handle == null || pet == null) return;
            var entry = new Entry { Owner = BossRushCoop.LocalPlayerId, Asset = BossRushCoop.LocalAssetId, SceneToken = BossRushCoop.SceneToken, Handle = handle, Pet = pet.Clone(),
                Base = LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel };
            instance.entries[entry.Owner] = entry;
            BossRushCoop.RequestAiRegistration(handle.Character);
            instance.Publish(entry);
        }
        internal static void ReleaseLocalCompanion()
        {
            if (instance == null || !BossRushCoop.IsConnected) return;
            if (BossRushCoop.IsHost) instance.Remove(BossRushCoop.LocalPlayerId, true);
            else
            {
                PetNestCoopPacket packet = Packet(null); packet.requestSequence = ++instance.nextRequestSequence;
                BossRushCoop.SendToHost(ReleaseTopic, packet);
            }
        }

        private void OnRequest(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || message.IsFromHost) return;
            PetNestCoopPacket packet = message.Read<PetNestCoopPacket>();
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            bool atBase = LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel;
            string reason;
            if (!CurrentWorld(packet) || !ValidPet(packet.pet) || player == null || !player.IsAlive || packet.asset != player.AssetId || packet.atBase != atBase
                || (!atBase && !PetNestModeGate.IsCompanionAllowed(owner, out reason))) return;
            if (!AcceptRequestSequence(packet, message.SenderId)) return;
            Entry entry;
            if (entries.TryGetValue(message.SenderId, out entry))
            {
                if (entry.Downed && entry.Asset == packet.asset) return;
                if (entry.Asset == packet.asset && entry.Pet.id == packet.pet.id && entry.Base == atBase)
                {
                    string projection = BossRushCoopJson.ToJson(packet.pet);
                    if (entry.Projection == projection) { Publish(entry); return; }
                    entry.Projection = projection;
                    long lastFed = entry.Pet.lastFeedUtcTicks;
                    entry.Pet = packet.pet.Clone();
                    if (entry.Handle != null)
                    {
                        PetNestCompanionSpawner.RefreshProgression(entry.Handle, entry.Pet);
                        entry.Handle.Agent.SetBehaviour(PetNestCareService.Resolve(entry.Pet));
                        if (entry.Pet.lastFeedUtcTicks > lastFed && entry.Handle.Health != null)
                            entry.Handle.Health.SetHealth(Mathf.Min(entry.Handle.Health.MaxHealth,
                                entry.Handle.Health.CurrentHealth + entry.Handle.Health.MaxHealth * PetNestTuning.FeedHealRatio));
                        Publish(entry);
                    }
                    return;
                }
                Remove(message.SenderId, true);
            }
            entry = new Entry { Owner = message.SenderId, Asset = packet.asset, SceneToken = packet.sceneToken, Pet = packet.pet.Clone(), Base = atBase, Owned = true, Spawning = true };
            entries[entry.Owner] = entry;
            string budget = entry.Owner + ":" + entry.Asset;
            if (atBase) killBudgets[budget] = 0;
            else if (killBudgets.ContainsKey(budget)) entry.KillExp = killBudgets[budget];
            Spawn(entry, player.Character, packet.run, packet.scene).Forget();
        }

        private static bool ValidPet(PetNestPetRecord pet)
        {
            return pet != null && !string.IsNullOrEmpty(pet.id) && pet.id.Length <= 128 && !string.IsNullOrEmpty(pet.lineageKey)
                && pet.lineageKey.Length <= 128 && pet.level >= 1 && pet.level <= PetNestTuning.PetMaxLevel
                && pet.exp >= 0 && pet.exp < PetNestTuning.PetExpPerLevel && pet.state != (int)PetNestPetState.Downed
                && pet.state != (int)PetNestPetState.OnExpedition && (pet.talents == null || pet.talents.Count <= 32)
                && (pet.scars == null || pet.scars.Count <= PetNestTuning.MaxScarsPerPet);
        }
        private async UniTaskVoid Spawn(Entry entry, CharacterMainControl master, string run, int scene)
        {
            PetNestCompanionHandle handle = null;
            try
            {
                PetNestLineageInfo lineage;
                if (!PetNestLineageCatalog.TryGet(entry.Pet.lineageKey, out lineage) || lineage == null || master == null) return;
                CharacterRandomPreset source = PetNestCompanionSpawner.ResolveCompanionSourcePreset(entry.Pet.lineageKey);
                if (source == null) return;
                handle = await PetNestCompanionSpawner.CreateIsolatedAsync(source, entry.Pet.lineageKey, lineage.ModelScale,
                    master.transform.position + PetNestCompanionSpawner.StagingOffset);
                if (handle == null || !IsSpawnCurrent(entry, master, run, scene)) return;
                string reason;
                if (!PetNestCompanionSpawner.TryActivate(handle, master.transform.position + PetNestCompanionSpawner.SpawnOffset,
                    master, owner, entry.Pet, out reason, !entry.Base, false)) return;
                entry.Handle = handle;
                handle = null;
                BossRushCoop.RequestAiRegistration(entry.Handle.Character);
                Publish(entry);
            }
            catch (Exception e) { ModBehaviour.DevLog("[PetNestCoop] 房主生成失败: " + e.Message); }
            finally
            {
                if (handle != null) PetNestCompanionSpawner.CleanupOnce(handle);
                FinishSpawn(entry);
            }
        }

        private bool IsSpawnCurrent(Entry entry, CharacterMainControl master, string run, int scene)
        {
            Entry current;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(entry.Owner);
            return BossRushCoop.IsHost && BossRushCoop.HasBoundScene && entry.SceneToken == BossRushCoop.SceneToken && run == BossRushCoop.RunId && scene == BossRushCoop.SceneGeneration
                && entries.TryGetValue(entry.Owner, out current) && ReferenceEquals(current, entry) && player != null && player.IsAlive
                && player.Character == master && player.AssetId == entry.Asset;
        }
        private void FinishSpawn(Entry entry)
        {
            entry.Spawning = false;
            Entry current;
            // 旧异步续作作废后不能移除同一 owner 已换崽/换槽后新建的 Entry。
            if (entry.Handle == null && entries.TryGetValue(entry.Owner, out current) && ReferenceEquals(current, entry))
                entries.Remove(entry.Owner);
        }

        private void Publish(Entry entry)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || entry.SceneToken != BossRushCoop.SceneToken || entry.Handle == null || entry.Handle.Character == null) return;
            entry.Ai = BossRushCoop.GetAiId(entry.Handle.Character);
            if (entry.Ai == 0) return;
            PetNestCoopPacket packet = Packet(entry.Pet);
            packet.owner = entry.Owner;
            packet.asset = entry.Asset;
            packet.ai = entry.Ai;
            packet.stateSequence = ++nextStateSequence;
            packet.atBase = entry.Base;
            packet.scale = entry.Handle.ModelScale;
            BossRushCoop.Broadcast(StateTopic, packet);
        }
        private void OnState(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            PetNestCoopPacket packet = message.Read<PetNestCoopPacket>();
            if (!CurrentWorld(packet) || string.IsNullOrEmpty(packet.owner)) return;
            long sequence;
            if (packet.stateSequence <= 0 || (stateSequences.TryGetValue(packet.owner, out sequence) && packet.stateSequence <= sequence)) return;
            if (packet.ai != 0 && (!ValidPet(packet.pet) || packet.scale <= 0f || packet.scale > 4f || float.IsNaN(packet.scale))) return;
            stateSequences[packet.owner] = packet.stateSequence;
            pending[packet.owner] = packet;
            BindReplica(packet);
        }
        private void BindReplica(PetNestCoopPacket packet)
        {
            if (!BossRushCoop.HasBoundScene) return;
            if (!CurrentWorld(packet)) { pending.Remove(packet.owner); return; }
            if (packet.ai == 0) { Remove(packet.owner, false); pending.Remove(packet.owner); return; }
            CharacterMainControl character = BossRushCoop.FindAi(packet.ai);
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(packet.owner);
            if (character == null || player == null || player.Character == null || packet.asset != player.AssetId) return;
            Entry previous;
            if (entries.TryGetValue(packet.owner, out previous) && previous.Asset == packet.asset && previous.Ai == packet.ai
                && previous.Handle != null && !previous.Handle.CleanedUp && previous.Handle.Character == character && previous.Handle.Agent != null)
            {
                previous.Pet = packet.pet;
                previous.Handle.ModelScale = packet.scale;
                if (character.modelRoot != null) character.modelRoot.localScale = Vector3.one * packet.scale;
                previous.Handle.Agent.SetBehaviour(PetNestCareService.Resolve(packet.pet));
                if (previous.Handle.Agent.Master != player.Character) previous.Handle.Agent.Bind(character, player.Character, false);
                pending.Remove(packet.owner);
                return;
            }
            Remove(packet.owner, false);
            bool personal = packet.owner == BossRushCoop.LocalPlayerId;
            PetNestPetRecord pet = personal ? PetNestService.TryGetPet(packet.pet.id) : packet.pet;
            if (pet == null) return;
            var entry = new Entry { Owner = packet.owner, Asset = packet.asset, SceneToken = packet.sceneToken, Pet = packet.pet, Ai = packet.ai, Base = packet.atBase,
                Handle = PetNestCompanionSpawner.AttachNetworkReplica(character, player.Character, pet, packet.scale, personal), Owned = true };
            entries[entry.Owner] = entry;
            if (personal && !entry.Base) PetNestCompanionRuntime.BindNetworkReplica(entry.Handle, pet.id);
            pending.Remove(packet.owner);
        }
        private void OnAiSpawned(int id, CharacterMainControl character)
        {
            if (!BossRushCoop.IsClient || pending.Count == 0) return;
            var copy = new List<PetNestCoopPacket>(pending.Values);
            foreach (PetNestCoopPacket packet in copy) if (packet.ai == id) BindReplica(packet);
        }

        private void OnRelease(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || message.IsFromHost) return;
            PetNestCoopPacket packet = message.Read<PetNestCoopPacket>();
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            if (!CurrentWorld(packet) || player == null || !player.IsConnected || packet.asset != player.AssetId
                || !AcceptRequestSequence(packet, message.SenderId)) return;
            Entry entry;
            if (entries.TryGetValue(message.SenderId, out entry) && packet.asset == entry.Asset)
                Remove(message.SenderId, true);
        }
        private bool AcceptRequestSequence(PetNestCoopPacket packet, string sender)
        {
            if (packet.owner != sender || packet.requestSequence <= 0) return false;
            string key = sender + ":" + packet.asset;
            long sequence;
            if (requestSequences.TryGetValue(key, out sequence) && packet.requestSequence <= sequence) return false;
            requestSequences[key] = packet.requestSequence;
            return true;
        }
        private void OnAward(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            PetNestCoopPacket packet = message.Read<PetNestCoopPacket>();
            if (!Current(packet) || packet.owner != BossRushCoop.LocalPlayerId || packet.asset != BossRushCoop.LocalAssetId
                || packet.pet == null || string.IsNullOrEmpty(packet.rewardId)) return;
            if (received.Contains(packet.rewardId)) { BossRushCoop.SendToHost(AckTopic, packet); return; }
            if (PetNestService.ApplyCoopOutcome(packet.pet.id, packet.experience, packet.downed, packet.place, packet.killer))
            {
                received.Add(packet.rewardId);
                BossRushCoop.SendToHost(AckTopic, packet);
                if (packet.downed) { PetNestCompanionRuntime.CleanupOnce(); Remove(packet.owner, false); }
                else RequestLocalDeployment(PetNestService.TryGetPet(packet.pet.id));
            }
        }
        private void QueueAward(PetNestCoopPacket packet)
        {
            packet.rewardId = Guid.NewGuid().ToString("N");
            outstanding[packet.rewardId] = packet;
            BossRushCoop.SendToPlayer(packet.owner, AwardTopic, packet);
        }
        private void OnAck(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || message.IsFromHost) return;
            PetNestCoopPacket packet = message.Read<PetNestCoopPacket>();
            PetNestCoopPacket award;
            if (Current(packet) && packet.rewardId != null && outstanding.TryGetValue(packet.rewardId, out award)
                && award.owner == message.SenderId && award.asset == packet.asset) outstanding.Remove(packet.rewardId);
        }
        private void OnDead(Health victim, DamageInfo damage)
        {
            if (!BossRushCoop.IsHost || victim == null || damage.fromCharacter == null || entries.Count == 0 || !victims.Add(victim)) return;
            CharacterMainControl killed = victim.TryGetCharacter();
            foreach (Entry entry in entries.Values)
            {
                if (!entry.Owned || entry.Base || entry.Downed || entry.Handle == null || entry.Handle.Character == null
                    || entry.KillExp >= PetNestTuning.PetExpCompanionKillRunCap || PetNestCompanionAgent.IsCompanionHealth(victim)) continue;
                CharacterMainControl companion = entry.Handle.Character;
                BossRushCoopPlayer master = BossRushCoop.FindPlayer(entry.Owner);
                bool shared = master != null && damage.fromCharacter == master.Character && killed != null
                    && Team.IsEnemy(companion.Team, killed.Team)
                    && (killed.transform.position - companion.transform.position).sqrMagnitude <= PetNestTuning.PetExpShareRadius * PetNestTuning.PetExpShareRadius;
                if (damage.fromCharacter != companion && !shared) continue;
                int amount = Math.Min(PetNestGrowth.KillExperience(victim.MaxHealth), PetNestTuning.PetExpCompanionKillRunCap - entry.KillExp);
                entry.KillExp += amount;
                killBudgets[entry.Owner + ":" + entry.Asset] = entry.KillExp;
                PetNestCoopPacket packet = Packet(entry.Pet);
                packet.owner = entry.Owner;
                packet.asset = entry.Asset;
                packet.experience = amount;
                QueueAward(packet);
            }
        }
        private void OnHurt(Health victim, DamageInfo damage)
        {
            if (!BossRushCoop.IsHost || entries.Count == 0 || victim == null) return;
            CharacterMainControl target = victim.TryGetCharacter();
            foreach (Entry entry in entries.Values)
            {
                if (!entry.Owned || entry.Handle == null || entry.Handle.Agent == null) continue;
                BossRushCoopPlayer master = BossRushCoop.FindPlayer(entry.Owner);
                CharacterMainControl companion = entry.Handle.Character;
                if (victim == entry.Handle.Health && damage.fromCharacter != null)
                    entry.Killer = damage.fromCharacter.characterPreset != null ? damage.fromCharacter.characterPreset.nameKey : damage.fromCharacter.name;
                CharacterMainControl enemy = null;
                if (master != null && damage.fromCharacter == master.Character && target != null && Team.IsEnemy(companion.Team, target.Team)) enemy = target;
                else if (master != null && master.Character != null && (victim == entry.Handle.Health || victim == master.Character.Health)
                    && damage.fromCharacter != null && Team.IsEnemy(companion.Team, damage.fromCharacter.Team)) enemy = damage.fromCharacter;
                if (enemy != null) entry.Handle.Agent.NotifyEngaged(enemy);
            }
        }
        internal static bool TryNotifyDowned(Health health)
        {
            if (instance == null || !BossRushCoop.IsHost || health == null) return false;
            foreach (Entry entry in instance.entries.Values)
                if (entry.Owned && entry.Handle != null && entry.Handle.Health == health)
                {
                    entry.Downed = true;
                    health.SetInvincible(true);
                    return true;
                }
            return false;
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            RefreshRewardRun();
            if (!BossRushCoop.IsConnected || !BossRushCoop.SessionReady || !BossRushCoop.HasBoundScene) return;
            if (BossRushCoop.IsHost && entries.Count > 0)
            {
                var remove = new List<string>();
                foreach (Entry entry in entries.Values)
                {
                    if (!entry.Owned) continue;
                    if (entry.Downed && entry.Handle != null)
                    {
                        PetNestCoopPacket packet = Packet(entry.Pet);
                        packet.owner = entry.Owner; packet.asset = entry.Asset; packet.downed = true;
                        packet.place = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                        packet.killer = entry.Killer;
                        QueueAward(packet);
                        PetNestCompanionSpawner.CleanupOnce(entry.Handle);
                        entry.Handle = null;
                        packet.ai = 0;
                        packet.stateSequence = ++nextStateSequence;
                        BossRushCoop.Broadcast(StateTopic, packet);
                    }
                    BossRushCoopPlayer player = BossRushCoop.FindPlayer(entry.Owner);
                    if (player == null || !player.IsAlive || player.AssetId != entry.Asset) remove.Add(entry.Owner);
                }
                foreach (string id in remove) Remove(id, true);
            }
            if (Time.unscaledTime < nextTick || SceneLoader.IsSceneLoading || !LevelManager.AfterInit) return;
            nextTick = Time.unscaledTime + 1f;
            if (BossRushCoop.IsClient)
            {
                PetNestPetRecord pet = PetNestService.DeployedPet;
                bool atBase = LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel;
                string reason;
                if (pet != null && pet.state != (int)PetNestPetState.Downed && pet.state != (int)PetNestPetState.OnExpedition
                    && (atBase || PetNestModeGate.IsCompanionAllowed(owner, out reason))) RequestLocalDeployment(pet);
                var copy = new List<PetNestCoopPacket>(pending.Values);
                foreach (PetNestCoopPacket packet in copy) BindReplica(packet);
            }
            else
            {
                // 已在单机生成的主人崽也要在建房 / 重连后登记，不能只依赖生成当帧的 hook。
                PetNestCompanionHandle local = LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel
                    ? PetNestBaseIdleSpawner.ActiveHandle : PetNestCompanionRuntime.ActiveHandle;
                Entry current;
                if (local != null && (!entries.TryGetValue(BossRushCoop.LocalPlayerId, out current) || current.Handle != local))
                    PublishLocalCompanion(local, PetNestService.DeployedPet);
                foreach (Entry entry in entries.Values) Publish(entry);
                var retry = new List<PetNestCoopPacket>(outstanding.Values);
                foreach (PetNestCoopPacket packet in retry)
                {
                    packet.scene = BossRushCoop.SceneGeneration;
                    BossRushCoop.SendToPlayer(packet.owner, AwardTopic, packet);
                }
            }
        }
        private void OnDisconnected(string id) { Remove(id, BossRushCoop.IsHost); pending.Remove(id); }
        private void Remove(string id, bool broadcast)
        {
            Entry entry;
            if (!entries.TryGetValue(id, out entry)) return;
            entries.Remove(id);
            if (entry.Owned && entry.Handle != null) PetNestCompanionSpawner.CleanupOnce(entry.Handle);
            if (broadcast && BossRushCoop.IsHost)
            {
                PetNestCoopPacket packet = Packet(null); packet.owner = id;
                packet.stateSequence = ++nextStateSequence;
                BossRushCoop.Broadcast(StateTopic, packet);
            }
        }
        private void Clear()
        {
            foreach (Entry entry in entries.Values) if (entry.Owned && entry.Handle != null) PetNestCompanionSpawner.CleanupOnce(entry.Handle);
            entries.Clear(); pending.Clear(); victims.Clear(); nextTick = 0f;
            RefreshRewardRun();
        }
        private void RefreshRewardRun()
        {
            string run = BossRushCoop.RunId;
            // 断线后的空握手身份不代表新房间；同房 welcome 不能重复经验或重开每局经验预算。
            if (string.IsNullOrEmpty(run) || run == rewardRun) return;
            rewardRun = run;
            stateSequences.Clear(); requestSequences.Clear();
            outstanding.Clear(); received.Clear(); killBudgets.Clear();
        }
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            foreach (Entry entry in entries.Values) if (entry.Owned && entry.Handle != null) PetNestCompanionSpawner.CleanupOnce(entry.Handle);
            entries.Clear(); pending.Clear(); victims.Clear(); nextTick = 0f;
            stateSequences.Clear();
            requestSequences.Clear();
        }
        public override void OnDestroy()
        {
            if (subscribed)
            {
                BossRushCoop.SessionChanged -= Clear;
                BossRushCoop.PlayerDisconnected -= OnDisconnected;
                BossRushCoop.AiSpawned -= OnAiSpawned;
                Health.OnDead -= OnDead;
                Health.OnHurt -= OnHurt;
                subscribed = false;
            }
            foreach (IDisposable channel in channels) channel.Dispose();
            channels.Clear(); Clear(); owner = null;
            stateSequences.Clear(); requestSequences.Clear(); outstanding.Clear(); received.Clear(); killBudgets.Clear();
            if (instance == this) instance = null;
        }
    }
}
