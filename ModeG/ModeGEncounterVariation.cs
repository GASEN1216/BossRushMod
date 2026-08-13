using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode G 编排变体与署名 Boss 注册表（规格 §6.2/§13.2/§18.3 重写版）。
    ///
    /// - 三个托管 Boss 使用 Mode G 自定义稳定 key（无官方独立 preset，§18.3）；
    /// - adapter 能力预检通过后经 SetSignatureEligibility 登记；初始一律 false，
    ///   "能力预检通过"只允许进入开发池（AllowDevTestEntry）；
    /// - Split/Pincer/Arc 落点偏移基于 WavePlan.GetFormationSpec 冻结描述。
    /// </summary>
    public static class ModeGEncounterVariation
    {
        #region Managed Signature Keys（Mode G 自定义稳定 key，归因/遥测/宿敌用）

        /// <summary>托管龙裔遗族稳定 key</summary>
        public const string ManagedDragonDescendantKey = "managed_dragon_descendant";
        /// <summary>托管龙王稳定 key</summary>
        public const string ManagedDragonKingKey = "managed_dragon_king";
        /// <summary>托管幽灵女巫稳定 key</summary>
        public const string ManagedPhantomWitchKey = "managed_phantom_witch";

        private static readonly string[] AllManagedKeys =
        {
            ManagedDragonDescendantKey,
            ManagedDragonKingKey,
            ManagedPhantomWitchKey
        };

        #endregion

        #region Signature Eligibility Registry（§18.3：初始一律 false）

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, bool> _eligibility = new Dictionary<string, bool>(StringComparer.Ordinal);

        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _eligibility.Clear();
            }
        }

        /// <summary>
        /// adapter 能力预检通过后登记 eligibility（幂等）。key 必须是托管 key。
        /// </summary>
        public static void SetSignatureEligibility(string key, bool eligible)
        {
            if (string.IsNullOrEmpty(key) || !IsManagedSignatureKey(key)) return;
            lock (_lock) { _eligibility[key] = eligible; }
        }

        /// <summary>
        /// 查询单个 key 的 eligibility（未登记 = false）。no-throw。
        /// </summary>
        public static bool IsSignatureEligible(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_lock)
            {
                bool eligible;
                return _eligibility.TryGetValue(key, out eligible) && eligible;
            }
        }

        /// <summary>
        /// eligible 署名 Boss key 升序快照（ModeGEntryPreview 冻结用）。
        /// 生产池为空且 AllowDevTestEntry 时返回三个托管 key 作为开发池（§18.3）。
        /// </summary>
        public static string[] GetEligibleSignatureKeys()
        {
            try
            {
                List<string> eligible = new List<string>();
                lock (_lock)
                {
                    for (int i = 0; i < AllManagedKeys.Length; i++)
                    {
                        bool ok;
                        if (_eligibility.TryGetValue(AllManagedKeys[i], out ok) && ok)
                        {
                            eligible.Add(AllManagedKeys[i]);
                        }
                    }
                }

                if (eligible.Count == 0 && !ModeGAvailability.IsProductionReady
                    && ModeGAvailability.AllowDevTestEntry)
                {
                    // 开发池：能力预检未通过的托管 key 仅作开发测试入口
                    eligible.AddRange(AllManagedKeys);
                }

                eligible.Sort(StringComparer.Ordinal);
                return eligible.ToArray();
            }
            catch
            {
                return new string[0];
            }
        }

        /// <summary>
        /// 是否 Mode G 托管署名 key（Dispatcher 路由判据）。
        /// </summary>
        public static bool IsManagedSignatureKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < AllManagedKeys.Length; i++)
            {
                if (string.Equals(AllManagedKeys[i], key, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// 托管 Boss 双语展示名（HUD/横幅用）。
        /// </summary>
        public static string GetManagedBossDisplayName(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (string.Equals(key, ManagedDragonDescendantKey, StringComparison.Ordinal))
                return L10n.T("龙裔遗族", "Dragon Descendant");
            if (string.Equals(key, ManagedDragonKingKey, StringComparison.Ordinal))
                return L10n.T("龙王", "Dragon King");
            if (string.Equals(key, ManagedPhantomWitchKey, StringComparison.Ordinal))
                return L10n.T("幽灵女巫", "Phantom Witch");
            return key;
        }

        #endregion

        #region Formation Offsets（基于 FormationSpec 冻结描述）

        /// <summary>
        /// 根据编排变体与 Boss 数量计算相对玩家锚点的 XZ 落点偏移。
        /// 半径取 FormationSpec.playerMinDistance；多 Boss 时按 bossPairMinDistance 拉开角度。
        /// </summary>
        public static UnityEngine.Vector2[] GetSpawnOffsets(
            ModeGPlanVariant variant, int bossCount, ModeGWavePlan.FormationSpec spec)
        {
            if (bossCount <= 0) return new UnityEngine.Vector2[0];
            if (bossCount == 1) return new UnityEngine.Vector2[] { new UnityEngine.Vector2(spec.playerMinDistance, 0f) };

            UnityEngine.Vector2[] offsets = new UnityEngine.Vector2[bossCount];
            float r = spec.playerMinDistance;

            switch (variant)
            {
                case ModeGPlanVariant.Pincer:
                {
                    // 钳形包夹：两翼对向玩家，前后微错开
                    for (int i = 0; i < bossCount; i++)
                    {
                        float side = (i % 2 == 0) ? 1f : -1f;
                        float depth = (i / 2) * spec.bossPairMinDistance * 0.5f;
                        offsets[i] = new UnityEngine.Vector2(side * r, side * depth);
                    }
                    break;
                }
                case ModeGPlanVariant.Arc:
                {
                    // 弧形包围：面向玩家的扇面（120 度跨度）
                    float spanRad = (float)(Math.PI * 2.0 / 3.0);
                    for (int i = 0; i < bossCount; i++)
                    {
                        float t = (float)i / (bossCount - 1) - 0.5f;
                        float angle = t * spanRad;
                        offsets[i] = new UnityEngine.Vector2(
                            (float)Math.Sin(angle) * r,
                            (float)Math.Cos(angle) * r);
                    }
                    break;
                }
                default:
                {
                    // Split 标准三角分布：均匀环形
                    for (int i = 0; i < bossCount; i++)
                    {
                        float angle = (float)((double)i / bossCount * Math.PI * 2.0);
                        offsets[i] = new UnityEngine.Vector2(
                            (float)Math.Cos(angle) * r,
                            (float)Math.Sin(angle) * r);
                    }
                    break;
                }
            }
            return offsets;
        }

        /// <summary>
        /// 编排变体双语展示名。
        /// </summary>
        public static string GetVariantDisplayName(ModeGPlanVariant variant)
        {
            switch (variant)
            {
                case ModeGPlanVariant.Split: return L10n.T("三角分布", "Split");
                case ModeGPlanVariant.Pincer: return L10n.T("钳形包夹", "Pincer");
                case ModeGPlanVariant.Arc: return L10n.T("弧形包围", "Arc");
                default: return L10n.T("标准", "Standard");
            }
        }

        #endregion
    }
}
