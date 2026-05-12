namespace BossRush
{
    /// <summary>
    /// Harmony Patch 分组接口 — 提供分组元数据与开关能力
    /// 本轮仅用于日志与预留开关语义，不改变 Patch apply 方式
    /// </summary>
    internal interface IHarmonyPatchGroup
    {
        /// <summary>分组名称（用于日志和调试）</summary>
        string GroupName { get; }

        /// <summary>是否启用（默认 true，预留未来按组禁用能力）</summary>
        bool IsEnabled { get; }
    }
}
