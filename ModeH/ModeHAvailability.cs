using System;

namespace BossRush
{
    /// <summary>
    /// Mode H 入口可用性与编译期开发门（设计提案 §16、§18.1、§25.1、§29.14）。
    ///
    /// 说明：
    /// - 这里的两个 Allow* 常量是**编译期开发门**，不进配置、不进存档、不进 ModConfig，
    ///   发布构建恒为 false，由 ModeHConfigApiGuard 断言；
    /// - 可用性结果是不持久化的只读派生（三态见 <see cref="ModeHAvailabilityStatus"/>）；
    /// - 真实仓库抵押**没有开关**：进入模式即知情同意（§22.1），
    ///   本类不得出现任何真实资产 feature gate。
    /// </summary>
    public static class ModeHAvailability
    {
        #region 编译期开发门（发布构建恒为 false）

        /// <summary>
        /// 开发构建是否允许用 raw PNG 冒充正式展示 bundle。
        /// 正式构建必须为 false，缺 bundle 时 fail-closed。
        /// </summary>
        public const bool AllowDevRawPngFallback = false;

        /// <summary>
        /// 是否暴露 §29.14 的编译期控制点诊断 harness。
        /// 发布构建恒为 false；为 true 时也只有只读诊断，不创建 Season、不写存档、
        /// 不发奖励、不消耗船票，因此不构成向 owner 交付的技术样机。
        /// </summary>
        public const bool AllowDevControlPointHarness = false;

        #endregion

        #region fail-closed 原因 ID（用于入口 UI 与诊断页）

        /// <summary>模式总开关关闭。</summary>
        public const string ReasonDisabled = "modeh_disabled";
        /// <summary>真实资产风险扫描尚未完成。</summary>
        public const string ReasonRiskScanPending = "modeh_risk_scan_pending";
        /// <summary>存在未终结的真实资产事务。</summary>
        public const string ReasonExternalAssetRisk = "modeh_external_asset_risk";
        /// <summary>存在恢复壳或 slot barrier，只允许恢复流程。</summary>
        public const string ReasonRecoveryOnly = "modeh_recovery_only";
        /// <summary>静态内容不可用（数据、候选池、口令矩阵）。</summary>
        public const string ReasonContentNotReady = "modeh_content_not_ready";
        /// <summary>已有活动 run owner。</summary>
        public const string ReasonRunOwnerActive = "modeh_run_owner_active";
        /// <summary>存在其他模式 owner 或 pending/startup 冲突。</summary>
        public const string ReasonOtherModeActive = "modeh_other_mode_active";
        /// <summary>当前地图缺少 Mode H 点位。</summary>
        public const string ReasonMapUnsupported = "modeh_map_unsupported";
        /// <summary>展示资源缺失。</summary>
        public const string ReasonPresentationMissing = "modeh_presentation_missing";
        /// <summary>当前 runtime 生产认证未通过。</summary>
        public const string ReasonCertificationFailed = "modeh_certification_failed";
        /// <summary>owner 引用缺失（宿主未就绪）。</summary>
        public const string ReasonOwnerMissing = "modeh_owner_missing";

        #endregion

        #region 可用性判定

        /// <summary>
        /// 新赛季入口可用性（不含进入目标场景后的 runtime 生产认证）。
        /// no-throw；任何异常都返回 Unavailable。
        /// </summary>
        public static ModeHAvailabilityStatus EvaluateNewSeason(ModBehaviour owner, out string reasonId)
        {
            reasonId = null;
            try
            {
                if (owner == null)
                {
                    reasonId = ReasonOwnerMissing;
                    return ModeHAvailabilityStatus.Unavailable;
                }
                if (!owner.IsModeHConfiguredEnabled())
                {
                    reasonId = ReasonDisabled;
                    return ModeHAvailabilityStatus.Unavailable;
                }
                if (!ModeHRuntimeGates.IsModeHRiskScanReady)
                {
                    reasonId = ReasonRiskScanPending;
                    return ModeHAvailabilityStatus.Unavailable;
                }
                if (ModeHRuntimeGates.IsModeHExternalAssetRiskBlocked)
                {
                    reasonId = ReasonExternalAssetRisk;
                    return ModeHAvailabilityStatus.Unavailable;
                }
                if (ModeHRuntimeGates.IsModeHRecoveryOnlyBlocked)
                {
                    reasonId = ReasonRecoveryOnly;
                    return ModeHAvailabilityStatus.Unavailable;
                }
                if (!ModeHRuntimeGates.IsModeHContentReady)
                {
                    reasonId = ReasonContentNotReady;
                    return ModeHAvailabilityStatus.Unavailable;
                }
                if (ModeHRuntimeGates.IsModeHRunOwnerActive)
                {
                    reasonId = ReasonRunOwnerActive;
                    return ModeHAvailabilityStatus.Unavailable;
                }
                return ModeHAvailabilityStatus.Available;
            }
            catch (Exception)
            {
                reasonId = ReasonContentNotReady;
                return ModeHAvailabilityStatus.Unavailable;
            }
        }

        /// <summary>
        /// 恢复入口可用性：允许在配置关闭时处理已有 H 记录（§18.1）。
        /// </summary>
        public static ModeHAvailabilityStatus EvaluateRecovery(out string reasonId)
        {
            reasonId = null;
            try
            {
                if (ModeHRuntimeGates.IsRecoveryEntryAllowed())
                {
                    return ModeHAvailabilityStatus.Available;
                }
                reasonId = ReasonRecoveryOnly;
                return ModeHAvailabilityStatus.Unavailable;
            }
            catch (Exception)
            {
                reasonId = ReasonRecoveryOnly;
                return ModeHAvailabilityStatus.Unavailable;
            }
        }

        /// <summary>
        /// 把原因 ID 转成本地化 key。
        ///
        /// 只有下面这张白名单里的原因才拼具体 key —— 它们与 ModeHLocalization 注入的
        /// `Unavailable_modeh_*` 一一对应。运行期还会出现 `entry_exception:NullReference`
        /// 这类内部原因，拼出来的 key 没有注入，官方本地化会把它显示成 *星号原文*，
        /// 所以一律回落到通用文案。
        /// </summary>
        public static string GetReasonLocalizationKey(string reasonId)
        {
            if (IsLocalizedReason(reasonId))
            {
                return ModeHConfig.LocalizationKeyPrefix + "Unavailable_" + reasonId;
            }
            return ModeHConfig.LocalizationKeyPrefix + "Unavailable_Generic";
        }

        /// <summary>该原因 ID 是否有对应的已注入文案。</summary>
        public static bool IsLocalizedReason(string reasonId)
        {
            if (string.IsNullOrEmpty(reasonId)) return false;
            return reasonId == ReasonDisabled
                || reasonId == ReasonRiskScanPending
                || reasonId == ReasonExternalAssetRisk
                || reasonId == ReasonRecoveryOnly
                || reasonId == ReasonContentNotReady
                || reasonId == ReasonRunOwnerActive
                || reasonId == ReasonOtherModeActive
                || reasonId == ReasonMapUnsupported
                || reasonId == ReasonPresentationMissing
                || reasonId == ReasonCertificationFailed
                || reasonId == ReasonOwnerMissing;
        }

        #endregion
    }
}
