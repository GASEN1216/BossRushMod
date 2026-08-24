namespace BossRush
{
    /// <summary>
    /// Mode G 正式可用性门控。
    /// 正式发布由 owner 显式裁决；开发直入和 raw PNG fallback 与正式入口独立。
    /// </summary>
    public static class ModeGAvailability
    {
        /// <summary>
        /// 正式路牌是否显示。
        /// </summary>
        // Owner release decision: the configured Boss pool and every map exposed by
        // the BossRush map-selection UI are approved for Mode G production use.
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
