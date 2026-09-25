using System;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        public bool ModeDStartNextWave() { return modeDRuntime.ModeDStartNextWave(); }
        internal void TickModeDIntegrity(float deltaTime) { modeDRuntime.TickModeDIntegrity(deltaTime); }
        internal void OnModeDWaveComplete() { modeDRuntime.OnModeDWaveComplete(); }
        private void NormalizeDamageMultiplier(CharacterMainControl character) { modeDRuntime.NormalizeDamageMultiplier(character); }
        private EnemyPresetInfo GetRandomBossPreset() { return modeDRuntime.GetRandomBossPreset(); }
        private EnemyPresetInfo GetRandomMinionPreset() { return modeDRuntime.GetRandomMinionPreset(); }
        private Vector3[] GenerateFallbackSpawnPointsAroundPlayer(Vector3 position, int pointCount = 10, float minRadius = 8f, float maxRadius = 15f)
        { return modeDRuntime.GenerateFallbackSpawnPointsAroundPlayer(position, pointCount, minRadius, maxRadius); }

        // 保持 D / Arena 在原调度位置共用完整性时钟的语义。
        internal float ModeDIntegrityCheckTimer { get { return waveIntegrityCheckTimer; } set { waveIntegrityCheckTimer = value; } }
        internal int ModeDConfiguredEnemiesPerWave { get { return config != null ? config.modeDEnemiesPerWave : 0; } }
        internal void ShowModeDEnemyBanner(string name, Vector3 position, Vector3 playerPosition, int current, int total, bool infinite, int wave, int bosses)
        { ShowEnemyBanner_UIAndSigns(name, position, playerPosition, current, total, infinite, wave, bosses); }
        internal void SpawnModeDEnemyCore(EnemyPresetInfo preset, Vector3 position, bool isBoss, Func<bool> isActiveCheck,
            Action<EnemySpawnContext> onSpawned, Action onFailed, int waveIndex)
        { SpawnEnemyCore(preset, position, isBoss, isActiveCheck, onSpawned, onFailed, waveIndex); }
        internal void RegisterModeDEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchor) { RegisterEnemyRecoveryAnchor(enemy, anchor); }
        internal void CheckModeDFlawlessAchievementForRuntime() { CheckModeDFlawlessAchievement(); }
        internal void CheckModeDClearAchievementsForRuntime() { CheckModeDClearAchievements(); }
    }
}
