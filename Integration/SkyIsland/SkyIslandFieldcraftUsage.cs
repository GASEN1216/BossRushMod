// ============================================================================
// SkyIslandFieldcraftUsage.cs - 天空岛局内耗材（风灯 / 驱风香 / 晴岚护符）、归航菜便当那一顿与晴岚航徽拉缆绳的「用」
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
            CharacterMainControl player = user as CharacterMainControl;
            if (item == null || player == null || player != CharacterMainControl.Main) return false;
            SkyIslandFieldcraft owner = SkyIslandFieldcraft.Current;
            // CA_UseItem 完成读条时会再问一次，之后无条件扣量。已开始的单效果耗材必须走进
            // OnUse 才能在离岛、失效或部署失败时补偿；普通可用性查询仍按 owner 门控。
            return (owner != null && owner.CanUse(Buff)) || (SingleConsumable && IsFinishingUse(item, player));
        }

        protected override void OnUse(Item item, object user)
        {
            bool applied = false;
            try
            {
                // 不消耗的物品（晴岚航徽）：官方 CA_UseItem 用完后按耐久决定要不要销毁。更新前发出的航徽可能没有耐久记录，先补满，免得用一次就没了。
                if (item != null && !item.Stackable && item.MaxDurability > 0f && item.Durability < 1f) item.Durability = item.MaxDurability;
                SkyIslandFieldcraft owner = SkyIslandFieldcraft.Current;
                if (owner != null)
                {
                    applied = owner.UseConsumable(Buff);
                    if (applied) return;
                    // owner 已经给出具体失败原因（例如没有落灯地面），不要覆盖成“离岛”。
                    return;
                }
                Duckov.UI.NotificationText.Push(SkyIslandFieldcraftRules.OffIsland);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SkyIslandItems] 群岛耗材使用失败: " + e.Message);
            }
            finally
            {
                // 与 RaidMealUsageBehavior 同口径：官方下一句同步 StackCount--，用 Count KV
                // 预补一件可同时保住最后一件与满堆。饭/药还有官方效果，不能整件退款。
                if (!applied && SingleConsumable && item != null && item.Stackable
                    && IsFinishingUse(item, user as CharacterMainControl))
                    item.SetInt("Count", item.StackCount + 1, true);
            }
        }

        private bool SingleConsumable
        {
            get
            {
                return Buff == SkyIslandFieldBuff.Lantern || Buff == SkyIslandFieldBuff.Incense
                    || Buff == SkyIslandFieldBuff.Charm || Buff == SkyIslandFieldBuff.Zapper;
            }
        }

        private static bool IsFinishingUse(Item item, CharacterMainControl player)
        {
            return player != null && player == CharacterMainControl.Main && player.CurrentAction is CA_UseItem
                && player.CurrentAction.Running && player.CurrentHoldItemAgent != null
                && player.CurrentHoldItemAgent.Item == item;
        }
    }
}
