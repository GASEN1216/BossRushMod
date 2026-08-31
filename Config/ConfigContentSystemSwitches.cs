// ============================================================================
// ConfigContentSystemSwitches.cs - 内容系统总开关的恒开策略
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行，
// Config.cs 已贴顶 1188 行）；语义是同一份 partial class。
//
// 策略（owner 2026-08-30 定）：遗种巢 / 日报 / 图鉴 / 词缀锻造 / 随机事件 /
// 鸭王征程 / 竞技场后山这些玩法系统属于**默认内容**，不是可配置项。
// 它们的总开关不再注册进 ModConfig UI，玩家看不到、也不需要拨。
// 频率档、调试旁路那类**旋钮**不受此策略约束，照常暴露。
//
// 为什么光撤 UI 注册还不够：这五个开关在 139f6a1 及更早版本里是注册过的，
// 而且 `petNestEnabled` 首发默认值是 false。于是老玩家的两个持久化位置都可能
// 存着 false —— 本地 config 文件（JsonUtility 全字段回灌）与 ModConfig 存储。
// UI 撤掉之后这些 false 无处改回，玩家会被永久关在系统外面且毫无提示。
// 因此除了撤注册，还必须：
//   1. LoadConfigFromModConfig 不再读这五个键（见 Config/Config.cs）；
//   2. LoadConfigFromFile 末尾调用 ForceContentSystemSwitchesOn 把残留值抹平。
//
// 字段本身保留：dormant 契约与各系统的 IsXxxConfiguredEnabled 只读入口全部不变，
// 将来若要重新放出某个开关，只需恢复它的 Register 调用并登记回白名单。
// 由 tests/ModConfigOptionChangeGuard.py 守卫。
// ============================================================================

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 把内容系统总开关强制拉回开启，抹掉历史版本残留的 false。
        /// 在 LoadConfigFromFile 末尾调用；no-throw，缺 config 直接返回。
        /// </summary>
        private void ForceContentSystemSwitchesOn()
        {
            if (config == null) return;

            config.petNestEnabled = true;
            config.dailyReportEnabled = true;
            config.codexEnabled = true;
            config.affixForgeEnabled = true;
            config.randomEventsEnabled = true;
            config.campaignEnabled = true;
            config.backMountainEnabled = true;
            // backMountainUnlockAll 是调试旁路，不属于内容开关，保持玩家可拨且默认关闭
        }
    }
}
