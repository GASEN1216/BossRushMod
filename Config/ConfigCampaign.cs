// ============================================================================
// ConfigCampaign.cs - 鸭王征程入口总开关的配置接线（M0 骨架）
// ============================================================================
// 与 Config/Config.cs 拆开只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行）；
// 语义是同一份 partial class：
//   - 征程属于**默认内容，恒为开启**：不注册进 ModConfig UI、不从 ModConfig 读、
//     默认值为 true，并由 ForceContentSystemSwitchesOn 抹平老档残留的 false。
//     策略与理由见 Config/ConfigContentSystemSwitches.cs。
//   - 唯一只读入口 IsCampaignConfiguredEnabled()：no-throw，缺配置返回 false；
//     禁止反射私有字段，也禁止把开关缓存进 CampaignTuning 或存档。
//   - 其余常量在 Campaign/CampaignTuning.cs，章节内容在 Assets/Data/Campaign/*.json。
//
// 字段与 dormant 契约保留：将来若要重新放出这个开关，恢复 Register 调用、
// 登记回 IsHandledModConfigOptionKey 白名单、并从 ForceContentSystemSwitchesOn 里摘掉即可。
// ============================================================================

using System;

namespace BossRush
{
    public partial class ModBehaviour
    {

        private partial class BossRushConfig
        {
            /// <summary>
            /// 鸭王征程入口开关。属于默认内容，恒为开启
            /// （见 Config/ConfigContentSystemSwitches.cs 的策略说明）。
            /// 字段与 dormant 契约保留：将来若要重新放出开关，只需恢复 Register 调用。
            /// </summary>
            public bool campaignEnabled = true;
        }

        /// <summary>
        /// 征程入口总开关的唯一只读入口：no-throw，缺配置返回 false。
        /// </summary>
        internal bool IsCampaignConfiguredEnabled()
        {
            try
            {
                return config != null && config.campaignEnabled;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
