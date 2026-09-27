using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        private void ReleaseBossRandomLootTrackingOnDestroy()
        {
            foreach (KeyValuePair<CharacterMainControl, Action<DamageInfo>> entry in trackedBossLootHooks)
            {
                try
                {
                    if (!object.ReferenceEquals(entry.Key, null) && !(entry.Key == null) && entry.Value != null)
                    {
                        entry.Key.BeforeCharacterSpawnLootOnDead -= entry.Value;
                    }
                }
                catch (Exception)
                {
                    // 宿主销毁时不让已销毁的官方角色中断其余退订。
                }
            }
            trackedBossLootHooks.Clear();
            bossSpawnTimes.Clear();
            bossOriginalLootCounts.Clear();
            bossRushLootboxPathBosses.Clear();
            bossRushLootboxPathTrackedBossScratch.Clear();
            bossRushLootboxPathStaleBossScratch.Clear();
        }

        internal bool ShouldDeferBlueBossExtraDropToBossRushLootbox(CharacterMainControl bossMain)
        {
            if (object.ReferenceEquals(bossMain, null))
            {
                return false;
            }

            return bossRushLootboxPathBosses.Contains(bossMain);
        }

        internal bool ShouldDeferExtraBossDropToModPath(CharacterMainControl bossMain)
        {
            if (object.ReferenceEquals(bossMain, null))
            {
                return false;
            }

            // 无间炼狱不登记奖励箱路径，但额外掉落仍需经世界掉落消费。
            if (InfiniteHellMode)
            {
                return true;
            }

            return bossRushLootboxPathBosses.Contains(bossMain);
        }

        private string GetBossLootTrackingDebugName(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return "<null>";
            }

            try
            {
                if (character.gameObject != null && !string.IsNullOrEmpty(character.gameObject.name))
                {
                    return character.gameObject.name;
                }
            }
            catch (Exception)
            {
                return "<unnamed>";
            }

            return "<unnamed>";
        }

        private void RollbackBossRandomLootTrackingRegistration(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return;
            }

            bossSpawnTimes.Remove(character);
            bossOriginalLootCounts.Remove(character);
            CountedDeadBosses.Remove(character);
            trackedBossLootHooks.Remove(character);
            FinalizeBossRushLootboxPathTracking(character);
        }

        internal void RegisterBossRandomLootTracking(CharacterMainControl character, int originalLootCount = 3, float spawnTimeOffset = 1f)
        {
            try
            {
                if (character == null)
                {
                    return;
                }

                bossSpawnTimes[character] = Time.time + spawnTimeOffset;
                bossOriginalLootCounts[character] = Mathf.Max(0, originalLootCount);
                CountedDeadBosses.Remove(character);
                MarkBossRushLootboxPathTracking(character);

                // 遗种巢掉落双轨（加法分支）：本函数覆盖全部 Boss 生成调用位，且天然不含
                // Mode G 托管路径与丧尸模式，正好等于首版掉落范围。开关关闭时内部直接返回。
                PetNestDropService.TryTrack(owner, character);

                // 词缀熔石掉落（加法分支）：同一挂接点、同一三段式，开关关闭时内部直接返回。
                AffixForgeStoneDropService.TryTrack(owner, character);

                Action<DamageInfo> existingHandler = null;
                if (trackedBossLootHooks.TryGetValue(character, out existingHandler) && existingHandler != null)
                {
                    try
                    {
                        character.BeforeCharacterSpawnLootOnDead -= existingHandler;
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[BossRush] [WARNING] 重绑随机掉落追踪前取消旧事件失败: boss="
                            + GetBossLootTrackingDebugName(character) + ", " + e.Message);
                    }
                }

                CharacterMainControl capturedCharacter = character;
                Action<DamageInfo> handler = (dmgInfo) => owner.OnBossBeforeSpawnLootForArena(capturedCharacter, dmgInfo);
                trackedBossLootHooks[character] = handler;

                try
                {
                    character.BeforeCharacterSpawnLootOnDead += handler;
                }
                catch (Exception e)
                {
                    RollbackBossRandomLootTrackingRegistration(character);
                    ModBehaviour.DevLog("[BossRush] [WARNING] 注册随机掉落追踪事件失败，已回滚追踪状态: boss="
                        + GetBossLootTrackingDebugName(character) + ", " + e.Message);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 注册随机掉落追踪失败: " + e.Message);
            }
        }

        internal void ClearBossRandomLootTracking(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return;
            }

            PetNestDropService.ClearTracking(character);
            AffixForgeStoneDropService.ClearTracking(character);

            Action<DamageInfo> handler = null;
            if (trackedBossLootHooks.TryGetValue(character, out handler))
            {
                try
                {
                    if (!(character == null) && handler != null)
                    {
                        character.BeforeCharacterSpawnLootOnDead -= handler;
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 清理随机掉落追踪事件失败: boss="
                        + GetBossLootTrackingDebugName(character) + ", " + e.Message);
                }
            }

            bossSpawnTimes.Remove(character);
            bossOriginalLootCounts.Remove(character);
            CountedDeadBosses.Remove(character);
            trackedBossLootHooks.Remove(character);
        }

        internal void MarkBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return;
            }

            if (owner.ShouldTrackBossRushLootboxPathForArena())
            {
                bossRushLootboxPathBosses.Add(character);
            }
            else
            {
                bossRushLootboxPathBosses.Remove(character);
            }
        }

        internal void FinalizeBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return;
            }

            bossRushLootboxPathBosses.Remove(character);
            FrostmourneBlueBossDropHandler.CancelPendingBossRushLootboxDrop(character);
            PhantomWitchScytheBossDropHandler.CancelPendingBossRushLootboxDrop(character);
            PetNestDropService.CancelPendingBossRushLootboxDrop(character);
            AffixForgeStoneDropService.CancelPendingBossRushLootboxDrop(character);
            SetBonusBossDropHandler.CancelPendingBossRushLootboxDrop(character);
            NewWeaponBossDropHandler.CancelPendingBossRushLootboxDrop(character);
        }

        internal void RefreshBossRushLootboxPathTrackingForTrackedBosses()
        {
            if (bossSpawnTimes.Count == 0)
            {
                bossRushLootboxPathBosses.Clear();
                return;
            }

            bossRushLootboxPathTrackedBossScratch.Clear();
            bossRushLootboxPathStaleBossScratch.Clear();
            foreach (CharacterMainControl boss in bossSpawnTimes.Keys)
            {
                bossRushLootboxPathTrackedBossScratch.Add(boss);
            }

            for (int i = 0; i < bossRushLootboxPathTrackedBossScratch.Count; i++)
            {
                CharacterMainControl boss = bossRushLootboxPathTrackedBossScratch[i];
                if (boss == null)
                {
                    bossRushLootboxPathStaleBossScratch.Add(boss);
                    continue;
                }

                MarkBossRushLootboxPathTracking(boss);
            }

            if (bossRushLootboxPathStaleBossScratch.Count == 0)
            {
                bossRushLootboxPathTrackedBossScratch.Clear();
                return;
            }

            for (int i = 0; i < bossRushLootboxPathStaleBossScratch.Count; i++)
            {
                CharacterMainControl staleBoss = bossRushLootboxPathStaleBossScratch[i];
                bossSpawnTimes.Remove(staleBoss);
                bossOriginalLootCounts.Remove(staleBoss);
                CountedDeadBosses.Remove(staleBoss);
                trackedBossLootHooks.Remove(staleBoss);
                bossRushLootboxPathBosses.Remove(staleBoss);
            }

            bossRushLootboxPathTrackedBossScratch.Clear();
            bossRushLootboxPathStaleBossScratch.Clear();
        }

    }
}
