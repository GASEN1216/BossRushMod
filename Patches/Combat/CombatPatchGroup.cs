// ============================================================================
// CombatPatchGroup.cs - 战斗相关 Patch 分组
// ============================================================================
// 说明：Combat 分组包含角色死亡追加掉落和枪械半掩体修复相关的 Harmony Patch
// ============================================================================

namespace BossRush
{
    internal sealed class CombatPatchGroup : IHarmonyPatchGroup
    {
        public string GroupName => "Combat";
        public bool IsEnabled => true;
    }
}
