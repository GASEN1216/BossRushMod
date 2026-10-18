using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopArenaState
    {
        public bool Active, ArenaActive, ArenaPlanned, Infinite, Waiting;
        public int Wave, Total, Defeated, BossesPerWave, Remaining, WaveTotal, InfiniteWave, EntryMode;
        public float Countdown;
        public string NextBoss;
        public string CurrentBoss;
        public string[] Bosses;
        public long CashPool;
        public string Epoch;
        public int RunSequence;
        public string[] WavePlayers, WaveAssets;
    }

    [Serializable]
    internal sealed class CoopArenaPersonal
    {
        public string Kind, Epoch, BossType;
        public int Wave, Difficulty, RunSequence;
        public long Cash;
        public int ItemType, Milestone;
    }

    internal sealed partial class WavesArenaRuntimeModule
    {
        private CoopGameplayChannel<CoopArenaState> coopChannel;
        private CoopPersonalDelivery<CoopArenaPersonal> coopPersonal;
        private readonly Dictionary<string, string> coopArenaParticipants = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> coopArenaWaveParticipants = new Dictionary<string, string>(StringComparer.Ordinal);
        private string coopArenaEpoch, coopArenaLocalEpoch, coopArenaLocalAsset;
        internal string CampaignEntryEpoch { get { return coopArenaEpoch; } }
        internal bool TryGetCoopCampaignParticipantAsset(string expectedEpoch, string playerId, out string assetId)
        {
            assetId = null;
            return expectedEpoch == coopArenaEpoch && coopArenaWaveParticipants.TryGetValue(playerId, out assetId);
        }
        private string coopArenaRoom;
        private string coopArenaProjectionRoom;
        private string coopArenaEntryToken, coopArenaEntryScheduledToken;
        private int coopArenaEntryHandle;

        private void RetireLostCoopArenaProjection()
        {
            if (coopArenaProjectionRoom == null || (BossRushCoop.IsClient && coopArenaProjectionRoom == BossRushCoop.RunId)) return;
            coopArenaProjectionRoom = null;
            waveGeneration++;
            SetBossRushRuntimeActive(false);
            ArenaActive = ArenaPlanned = WaitingForNextWave = false;
            WaveCountdown = 0f; CoopEntryMode = 0;
            coopArenaEntryToken = coopArenaEntryScheduledToken = null;
            milestoneDelivery = null;
            foreach (MonoBehaviour replica in CurrentWaveBosses)
                if (replica != null && replica.gameObject != null)
                { replica.gameObject.SetActive(false); UnityEngine.Object.Destroy(replica.gameObject); }
            CurrentWaveBosses.Clear(); CurrentBoss = null;
        }
        private int coopArenaRunSequence;
        private long coopArenaFactSequence;
        private readonly HashSet<string> coopArenaBossFacts = new HashSet<string>(StringComparer.Ordinal);
        private string coopArenaVictoryEpoch;
        internal int CoopEntryMode { get; private set; }
        internal void SetCoopEntryMode(int mode) { if (BossRushCoop.HasWorldAuthority) CoopEntryMode = mode; }

        internal void TryCreateArenaRescueTeleport(Action createRescue)
        {
            try { createRescue(); }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush Coop] Rescue teleport creation failed: " + e.Message); }
        }

        internal IEnumerator SetupCoopClientEntry(Scene scene, Action createEntry, Action continueEntry, Action createRescue)
        {
            string room = BossRushCoop.RunId;
            while (BossRushCoop.IsClient && BossRushCoop.RunId == room && scene.isLoaded && SceneManager.GetActiveScene().handle == scene.handle
                && (!BossRushCoop.HasBoundScene || coopArenaEntryToken != BossRushCoop.SceneToken || CoopEntryMode == 0))
                yield return null;
            if (!BossRushCoop.IsClient || BossRushCoop.RunId != room || !scene.isLoaded || SceneManager.GetActiveScene().handle != scene.handle) yield break;
            // 客端只接房主选择的世界，各模式仍管理个人装备与本地界面。
            if (BossRushCoop.IsClient && BossRushCoop.RunId == room && scene.isLoaded && SceneManager.GetActiveScene().handle == scene.handle && (CoopEntryMode == 1 || CoopEntryMode == 2))
            {
                // 原生联机投票会跳过 WaitForConfirm/InitialSpawn；仅首次本地设施
                // 初始化复用房主普通/白手起家落点，不持续拉回，不操作其他玩家。
                CharacterMainControl local = CharacterMainControl.Main;
                string token = BossRushCoop.SceneToken;
                int generation = BossRushCoop.SceneGeneration;
                string asset = BossRushCoop.LocalAssetId;
                if (local == null || local.Health == null || local.Health.IsDead || !BossRushCoop.HasBoundScene
                    || string.IsNullOrEmpty(asset) || coopArenaEntryToken != token) yield break;
                bool aligned = false;
                try
                {
                    Vector3 target = ModBehaviour.GetDefaultPositionForScene(scene.name);
                    if (local.gameObject.scene != scene) SceneManager.MoveGameObjectToScene(local.gameObject, scene);
                    if (!BossRushInitialSpawn.HasArrived(target) && (local.transform.position - target).sqrMagnitude > 0.25f)
                        local.SetPosition(target);
                    aligned = true;
                    ModBehaviour.DevLog("[BossRush Coop] 客端首次竞技场落点: 本机=" + BossRushCoop.LocalPlayerId
                        + ", 身体=" + local.GetInstanceID() + ", 目标=" + target + ", 实际=" + local.transform.position);
                }
                catch (Exception e) { Debug.LogWarning("[BossRush Coop] 客端竞技场落点失败: " + e.Message); }
                if (!aligned || !BossRushCoop.IsClient || BossRushCoop.RunId != room || !BossRushCoop.HasBoundScene
                    || BossRushCoop.SceneToken != token || coopArenaEntryToken != token || BossRushCoop.SceneGeneration != generation
                    || BossRushCoop.LocalAssetId != asset
                    || !scene.isLoaded || SceneManager.GetActiveScene().handle != scene.handle
                    || local == null || CharacterMainControl.Main != local || local.Health == null || local.Health.IsDead) yield break;
                createEntry();
                continueEntry();
                createRescue();
            }
            if (CoopEntryMode != 3 && CoopEntryMode != 4) BossRushMapSelectionHelper.ClearPendingEntryFlowState();
        }

        private void ScheduleCoopArenaEntry()
        {
            if (!BossRushCoop.IsClient || !BossRushCoop.HasBoundScene || !ArenaActive || (CoopEntryMode != 1 && CoopEntryMode != 2)
                || string.IsNullOrEmpty(BossRushCoop.SceneToken)) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded || (coopArenaEntryScheduledToken == BossRushCoop.SceneToken && coopArenaEntryHandle == scene.handle)) return;
            coopArenaEntryScheduledToken = BossRushCoop.SceneToken;
            coopArenaEntryHandle = scene.handle;
            owner.StartCoopArenaEntryForScene(scene);
        }

        private void InitializeCoopArena()
        {
            coopChannel = new CoopGameplayChannel<CoopArenaState>("Arena", CaptureCoopArena, ApplyCoopArena, ExecuteCoopArena);
            coopPersonal = new CoopPersonalDelivery<CoopArenaPersonal>("Arena", ApplyCoopArenaPersonal);
        }

        private void BeginCoopArenaRun()
        {
            if (!BossRushCoop.IsConnected || !BossRushCoop.IsHost) return;
            RefreshCoopArenaRoom();
            coopArenaRunSequence++;
            coopArenaEpoch = Guid.NewGuid().ToString("N");
            coopArenaBossFacts.Clear();
            coopArenaFactSequence = 0;
            CaptureCoopArenaParticipants(coopArenaParticipants);
            CaptureCoopArenaWaveParticipants();
            coopArenaLocalEpoch = coopArenaEpoch;
            coopArenaLocalAsset = BossRushCoop.LocalAssetId;
            foreach (var player in coopArenaParticipants)
                if (player.Key != BossRushCoop.LocalPlayerId)
                    coopPersonal.Deliver(player.Key, coopArenaEpoch + ":begin", new CoopArenaPersonal
                    { Kind = InfiniteHellMode ? "beginHell" : "begin", Epoch = coopArenaEpoch, RunSequence = coopArenaRunSequence }, player.Value);
        }

        private void RefreshCoopArenaRoom()
        {
            if (coopArenaRoom == BossRushCoop.RunId) return;
            coopArenaRoom = BossRushCoop.RunId;
            coopArenaRunSequence = 0; coopArenaEpoch = coopArenaLocalEpoch = coopArenaLocalAsset = null;
            coopArenaParticipants.Clear(); coopArenaWaveParticipants.Clear(); coopArenaBossFacts.Clear();
        }

        private static void CaptureCoopArenaParticipants(Dictionary<string, string> participants)
        {
            participants.Clear();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (player.IsAlive && !string.IsNullOrEmpty(player.AssetId)) participants[player.Id] = player.AssetId;
        }

        private void CaptureCoopArenaWaveParticipants()
        {
            if (!BossRushCoop.IsHost) return;
            coopArenaWaveParticipants.Clear();
            foreach (var entry in coopArenaParticipants)
            {
                BossRushCoopPlayer player = BossRushCoop.FindPlayer(entry.Key);
                if (player != null && player.IsAlive && player.AssetId == entry.Value)
                {
                    coopArenaWaveParticipants.Add(entry.Key, entry.Value);
                    if (coopPersonal != null) coopPersonal.Deliver(entry.Key, coopArenaEpoch + ":begin", new CoopArenaPersonal
                    { Kind = InfiniteHellMode ? "beginHell" : "begin", Epoch = coopArenaEpoch, RunSequence = coopArenaRunSequence }, entry.Value);
                }
            }
        }

        private int CoopArenaRewardCopies
        {
            get { return BossRushCoop.IsConnected ? coopArenaWaveParticipants.Count : 1; }
        }

        internal bool HasLivingCoopArenaParticipant()
        {
            if (owner != null && owner.IsModeDActive) return ModeDRuntimeModule.HasLivingCoopParticipant(owner);
            if (string.IsNullOrEmpty(coopArenaEpoch)) return BossRushCoop.AnyPlayerAlive;
            foreach (var entry in InfiniteHellMode ? coopArenaWaveParticipants : coopArenaParticipants)
            {
                BossRushCoopPlayer player = BossRushCoop.FindPlayer(entry.Key);
                if (player != null && player.IsAlive && player.AssetId == entry.Value) return true;
            }
            return false;
        }

        private bool TryBeginCoopArenaVictory()
        {
            if (!BossRushCoop.IsConnected) return true;
            if (string.IsNullOrEmpty(coopArenaEpoch) || coopArenaVictoryEpoch == coopArenaEpoch) return false;
            coopArenaVictoryEpoch = coopArenaEpoch;
            return true;
        }

        private void QueueCoopArenaVictoryCopies(int quality)
        {
            if (!BossRushCoop.IsHost) return;
            DeliverCoopArenaReward("box", 0, quality, 0, 0);
        }

        private void TickCoopArenaVictoryCopies()
        {
            TickCoopArenaRewards();
        }

        private void DeliverCoopArenaFact(string kind, int wave = 0, string bossType = null)
        {
            if (!BossRushCoop.IsHost || string.IsNullOrEmpty(coopArenaEpoch)) return;
            string id = coopArenaEpoch + ":" + kind + ":" + (++coopArenaFactSequence);
            var cohort = coopArenaWaveParticipants;
            foreach (var participant in cohort)
                coopPersonal.Deliver(participant.Key, id, new CoopArenaPersonal
                { Kind = kind, Epoch = coopArenaEpoch, Wave = wave, BossType = bossType, Difficulty = BossesPerWave, RunSequence = coopArenaRunSequence }, participant.Value);
        }

        private void ReportCoopArenaBoss(CharacterMainControl boss)
        {
            if (!BossRushCoop.IsHost || boss == null) return;
            string entity = BossRushCoop.GetEntityId(boss) ?? ("instance:" + boss.GetInstanceID());
            if (!coopArenaBossFacts.Add(entity)) return;
            string name = (boss.name ?? string.Empty).ToLowerInvariant();
            string type = name.Contains("dragondescendant") || name.Contains("dragon_descendant") || name.Contains("龙裔") ? "DragonDescendant"
                : name.Contains("dragonking") || name.Contains("dragon_king") || name.Contains("龙王") || name.Contains("焚天") ? "DragonKing" : "Normal";
            if (owner.IsModeDActive) { ModeDRuntimeModule.ReportCoopBoss(owner, boss, type); return; }
            // 包含房主：所有个人成绩都按本局冻结资产交付，换槽不继承旧击杀。
            string id = coopArenaEpoch + ":boss:" + entity;
            foreach (var participant in coopArenaWaveParticipants)
                coopPersonal.Deliver(participant.Key, id, new CoopArenaPersonal { Kind = "boss", Epoch = coopArenaEpoch, BossType = type, Difficulty = BossesPerWave, RunSequence = coopArenaRunSequence }, participant.Value);
        }

        private bool ApplyCoopArenaPersonal(CoopArenaPersonal fact)
        {
            if (owner == null || fact == null) return false;
            RefreshCoopArenaRoom();
            if (fact.Kind == "box" || fact.Kind == "waveReward" || fact.Kind == "milestone") return ApplyCoopArenaReward(fact);
            if (fact.Kind == "begin" || fact.Kind == "beginHell")
            {
                // 新局的状态先抵达时不能由迟到旧 begin 重置个人无伤计时。
                if (fact.RunSequence < coopArenaRunSequence) return true;
                coopArenaRunSequence = fact.RunSequence; coopArenaEpoch = fact.Epoch;
                if (coopArenaLocalEpoch != fact.Epoch || coopArenaLocalAsset != BossRushCoop.LocalAssetId)
                {
                    owner.BeginAchievementSessionForArena(fact.Kind == "beginHell" ? "InfiniteHell" : "BossRush");
                    coopArenaLocalEpoch = fact.Epoch; coopArenaLocalAsset = BossRushCoop.LocalAssetId;
                }
                return true;
            }
            bool currentSession = coopArenaLocalEpoch == fact.Epoch && coopArenaLocalAsset == BossRushCoop.LocalAssetId;
            bool damage = AchievementTracker.HasTakenDamage, healed = AchievementTracker.HasUsedHealItem;
            float entered = AchievementTracker.ArenaEnterTime;
            int difficulty = BossesPerWave;
            try
            {
                // 成绩迟到跨局仍记累计；未保存该局的个人观测时不能凭新局无伤解锁旧局无伤/速通。
                if (!currentSession) { AchievementTracker.HasTakenDamage = AchievementTracker.HasUsedHealItem = true; AchievementTracker.ArenaEnterTime = Time.time - 3600f; }
                BossesPerWave = Math.Max(1, fact.Difficulty);
                if (fact.Kind == "boss") owner.CheckBossKillAchievementsOnceForArena(null, fact.BossType);
                else if (fact.Kind == "clear") { owner.CheckClearAchievementsForArena(); owner.NotifyCourierBossRushCompleted(); }
                else if (fact.Kind == "hell") owner.CheckInfiniteHellAchievementsForArena(fact.Wave);
                else return true;
                return true;
            }
            finally
            {
                BossesPerWave = difficulty;
                if (!currentSession) { AchievementTracker.HasTakenDamage = damage; AchievementTracker.HasUsedHealItem = healed; AchievementTracker.ArenaEnterTime = entered; }
            }
        }

        internal bool RequestCoopArena(string action, int value = 0, bool flag = false)
        {
            return coopChannel != null && coopChannel.Request(action, value, null, flag);
        }

        private CoopArenaState CaptureCoopArena()
        {
            List<string> bosses = new List<string>();
            foreach (MonoBehaviour boss in CurrentWaveBosses)
            {
                string id = BossRushCoop.GetEntityId(boss as CharacterMainControl);
                if (!string.IsNullOrEmpty(id)) bosses.Add(id);
            }
            return new CoopArenaState
            {
                Active = IsActive, ArenaActive = ArenaActive, ArenaPlanned = ArenaPlanned, EntryMode = CoopEntryMode,
                Infinite = InfiniteHellMode, Waiting = WaitingForNextWave, Wave = CurrentEnemyIndex,
                Total = TotalEnemies, Defeated = DefeatedEnemies, BossesPerWave = BossesPerWave,
                Remaining = BossesInCurrentWaveRemaining, WaveTotal = BossesInCurrentWaveTotal,
                InfiniteWave = InfiniteHellWaveIndex, Countdown = WaveCountdown, NextBoss = NextWaveBossName,
                CurrentBoss = BossRushCoop.GetEntityId(CurrentBoss as CharacterMainControl),
                Bosses = bosses.ToArray(), CashPool = InfiniteHellCashPool, Epoch = coopArenaEpoch, RunSequence = coopArenaRunSequence,
                WavePlayers = new List<string>(coopArenaWaveParticipants.Keys).ToArray(), WaveAssets = new List<string>(coopArenaWaveParticipants.Values).ToArray()
            };
        }

        private void ApplyCoopArena(CoopArenaState state)
        {
            RetireLostCoopArenaProjection();
            if (BossRushCoop.IsClient && (state.Active || state.ArenaActive || state.ArenaPlanned)) coopArenaProjectionRoom = BossRushCoop.RunId;
            RefreshCoopArenaRoom();
            if (state.RunSequence < coopArenaRunSequence) return;
            coopArenaRunSequence = state.RunSequence;
            CoopEntryMode = state.EntryMode;
            coopArenaEntryToken = BossRushCoop.SceneToken;
            coopArenaEpoch = state.Epoch;
            coopArenaWaveParticipants.Clear();
            if (state.WavePlayers != null && state.WaveAssets != null && state.WavePlayers.Length == state.WaveAssets.Length)
                for (int i = 0; i < state.WavePlayers.Length; i++)
                    if (!string.IsNullOrEmpty(state.WavePlayers[i]) && !string.IsNullOrEmpty(state.WaveAssets[i])) coopArenaWaveParticipants[state.WavePlayers[i]] = state.WaveAssets[i];
            bool started = !IsActive && state.Active;
            bool ended = IsActive && !state.Active;
            IsActive = state.Active;
            ArenaActive = state.ArenaActive;
            ArenaPlanned = state.ArenaPlanned;
            InfiniteHellMode = state.Infinite;
            WaitingForNextWave = state.Waiting;
            CurrentEnemyIndex = state.Wave;
            TotalEnemies = state.Total;
            DefeatedEnemies = state.Defeated;
            BossesPerWave = Math.Max(1, state.BossesPerWave);
            BossesInCurrentWaveRemaining = state.Remaining;
            BossesInCurrentWaveTotal = state.WaveTotal;
            InfiniteHellWaveIndex = state.InfiniteWave;
            WaveCountdown = state.Countdown;
            NextWaveBossName = state.NextBoss;
            InfiniteHellCashPool = state.CashPool;
            ScheduleCoopArenaEntry();
            CurrentBoss = BossRushCoop.FindEntity(state.CurrentBoss);
            CoopAiPredictionHealthProtection.MarkReplica(CurrentBoss as CharacterMainControl);
            CurrentWaveBosses.Clear();
            if (state.Bosses != null)
            {
                foreach (string id in state.Bosses)
                {
                    CharacterMainControl boss = BossRushCoop.FindEntity(id);
                    if (boss != null) { CoopAiPredictionHealthProtection.MarkReplica(boss); CurrentWaveBosses.Add(boss); }
                }
            }
            if (started) owner.ShowMessage(L10n.T("BossRush 挑战开始！", "BossRush challenge started!"));
            if (ended && owner.ArenaRewardSignInteract != null)
            {
                if (state.Defeated >= state.Total && state.Total > 0) owner.ArenaRewardSignInteract.SetVictoryMode();
            }
        }

        private void ExecuteCoopArena(CoopGameplayCommand command)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            if (player == null || !player.IsAlive || owner == null) return;
            if (IsActive && ArenaActive && !string.IsNullOrEmpty(player.AssetId)
                && (command.Action == "enter" || command.Action == "configure" || command.Action == "first" || command.Action == "next"))
            {
                // 原路牌的明确操作即为加入；本波已冻结，只从下一波取得完整个人份额。
                coopArenaParticipants[player.Id] = player.AssetId;
            }
            switch (command.Action)
            {
                case "enter":
                    if (!owner.IsActive && !ArenaActive) owner.StartBossRush();
                    break;
                case "configure":
                    if (!owner.IsActive && owner.IsCurrentSceneValidBossRushArena())
                        ConfigureBossRushMode(Mathf.Clamp(command.Value, 1, 10), command.Flag);
                    break;
                case "first":
                    if (owner.IsCurrentSceneValidBossRushArena()) StartFirstWave();
                    break;
                case "next":
                    if (owner.IsActive && WaitingForNextWave) SpawnNextEnemy();
                    break;
            }
        }
    }
}
