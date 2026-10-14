using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopModeDState
    {
        public bool Active, Complete;
        public int Wave, Expected, Resolved;
        public string Epoch;
        public string[] Enemies;
    }

    [Serializable]
    internal sealed class CoopModeDPersonal
    {
        public string Epoch, Kind, BossType;
        public int Wave;
    }

    internal sealed partial class ModeDRuntimeModule
    {
        private CoopGameplayChannel<CoopModeDState> coopChannel;
        private CoopPersonalDelivery<CoopModeDPersonal> coopPersonal;
        private string coopEpoch;
        private string coopDeliveredEpoch;
        private string coopAchievementEpoch;
        private readonly Dictionary<string, string> coopWaveParticipants = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> coopBossFacts = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> coopModeDParticipants = new Dictionary<string, string>(StringComparer.Ordinal);
        private string coopModeDParticipantEpoch;
        private string coopModeDLocalAsset;
        private float coopModeDAdmissionRetry, coopModeDRollbackRetry;
        private readonly Dictionary<string, bool> coopModeDAdmissionDecisions = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> coopModeDAdmitted = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> coopModeDStarterAttempts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<Item> coopModeDStarterRollback = new List<Item>();

        private void RefreshCoopModeDParticipants()
        {
            if (coopModeDParticipantEpoch == coopEpoch) return;
            coopModeDParticipantEpoch = coopEpoch;
            coopModeDParticipants.Clear();
            coopModeDAdmitted.Clear(); coopModeDStarterAttempts.Clear();
            coopModeDLocalAsset = BossRushCoop.LocalAssetId;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (player.IsAlive && !string.IsNullOrEmpty(player.AssetId)) coopModeDParticipants[player.Id] = player.AssetId;
            if (coopDeliveredEpoch == coopEpoch + ":" + coopModeDLocalAsset)
                coopModeDAdmitted[BossRushCoop.LocalPlayerId] = coopModeDLocalAsset;
        }

        internal static void ReportCoopBoss(ModBehaviour expectedOwner, CharacterMainControl boss, string bossType)
        {
            if (!BossRushCoop.IsHost || current == null || current.owner != expectedOwner || !current.modeDActive || boss == null) return;
            string id = current.coopEpoch + ":boss:" + (BossRushCoop.GetEntityId(boss) ?? ("instance:" + boss.GetInstanceID()));
            if (!current.coopBossFacts.Add(id)) return;
            foreach (var player in current.coopWaveParticipants)
                current.coopPersonal.Deliver(player.Key, id, new CoopModeDPersonal
                { Epoch = current.coopEpoch, Kind = "boss", BossType = bossType }, player.Value);
        }

        private void CaptureCoopModeDWaveParticipants()
        {
            if (!BossRushCoop.IsHost) return;
            RefreshCoopModeDParticipants();
            coopWaveParticipants.Clear();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                string asset;
                string admitted;
                if (player.IsAlive && coopModeDParticipants.TryGetValue(player.Id, out asset) && asset == player.AssetId
                    && coopModeDAdmitted.TryGetValue(player.Id, out admitted) && admitted == asset)
                    coopWaveParticipants[player.Id] = asset;
            }
        }

        internal static bool HasLivingCoopParticipant(ModBehaviour expectedOwner)
        {
            if (current == null || current.owner != expectedOwner || !current.modeDActive) return BossRushCoop.AnyPlayerAlive;
            foreach (var entry in current.coopModeDParticipants)
            {
                BossRushCoopPlayer player = BossRushCoop.FindPlayer(entry.Key);
                if (player != null && player.IsAlive && player.AssetId == entry.Value) return true;
            }
            return false;
        }

        private void DeliverCoopModeDWaveComplete()
        {
            if (!BossRushCoop.IsHost) return;
            foreach (var player in coopWaveParticipants)
                coopPersonal.Deliver(player.Key, coopEpoch + ":wave:" + modeDWaveIndex, new CoopModeDPersonal
                { Epoch = coopEpoch, Kind = "wave", Wave = modeDWaveIndex }, player.Value);
        }

        private void InitializeCoopModeD()
        {
            coopChannel = new CoopGameplayChannel<CoopModeDState>("ModeD", CaptureCoopModeD, ApplyCoopModeD, ExecuteCoopModeD);
            coopPersonal = new CoopPersonalDelivery<CoopModeDPersonal>("ModeD", ApplyCoopModeDStarter);
        }

        private string coopProjectionRoom;
        private bool coopProjectionRetiring;

        private void RetireLostCoopProjection()
        {
            if (coopProjectionRoom == null || (BossRushCoop.IsClient && coopProjectionRoom == BossRushCoop.RunId)) return;
            // 原客端镜像只属于原房间；网络停止或换房后先撤销，不能提升为单机世界。
            coopProjectionRetiring = true;
            try
            {
                generation++;
                            CleanupModeDRuntimeOnDestroy();
            }
            finally { coopProjectionRetiring = false; coopProjectionRoom = null; }
        }

        private void TickCoopModeD(float deltaTime)
        {
            RetireLostCoopProjection();
            coopChannel.Tick(deltaTime);
            coopPersonal.Tick(deltaTime);
            TickCoopModeDStarterRollback(deltaTime);
            coopModeDAdmissionRetry -= Mathf.Max(0f, deltaTime);
            bool decision;
            if (BossRushCoop.IsClient && modeDActive && coopModeDLocalAsset == BossRushCoop.LocalAssetId && coopModeDAdmissionRetry <= 0f
                && coopModeDAdmissionDecisions.TryGetValue(coopEpoch + ":" + coopModeDLocalAsset, out decision))
            { coopModeDAdmissionRetry = 1f; coopChannel.Request("admission", text: coopEpoch, flag: decision); }
            if (!BossRushCoop.IsHost || !modeDActive) return;
            RefreshCoopModeDParticipants();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                string asset;
                if (!coopModeDParticipants.TryGetValue(player.Id, out asset))
                { asset = player.AssetId; if (!string.IsNullOrEmpty(asset)) coopModeDParticipants[player.Id] = asset; }
                if (player.IsAlive && asset == player.AssetId)
                {
                    int attempt; coopModeDStarterAttempts.TryGetValue(player.Id, out attempt);
                    coopPersonal.Deliver(player.Id, "start:" + coopEpoch + ":" + attempt, new CoopModeDPersonal { Epoch = coopEpoch }, asset);
                }
            }
        }

        private bool ApplyCoopModeDStarter(CoopModeDPersonal receipt)
        {
            if (receipt.Kind == "wave" || receipt.Kind == "boss")
            {
                if (owner == null) return false;
                bool oldSession = receipt.Epoch != coopEpoch || coopAchievementEpoch != receipt.Epoch + ":" + BossRushCoop.LocalAssetId;
                bool damage = AchievementTracker.HasTakenDamage, picked = AchievementTracker.HasPickedUpItem;
                float entered = AchievementTracker.ArenaEnterTime;
                try
                {
                    if (oldSession) { AchievementTracker.HasTakenDamage = AchievementTracker.HasPickedUpItem = true; AchievementTracker.ArenaEnterTime = Time.time - 3600f; }
                    if (receipt.Kind == "boss") owner.CheckBossKillAchievementsOnceForArena(null, receipt.BossType);
                    else
                    {
                        if (receipt.Wave >= 5) owner.CheckModeDFlawlessAchievementForRuntime();
                        if (receipt.Wave >= 10) owner.CheckModeDClearAchievementsForRuntime();
                    }
                    return true;
                }
                finally
                {
                    if (oldSession) { AchievementTracker.HasTakenDamage = damage; AchievementTracker.HasPickedUpItem = picked; AchievementTracker.ArenaEnterTime = entered; }
                }
            }
            if (!modeDActive || receipt.Epoch != coopEpoch || CharacterMainControl.Main == null) return false;
            string deliveryTag = receipt.Epoch + ":" + BossRushCoop.LocalAssetId;
            if (coopDeliveredEpoch == deliveryTag) return true;
            if (coopModeDLocalAsset != BossRushCoop.LocalAssetId) return false;
            bool admitted;
            if (!coopModeDAdmissionDecisions.TryGetValue(deliveryTag, out admitted))
            {
                admitted = TryCommitCoopModeDStarter();
                coopModeDAdmissionDecisions[deliveryTag] = admitted;
                if (admitted) coopDeliveredEpoch = deliveryTag;
            }
            if (BossRushCoop.IsHost)
            { if (admitted) coopModeDAdmitted[BossRushCoop.LocalPlayerId] = coopModeDLocalAsset; }
            else coopChannel.Request("admission", text: coopEpoch, flag: admitted);
            if (admitted && modeDWaveIndex > 0 && coopAchievementEpoch != deliveryTag)
            { owner.BeginAchievementSessionForArena("ModeD"); coopAchievementEpoch = deliveryTag; }
            return true;
        }

        // 发装只操作捕获的角色根；回调里发生切槽时也不扫描或删除新槽物品。
        private static List<Item> CollectCoopModeDStarterItems(Item root)
        {
            var items = new List<Item>();
            if (root == null) return items;
            if (root.Inventory != null && root.Inventory.Content != null)
                foreach (Item item in root.Inventory.Content) if (item != null && !items.Contains(item)) items.Add(item);
            if (root.Slots != null)
                foreach (var slot in root.Slots) if (slot != null && slot.Content != null && !items.Contains(slot.Content)) items.Add(slot.Content);
            return items;
        }

        private bool TryCommitCoopModeDStarter()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead || player.CharacterItem == null
                || coopModeDStarterRollback.Count != 0 || owner == null || !owner.IsPlayerNaked()
                || owner.IsModeEActive || !ModeHRuntimeGates.IsLegacyModeEntryAllowed()) return false;
            string asset = BossRushCoop.LocalAssetId;
            Item root = player.CharacterItem;
            var original = new HashSet<Item>(CollectCoopModeDStarterItems(root));
            bool committed = false;
            try
            {
                ItemPool.InitializeModeDItemPools(ItemPool.FindTagByName);
                committed = ItemPool.GivePlayerStarterKit() && BossRushCoop.LocalAssetId == asset
                    && CharacterMainControl.Main == player && player.CharacterItem == root;
                return committed;
            }
            catch (Exception e) { ModBehaviour.DevLog("[ModeD/Coop] 开局发装失败: " + e.Message); return false; }
            finally
            {
                if (!committed)
                {
                    foreach (Item item in CollectCoopModeDStarterItems(root))
                        if (!original.Contains(item) && !coopModeDStarterRollback.Contains(item)) coopModeDStarterRollback.Add(item);
                    TickCoopModeDStarterRollback(1f);
                }
            }
        }

        private void TickCoopModeDStarterRollback(float deltaTime)
        {
            if (coopModeDStarterRollback.Count == 0) return;
            coopModeDRollbackRetry -= Mathf.Max(0f, deltaTime);
            if (coopModeDRollbackRetry > 0f) return;
            coopModeDRollbackRetry = 1f;
            for (int i = coopModeDStarterRollback.Count - 1; i >= 0; i--)
            {
                Item item = coopModeDStarterRollback[i];
                try { if (item != null) { item.Detach(); item.DestroyTree(); } coopModeDStarterRollback.RemoveAt(i); }
                catch (Exception e) { ModBehaviour.DevLog("[ModeD/Coop] 开局物资回滚待重试: " + e.Message); }
            }
        }

        private bool RetryCoopModeDEntry()
        {
            if (!BossRushCoop.IsConnected || !modeDActive || coopModeDLocalAsset != BossRushCoop.LocalAssetId) return false;
            string tag = coopEpoch + ":" + coopModeDLocalAsset;
            if (coopDeliveredEpoch == tag) return true;
            TickCoopModeDStarterRollback(1f);
            if (coopModeDStarterRollback.Count != 0 || !owner.IsPlayerNaked()) return false;
            coopModeDAdmissionDecisions.Remove(tag);
            if (BossRushCoop.IsHost) ApplyCoopModeDStarter(new CoopModeDPersonal { Epoch = coopEpoch });
            else coopChannel.Request("retry", text: coopEpoch);
            return true;
        }

        private CoopModeDState CaptureCoopModeD()
        {
            List<string> ids = new List<string>();
            foreach (CharacterMainControl enemy in modeDCurrentWaveEnemies)
            {
                string id = BossRushCoop.GetEntityId(enemy);
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return new CoopModeDState
            {
                Active = modeDActive, Complete = modeDWaveCompletePending, Wave = modeDWaveIndex,
                Expected = modeDExpectedEnemiesInCurrentWave, Resolved = modeDSpawnResolvedInCurrentWave,
                Epoch = coopEpoch, Enemies = ids.ToArray()
            };
        }

        private void ApplyCoopModeD(CoopModeDState state)
        {
            RetireLostCoopProjection();
            if (BossRushCoop.IsClient && state.Active) coopProjectionRoom = BossRushCoop.RunId;
            bool started = !modeDActive && state.Active;
            if (coopEpoch != state.Epoch) coopModeDLocalAsset = BossRushCoop.LocalAssetId;
            bool waveChanged = modeDWaveIndex != state.Wave;
            modeDActive = state.Active;
            coopEpoch = state.Epoch;
            modeDWaveIndex = state.Wave;
            modeDWaveCompletePending = state.Complete;
            modeDExpectedEnemiesInCurrentWave = state.Expected;
            modeDSpawnResolvedInCurrentWave = state.Resolved;
            modeDCurrentWaveEnemies.Clear();
            if (state.Enemies != null)
            {
                foreach (string id in state.Enemies)
                {
                    CharacterMainControl enemy = BossRushCoop.FindEntity(id);
                    if (enemy != null) { CoopAiPredictionHealthProtection.MarkReplica(enemy); modeDCurrentWaveEnemies.Add(enemy); }
                }
            }
            if (started) SetupSignForModeD(owner.ArenaRewardSignInteract);
            string achievementTag = state.Epoch + ":" + BossRushCoop.LocalAssetId;
            if (state.Active && state.Wave > 0 && coopDeliveredEpoch == achievementTag && coopAchievementEpoch != achievementTag)
            { owner.BeginAchievementSessionForArena("ModeD"); coopAchievementEpoch = achievementTag; }
            if (waveChanged && owner.ArenaRewardSignInteract != null) owner.ArenaRewardSignInteract.SetCheerMode();
            if (state.Complete) ShowModeDNextWaveOption();
        }

        private void ExecuteCoopModeD(CoopGameplayCommand command)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            if (player == null || !player.IsAlive || owner == null || !owner.IsCurrentSceneValidBossRushArena()) return;
            if (command.Action == "admission" || command.Action == "retry")
            {
                string asset;
                if (!modeDActive || command.Text != coopEpoch || command.AssetId != player.AssetId
                    || !coopModeDParticipants.TryGetValue(player.Id, out asset) || asset != command.AssetId) return;
                if (command.Action == "admission")
                {
                    if (command.Flag)
                    {
                        coopModeDAdmitted[player.Id] = asset;
                        if (modeDWaveIndex > 0 && !modeDWaveCompletePending) coopWaveParticipants[player.Id] = asset;
                    }
                    else coopModeDAdmitted.Remove(player.Id);
                }
                else
                { int attempt; coopModeDStarterAttempts.TryGetValue(player.Id, out attempt); coopModeDStarterAttempts[player.Id] = attempt + 1; }
                return;
            }
            if (command.Action == "start" && !modeDActive) StartModeD();
            string admitted;
            if (command.Action == "next" && modeDActive && coopModeDAdmitted.TryGetValue(player.Id, out admitted) && admitted == player.AssetId) ModeDStartNextWave();
        }
    }
}
