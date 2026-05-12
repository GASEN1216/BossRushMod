// ============================================================================
// BaseHubPatchGroup.cs - 基地设施 Patch 分组
// ============================================================================
// 说明：BaseHub 分组包含 StockShop 商店注入和船点交互注入相关的 Harmony Patch
// ============================================================================

namespace BossRush
{
    internal sealed class BaseHubPatchGroup : IHarmonyPatchGroup
    {
        public string GroupName => "BaseHub";
        public bool IsEnabled => true;
    }
}
