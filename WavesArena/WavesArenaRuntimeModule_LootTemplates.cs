using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.Utilities;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        private Func<bool> hasLootBoxConfig;
        private Func<bool> getLootBoxBlocksBullets;

        internal void BindLootBoxPolicies(Func<bool> hasConfig, Func<bool> blocksBullets)
        {
            hasLootBoxConfig = hasConfig;
            getLootBoxBlocksBullets = blocksBullets;
        }

        internal static InteractableLootbox GetLootBoxTemplateWithLoader()
        {
            if (CachedLootBoxTemplateWithLoader != null)
            {
                return CachedLootBoxTemplateWithLoader;
            }

            try
            {
                var all = Resources.FindObjectsOfTypeAll<InteractableLootbox>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var box = all[i];
                        if (box == null)
                        {
                            continue;
                        }

                        var loader = box.GetComponent<Duckov.Utilities.LootBoxLoader>();
                        if (loader != null)
                        {
                            CachedLootBoxTemplateWithLoader = box;
                            ModBehaviour.DevLog("[BossRush] 发现带 LootBoxLoader 的 Lootbox 模板: " + box.name);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[BossRush] 查找 LootBoxLoader 模板失败: " + ex.Message);
            }

            return CachedLootBoxTemplateWithLoader;
        }

        internal static InteractableLootbox GetDifficultyRewardLootBoxTemplate()
        {
            if (CachedDifficultyRewardLootBoxTemplate != null)
            {
                try
                {
                    ModBehaviour.DevLog("[BossRush] 使用缓存的通关奖励 Lootbox 模板: " + CachedDifficultyRewardLootBoxTemplate.name);
                }
                catch {}
                return CachedDifficultyRewardLootBoxTemplate;
            }

            try
            {
                var all = Resources.FindObjectsOfTypeAll<InteractableLootbox>();
                if (all != null)
                {
                    string mainName = null;
                    if (CachedLootBoxTemplateWithLoader != null)
                    {
                        mainName = CachedLootBoxTemplateWithLoader.name;
                        if (!string.IsNullOrEmpty(mainName) && mainName.EndsWith("(Clone)", StringComparison.Ordinal))
                        {
                            mainName = mainName.Substring(0, mainName.Length - "(Clone)".Length);
                        }
                    }

                    for (int i = 0; i < all.Length; i++)
                    {
                        var box = all[i];
                        if (box == null)
                        {
                            continue;
                        }

                        var loader = box.GetComponent<Duckov.Utilities.LootBoxLoader>();
                        if (loader == null)
                        {
                            continue;
                        }

                        CachedDifficultyRewardLootBoxTemplate = box;
                        ModBehaviour.DevLog("[BossRush] 发现用于通关奖励的 Lootbox 模板: " + box.name);
                        break;
                    }

                    if (CachedDifficultyRewardLootBoxTemplate == null)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            var box = all[i];
                            if (box == null)
                            {
                                continue;
                            }

                            CachedDifficultyRewardLootBoxTemplate = box;
                            ModBehaviour.DevLog("[BossRush] 通关奖励未找到专用 Lootbox 模板，退回使用: " + box.name);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[BossRush] 查找通关奖励 Lootbox 模板失败: " + ex.Message);
            }

            // 如果没有找到不同的，就退回到 Boss 掉落使用的模板
            if (CachedDifficultyRewardLootBoxTemplate == null)
            {
                CachedDifficultyRewardLootBoxTemplate = GetLootBoxTemplateWithLoader();
            }

            return CachedDifficultyRewardLootBoxTemplate;
        }

        internal void ApplyLootBoxCoverSetting(InteractableLootbox lootbox, bool ignoreConfig = false)
        {
            if (lootbox == null)
            {
                return;
            }

            if (!ignoreConfig)
            {
                if (!hasLootBoxConfig() || getLootBoxBlocksBullets())
                {
                    return;
                }
            }

            try
            {
                Collider selfCol = lootbox.GetComponent<Collider>();
                if (selfCol != null && !selfCol.isTrigger)
                {
                    selfCol.isTrigger = true;
                }

                Collider[] childCols = lootbox.GetComponentsInChildren<Collider>(true);
                if (childCols != null)
                {
                    for (int i = 0; i < childCols.Length; i++)
                    {
                        Collider c = childCols[i];
                        if (c != null && !c.isTrigger)
                        {
                            c.isTrigger = true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ApplyLootBoxCoverSetting 失败: " + e.Message);
            }
        }
    }
}
