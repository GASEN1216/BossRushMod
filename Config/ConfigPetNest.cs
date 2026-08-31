// ============================================================================
// ConfigPetNest.cs - 遗种巢入口总开关的配置接线（实施计划 步骤 2）
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行）；
// 语义是同一份 partial class：
//   - 归位依据 AGENTS.md 4.8 第 1 层：运行时可调开关走 Config + ModConfig；
//     遗种巢**只有这一个** 配置项，其余数值全部在 PetNest/PetNestTuning.cs。
//   - 镜像键 `BossRush_PetNestEnabled`，实装期默认 false（全系统 dormant）。
//   - 唯一只读入口 IsPetNestConfiguredEnabled()：no-throw，缺配置返回 false；
//     禁止反射私有字段，也禁止把开关缓存进 PetNestTuning 或存档。
// ============================================================================

using System;
using System.Reflection;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>ModConfig 镜像键（与 Mode H 的 `_ModeHEnabled` 同形）。</summary>
        private const string PetNestModConfigKeySuffix = "_PetNestEnabled";

        private partial class BossRushConfig
        {
            /// <summary>
            /// 遗种巢（PetNest）唯一可写入口开关，默认开启（属于默认内容）。
            /// 关闭时整个子系统 dormant：不订阅存档、不建目录、不产蛋、不生成随从。
            /// </summary>
            public bool petNestEnabled = true;
        }

        /// <summary>批量加载路径：从 ModConfig 读遗种巢开关。</summary>
        private void LoadPetNestEnabledFromModConfig(MethodInfo boolLoadMethod)
        {
            try
            {
                if (boolLoadMethod == null || config == null) return;
                string key = ModName + PetNestModConfigKeySuffix;
                object result = boolLoadMethod.Invoke(null, new object[] { key, config.petNestEnabled });
                config.petNestEnabled = (bool)result;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 加载遗种巢开关失败: " + ex.Message);
            }
        }

        /// <summary>单键变更路径：命中遗种巢键时重新读取并返回 true。</summary>
        private bool TryLoadPetNestSingleModConfigValue(string changedKey, MethodInfo loadMethod)
        {
            try
            {
                if (loadMethod == null || config == null) return false;
                string key = ModName + PetNestModConfigKeySuffix;
                if (changedKey != key) return false;

                MethodInfo boolLoadMethod = loadMethod.MakeGenericMethod(typeof(bool));
                object result = boolLoadMethod.Invoke(null, new object[] { key, config.petNestEnabled });
                config.petNestEnabled = (bool)result;
                return true;
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 单键加载遗种巢开关失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>向 ModConfig 注册遗种巢开关选项。</summary>
        private void RegisterPetNestModConfigOption(MethodInfo addBoolMethod)
        {
            try
            {
                if (addBoolMethod == null || config == null) return;
                string label = L10n.T("遗种巢：养崽与天灾远征", "PetNest: Cub Raising and Expeditions");
                string key = ModName + PetNestModConfigKeySuffix;
                addBoolMethod.Invoke(null, new object[] { ModName, key, label, config.petNestEnabled });
                DevLog("[BossRush] 遗种巢配置项注册成功");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 注册遗种巢配置项失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 遗种巢入口总开关的唯一只读入口：no-throw，缺配置返回 false。
        /// 禁止反射私有字段，也禁止把开关缓存进 PetNestTuning 或存档。
        /// </summary>
        internal bool IsPetNestConfiguredEnabled()
        {
            try
            {
                return config != null && config.petNestEnabled;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 遗种巢占用的 TypeID。与 Config/Config.cs 里的主表是同一个 partial class，
    /// 拆开只为单文件行数预算；台账见 docs/Bossrush使用物品ID表.md 与 AGENTS.md 4.3。
    /// </summary>
    public static partial class BossRushItemIds
    {
        /// <summary>遗种蛋。全 Boss 谱系共用这一个号，血脉写在物品 KV 上。</summary>
        public const int RelicEgg = 500059;
    }
}
