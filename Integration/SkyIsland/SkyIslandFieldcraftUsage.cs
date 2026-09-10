// ============================================================================
// SkyIslandFieldcraftUsage.cs - 天空岛局内耗材（风灯 / 驱风香 / 晴岚护符）的「用」
// ============================================================================
// 形态照 SkyIslandCompassUsage。效果由天空岛会话里的 SkyIslandFieldcraft 执行（它持有本趟的增益记录与夜风状态），
// 这里只在玩家按下使用时找一次 owner，不在任何每帧路径上。
//
// 与罗盘的区别：耗材是**会被吃掉的**（官方 CA_UseItem 用完扣一层堆叠），所以离岛时 CanBeUsed 直接返回 false，
// 按钮置灰，不会白白吃掉一件。
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>天空岛局内耗材使用行为：效果种类由物品配置器写入 <see cref="buff"/>。</summary>
    public class SkyIslandFieldcraftUsage : UsageBehavior
    {
        /// <summary><see cref="SkyIslandFieldBuff"/> 的序号。公有字段：运行时克隆的 prefab 实例化时随之复制。</summary>
        public int buff = (int)SkyIslandFieldBuff.None;

        private SkyIslandFieldBuff Buff { get { return (SkyIslandFieldBuff)buff; } }

        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = SkyIslandFieldcraftRules.UsageText(Buff)
                };
            }
        }

        public override bool CanBeUsed(Item item, object user)
        {
            SkyIslandFieldcraft owner = SkyIslandFieldcraft.Current;
            return item != null && user is CharacterMainControl && owner != null && owner.CanUse(Buff);
        }

        protected override void OnUse(Item item, object user)
        {
            try
            {
                SkyIslandFieldcraft owner = SkyIslandFieldcraft.Current;
                if (owner != null && owner.UseConsumable(Buff)) return;
                Duckov.UI.NotificationText.Push(SkyIslandFieldcraftRules.OffIsland);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SkyIslandItems] 群岛耗材使用失败: " + e.Message);
            }
        }
    }
}
