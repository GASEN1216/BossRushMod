// ============================================================================
// PetNestLineageCatalog.cs - 遗种巢血脉目录（实施计划 步骤 1）
// ============================================================================
// 职责：把「哪些 Boss 有幼体、幼体长什么样、属什么元素」收敛成唯一查询入口。
//
// 资格口径（与 Mode G 普通官方 Boss 同源，owner 已批准）：
//   ModBehaviour.GetFilteredEnemyPresets() 的过滤池 + 三个自定义 Boss 常量。
//   **不经宿主单例**：owner 由运行时模块显式传入，避免给冻结的宿主单例
//   引用分类基线增列（tests/ModBehaviourInstanceClassificationGuard.py）。
//
// fail-closed 纪律：
//   - 血脉 nameKey 在官方 preset 里查不到 -> 不进目录（该血脉不产蛋、不可孵化），
//     不阻塞其他血脉，也不回落到"随便找一个同阵营强敌"；
//   - 目录构建异常 -> 目录为空，整个遗种巢按"无可用血脉"降级，不崩。
//
// 元素推导：官方 preset 的 elementFactor_* 是**承伤系数**，越低越抗。
//   取最低的那一项当作该血脉的自有元素；全为 1（无偏好）时回落 physics。
//   自定义 Boss 走显式覆盖表，不参与推导。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>一个血脉的目录条目。只读快照，构建后不再变更。</summary>
    internal sealed class PetNestLineageInfo
    {
        /// <summary>血脉 key（官方 preset nameKey 或自定义 Boss 常量）。</summary>
        internal string LineageKey;
        /// <summary>显示名（取 preset.DisplayName，缺失时回落 LineageKey）。</summary>
        internal string DisplayName;
        /// <summary>血脉自有元素（远征亲和判定用）。</summary>
        internal ElementTypes Element;
        /// <summary>幼体视觉缩放档（只作用于 modelRoot）。</summary>
        internal float ModelScale;
        /// <summary>是否是本 Mod 的自定义 Boss。</summary>
        internal bool IsCustomBoss;
    }

    /// <summary>遗种巢血脉目录。惰性构建，只在需要时扫一次 preset 缓存。</summary>
    internal static class PetNestLineageCatalog
    {
        #region 自定义 Boss 覆盖表

        /// <summary>
        /// 三个自定义 Boss 的血脉常量。它们不在官方 preset 池里，走显式登记。
        /// 幼体 adapter 不可用时 fail-closed 为该血脉不产蛋，不阻塞其他血脉。
        /// </summary>
        private static readonly string[] CustomBossLineageKeys =
        {
            DragonDescendantConfig.BOSS_NAME_KEY,
            DragonKingConfig.BossNameKey,
            PhantomWitchConfig.BossNameKey,
        };

        private static readonly Dictionary<string, ElementTypes> CustomBossElements =
            new Dictionary<string, ElementTypes>(StringComparer.Ordinal)
            {
                { DragonDescendantConfig.BOSS_NAME_KEY, ElementTypes.fire },
                { DragonKingConfig.BossNameKey, ElementTypes.fire },
                { PhantomWitchConfig.BossNameKey, ElementTypes.ghost },
            };

        /// <summary>
        /// 体型偏大的血脉用更小的缩放档，让全谱系幼体在视觉体量上大致齐平。
        /// 首版只覆盖自定义 Boss；官方 Boss 一律用基准档。
        /// </summary>
        private static readonly Dictionary<string, float> LineageModelScaleOverrides =
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { DragonDescendantConfig.BOSS_NAME_KEY, 0.3f },
                { DragonKingConfig.BossNameKey, 0.25f },
                { PhantomWitchConfig.BossNameKey, 0.35f },
            };

        #endregion

        #region 状态

        private static readonly object _lock = new object();
        private static Dictionary<string, PetNestLineageInfo> _byKey;
        private static List<PetNestLineageInfo> _ordered;
        private static bool _built;

        /// <summary>目录是否已构建。</summary>
        internal static bool IsBuilt { get { return _built; } }

        /// <summary>目录条目数（未构建返回 0）。</summary>
        internal static int Count
        {
            get { lock (_lock) { return _ordered != null ? _ordered.Count : 0; } }
        }

        #endregion

        #region 构建

        /// <summary>
        /// 幂等构建。owner 为 null 时只登记自定义 Boss（官方池不可达）。
        /// </summary>
        internal static void EnsureBuilt(ModBehaviour owner)
        {
            if (_built) return;
            lock (_lock)
            {
                if (_built) return;
                Dictionary<string, PetNestLineageInfo> byKey =
                    new Dictionary<string, PetNestLineageInfo>(StringComparer.Ordinal);
                List<PetNestLineageInfo> ordered = new List<PetNestLineageInfo>(64);

                try
                {
                    AddOfficialLineages(owner, byKey, ordered);
                    AddCustomLineages(byKey, ordered);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[PetNest] 血脉目录构建失败，按无可用血脉降级: " + e.Message);
                }

                _byKey = byKey;
                _ordered = ordered;
                _built = true;
                ModBehaviour.DevLog("[PetNest] 血脉目录构建完成，条目数=" + ordered.Count);
            }
        }

        private static void AddOfficialLineages(
            ModBehaviour owner,
            Dictionary<string, PetNestLineageInfo> byKey,
            List<PetNestLineageInfo> ordered)
        {
            if (owner == null) return;

            List<EnemyPresetInfo> pool = null;
            try { pool = owner.GetFilteredEnemyPresets(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 读取 Boss 过滤池失败: " + e.Message);
                return;
            }
            if (pool == null) return;

            for (int i = 0; i < pool.Count; i++)
            {
                EnemyPresetInfo info = pool[i];
                if (info == null || string.IsNullOrEmpty(info.name)) continue;
                if (byKey.ContainsKey(info.name)) continue;

                // fail-closed：nameKey 查不到官方 preset 就不进目录
                CharacterRandomPreset preset = PetNestCompanionSpawner.ResolveSourcePreset(info.name);
                if (preset == null) continue;

                PetNestLineageInfo entry = new PetNestLineageInfo();
                entry.LineageKey = info.name;
                entry.DisplayName = ResolveDisplayName(preset, info);
                entry.Element = DeriveElement(preset);
                entry.ModelScale = ResolveModelScale(info.name);
                entry.IsCustomBoss = false;

                byKey[entry.LineageKey] = entry;
                ordered.Add(entry);
            }
        }

        private static void AddCustomLineages(
            Dictionary<string, PetNestLineageInfo> byKey,
            List<PetNestLineageInfo> ordered)
        {
            for (int i = 0; i < CustomBossLineageKeys.Length; i++)
            {
                string key = CustomBossLineageKeys[i];
                if (string.IsNullOrEmpty(key) || byKey.ContainsKey(key)) continue;

                PetNestLineageInfo entry = new PetNestLineageInfo();
                entry.LineageKey = key;
                entry.DisplayName = key;
                ElementTypes element;
                entry.Element = CustomBossElements.TryGetValue(key, out element)
                    ? element
                    : ElementTypes.physics;
                entry.ModelScale = ResolveModelScale(key);
                entry.IsCustomBoss = true;

                // 自定义 Boss 的 runtime preset 是运行时构造的，目录构建期通常查不到；
                // 查得到时用它的显示名，查不到也照常登记（孵化期再解析，解析失败 fail-closed）。
                CharacterRandomPreset preset = PetNestCompanionSpawner.ResolveSourcePreset(key);
                if (preset != null)
                {
                    entry.DisplayName = ResolveDisplayName(preset, null);
                }

                byKey[entry.LineageKey] = entry;
                ordered.Add(entry);
            }
        }

        private static string ResolveDisplayName(CharacterRandomPreset preset, EnemyPresetInfo info)
        {
            try
            {
                string display = preset != null ? preset.DisplayName : null;
                if (!string.IsNullOrEmpty(display)) return display;
            }
            catch (Exception) { }

            if (info != null && !string.IsNullOrEmpty(info.displayName)) return info.displayName;
            if (info != null && !string.IsNullOrEmpty(info.name)) return info.name;
            return preset != null ? preset.nameKey : string.Empty;
        }

        /// <summary>
        /// 从 elementFactor_*（承伤系数，越低越抗）推导血脉自有元素。
        /// 全为 1（无偏好）时回落 physics。
        /// </summary>
        internal static ElementTypes DeriveElement(CharacterRandomPreset preset)
        {
            if (preset == null) return ElementTypes.physics;
            try
            {
                ElementTypes best = ElementTypes.physics;
                float bestFactor = preset.elementFactor_Physics;

                if (preset.elementFactor_Fire < bestFactor)
                {
                    bestFactor = preset.elementFactor_Fire; best = ElementTypes.fire;
                }
                if (preset.elementFactor_Ice < bestFactor)
                {
                    bestFactor = preset.elementFactor_Ice; best = ElementTypes.ice;
                }
                if (preset.elementFactor_Poison < bestFactor)
                {
                    bestFactor = preset.elementFactor_Poison; best = ElementTypes.poison;
                }
                if (preset.elementFactor_Electricity < bestFactor)
                {
                    bestFactor = preset.elementFactor_Electricity; best = ElementTypes.electricity;
                }
                if (preset.elementFactor_Space < bestFactor)
                {
                    bestFactor = preset.elementFactor_Space; best = ElementTypes.space;
                }
                if (preset.elementFactor_Ghost < bestFactor)
                {
                    bestFactor = preset.elementFactor_Ghost; best = ElementTypes.ghost;
                }

                // 无明显抗性偏好：回落 physics
                if (bestFactor >= 1f) return ElementTypes.physics;
                return best;
            }
            catch (Exception)
            {
                return ElementTypes.physics;
            }
        }

        private static float ResolveModelScale(string lineageKey)
        {
            float scale;
            if (!string.IsNullOrEmpty(lineageKey)
                && LineageModelScaleOverrides.TryGetValue(lineageKey, out scale))
            {
                return scale;
            }
            return PetNestTuning.DefaultCubModelScale;
        }

        #endregion

        #region 查询

        /// <summary>按血脉 key 查目录。未构建或查不到返回 false（调用方一律 fail-closed）。</summary>
        internal static bool TryGet(string lineageKey, out PetNestLineageInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(lineageKey)) return false;
            lock (_lock)
            {
                if (_byKey == null) return false;
                return _byKey.TryGetValue(lineageKey, out info);
            }
        }

        /// <summary>全部血脉（构建顺序）。返回内部列表的只读视图，调用方不得修改。</summary>
        internal static IList<PetNestLineageInfo> All
        {
            get
            {
                lock (_lock)
                {
                    if (_ordered == null) return new List<PetNestLineageInfo>();
                    return _ordered;
                }
            }
        }

        /// <summary>该血脉是否可产蛋 / 可孵化。</summary>
        internal static bool IsKnownLineage(string lineageKey)
        {
            PetNestLineageInfo info;
            return TryGet(lineageKey, out info);
        }

        /// <summary>目的地对应的元素（远征亲和判定用）。</summary>
        internal static ElementTypes GetDestinationElement(string destinationId)
        {
            if (string.Equals(destinationId, PetNestTuning.DestinationStormSea, StringComparison.Ordinal))
            {
                return ElementTypes.electricity;
            }
            if (string.Equals(destinationId, PetNestTuning.DestinationAcidRuins, StringComparison.Ordinal))
            {
                return ElementTypes.poison;
            }
            if (string.Equals(destinationId, PetNestTuning.DestinationFrozenWaste, StringComparison.Ordinal))
            {
                return ElementTypes.ice;
            }
            return ElementTypes.physics;
        }

        #endregion

        #region 清理

        /// <summary>
        /// 作废目录（Boss 池筛选变化 / preset 缓存刷新时）。下次 EnsureBuilt 会重建。
        /// </summary>
        internal static void Invalidate()
        {
            lock (_lock)
            {
                _byKey = null;
                _ordered = null;
                _built = false;
            }
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Invalidate();
        }

        #endregion
    }
}
