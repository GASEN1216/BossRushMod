using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>权威战斗事实定向结算到击杀者/受伤者的个人服务；不传输个人存档。</summary>
    internal sealed class CoopPersonalProgressRuntimeModule : BossRushRuntimeModuleBase
    {
        [Serializable] internal sealed class Report { public int Kills, BossKills, Deaths; public float Dealt, Taken, Peak; }
        [Serializable] internal sealed class Kill { public string Key, Mode, Scene; public float Seconds; public long Ticks; }
        [Serializable] private sealed class GardenEligibility { public string Run, Asset; public bool Unlocked; }
        private sealed class Bucket { internal string Id, Asset; internal int Kills, BossKills, Deaths; internal float Dealt, Taken, Peak; }
        private readonly System.Collections.Generic.List<Bucket> buckets = new System.Collections.Generic.List<Bucket>();
        private CoopPersonalDelivery<Report> reports;
        private CoopPersonalDelivery<Kill> codex;
        private CoopWaylayerEscrow waylayer;
        private float timer;
        private float gardenTimer;
        private IDisposable gardenSubscription;
        private readonly System.Collections.Generic.Dictionary<string, GardenEligibility> gardens = new System.Collections.Generic.Dictionary<string, GardenEligibility>(StringComparer.Ordinal);
        private string gardenRun;
        private string statsRun;
        private long sequence;
        private int generation = -1;
        internal static CoopPersonalProgressRuntimeModule Current { get; private set; }
        public override string ModuleName { get { return "CoopPersonalProgress"; } }
        public override void OnAwake(ModBehaviour owner)
        {
            Current = this;
            statsRun = BossRushCoop.RunId;
            reports = new CoopPersonalDelivery<Report>("daily", ApplyReport);
            codex = new CoopPersonalDelivery<Kill>("codex", ApplyKill);
            waylayer = new CoopWaylayerEscrow();
            gardenSubscription = BossRushCoop.Register("personal.garden.eligibility", OnGardenEligibility);
        }
        internal static bool AnyGardenUnlocked
        {
            get
            {
                if (BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Garden)) return true;
                if (Current == null || !BossRushCoop.IsHost) return false;
                foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                { GardenEligibility state; if (player.IsConnected && Current.gardens.TryGetValue(player.Id, out state) && state.Asset == player.AssetId && state.Unlocked) return true; }
                return false;
            }
        }
        private void OnGardenEligibility(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            if (player == null || !player.IsConnected) return;
            GardenEligibility state = message.Read<GardenEligibility>();
            if (state != null && state.Run == BossRushCoop.RunId && !string.IsNullOrEmpty(state.Asset) && state.Asset == player.AssetId)
                gardens[message.SenderId] = state;
        }
        private void RefreshStatsSession()
        {
            if (statsRun == BossRushCoop.RunId) return;
            statsRun = BossRushCoop.RunId;
            buckets.Clear();
        }
        private Bucket Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            RefreshStatsSession();
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(id);
            if (player == null || string.IsNullOrEmpty(player.AssetId)) return null;
            foreach (Bucket bucket in buckets) if (bucket.Id == id && bucket.Asset == player.AssetId) return bucket;
            var created = new Bucket { Id = id, Asset = player.AssetId };
            buckets.Add(created);
            return created;
        }
        internal static void Hurt(Health target, DamageInfo info)
        {
            if (Current == null || !BossRushCoop.IsHost || target == null) return;
            float damage = info.finalDamage;
            if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            CharacterMainControl victim = target.TryGetCharacter();
            Bucket taken = Current.Get(BossRushCoop.GetPlayerId(victim));
            if (taken != null) { taken.Taken += damage; return; }
            if (victim == null || info.fromCharacter == null || !Team.IsEnemy(info.fromCharacter.Team, victim.Team)) return;
            Bucket dealt = Current.Get(BossRushCoop.GetPlayerId(info.fromCharacter));
            if (dealt != null) { dealt.Dealt += damage; dealt.Peak = Mathf.Max(dealt.Peak, damage); }
        }
        internal static void Dead(Health target, DamageInfo info)
        {
            if (Current == null || !BossRushCoop.IsHost || target == null) return;
            CharacterMainControl victim = target.TryGetCharacter();
            Bucket dead = Current.Get(BossRushCoop.GetPlayerId(victim));
            if (dead != null) { dead.Deaths++; return; }
            if (victim == null || info.fromCharacter == null || !Team.IsEnemy(info.fromCharacter.Team, victim.Team)) return;
            Bucket killer = Current.Get(BossRushCoop.GetPlayerId(info.fromCharacter));
            if (killer != null) { killer.Kills++; if (victim.isBossCharacter) killer.BossKills++; }
        }
        internal static void CodexKill(string player, string key, float seconds, string mode, string scene)
        {
            if (Current != null && BossRushCoop.IsHost) Current.codex.Deliver(player,
                "kill:" + (++Current.sequence), new Kill { Key = key, Seconds = seconds, Mode = mode, Scene = scene, Ticks = DateTime.UtcNow.Ticks });
        }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (waylayer != null) waylayer.Tick(unscaledDeltaTime);
            if (!BossRushCoop.IsConnected) return;
            RefreshStatsSession();
            if (gardenRun != BossRushCoop.RunId) { gardenRun = BossRushCoop.RunId; gardens.Clear(); gardenTimer = 0f; }
            if (BossRushCoop.IsClient && (gardenTimer -= unscaledDeltaTime) <= 0f)
            { gardenTimer = 1f; BossRushCoop.SendToHost("personal.garden.eligibility", new GardenEligibility
                { Run = BossRushCoop.RunId, Asset = BossRushCoop.LocalAssetId, Unlocked = BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Garden) }); }
            reports.Tick(unscaledDeltaTime); codex.Tick(unscaledDeltaTime);
            if (generation != BossRushCoop.SceneGeneration)
            {
                generation = BossRushCoop.SceneGeneration;
                // 场景结束时已有结算包由个人递送服务处理；不保留上一张图角色的热路径槽位。
                Flush();
            }
            if (!BossRushCoop.IsHost || (timer -= unscaledDeltaTime) > 0f) return;
            timer = 0.25f; Flush();
        }
        private void Flush()
        {
            if (!BossRushCoop.IsHost) return;
            foreach (Bucket bucket in buckets)
            {
                if (bucket.Id == null || (bucket.Kills == 0 && bucket.Deaths == 0 && bucket.Dealt == 0f && bucket.Taken == 0f)) continue;
                Report report = new Report { Kills = bucket.Kills, BossKills = bucket.BossKills, Deaths = bucket.Deaths,
                    Dealt = bucket.Dealt, Taken = bucket.Taken, Peak = bucket.Peak };
                if (!reports.Deliver(bucket.Id, "stats:" + (++sequence), report, bucket.Asset)) continue;
                bucket.Kills = bucket.BossKills = bucket.Deaths = 0; bucket.Dealt = bucket.Taken = bucket.Peak = 0f;
            }
            buckets.RemoveAll(bucket => bucket.Kills == 0 && bucket.Deaths == 0 && bucket.Dealt == 0f && bucket.Taken == 0f);
        }
        private static bool ApplyReport(Report data)
        {
            if (!DailyReportStatsCollector.IsSubscribed || DailyReportPersistence.HasWriteBarrier || DailyReportPersistence.IsStoreFaulted) return false;
            if (data.Kills < 0 || data.Kills > 4096 || data.Deaths < 0 || data.Deaths > 4096 || data.BossKills < 0 || data.BossKills > data.Kills) return false;
            for (int i = 0; i < data.Kills; i++) DailyReportService.ReportKill(i < data.BossKills);
            for (int i = 0; i < data.Deaths; i++) DailyReportService.ReportPlayerDeath();
            if (data.Dealt > 0f) DailyReportService.ReportCoopDamageDealt(data.Dealt, data.Peak);
            if (data.Taken > 0f) DailyReportService.ReportDamageTaken(data.Taken);
            return true;
        }
        private static bool ApplyKill(Kill data) { return CodexKillCollector.ApplyCoopKill(data.Key, data.Seconds, data.Mode, data.Scene, data.Ticks); }
        public override void OnDestroy()
        {
            if (waylayer != null) waylayer.Dispose(); waylayer = null;
            if (reports != null) reports.Dispose(); if (codex != null) codex.Dispose();
            if (gardenSubscription != null) gardenSubscription.Dispose(); gardenSubscription = null; gardens.Clear(); buckets.Clear();
            reports = null; codex = null;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
