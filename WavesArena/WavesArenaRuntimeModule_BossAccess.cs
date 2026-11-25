using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        // D and Arena deliberately share this clock. Only its owner advances/resets it.
        internal void ResetWaveIntegrityCheck() { WaveIntegrityCheckTimer = 0f; }

        internal bool AdvanceWaveIntegrityCheck(float deltaTime)
        {
            WaveIntegrityCheckTimer += deltaTime;
            if (WaveIntegrityCheckTimer >= ModBehaviour.WaveIntegrityCheckInterval)
            {
                WaveIntegrityCheckTimer = 0f;
                return true;
            }
            return false;
        }

        internal void RegisterContentWaveBoss(CharacterMainControl character)
        {
            ApplyExpeditionTierToContentBoss(character);
            CurrentBoss = character;
            if (BossesPerWave > 1 && !CurrentWaveBosses.Contains(character))
            {
                CurrentWaveBosses.Add(character);
            }
        }

        internal void ClearContentCurrentBoss(CharacterMainControl character)
        {
            if (CurrentBoss == character) CurrentBoss = null;
        }

        internal void RemoveContentWaveBoss(CharacterMainControl character)
        {
            CurrentWaveBosses.Remove(character);
        }

        internal bool HasEnemyPresetCatalog { get { return EnemyPresets != null; } }

        internal EnemyPresetInfo FindEnemyPresetForContent(string nameKey)
        {
            return ModBossPresetLookup.FindByNameKey(EnemyPresets, nameKey);
        }

        internal void AddEnemyPresetFromContent(EnemyPresetInfo preset)
        {
            EnemyPresets.Add(preset);
        }

        // These actions deliberately do not subscribe loot events: each content Boss
        // retains its original subscription point and delegate identity.
        internal void RecordContentBossLoot(CharacterMainControl character, float spawnTime, int originalLootCount)
        {
            bossSpawnTimes[character] = spawnTime;
            bossOriginalLootCounts[character] = originalLootCount;
        }

        internal void RemoveContentBossLootRecord(CharacterMainControl character)
        {
            bossSpawnTimes.Remove(character);
            bossOriginalLootCounts.Remove(character);
        }

        internal int ContentBossLootRecordCount { get { return bossSpawnTimes.Count; } }

        internal void CopyTrackedBossCharactersTo(ICollection<CharacterMainControl> destination)
        {
            foreach (var entry in bossSpawnTimes)
            {
                if (entry.Key != null) destination.Add(entry.Key);
            }
        }

        /// <summary>
        /// 烽烟圈的竞技场 / Mode D 来源：掉落登记表里的 Boss，再补上本波登记的内容 Boss。
        /// 冠军之影、蚀日花冠经 RegisterContentWaveBoss 只进 CurrentBoss / CurrentWaveBosses，刻意不挂随机掉落漏斗，
        /// 不在 bossSpawnTimes 里；只读掉落表时这两只在 M 图上永远没有烽烟圈（2026-11-24 F3 BOSS_MAP_BEACON 实测）。
        /// 不改 CopyTrackedBossCharactersTo 本身：女巫离场清理会逐个销毁那张表里的角色。
        /// </summary>
        internal void CopyMapBeaconBossesTo(ICollection<CharacterMainControl> destination)
        {
            // 噩梦战帖「盲战」：竞技场首领不进烽烟圈（Mode D 借用本模块时 LiveExpeditionTier 退回标准档，不受影响）
            if (ArenaExpeditionTuning.HidesMapBeacons(LiveExpeditionTier())) return;
            CopyTrackedBossCharactersTo(destination);
            AddMapBeaconBoss(destination, CurrentBoss as CharacterMainControl);
            for (int i = 0; i < CurrentWaveBosses.Count; i++)
            {
                AddMapBeaconBoss(destination, CurrentWaveBosses[i] as CharacterMainControl);
            }
        }

        private static void AddMapBeaconBoss(ICollection<CharacterMainControl> destination, CharacterMainControl boss)
        {
            if (boss != null && !destination.Contains(boss)) destination.Add(boss);
        }
    }
}
