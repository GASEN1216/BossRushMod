using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>蚊群是共享世界；痒、面罩、耗材和驱蚋委托属于被叮/击杀的玩家。</summary>
    internal sealed partial class SkyIslandGnats
    {
        [Serializable] private sealed class GnatWorld
        { public string Travel, SceneToken; public long Revision; public GnatPose[] Gnats; public string[] Killed; public Vector3[] Zappers; }
        [Serializable] private sealed class GnatPose
        { public string Key; public Vector3 Position, Step, Dash; public int Phase; public float BiteLeft; }
        [Serializable] private sealed class EnvironmentState
        {
            public string Travel, Region, SceneToken;
            public bool Night, Incense, Lantern, Smoke, Lamp, Veil, Mask, Enemies;
            public int Wind, Frogs;
            [NonSerialized] internal float NextSample, NextSpawn, SpawnCooldown, LastBite = -100f, FanReady;
        }
        [Serializable] private sealed class UseRequest { public string Travel, Id, AssetId, SceneToken; public int Buff; }
        [Serializable] private sealed class PersonalEvent { public string Travel, Id, Message, AssetId; public int Kind; public bool Accepted; }
        private readonly Dictionary<string, EnvironmentState> coopEnvironments = new Dictionary<string, EnvironmentState>(StringComparer.Ordinal);
        private readonly HashSet<string> coopKilled = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> coopRecentKilled = new Queue<string>();
        private readonly Dictionary<string, PersonalEvent> coopUses = new Dictionary<string, PersonalEvent>(StringComparer.Ordinal);
        private IDisposable coopGnatWorld, coopGnatEnvironment, coopGnatUse;
        private CoopPersonalDelivery<PersonalEvent> coopGnatPersonal;
        private GnatWorld coopLatest;
        private EnvironmentState coopRemoteEnvironment;
        private string coopReplicaKey, coopPendingUse, coopPendingAsset;
        private int coopPendingBuff, coopZapperCount;
        private long coopGnatSequence, coopBiteSequence, coopWorldRevision, coopReceivedRevision;
        private float coopNextWorld, coopUseAt, coopNextUseRequest;
        private bool coopSamplingRemote, coopCreatingReplica;
        private SkyIslandInventoryTransaction coopUseCost;
        private string CoopTravel { get { return SkyIslandCoopRuntime.Current != null ? SkyIslandCoopRuntime.Current.TravelId : null; } }
        internal bool CoopUsePending { get { return !string.IsNullOrEmpty(coopPendingUse); } }

        /// <summary>房主只处理真实同行玩家的直线弹，客端不重复推动躲闪状态机。</summary>
        internal void OnProjectile(Projectile projectile, ProjectileContext context)
        {
            if (!BossRushCoop.HasWorldAuthority) return;
            if (!Usable || alive == 0 || projectile == null) return;
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                bool mine = BossRushCoop.IsConnected ? BossRushCoop.IsPlayer(context.fromCharacter) : main != null && context.fromCharacter == main;
                if (!SkyIslandMosquitoRules.ShouldTrackProjectile(mine, context.gravity, context.explosionRange)) return;
                Vector3 origin = context.firstFrameCheck ? context.firstFrameCheckStartPoint : projectile.transform.position;
                var shot = new SkyIslandGnatShot(ToVec(origin), ToVec(context.direction), context.speed, context.distance, projectile.radius);
                int frame = Time.frameCount;
                for (int i = 0; i < gnats.Length; i++)
                {
                    Gnat gnat = gnats[i];
                    if (gnat == null || gnat.Leaving || gnat.Root == null || !gnat.Root.activeSelf) continue;
                    gnat.Motor.OnShot(ToVec(gnat.Position), shot, frame, this);
                }
            }
            catch (Exception e)
            {
                Fail("projectile", e);
            }
        }

        private void SubscribeCoopGnats()
        {
            coopGnatWorld = BossRushCoop.Register("sky.gnats.world", ReceiveCoopGnats);
            coopGnatEnvironment = BossRushCoop.Register("sky.gnats.environment", ReceiveCoopEnvironment);
            coopGnatUse = BossRushCoop.Register("sky.gnats.use", ReceiveCoopUse);
            coopGnatPersonal = new CoopPersonalDelivery<PersonalEvent>("sky.gnats.personal", ApplyCoopPersonal);
        }
        private void ReceiveCoopGnats(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            GnatWorld data = message.Read<GnatWorld>();
            if (data == null || data.Travel != CoopTravel || string.IsNullOrEmpty(data.SceneToken) || data.SceneToken != BossRushCoop.SceneToken || data.Gnats == null || data.Gnats.Length > gnats.Length
                || data.Killed == null || data.Killed.Length > 4096 || data.Zappers == null || data.Zappers.Length > zappers.Length) return;
            if (coopLatest != null && coopLatest.SceneToken != data.SceneToken) { coopReceivedRevision = 0; coopKilled.Clear(); }
            if (data.Revision <= coopReceivedRevision) return;
            coopReceivedRevision = data.Revision;
            foreach (string key in data.Killed) if (!string.IsNullOrEmpty(key)) coopKilled.Add(key);
            coopLatest = data; coopZapperCount = data.Zappers.Length;
        }
        private void ReceiveCoopEnvironment(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || BossRushCoop.FindPlayer(message.SenderId) == null) return;
            EnvironmentState data = message.Read<EnvironmentState>();
            if (data == null || data.Travel != CoopTravel || string.IsNullOrEmpty(data.SceneToken) || data.SceneToken != BossRushCoop.SceneToken
                || data.Wind < 0 || data.Wind > 3 || data.Frogs < 0 || data.Frogs > 64) return;
            EnvironmentState previous;
            if (coopEnvironments.TryGetValue(message.SenderId, out previous))
            { data.NextSample = previous.NextSample; data.NextSpawn = previous.NextSpawn; data.SpawnCooldown = previous.SpawnCooldown;
                data.LastBite = previous.LastBite; data.FanReady = previous.FanReady; }
            coopEnvironments[message.SenderId] = data;
        }
        private void ReportCoopEnvironment(bool isNight, int wind, string region, bool incense, bool light, bool smoke, bool lamp)
        {
            if (!BossRushCoop.IsConnected || !BossRushCoop.HasBoundScene) return;
            string reason;
            var data = new EnvironmentState { Travel = CoopTravel, SceneToken = BossRushCoop.SceneToken, Region = region, Night = isNight, Wind = wind,
                Incense = incense, Lantern = light, Smoke = smoke, Lamp = lamp, Veil = veilCarried,
                Mask = SkyIslandBossGearWorn.MossgauzeMask, Enemies = !session.CanOpenStoryPanel(out reason),
                Frogs = story != null ? SkyIslandMosquitoRules.FrogsReleased(story.Current) : 0 };
            if (BossRushCoop.IsClient) BossRushCoop.SendToHost("sky.gnats.environment", data);
            else
            {
                EnvironmentState previous;
                if (coopEnvironments.TryGetValue(BossRushCoop.LocalPlayerId, out previous))
                { data.LastBite = previous.LastBite; data.FanReady = previous.FanReady; }
                coopEnvironments[BossRushCoop.LocalPlayerId] = data;
            }
        }
        private void SetCoopTargetTraits(CharacterMainControl target)
        {
            if (!BossRushCoop.IsConnected || target == null) return;
            EnvironmentState state;
            if (!coopEnvironments.TryGetValue(BossRushCoop.GetPlayerId(target), out state))
            { lantern = veilCarried = false; lastBiteAt = -100f; return; }
            lantern = state.Lantern; veilCarried = state.Veil; lastBiteAt = state.LastBite;
        }
        private bool CoopTargetMask(CharacterMainControl target)
        {
            EnvironmentState state;
            return BossRushCoop.IsConnected && coopEnvironments.TryGetValue(BossRushCoop.GetPlayerId(target), out state)
                ? state.Mask : target == CharacterMainControl.Main && SkyIslandBossGearWorn.MossgauzeMask;
        }
        private void RegisterCoopGnat(Gnat gnat)
        {
            if (!BossRushCoop.IsConnected || gnat == null) return;
            gnat.CoopKey = coopCreatingReplica ? coopReplicaKey : "sky-gnat:" + BossRushCoop.RunId + ":" + CoopTravel + ":" + (++coopGnatSequence);
            BossRushCoop.RegisterWorldDestructible(gnat.Receiver.simpleHealth, gnat.CoopKey);
            if (BossRushCoop.IsHost)
                gnat.Root.AddComponent<SkyIslandGnatCoopLife>().Bind(this, gnat.CoopKey, gnat.Receiver);
            // 每一位玩家胶囊都不能被共享小实体顶住；射线/弹道仍经过接收体。
            Collider collider = gnat.Root.GetComponent<Collider>();
            foreach (BossRushCoopPlayer peer in BossRushCoop.Players)
            {
                Collider carrier = peer.Character != null && peer.Character.movementControl != null
                    ? peer.Character.movementControl.GetComponent<Collider>() : null;
                if (collider != null && carrier != null) Physics.IgnoreCollision(collider, carrier, true);
            }
        }
        internal void OnCoopGnatKilled(string key, DamageInfo info)
        {
            if (!BossRushCoop.IsHost || disposed || !coopKilled.Add(key)) return;
            coopRecentKilled.Enqueue(key);
            string player = BossRushCoop.GetPlayerId(info.fromCharacter);
            if (!string.IsNullOrEmpty(player)) coopGnatPersonal.Deliver(player, key,
                new PersonalEvent { Travel = CoopTravel, Id = key, Kind = 2 });
            coopNextWorld = 0f;
        }
        private void ReportCoopBite(CharacterMainControl player)
        {
            if (!BossRushCoop.IsConnected) { bitesSinceSample++; return; }
            string id = BossRushCoop.GetPlayerId(player);
            EnvironmentState state;
            if (coopEnvironments.TryGetValue(id, out state)) state.LastBite = Time.time;
            if (!string.IsNullOrEmpty(id)) coopGnatPersonal.Deliver(id, CoopTravel + ":bite:" + (++coopBiteSequence),
                new PersonalEvent { Travel = CoopTravel, Kind = 1 });
        }
        private bool ApplyCoopPersonal(PersonalEvent data)
        {
            if (disposed || data == null || data.Travel != CoopTravel) return false;
            if (data.Kind == 1) { bitesSinceSample++; return true; }
            if (data.Kind == 2) { if (session.Bounty == null) return false; session.Bounty.ReportGnatCulled(); return true; }
            if (data.Kind != 3) return true;
            if (data.AssetId != BossRushCoop.LocalAssetId) return false;
            if (data.Id != coopPendingUse) return true;
            if (data.Accepted && coopUseCost != null) coopUseCost.Commit();
            if (coopUseCost != null) coopUseCost.Dispose(); coopUseCost = null;
            if (data.Accepted && coopPendingBuff == (int)SkyIslandFieldBuff.Fan) fanReadyAt = Time.time + SkyIslandMosquitoRules.FanCooldownSeconds;
            coopPendingUse = null;
            if (!string.IsNullOrEmpty(data.Message)) session.Announce(data.Message, !data.Accepted);
            if (data.Accepted && story != null) story.LogTiming("consumable", ((SkyIslandFieldBuff)coopPendingBuff).ToString());
            return true;
        }
        internal bool QueueCoopUse(SkyIslandFieldBuff buff, Item item)
        {
            if (!BossRushCoop.IsClient || CoopUsePending || item == null || !Usable || !session.IsReady) return false;
            coopPendingUse = Guid.NewGuid().ToString("N"); coopPendingBuff = (int)buff;
            coopPendingAsset = BossRushCoop.LocalAssetId;
            // CA_UseItem 下一句同步扣 Count；失败补偿先结束，再在下一帧从本人背包预留。
            coopUseAt = Time.unscaledTime; coopNextUseRequest = 0f;
            return buff == SkyIslandFieldBuff.Fan;
        }
        private void ReceiveCoopUse(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || !session.IsWorldReady || !BossRushCoop.SessionReady) return;
            UseRequest data = message.Read<UseRequest>(); BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            if (data == null || data.Travel != CoopTravel || string.IsNullOrEmpty(data.SceneToken) || data.SceneToken != BossRushCoop.SceneToken || string.IsNullOrEmpty(data.Id) || data.Id.Length > 64
                || player == null || !player.IsAlive || string.IsNullOrEmpty(data.AssetId) || data.AssetId != player.AssetId
                || (data.Buff != (int)SkyIslandFieldBuff.Fan && data.Buff != (int)SkyIslandFieldBuff.Zapper)) return;
            string key = player.Id + ":" + data.AssetId + ":" + data.Id; PersonalEvent reply;
            if (!coopUses.TryGetValue(key, out reply))
            {
                float previousFan = fanReadyAt; EnvironmentState environment;
                coopEnvironments.TryGetValue(player.Id, out environment);
                if (environment != null) fanReadyAt = environment.FanReady;
                string said; bool accepted = data.Buff == (int)SkyIslandFieldBuff.Fan
                    ? SwingFan(player.Character, out said) : DeployZapper(player.Character, out said);
                if (environment != null) environment.FanReady = fanReadyAt; fanReadyAt = previousFan;
                reply = new PersonalEvent { Travel = CoopTravel, Id = data.Id, AssetId = data.AssetId, Kind = 3, Accepted = accepted, Message = said };
                coopUses[key] = reply;
            }
            coopGnatPersonal.Deliver(player.Id, CoopTravel + ":use:" + key, reply, data.AssetId);
        }
        private void TickCoopGnats()
        {
            if (!BossRushCoop.IsConnected || disposed) return;
            coopGnatPersonal.Tick(Time.unscaledDeltaTime);
            if (!BossRushCoop.HasBoundScene) return;
            if (BossRushCoop.IsClient)
            {
                TickCoopUse();
                return;
            }
            foreach (BossRushCoopPlayer peer in BossRushCoop.Players)
            {
                EnvironmentState state;
                if (peer.IsLocal || !peer.IsAlive || !coopEnvironments.TryGetValue(peer.Id, out state) || Time.time < state.NextSample) continue;
                state.NextSample = Time.time + SkyIslandFieldcraftRules.TickInterval;
                float previousNext = nextSpawnCheck, previousCooldown = spawnCooldownUntil;
                bool previousVeil = veilCarried, previousLantern = lantern;
                nextSpawnCheck = state.NextSpawn; spawnCooldownUntil = state.SpawnCooldown;
                veilCarried = state.Veil; lantern = state.Lantern; coopSamplingRemote = true; coopRemoteEnvironment = state;
                try { Sample(NightNow, state.Wind, state.Region, peer.Character, state.Incense, state.Lantern, state.Smoke, state.Lamp, SkyIslandFieldcraftRules.TickInterval); }
                finally { state.NextSpawn = nextSpawnCheck; state.SpawnCooldown = spawnCooldownUntil;
                    nextSpawnCheck = previousNext; spawnCooldownUntil = previousCooldown; veilCarried = previousVeil; lantern = previousLantern;
                    coopSamplingRemote = false; coopRemoteEnvironment = null; }
            }
            if (Time.unscaledTime < coopNextWorld) return;
            coopNextWorld = Time.unscaledTime + 0.1f;
            var poses = new List<GnatPose>(gnats.Length); var lamps = new List<Vector3>(zappers.Length);
            foreach (Gnat gnat in gnats) if (gnat != null && gnat.Root != null && gnat.Root.activeSelf)
                poses.Add(new GnatPose { Key = gnat.CoopKey, Position = gnat.Position, Step = gnat.LastStep,
                    Phase = (int)gnat.Motor.Phase, Dash = ToVector(gnat.Motor.DashDirection), BiteLeft = Mathf.Max(0f, gnat.BiteShownUntil - Time.time) });
            foreach (Zapper lamp in zappers) if (lamp != null && lamp.Root != null) lamps.Add(lamp.Root.transform.position);
            var killed = new List<string>(Math.Min(coopRecentKilled.Count, 4096));
            foreach (string key in coopRecentKilled) { if (killed.Count == 4096) break; killed.Add(key); }
            // 可靠有序频道只携带本次成功发送前的新击杀；不把整趟历史表放进每个 10 Hz 姿态包。
            if (BossRushCoop.Broadcast("sky.gnats.world", new GnatWorld { Travel = CoopTravel, SceneToken = BossRushCoop.SceneToken,
                Revision = ++coopWorldRevision, Gnats = poses.ToArray(), Killed = killed.ToArray(), Zappers = lamps.ToArray() }))
                for (int i = 0; i < killed.Count; i++) coopRecentKilled.Dequeue();
        }
        private void TickCoopUse()
        {
            if (!BossRushCoop.HasBoundScene) return;
            // 换槽暂停旧请求；原槽重新选中后同一身份继续等待回执，不能退款已获世界效果的旧请求。
            if (CoopUsePending && coopPendingAsset != BossRushCoop.LocalAssetId) return;
            if (!CoopUsePending || Time.unscaledTime <= coopUseAt || Time.unscaledTime < coopNextUseRequest) return;
            if (coopPendingBuff == (int)SkyIslandFieldBuff.Zapper && coopUseCost == null
                && !SkyIslandInventoryTransaction.TryReserve(CharacterMainControl.Main,
                    new[] { new SkyIslandIngredient(BossRushItemIds.SkyIslandGnatZapper, 1) }, out coopUseCost))
            { coopPendingUse = null; session.Announce(SkyIslandFieldcraftRules.OffIsland, true); return; }
            coopNextUseRequest = Time.unscaledTime + 0.5f;
            BossRushCoop.SendToHost("sky.gnats.use", new UseRequest { Travel = CoopTravel, SceneToken = BossRushCoop.SceneToken, Id = coopPendingUse, Buff = coopPendingBuff, AssetId = coopPendingAsset });
        }
        private void FrameCoopReplicas(float now, CharacterMainControl player)
        {
            if (!IsCoopWorldCurrent() || !Usable || player == null) return;
            Quaternion view = ViewRotation();
            for (int i = 0; i < gnats.Length; i++)
            {
                Gnat gnat = gnats[i]; if (gnat == null) continue;
                bool found = false; foreach (GnatPose pose in coopLatest.Gnats) if (pose.Key == gnat.CoopKey) { found = true; break; }
                if (!found) Remove(i, coopKilled.Contains(gnat.CoopKey), now, view);
            }
            Gnat nearest = null; float distance = BuzzRange * BuzzRange;
            foreach (GnatPose pose in coopLatest.Gnats)
            {
                if (pose == null || string.IsNullOrEmpty(pose.Key) || coopKilled.Contains(pose.Key)) continue;
                Gnat gnat = null; foreach (Gnat candidate in gnats) if (candidate != null && candidate.CoopKey == pose.Key) { gnat = candidate; break; }
                if (gnat == null)
                {
                    coopCreatingReplica = true; coopReplicaKey = pose.Key;
                    try { Spawn(pose.Position, now); } finally { coopCreatingReplica = false; coopReplicaKey = null; }
                    foreach (Gnat candidate in gnats) if (candidate != null && candidate.CoopKey == pose.Key) { gnat = candidate; break; }
                }
                if (gnat == null || gnat.Root == null) continue;
                gnat.Position = pose.Position; gnat.LastStep = pose.Step; gnat.Root.transform.position = pose.Position;
                int frame = pose.Phase == (int)SkyIslandGnatPhase.Windup ? FrameWindup : pose.Phase == (int)SkyIslandGnatPhase.Dash
                    ? FrameDash : pose.BiteLeft > 0f ? FrameBite : (int)(now * WingFramesPerSecond + gnat.Phase * 3f) % 3;
                gnat.Sprite.sprite = frames[frame];
                gnat.Visual.rotation = view;
                if (frame == FrameDash)
                    gnat.Visual.rotation = view * Quaternion.Euler(0f, 0f, Mathf.Atan2(Vector3.Dot(pose.Dash, view * Vector3.up), Vector3.Dot(pose.Dash, view * Vector3.right)) * Mathf.Rad2Deg);
                else if (Mathf.Abs(Vector3.Dot(pose.Step, view * Vector3.right)) > 1e-4f) gnat.Sprite.flipX = Vector3.Dot(pose.Step, view * Vector3.right) < 0f;
                float sqr = (pose.Position - player.transform.position).sqrMagnitude; ApplyWake(gnat, frame == FrameDash, sqr);
                if (sqr < distance) { nearest = gnat; distance = sqr; }
            }
            Physics.SyncTransforms(); TickSplats(now, view); TickBuzz(nearest, now);
        }
        private bool IsCoopWorldCurrent()
        { return BossRushCoop.HasBoundScene && coopLatest != null && coopLatest.SceneToken == BossRushCoop.SceneToken; }
        private void DisposeCoopGnats()
        {
            if (coopGnatWorld != null) coopGnatWorld.Dispose(); if (coopGnatEnvironment != null) coopGnatEnvironment.Dispose();
            if (coopGnatUse != null) coopGnatUse.Dispose(); if (coopGnatPersonal != null) coopGnatPersonal.Dispose();
            if (coopUseCost != null) coopUseCost.Dispose();
            coopGnatWorld = coopGnatEnvironment = coopGnatUse = null; coopGnatPersonal = null; coopUseCost = null;
            coopPendingUse = null; coopLatest = null; coopEnvironments.Clear(); coopKilled.Clear(); coopRecentKilled.Clear(); coopUses.Clear();
        }
    }

    internal sealed class SkyIslandGnatCoopLife : MonoBehaviour
    {
        private SkyIslandGnats owner; private DamageReceiver receiver; private string key; private bool subscribed;
        internal void Bind(SkyIslandGnats world, string id, DamageReceiver damage)
        {
            Unsubscribe(); owner = world; key = id; receiver = damage;
            if (receiver == null || receiver.OnDeadEvent == null) return;
            receiver.OnDeadEvent.AddListener(OnDead); subscribed = true;
        }
        private void OnDead(DamageInfo info) { if (owner != null) owner.OnCoopGnatKilled(key, info); }
        private void Unsubscribe()
        { if (subscribed && receiver != null && receiver.OnDeadEvent != null) receiver.OnDeadEvent.RemoveListener(OnDead); subscribed = false; }
        private void OnDestroy() { Unsubscribe(); owner = null; receiver = null; }
    }
}
