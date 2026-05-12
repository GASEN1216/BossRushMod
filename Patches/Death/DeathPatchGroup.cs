// ============================================================================
// DeathPatchGroup.cs - 死亡/尸体相关 Patch 分组
// ============================================================================
// 说明：Death 分组包含 DeadBody 尸体生成、墓地战利品、尸体触碰相关的 Harmony Patch
// ============================================================================

namespace BossRush
{
    internal sealed class DeathPatchGroup : IHarmonyPatchGroup
    {
        public string GroupName => "Death";
        public bool IsEnabled => true;
    }
}
