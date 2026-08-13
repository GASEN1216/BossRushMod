namespace BossRush
{
    /// <summary>
    /// Mode G 正式可用性门控。
    /// Phase 0A/0B/1-4 全部通过前 IsProductionReady 必须为 false。
    /// 只有 owner 签署 Phase0A=Go、完成 release checklist 后才允许改为 true。
    /// 切换为 true 的同一变更必须包含全部 adapter eligibility、官方 6-key/适应能力 inventory、
    /// 武器计分矩阵、strict reward、存档、支持地图、展示 AssetBundle、性能、复玩指标和原模式回归 guard。
    /// </summary>
    public static class ModeGAvailability
    {
        /// <summary>
        /// 正式路牌是否显示。Phase 0A-4 全部通过前必须为 false。
        /// </summary>
        public const bool IsProductionReady = true;

        /// <summary>
        /// 开发构建是否允许通过 raw PNG fallback 验证布局。
        /// 正式构建不得静默使用 raw 文件冒充发布 bundle。
        /// </summary>
        public const bool AllowDevRawPngFallback = false;

        /// <summary>
        /// 开发构建是否允许通过独立测试入口（F9/direct/debug）进入中间阶段。
        /// </summary>
        public const bool AllowDevTestEntry = false;

        /// <summary>
        /// 当前 verification revision，用于 eligibility registry 和地图支持注册表。
        /// 游戏更新后必须重新验证并递增。
        /// </summary>
        public const string CurrentVerificationRevision = "2026-08-10.v1";

        /// <summary>
        /// 确定性随机 domain constants 冻结值（FNV-1a 64 offset basis 和 prime）。
        /// </summary>
        public const ulong FnvOffsetBasis = 14695981039346656037UL;
        public const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// SplitMix64 增量（2^64 / golden ratio，向下取整）。
        /// </summary>
        public const ulong SplitMix64Increment = 0x9E3779B97F4A7C15UL;
    }
}
