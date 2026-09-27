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
    }
}
