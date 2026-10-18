using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    // 瞬时个人效果只接受房主已确认的死亡事实。
    [Serializable]
    internal sealed class CoopZombieKillReceipt
    {
        public int Run, Wave, Generation, Heal;
        public string SceneToken;
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private sealed class CoopZombieKillOptions
        {
            internal int Siphon, SecondWind, DoomPulse, Kills;
        }
        private readonly Dictionary<string, CoopZombieKillOptions> coopZombieKillOptions
            = new Dictionary<string, CoopZombieKillOptions>(StringComparer.Ordinal);
        private CoopPersonalDelivery<CoopZombieKillReceipt> coopZombieKillDelivery;
        private long coopZombieKillSequence;

        private void InitializeCoopZombieKillRewards()
        {
            coopZombieKillDelivery = new CoopPersonalDelivery<CoopZombieKillReceipt>("ZombieKills", ApplyCoopZombieKillReceipt);
        }

        private void TickCoopZombieKillRewards(float deltaTime)
        {
            if (coopZombieKillDelivery != null) coopZombieKillDelivery.Tick(deltaTime);
        }

        private void ResetCoopZombieKillRewards()
        {
            coopZombieKillOptions.Clear();
            coopZombieKillSequence = 0;
        }

        private void CleanupCoopZombieKillRewards()
        {
            if (coopZombieKillDelivery != null) coopZombieKillDelivery.Dispose();
            coopZombieKillDelivery = null;
            ResetCoopZombieKillRewards();
        }

        // 只在现有奖励选择事务完成去重后记录，不采信客端自报的奖励层数。
        private void RecordCoopZombieKillRewardSelection(BossRushCoopPlayer player, ZombieModeRewardType reward)
        {
            if (!BossRushCoop.IsHost || player == null || player.IsLocal || !IsCoopZombieParticipant(player)) return;
            if (reward != ZombieModeRewardType.TriggerPurificationSiphon
                && reward != ZombieModeRewardType.TriggerSecondWind && reward != ZombieModeRewardType.TriggerDoomPulse) return;
            string key = CoopZombieParticipantKey(player);
            CoopZombieKillOptions options;
            if (!coopZombieKillOptions.TryGetValue(key, out options))
                coopZombieKillOptions.Add(key, options = new CoopZombieKillOptions());
            if (reward == ZombieModeRewardType.TriggerPurificationSiphon) options.Siphon = Math.Min(2, options.Siphon + 1);
            else if (reward == ZombieModeRewardType.TriggerSecondWind) options.SecondWind = Math.Min(5, options.SecondWind + 1);
            else options.DoomPulse = Math.Min(3, options.DoomPulse + 1);
        }

        private void HandleCoopZombieRemoteKillRewards(int runId, DamageInfo damageInfo,
            CharacterMainControl victim, ZombieModeEnemyRuntimeMarker marker)
        {
            if (!BossRushCoop.IsHost || !IsZombieModeRunValid(runId) || damageInfo.isFromBuffOrEffect
                || victim == null || marker == null || marker.RunId != runId || damageInfo.fromCharacter == null) return;
            string playerId = BossRushCoop.GetPlayerId(damageInfo.fromCharacter);
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            if (player == null || player.IsLocal || !player.IsAlive || !IsCoopZombieParticipant(player)) return;
            CoopZombieKillOptions options;
            if (!coopZombieKillOptions.TryGetValue(CoopZombieParticipantKey(player), out options)) return;
            if (options.Siphon > 0)
            {
                int bonus = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(1, marker.PurificationPointValue) * 0.20f * options.Siphon));
                SpawnZombieModeDeathStars(runId, victim.transform.position, bonus, 1);
            }
            if (options.SecondWind > 0 && coopZombieKillDelivery != null)
                coopZombieKillDelivery.Deliver(player.Id, runId + ":kill:" + (++coopZombieKillSequence),
                    new CoopZombieKillReceipt { Run = runId, Wave = runState.CurrentWave,
                        Generation = BossRushCoop.SceneGeneration, SceneToken = BossRushCoop.SceneToken,
                        Heal = Math.Min(10, 2 * options.SecondWind) }, coopZombieParticipants[player.Id]);
            if (options.DoomPulse > 0 && ++options.Kills >= Math.Max(20, 40 - 6 * (options.DoomPulse - 1)))
            {
                options.Kills = 0;
                DeferCoopZombieKillPulse(runId, player, options.DoomPulse);
            }
        }

        private bool ApplyCoopZombieKillReceipt(CoopZombieKillReceipt receipt)
        {
            if (receipt == null) return false;
            // 过期战斗的回血不能作用于后续波次、场景或新局。
            if (receipt.Run != runState.RunId || receipt.Wave < runState.CurrentWave
                || receipt.Generation != BossRushCoop.SceneGeneration || receipt.SceneToken != BossRushCoop.SceneToken
                || !IsCoopZombieLocalAssetCurrent() || !IsZombieModeRunValid(receipt.Run)) return true;
            if (receipt.Wave > runState.CurrentWave || !BossRushCoop.HasBoundScene) return false;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null) return false;
            if (!player.Health.IsDead) player.Health.AddHealth(Mathf.Clamp(receipt.Heal, 0, 10));
            return true;
        }

        private void DeferCoopZombieKillPulse(int runId, BossRushCoopPlayer player, int stacks)
        {
            if (owner == null || player.Character == null) return;
            string room = BossRushCoop.RunId, token = BossRushCoop.SceneToken, asset = player.AssetId;
            CharacterMainControl actor = player.Character;
            LevelManager level = LevelManager.Instance;
            var record = new ZombieModeRunOnlyRecord { RunId = runId, Kind = ZombieModeRunOnlyObjectKind.Coroutine };
            Coroutine coroutine = null;
            record.CleanupAction = delegate
            {
                if (owner != null && coroutine != null) owner.StopCoroutine(coroutine);
                record.CleanupAction = null; runState.RunOnlyObjects.Remove(record);
            };
            runState.RunOnlyObjects.Add(record);
            try
            {
                coroutine = owner.StartCoroutine(CoopZombieKillPulseNextFrame(record, runId, stacks, actor, level,
                    () => BossRushCoop.IsHost && BossRushCoop.HasBoundScene && room == BossRushCoop.RunId
                        && token == BossRushCoop.SceneToken && IsZombieModeRunValid(runId)
                        && player.AssetId == asset && player.Character == actor && player.IsAlive && IsCoopZombieParticipant(player)));
            }
            catch (Exception e)
            {
                record.CleanupAction = null; runState.RunOnlyObjects.Remove(record);
                ModBehaviour.DevLog("[ZombieMode] Co-op kill pulse scheduling failed: " + e.Message);
            }
        }

        private IEnumerator CoopZombieKillPulseNextFrame(ZombieModeRunOnlyRecord record, int runId, int stacks,
            CharacterMainControl actor, LevelManager level, Func<bool> valid)
        {
            yield return null;
            try
            {
                while (owner != null && record.CleanupAction != null && valid() && actor != null
                    && actor.Health != null && !actor.Health.IsDead && ReferenceEquals(level, LevelManager.Instance))
                {
                    if (IsZombieModeRuntimePaused()) { yield return null; continue; }
                    if (level != null && level.ExplosionManager != null)
                        TriggerDoomPulse(actor, stacks, (point, radius, damage) =>
                        {
                            try
                            {
                                DamageInfo info = new DamageInfo(actor);
                                info.damageValue = damage; info.damagePoint = point;
                                Vector3 normal = actor.transform.position - point;
                                info.damageNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
                                info.isExplosion = true; info.isFromBuffOrEffect = true;
                                level.ExplosionManager.CreateExplosion(point, radius, info, ExplosionFxTypes.normal, 0.35f, false);
                            }
                            catch (Exception e) { ModBehaviour.DevLog("[ZombieMode] Co-op kill pulse failed: " + e.Message); }
                        });
                    yield break;
                }
            }
            finally { record.CleanupAction = null; runState.RunOnlyObjects.Remove(record); }
        }
    }
}
