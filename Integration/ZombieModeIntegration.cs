// ============================================================================
// ZombieModeIntegration.cs - 末日丧尸模式集成边界文档
// ============================================================================
// 末日丧尸模式（ZombieMode）的集成代码已经按职责分散在以下位置，
// 本文件作为"集成边界标记"，让后续读者一眼看到丧尸模式跨文件的接入点：
//
//   ZombieMode/*.cs                           - 入场/波次/Boss/奖励/污染/UI 主体（23 个 partial 文件）
//   Integration/Items/ZombieTide*.cs          - 邀请函/信标物品配置 + 使用逻辑
//   Utilities/EnemyRecoveryMonitor.cs         - 丧尸模式专用敌人恢复监控
//   ModBehaviour.cs                           - TickZombieMode + 清理钩子调用
//   Localization/LocalizationInjector.cs      - L10n key 注入
//   DebugAndTools/F3DebugCheatMenu.cs         - F3 调试菜单入口
//   MapSelection/BossRushMapSelectionHelper   - 共享地图选择 helper
//
// BossRushIntegration.cs 内只保留一行调度：
//   OnSceneLoaded → TryHandleZombieModePendingMapSceneLoaded(scene, loadedMapConfig)
//   该方法定义在 ZombieMode/ZombieModeEntry.cs，属于 partial class ModBehaviour。
//
// 这种分散布局符合项目其它模式（ModeD/E/F）的惯例，无需把零散调度搬到
// 一个 ZombieModeIntegration partial 内部——审查文档当时认为
// BossRushIntegration 行数翻倍至 6786，实际为 3394 行（CRLF 噪声放大），
// 既有结构已经满足"丧尸模式独立成 partial"的目标。
// ============================================================================

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 当前没有需要在此 partial 持有的丧尸特定状态/方法。
        // 如未来需要新增"跨多个 ZombieMode 文件、又不属于具体子模块"的整合代码，
        // 在此追加；继续避免把它们塞进 BossRushIntegration.cs。
    }
}
