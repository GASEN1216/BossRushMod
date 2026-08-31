// ============================================================================
// ConfigModConfigKeys.cs - ModConfig 变更键白名单（从 Config.cs 提取）
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行，
// Config.cs 已贴顶）；语义是同一份 partial class，行为与提取前逐字一致。
//
// 这张白名单是 ModConfig 变更事件的**前置闸门**：
//   OnModConfigOptionsChanged 第一件事就是 `if (!IsHandledModConfigOptionKey(k)) return;`
// 因此新增配置项时**必须**同步登记本表，否则对应的
// TryLoadXxxSingleModConfigValue 分支永远不可达——表现为玩家在 ModConfig UI 里
// 拨动开关毫无反应，且不报任何错。历史上 PetNest / 日报 / 图鉴 / 词缀锻造 /
// 随机事件五个系统都漏登记过，是本仓库重复踩过的坑。
//
// 老键沿用字面量（改动它们等于破坏玩家已有配置）；新键一律引用各子系统的
// KeySuffix 常量，避免白名单与注册处的字面量各写一遍而漂移。
// 由 tests/ModConfigOptionChangeGuard.py 守卫。
// ============================================================================

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 该 ModConfig 键是否由本 Mod 处理。OnModConfigOptionsChanged 的前置闸门。
        /// 新增配置项必须在此登记，否则单键热更新分支不可达。
        /// </summary>
        private bool IsHandledModConfigOptionKey(string changedKey)
        {
            return changedKey == ModName + "_waveIntervalSeconds" ||
                   changedKey == ModName + "_EnableRandomBossLoot" ||
                   changedKey == ModName + "_UseLegacyBossLootProbabilities" ||
                   changedKey == ModName + "_UseInteractBetweenWaves" ||
                   changedKey == ModName + "_LootBoxBlocksBullets" ||
                   changedKey == ModName + "_InfiniteHellBossesPerWave" ||
                   changedKey == ModName + "_BossStatMultiplier" ||
                   changedKey == ModName + "_milestoneRestBonusSeconds" ||
                   changedKey == ModName + "_EnableDragonDash" ||
                   changedKey == ModName + "_UseWolfModelForWildHorn" ||
                   changedKey == ModName + "_EnableDeathWraithSystem" ||
                   changedKey == ModName + "_EnableMutators" ||
                   changedKey == ModName + "_MutatorCount" ||
                   changedKey == ModName + "_ModeDEnemiesPerWave" ||
                   changedKey == ModName + "_AchievementHotkey" ||
                   changedKey == ModName + "_ModeHEnabled" ||
                   // 以下键的单键加载委托给各子系统的 TryLoadXxxSingleModConfigValue，
                   // 这里直接复用各自的 KeySuffix 常量，避免白名单与注册处的字面量漂移。
                   // 八个内容系统总开关已不再注册进 ModConfig UI（恒为开启），
                   // 因此不登记在此：登记了也永远收不到变更事件，属于死条目。
                   changedKey == ModName + RandomEventsFrequencyModConfigKeySuffix ||
                   changedKey == ModName + ModeGAbandonHotkeyModConfigKeySuffix ||
                   changedKey == ModName + BackMountainUnlockAllModConfigKeySuffix;
        }
    }
}
