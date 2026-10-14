using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>墓碑事件仍属于本人；房主只持有生成用的临时快照，不读取或保存别人档案。</summary>
    internal sealed partial class DeathWraithRuntimeModule
    {
        // ItemTreeData/CustomData 的官方 SerializeField 保存物品原始变量；大快照分片，避免超过公共包上限。
        [Serializable] private sealed class CoopWraithPart
        { public string Run, Asset, Scene, Key, Data; public Vector3 Position; public int Generation, Part, Count; }
        [Serializable] private sealed class CoopWraithWorld
        { public string Run; public int Generation; public CoopWraithActor[] Actors; }
        [Serializable] private sealed class CoopWraithActor
        { public string Key, Owner, Asset, PlayerName, Fingerprint; public uint Raid; public int Ai, Tier; public bool Dead; }
        [Serializable] private sealed class CoopWraithClear
        { public string Asset, Fingerprint; public uint Raid; }
        private sealed class CoopWraithPending
        {
            internal string Asset, Scene, Key, Fingerprint;
            internal uint Raid; internal Vector3 Position;
            internal string[] Parts; internal int NextPart; internal float NextAt;
        }
        private sealed class CoopWraithAssembly
        { internal string[] Parts; internal int Received; internal float At; }
        private sealed class CoopWraithSpawn
        {
            internal string Key, Owner, Asset, Fingerprint, Run;
            internal int Generation; internal WraithInfo Info; internal Vector3 Position;
            internal CharacterMainControl Character; internal bool Spawning, Dead;
        }
        private readonly Dictionary<string, CoopWraithPending> coopWraithPending = new Dictionary<string, CoopWraithPending>(StringComparer.Ordinal);
        private readonly Dictionary<string, CoopWraithAssembly> coopWraithAssemblies = new Dictionary<string, CoopWraithAssembly>(StringComparer.Ordinal);
        private readonly Dictionary<string, CoopWraithSpawn> coopWraithSpawns = new Dictionary<string, CoopWraithSpawn>(StringComparer.Ordinal);
        private readonly Dictionary<CharacterMainControl, CoopWraithSpawn> coopWraithOwners = new Dictionary<CharacterMainControl, CoopWraithSpawn>();
        private readonly Dictionary<int, CharacterMainControl> coopWraithBound = new Dictionary<int, CharacterMainControl>();
        private IDisposable coopWraithRequests, coopWraithWorldSubscription;
        private CoopPersonalDelivery<CoopWraithClear> coopWraithClears;
        private CoopWraithWorld coopWraithLatest;
        private string coopWraithRun;
        private int coopWraithGeneration = -1;
        private float coopWraithPublishAt;
        private float coopWraithSendAt;

        private void InitializeCoopDeathWraith()
        {
            if (coopWraithRequests != null) return;
            coopWraithRequests = BossRushCoop.Register("wraith.spawn", ReceiveCoopWraithPart);
            coopWraithWorldSubscription = BossRushCoop.Register("wraith.world", ReceiveCoopWraithWorld);
            coopWraithClears = new CoopPersonalDelivery<CoopWraithClear>("wraith.clear", ApplyCoopWraithClear);
        }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!BossRushCoop.IsConnected) return;
            RefreshCoopWraithWorld();
            coopWraithClears.Tick(unscaledDeltaTime);
            TickCoopWraithRequests();
            if (BossRushCoop.IsClient) { BindCoopWraithReplicas(); return; }
            foreach (CoopWraithSpawn entry in new List<CoopWraithSpawn>(coopWraithSpawns.Values))
            {
                if (!entry.Dead && !entry.Spawning && entry.Character == null && BossRushCoop.SessionReady)
                    TrySpawnStoredDeathWraithForRaid_DeathWraith(entry.Info.raidID, entry.Position, entry);
                if (entry.Dead) DeliverCoopWraithClear(entry);
            }
            if (Time.unscaledTime < coopWraithPublishAt) return;
            coopWraithPublishAt = Time.unscaledTime + 0.5f;
            PublishCoopWraithWorld();
            foreach (var pair in new List<KeyValuePair<string, CoopWraithAssembly>>(coopWraithAssemblies))
                if (Time.unscaledTime - pair.Value.At > 30f) coopWraithAssemblies.Remove(pair.Key);
        }
        private void RefreshCoopWraithWorld()
        {
            // 原生重连到 welcome 之间的空 RunId 不能清掉尚未完成分片的墓箱请求。
            if (string.IsNullOrEmpty(BossRushCoop.RunId)) return;
            if (coopWraithRun == BossRushCoop.RunId && coopWraithGeneration == BossRushCoop.SceneGeneration) return;
            // 墓箱回调可能早于新场景 generation；仅保留本槽、已实际到达目标子场景的请求。
            var waiting = new List<CoopWraithPending>(coopWraithPending.Values);
            bool sameRun = string.IsNullOrEmpty(coopWraithRun) || coopWraithRun == BossRushCoop.RunId;
            ClearCoopWraithWorld();
            coopWraithRun = BossRushCoop.RunId; coopWraithGeneration = BossRushCoop.SceneGeneration;
            if (sameRun)
                foreach (CoopWraithPending request in waiting)
                    if (request.Asset == BossRushCoop.LocalAssetId && request.Scene == GetActiveSubSceneId_DeathWraith())
                        coopWraithPending[request.Key] = request;
        }
        private static string CoopWraithFingerprint(string json)
        {
            using (SHA256 hash = SHA256.Create())
            { return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", string.Empty); }
        }
        private static string[] SplitCoopWraithSnapshot(string json)
        {
            var parts = new List<string>();
            for (int offset = 0; offset < json.Length;)
            {
                int count = Math.Min(12000, json.Length - offset);
                // 共用 codec 保留 emoji 原文；不能把 UTF-16 代理对拆进两条 UTF-8 消息。
                if (offset + count < json.Length && char.IsHighSurrogate(json[offset + count - 1])
                    && char.IsLowSurrogate(json[offset + count])) count--;
                parts.Add(json.Substring(offset, count)); offset += count;
            }
            return parts.ToArray();
        }
        private void QueueCoopDeathWraith(uint raid, Vector3 position)
        {
            RefreshCoopWraithWorld();
            string asset = BossRushCoop.LocalAssetId;
            string key = asset + ":" + raid;
            if (string.IsNullOrEmpty(asset) || coopWraithPending.ContainsKey(key)) return;
            coopWraithPending.Add(key, new CoopWraithPending { Key = key, Asset = asset,
                Raid = raid, Scene = GetActiveSubSceneId_DeathWraith(), Position = position });
        }
        private void TickCoopWraithRequests()
        {
            if (!BossRushCoop.SessionReady || !IsDeathWraithSystemEnabled()) return;
            foreach (CoopWraithPending request in new List<CoopWraithPending>(coopWraithPending.Values))
            {
                if (request.Asset != BossRushCoop.LocalAssetId || request.Scene != GetActiveSubSceneId_DeathWraith()) continue;
                if (request.Parts == null)
                {
                    WraithInfo info = FindStoredDeathWraithInfoByRaidId_DeathWraith(request.Raid);
                    if (info == null) continue; // 原档读取失败时保留请求，由原读取写屏障重试。
                    string json = BossRushCoopJson.ToJson(info);
                    request.Fingerprint = CoopWraithFingerprint(json);
                    if (BossRushCoop.IsHost)
                    { AdmitCoopWraith(BossRushCoop.LocalPlayerId, request.Asset, request.Key + ":" + request.Fingerprint, info, request.Fingerprint, request.Position); coopWraithPending.Remove(request.Key); continue; }
                    request.Parts = SplitCoopWraithSnapshot(json);
                }
                if (Time.unscaledTime < request.NextAt || Time.unscaledTime < coopWraithSendAt) continue;
                int index = request.NextPart;
                BossRushCoop.SendToHost("wraith.spawn", new CoopWraithPart { Run = coopWraithRun, Generation = coopWraithGeneration,
                    Asset = request.Asset, Scene = request.Scene, Key = request.Key + ":" + request.Fingerprint, Position = request.Position, Part = index, Count = request.Parts.Length, Data = request.Parts[index] });
                coopWraithSendAt = Time.unscaledTime + 0.05f;
                request.NextPart = (index + 1) % request.Parts.Length;
                if (request.NextPart == 0) request.NextAt = Time.unscaledTime + 1f;
            }
        }
        private void ReceiveCoopWraithPart(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.SessionReady || !IsDeathWraithSystemEnabled()) return;
            RefreshCoopWraithWorld();
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            CoopWraithPart part = message.Read<CoopWraithPart>();
            if (player == null || !player.IsConnected || part == null || part.Run != coopWraithRun || part.Generation != coopWraithGeneration
                || string.IsNullOrEmpty(part.Asset) || part.Asset != player.AssetId || part.Scene != GetActiveSubSceneId_DeathWraith()
                || string.IsNullOrEmpty(part.Key) || part.Key.Length > 192 || !part.Key.StartsWith(part.Asset + ":", StringComparison.Ordinal)
                || part.Count <= 0 || part.Count > 512 || part.Part < 0 || part.Part >= part.Count || part.Data == null || part.Data.Length > 12000
                || !FiniteWraithPoint(part.Position)) return;
            string key = player.Id + ":" + part.Key;
            if (coopWraithSpawns.ContainsKey(key)) { coopWraithPublishAt = 0f; return; }
            CoopWraithAssembly assembly;
            if (!coopWraithAssemblies.TryGetValue(key, out assembly))
            {
                int outstanding = 0;
                foreach (string existing in coopWraithAssemblies.Keys) if (existing.StartsWith(player.Id + ":", StringComparison.Ordinal)) outstanding++;
                if (outstanding >= 16) return;
                assembly = new CoopWraithAssembly { Parts = new string[part.Count] }; coopWraithAssemblies.Add(key, assembly);
            }
            if (assembly.Parts.Length != part.Count) return;
            assembly.At = Time.unscaledTime;
            if (assembly.Parts[part.Part] == null) { assembly.Parts[part.Part] = part.Data; assembly.Received++; }
            if (assembly.Received != part.Count) return;
            string json = string.Concat(assembly.Parts); coopWraithAssemblies.Remove(key);
            WraithInfo info = BossRushCoopJson.FromJson<WraithInfo>(json);
            if (info == null || !info.valid || info.itemTreeData == null || info.subSceneID != part.Scene || !FiniteWraithPosition(info)) return;
            string fingerprint = CoopWraithFingerprint(json);
            if (part.Key != part.Asset + ":" + info.raidID + ":" + fingerprint) return;
            AdmitCoopWraith(player.Id, part.Asset, part.Key, info, fingerprint, part.Position);
        }
        private static bool FiniteWraithPosition(WraithInfo info)
        { return !float.IsNaN(info.posX) && !float.IsInfinity(info.posX) && !float.IsNaN(info.posY) && !float.IsInfinity(info.posY) && !float.IsNaN(info.posZ) && !float.IsInfinity(info.posZ); }
        private static bool FiniteWraithPoint(Vector3 point)
        { return !float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z); }
        private void AdmitCoopWraith(string player, string asset, string requestKey, WraithInfo info, string fingerprint, Vector3 position)
        {
            string key = player + ":" + requestKey;
            if (coopWraithSpawns.ContainsKey(key)) return;
            coopWraithSpawns.Add(key, new CoopWraithSpawn { Key = key, Owner = player, Asset = asset, Fingerprint = fingerprint,
                Info = info, Position = position, Run = coopWraithRun, Generation = coopWraithGeneration });
            coopWraithPublishAt = 0f;
        }
        private bool IsCoopWraithSpawnCurrent(CoopWraithSpawn entry)
        {
            CoopWraithSpawn current;
            return BossRushCoop.IsHost && entry.Run == BossRushCoop.RunId && entry.Generation == BossRushCoop.SceneGeneration
                && coopWraithSpawns.TryGetValue(entry.Key, out current) && ReferenceEquals(entry, current);
        }
        private void RegisterCoopWraith(CoopWraithSpawn entry, CharacterMainControl character)
        {
            entry.Character = character; coopWraithOwners[character] = entry;
            BossRushCoop.RequestAiRegistration(character); coopWraithPublishAt = 0f;
        }
        private bool HandleCoopWraithDeath(Health health)
        {
            if (!BossRushCoop.IsHost || health == null) return false;
            CharacterMainControl character = health.TryGetCharacter(); CoopWraithSpawn entry;
            if (character == null || !coopWraithOwners.TryGetValue(character, out entry)) return false;
            if (entry.Dead) return true;
            entry.Dead = true; DeliverCoopWraithClear(entry);
            DestroyOwnedWraithPresetClone_DeathWraith(character); coopWraithPublishAt = 0f;
            return true;
        }
        private void DeliverCoopWraithClear(CoopWraithSpawn entry)
        {
            coopWraithClears.Deliver(entry.Owner, entry.Key + ":killed", new CoopWraithClear
                { Asset = entry.Asset, Raid = entry.Info.raidID, Fingerprint = entry.Fingerprint }, entry.Asset);
        }
        private bool ApplyCoopWraithClear(CoopWraithClear fact)
        {
            if (fact == null || fact.Asset != BossRushCoop.LocalAssetId) return false;
            List<WraithInfo> records = LoadStoredDeathWraithInfos_DeathWraith();
            if (records == null) return false;
            WraithInfo original = FindStoredDeathWraithInfoByRaidId_DeathWraith(fact.Raid);
            if (original != null && CoopWraithFingerprint(BossRushCoopJson.ToJson(original)) != fact.Fingerprint) return true;
            if (original != null) RemoveStoredDeathWraithInfoByRaidId_DeathWraith(fact.Raid, "联机亡魂被击杀");
            FlushDeathWraithListIfDirty_DeathWraith();
            return !_deathWraithListDirty; // 缓存写失败时继续重发，不能提前确认个人删除。
        }
        private void PublishCoopWraithWorld()
        {
            var actors = new List<CoopWraithActor>(coopWraithSpawns.Count);
            foreach (CoopWraithSpawn entry in coopWraithSpawns.Values)
                actors.Add(new CoopWraithActor { Key = entry.Key, Owner = entry.Owner, Asset = entry.Asset, Raid = entry.Info.raidID,
                    Ai = BossRushCoop.GetAiId(entry.Character), Dead = entry.Dead, PlayerName = entry.Info.playerName,
                    Fingerprint = entry.Fingerprint, Tier = (int)ClassifyWraithTier_DeathWraith(entry.Info.droppedItemsValue, entry.Info.playerTotalWealth) });
            BossRushCoop.Broadcast("wraith.world", new CoopWraithWorld { Run = coopWraithRun, Generation = coopWraithGeneration, Actors = actors.ToArray() });
        }
        private void ReceiveCoopWraithWorld(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            RefreshCoopWraithWorld();
            CoopWraithWorld world = message.Read<CoopWraithWorld>();
            if (world == null || world.Run != coopWraithRun || world.Generation != coopWraithGeneration || world.Actors == null || world.Actors.Length > 4096) return;
            coopWraithLatest = world;
        }
        private void BindCoopWraithReplicas()
        {
            if (coopWraithLatest == null) return;
            foreach (CoopWraithActor entry in coopWraithLatest.Actors)
            {
                if (entry == null) continue;
                CoopWraithPending request;
                string pendingKey = entry.Asset + ":" + entry.Raid;
                if (entry.Owner == BossRushCoop.LocalPlayerId && coopWraithPending.TryGetValue(pendingKey, out request)
                    && request.Fingerprint == entry.Fingerprint) coopWraithPending.Remove(pendingKey);
                if (entry.Ai == 0) continue;
                CharacterMainControl replica = BossRushCoop.FindAi(entry.Ai); if (replica == null) continue;
                CharacterMainControl bound;
                if (coopWraithBound.TryGetValue(entry.Ai, out bound) && ReferenceEquals(bound, replica)) continue;
                CoopAiPredictionHealthProtection.MarkReplica(replica);
                replica.gameObject.name = "BossRush_DeathWraith_" + entry.Key;
                replica.dropBoxOnDead = false;
                if (replica.Health != null) replica.Health.showHealthBar = true;
                NPCNameTagHelper.RegisterOriginalHealthBarName(replica.transform,
                    GetWraithDisplayName_DeathWraith(entry.PlayerName, (WraithTier)entry.Tier), 2.2f, "[CoopWraith]");
                coopWraithBound[entry.Ai] = replica;
            }
        }
        private void ClearCoopWraithWorld()
        {
            foreach (CoopWraithSpawn entry in coopWraithSpawns.Values)
                if (entry.Character != null) DestroyWraithInstance_DeathWraith(entry.Character, "联机场景清理");
            coopWraithSpawns.Clear(); coopWraithOwners.Clear(); coopWraithAssemblies.Clear();
            coopWraithPending.Clear(); coopWraithBound.Clear(); coopWraithLatest = null;
        }
        private void DisposeCoopDeathWraith()
        {
            ClearCoopWraithWorld();
            if (coopWraithRequests != null) coopWraithRequests.Dispose();
            if (coopWraithWorldSubscription != null) coopWraithWorldSubscription.Dispose();
            if (coopWraithClears != null) coopWraithClears.Dispose();
            coopWraithRequests = coopWraithWorldSubscription = null; coopWraithClears = null;
        }
    }
}
