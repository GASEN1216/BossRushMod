using System;
using System.Collections.Generic;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>Dev 验收：过滤一个官方 Boss，验证图鉴及时重建且不重扫预设。</summary>
        internal bool DebugValidateCodexFilterRefresh(out string metrics)
        {
            metrics = string.Empty;
            if (!DevModeEnabled || CodexRuntime == null) return false;
            string selected = null;
            bool original = true;
            try
            {
                if (!EnsureEnemyPresetsReadyForGameplayCatalogs()) return false;
                IList<CodexBossInfo> before = CodexRuntime.GetCatalogSnapshot();
                int officialBefore = CountOfficialCodexEntries(before);
                int scanBefore = EnemyPresetInitializationScanCount;
                int buildBefore = CodexBossCatalog.BuildCount;

                foreach (KeyValuePair<string, bool> pair in bossEnabledStates)
                {
                    if (!pair.Value) continue;
                    if (string.Equals(pair.Key, DragonDescendantConfig.BOSS_NAME_KEY, StringComparison.Ordinal)
                        || string.Equals(pair.Key, DragonKingConfig.BossNameKey, StringComparison.Ordinal)
                        || string.Equals(pair.Key, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal))
                        continue;
                    selected = pair.Key;
                    original = pair.Value;
                    break;
                }
                if (string.IsNullOrEmpty(selected)) return false;

                bossEnabledStates[selected] = false;
                InvalidateFilteredPresetsCache();
                int officialFiltered = CountOfficialCodexEntries(CodexRuntime.GetCatalogSnapshot());

                bossEnabledStates[selected] = original;
                InvalidateFilteredPresetsCache();
                int officialRestored = CountOfficialCodexEntries(CodexRuntime.GetCatalogSnapshot());
                int scanAfter = EnemyPresetInitializationScanCount;
                int buildAfter = CodexBossCatalog.BuildCount;

                bool ok = officialBefore > 0 && officialFiltered == officialBefore - 1
                    && officialRestored == officialBefore && scanAfter == scanBefore
                    && buildAfter >= buildBefore + 2;
                metrics = "key=" + selected + ",official=" + officialBefore + "->" + officialFiltered
                    + "->" + officialRestored + ",preset_scans=" + scanBefore + "->" + scanAfter
                    + ",catalog_builds=" + buildBefore + "->" + buildAfter;
                return ok;
            }
            catch (Exception e)
            {
                metrics = e.GetType().Name + ":" + e.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(selected))
                {
                    bossEnabledStates[selected] = original;
                    InvalidateFilteredPresetsCache();
                    if (CodexRuntime != null) CodexRuntime.GetCatalogSnapshot();
                }
            }
        }

        private static int CountOfficialCodexEntries(IList<CodexBossInfo> entries)
        {
            if (entries == null) return 0;
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                CodexBossInfo info = entries[i];
                if (info != null && !info.IsCustomBoss && !info.IsZombieBoss && !info.IsHistoricalOnly) count++;
            }
            return count;
        }
    }
}
