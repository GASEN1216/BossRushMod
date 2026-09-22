using System;
using System.Collections.Generic;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// Dev 验收：过滤一个官方 Boss，验证图鉴及时重建且不重扫预设。
        /// 2026-09-20 起官方名单会把筛选器关掉的 Boss 补成锁定卡（e80b1c3f，owner 拍板），
        /// 所以目录里的官方条目数不随筛选器变，变的是「池子给出」的条目数（IsInCurrentPool）。
        /// </summary>
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
                int poolBefore = CountPoolBackedCodexEntries(before);
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
                IList<CodexBossInfo> filtered = CodexRuntime.GetCatalogSnapshot();
                int officialFiltered = CountOfficialCodexEntries(filtered);
                int poolFiltered = CountPoolBackedCodexEntries(filtered);

                bossEnabledStates[selected] = original;
                InvalidateFilteredPresetsCache();
                IList<CodexBossInfo> restored = CodexRuntime.GetCatalogSnapshot();
                int officialRestored = CountOfficialCodexEntries(restored);
                int poolRestored = CountPoolBackedCodexEntries(restored);
                int scanAfter = EnemyPresetInitializationScanCount;
                int buildAfter = CodexBossCatalog.BuildCount;

                // 名单在位时目录不缩（被关掉的那只变成锁定卡）；名单读不出来（fail-open）时退回旧口径少一格。
                bool ok = officialBefore > 0 && poolBefore > 0
                    && poolFiltered == poolBefore - 1 && poolRestored == poolBefore
                    && (officialFiltered == officialBefore || officialFiltered == officialBefore - 1)
                    && officialRestored == officialBefore && scanAfter == scanBefore
                    && buildAfter >= buildBefore + 2;
                metrics = "key=" + selected + ",official=" + officialBefore + "->" + officialFiltered
                    + "->" + officialRestored + ",pool=" + poolBefore + "->" + poolFiltered + "->" + poolRestored
                    + ",preset_scans=" + scanBefore + "->" + scanAfter
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

        private static int CountPoolBackedCodexEntries(IList<CodexBossInfo> entries)
        {
            if (entries == null) return 0;
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                CodexBossInfo info = entries[i];
                if (info != null && !info.IsCustomBoss && !info.IsZombieBoss && !info.IsHistoricalOnly && info.IsInCurrentPool) count++;
            }
            return count;
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
