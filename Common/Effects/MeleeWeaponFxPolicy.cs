using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 近战武器 FX 初始化策略值对象
    /// 由具体武器显式声明自己允许什么 fallback，取代三份 EnsureMeleeAttackFx 模板复制
    /// </summary>
    internal readonly struct MeleeWeaponFxPolicy
    {
        /// <summary>是否允许 slashFx 回退赋值</summary>
        public readonly bool AllowSlashFxFallback;

        /// <summary>是否允许 hitFx 回退赋值</summary>
        public readonly bool AllowHitFxFallback;

        /// <summary>可选的 slashFx 缩放覆盖（null 表示使用默认逻辑）</summary>
        public readonly Vector3? OverrideSlashFxScale;

        public MeleeWeaponFxPolicy(
            bool allowSlashFxFallback,
            bool allowHitFxFallback,
            Vector3? overrideSlashFxScale = null)
        {
            AllowSlashFxFallback = allowSlashFxFallback;
            AllowHitFxFallback = allowHitFxFallback;
            OverrideSlashFxScale = overrideSlashFxScale;
        }

        /// <summary>
        /// 对外统一入口 - 取代三个 EnsureMeleeAttackFx 的重复模板
        /// </summary>
        /// <param name="meleeAgent">目标近战武器 Agent</param>
        /// <param name="getFallbackSlashFx">获取 slashFx 回退对象的工厂委托（延迟求值）</param>
        /// <param name="getFallbackHitFx">获取 hitFx 回退对象的工厂委托（延迟求值）</param>
        public void ApplyTo(
            ItemAgent_MeleeWeapon meleeAgent,
            Func<GameObject> getFallbackSlashFx,
            Func<GameObject> getFallbackHitFx)
        {
            if (meleeAgent == null)
            {
                return;
            }

            // ---- slashFx 分支契约 ----
            // 当 AllowSlashFxFallback == false 时，完全跳过 slashFx 赋值，
            // 供需要保留原始 slashFx 状态的武器显式使用。
            if (AllowSlashFxFallback && meleeAgent.slashFx == null)
            {
                meleeAgent.slashFx = getFallbackSlashFx?.Invoke();
            }

            // ---- hitFx 分支 ----
            if (AllowHitFxFallback && meleeAgent.hitFx == null)
            {
                meleeAgent.hitFx = getFallbackHitFx?.Invoke();
            }

            // ---- 缩放修正 ----
            if (meleeAgent.slashFx != null && meleeAgent.slashFx.transform != null)
            {
                Vector3 scale = meleeAgent.slashFx.transform.localScale;
                if (scale.sqrMagnitude <= 0.0001f)
                {
                    meleeAgent.slashFx.transform.localScale =
                        OverrideSlashFxScale ?? new Vector3(1.8f, 1.8f, 1.8f);
                }
            }
        }
    }
}
