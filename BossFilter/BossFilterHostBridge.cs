using System.Collections.Generic;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // 旧调用点在原生命周期槽位转发；筛选状态和窗口引用归模块所有。
        private Dictionary<string, bool> bossEnabledStates { get { return bossFilterRuntime.EnabledStates; } }
        private bool bossPoolFilterInitialized { get { return bossFilterRuntime.IsInitialized; } }
        internal List<EnemyPresetInfo> BossFilterEnemyPresets { get { return enemyPresets; } }

        internal List<string> GetBossFilterDisabledBosses()
        {
            return config != null ? config.disabledBosses : null;
        }

        internal Dictionary<string, float> GetBossFilterSavedFactors()
        {
            return config != null ? config.bossInfiniteHellFactors : null;
        }

        internal void InitializeBossFilterEnemyPresets() { InitializeEnemyPresets(); }

        internal void SaveBossFilterConfiguration(List<string> disabledBosses, Dictionary<string, float> savedFactors)
        {
            if (config == null) config = new BossRushConfig();
            if (config.disabledBosses == null) config.disabledBosses = new List<string>();
            else config.disabledBosses.Clear();
            config.disabledBosses.AddRange(disabledBosses);
            if (config.bossInfiniteHellFactors == null) config.bossInfiniteHellFactors = new Dictionary<string, float>();
            else config.bossInfiniteHellFactors.Clear();
            foreach (var pair in savedFactors) config.bossInfiniteHellFactors[pair.Key] = pair.Value;
            SaveConfigToFile();
        }

        private void InitializeBossPoolFilter() { bossFilterRuntime.InitializeBossPoolFilter(); }
        private void InvalidateFilteredPresetsCache() { bossFilterRuntime.InvalidateFilteredPresetsCache(); }
        private void ResetBossPoolFilterStateForEnemyPresetRefresh() { bossFilterRuntime.ResetBossPoolFilterStateForEnemyPresetRefresh(); }
        private void CheckBossPoolWindowHotkey() { bossFilterRuntime.CheckBossPoolWindowHotkey(); }
        private void BossPoolLateUpdate() { bossFilterRuntime.BossPoolLateUpdate(); }
        private void DestroyBossPoolUI() { bossFilterRuntime.DestroyBossPoolUI(); }
        public bool IsBossEnabled(string bossName) { return bossFilterRuntime.IsBossEnabled(bossName); }
        public void SetBossEnabled(string bossName, bool enabled) { bossFilterRuntime.SetBossEnabled(bossName, enabled); }
        public List<EnemyPresetInfo> GetFilteredEnemyPresets() { return bossFilterRuntime.GetFilteredEnemyPresets(); }
        public float GetBossInfiniteHellFactor(string bossName) { return bossFilterRuntime.GetBossInfiniteHellFactor(bossName); }
        public void EnableAllBosses() { bossFilterRuntime.EnableAllBosses(); }
        public void DisableAllBosses() { bossFilterRuntime.DisableAllBosses(); }
        public void OpenBossPoolWindow() { bossFilterRuntime.OpenBossPoolWindow(); }
        public void CloseBossPoolWindow() { bossFilterRuntime.CloseBossPoolWindow(); }
    }
}
