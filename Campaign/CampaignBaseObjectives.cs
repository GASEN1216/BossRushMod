// ============================================================================
// CampaignBaseObjectives.cs - 基地侧目标的事实提供者注册表
// ============================================================================
// garden_built（官方菜地已建成）与 trophy_displayed（官方枪械展示架 / 假人上摆着 Mod 战利品）的事实
// 归 Integration/BackMountain 所有，征程只读。依赖方向不变（后山读征程的 token 契约、
// 征程不引用后山类型）：后山在自己的 bootstrap 里把提供者注册进来、销毁时撤掉；
// 没有提供者时一律「未完成」（fail-closed），执行回归用替身提供者穷举。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>基地侧目标事实源。无提供者 = 未完成。</summary>
    internal static class CampaignBaseObjectives
    {
        private static readonly Dictionary<CampaignObjectiveKind, Func<bool>> _providers =
            new Dictionary<CampaignObjectiveKind, Func<bool>>();

        /// <summary>登记（同一类型后登记的覆盖先登记的）。</summary>
        internal static void RegisterProvider(CampaignObjectiveKind kind, Func<bool> provider)
        {
            if (provider == null) { _providers.Remove(kind); return; }
            _providers[kind] = provider;
        }

        internal static void UnregisterProvider(CampaignObjectiveKind kind)
        {
            _providers.Remove(kind);
        }

        internal static bool HasProvider(CampaignObjectiveKind kind)
        {
            return _providers.ContainsKey(kind);
        }

        /// <summary>某类基地侧目标现在是否已达成。提供者异常按未完成处理。</summary>
        internal static bool IsDone(CampaignObjectiveKind kind)
        {
            Func<bool> provider;
            if (!_providers.TryGetValue(kind, out provider) || provider == null) return false;
            try { return provider(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 基地侧目标判据异常 " + kind + ": " + e.Message);
                return false;
            }
        }

        /// <summary>本章全部基地侧目标是否达成；没有基地侧目标返回 true。</summary>
        internal static bool AllDone(CampaignChapterDef def)
        {
            if (def == null) return false;
            for (int i = 0; i < def.Objectives.Count; i++)
            {
                CampaignObjectiveDef objective = def.Objectives[i];
                if (objective != null && objective.IsBaseScope && !IsDone(objective.Kind)) return false;
            }
            return true;
        }

        /// <summary>第一条未达成的基地侧目标；全达成返回 null。</summary>
        internal static CampaignObjectiveDef FirstPending(CampaignChapterDef def)
        {
            if (def == null) return null;
            for (int i = 0; i < def.Objectives.Count; i++)
            {
                CampaignObjectiveDef objective = def.Objectives[i];
                if (objective != null && objective.IsBaseScope && !IsDone(objective.Kind)) return objective;
            }
            return null;
        }

        /// <summary>按目标序号的达成位，供事实指纹用。</summary>
        internal static int DoneBits(CampaignChapterDef def)
        {
            if (def == null) return 0;
            int bits = 0;
            for (int i = 0; i < def.Objectives.Count && i < 31; i++)
            {
                CampaignObjectiveDef objective = def.Objectives[i];
                if (objective != null && objective.IsBaseScope && IsDone(objective.Kind)) bits |= 1 << i;
            }
            return bits;
        }

        /// <summary>只供执行回归与宿主销毁使用；提供者平时由登记方自己撤销。</summary>
        internal static void ResetStaticCaches()
        {
            _providers.Clear();
        }
    }
}
