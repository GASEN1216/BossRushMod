// ============================================================================
// EquipmentLocalization.cs - 装备本地化
// ============================================================================
// 模块说明：
//   统一管理所有自定义装备的本地化文本注入
//   - 龙套装（龙头、龙甲）
//   - 未来可扩展其他装备
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 装备本地化管理
    /// </summary>
    public static class EquipmentLocalization
    {
        #region 龙套装本地化数据
        
        // 赤龙首（原龙头）
        private static readonly string DragonHelmNameCN = "赤龙首";
        private static readonly string DragonHelmNameEN = "Crimson Dragon Helm";
        private static readonly string DragonHelmDescCN = "火龙残骸锻成的头盔，摸着还热。\n<color=#FFD700>【龙之套装】</color>与焰鳞甲同时穿戴：\n双击方向键冲刺3米。火焰伤害转为治疗。";
        private static readonly string DragonHelmDescEN = "A helm forged from fire dragon remains. Still warm to the touch.\n<color=#FFD700>[Dragon Set]</color> Wear with Flame Scale Armor:\nDouble-tap movement to dash 3m. Fire damage heals you.";
        
        // 焰鳞甲（原龙甲）
        private static readonly string DragonArmorNameCN = "焰鳞甲";
        private static readonly string DragonArmorNameEN = "Flame Scale Armor";
        private static readonly string DragonArmorDescCN = "护甲留着火龙胸鳞的弧度，贴身的一面总是温的。\n<color=#FFD700>【龙之套装】</color>与赤龙首同时穿戴：\n双击方向键冲刺3米。火焰伤害转为治疗。";
        private static readonly string DragonArmorDescEN = "Armor shaped from a fire dragon's chest scales. Warm against the skin.\n<color=#FFD700>[Dragon Set]</color> Wear with Crimson Dragon Helm:\nDouble-tap movement to dash 3m. Fire damage heals you.";
        
        #endregion
        
        #region 龙裔遗族Boss本地化数据
        
        // 龙裔遗族Boss名称（红色显示）
        private static readonly string DragonDescendantNameCN = "<color=red>龙裔遗族</color>";
        private static readonly string DragonDescendantNameEN = "<color=red>Dragon Descendant</color>";
        
        // 复活台词
        private static readonly string DragonDescendantResurrectionCN = "我...命不该绝！";
        private static readonly string DragonDescendantResurrectionEN = "I... shall not fall!";
        
        #endregion
        
        #region 龙王套装本地化数据
        
        // 焚天龙皇Boss名称（红色显示）
        private static readonly string DragonKingBossNameCN = "<color=red>焚天龙皇</color>";
        private static readonly string DragonKingBossNameEN = "<color=red>Skyburner Dragon Lord</color>";
        
        // 龙王之冕（龙王专属头盔）
        private static readonly string DragonKingHelmNameCN = "龙王之冕";
        private static readonly string DragonKingHelmNameEN = "Dragon King's Crown";
        private static readonly string DragonKingHelmDescCN = "龙王遗下的冠冕，额前的王印烫得碰不得。\n<color=#FFD700>【龙王套装】</color>与龙王鳞铠同时穿戴：\n双击方向键冲刺6米，可再接一次3米冲刺。\n冲刺留下熔浆灼烧敌人。火焰伤害转为治疗。";
        private static readonly string DragonKingHelmDescEN = "The Dragon King's crown. Its royal seal is too hot to touch.\n<color=#FFD700>[Dragon King Set]</color> Wear with Dragon King's Scale Mail:\nDouble-tap movement to dash 6m, then chain a 3m dash.\nDashes leave burning lava. Fire damage heals you.";
        
        // 龙王鳞铠（龙王专属护甲）
        private static readonly string DragonKingArmorNameCN = "龙王鳞铠";
        private static readonly string DragonKingArmorNameEN = "Dragon King's Scale Mail";
        private static readonly string DragonKingArmorDescCN = "龙王心口的鳞甲锻成，贴近时能听到微弱的跳动。\n<color=#FFD700>【龙王套装】</color>与龙王之冕同时穿戴：\n双击方向键冲刺6米，可再接一次3米冲刺。\n冲刺留下熔浆灼烧敌人。火焰伤害转为治疗。";
        private static readonly string DragonKingArmorDescEN = "Forged from scales over the Dragon King's heart. A faint beat remains.\n<color=#FFD700>[Dragon King Set]</color> Wear with Dragon King's Crown:\nDouble-tap movement to dash 6m, then chain a 3m dash.\nDashes leave burning lava. Fire damage heals you.";
        
        #endregion

        #region 幽灵女巫Boss本地化数据

        private static readonly string PhantomWitchBossNameCN = "<color=red>幽灵女巫</color>";
        private static readonly string PhantomWitchBossNameEN = "<color=red>Phantom Witch</color>";

        #endregion
        
        #region 龙息武器本地化数据
        
        // 龙息武器名称
        private static readonly string DragonBreathNameCN = "龙息";
        private static readonly string DragonBreathNameEN = "Dragon's Breath";
        
        // 龙息武器描述
        private static readonly string DragonBreathDescCN = "J-Lab实验室将赤龙的残骸与MCX相结合的完美艺术品。按下扳机的那一刻，你会明白\"生存\"和\"撤离\"之间还有第三个选项：把道路烤出来。";
        private static readonly string DragonBreathDescEN = "A masterpiece from J-Lab, fusing crimson dragon remains with the MCX. The moment you pull the trigger, you'll realize there's a third option between 'survive' and 'extract': burn your way out.";
        
        #endregion
        
        #region 龙焰灼烧Buff本地化数据
        
        // 龙焰灼烧Buff名称
        private static readonly string DragonBurnNameCN = "龙焰灼烧";
        private static readonly string DragonBurnNameEN = "Dragon Burn";
        
        // 龙焰灼烧Buff描述
        private static readonly string DragonBurnDescCN = "每秒受到最大生命值0.1%+1点真实火焰伤害，最多叠加10层，持续10秒";
        private static readonly string DragonBurnDescEN = "Takes 0.1% max HP + 1 true fire damage per second per layer, stacks up to 10, lasts 10 seconds";
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 注入所有装备本地化
        /// </summary>
        public static void InjectAllEquipmentLocalizations()
        {
            try
            {
                InjectDragonSetLocalization();
                InjectDragonKingSetLocalization();  // 龙王套装
                InjectPhantomWitchBossLocalization();
                InjectDragonDescendantLocalization();
                InjectDragonBreathWeaponLocalization();
                InjectDragonBurnBuffLocalization();
                InjectFrostSetLocalization();       // 冰霜套装
                InjectThunderSetLocalization();     // 雷霆套装
                SkyIslandBossGearConfig.InjectLocalization();  // 天空岛头目 / 岛主的专属装备
                ModBehaviour.DevLog("[EquipmentLocalization] 所有装备本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入装备本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙裔遗族Boss本地化
        /// </summary>
        public static void InjectDragonDescendantLocalization()
        {
            try
            {
                string displayName = L10n.T(DragonDescendantNameCN, DragonDescendantNameEN);
                
                // 注入Boss名称键
                LocalizationHelper.InjectLocalization(DragonDescendantConfig.BOSS_NAME_KEY, displayName);
                LocalizationHelper.InjectLocalization("Characters_" + DragonDescendantConfig.BOSS_NAME_KEY, displayName);
                LocalizationHelper.InjectLocalization("DragonDescendant_Preset", displayName);
                LocalizationHelper.InjectLocalization(DragonDescendantConfig.BOSS_NAME_CN, displayName);
                LocalizationHelper.InjectLocalization(DragonDescendantConfig.BOSS_NAME_EN, displayName);
                LocalizationHelper.InjectLocalization("龙裔遗族", displayName);
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙裔遗族本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙裔遗族本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取龙裔遗族显示名称
        /// </summary>
        public static string GetDragonDescendantName()
        {
            return L10n.T(DragonDescendantNameCN, DragonDescendantNameEN);
        }
        
        /// <summary>
        /// 获取龙裔遗族复活台词
        /// </summary>
        public static string GetDragonDescendantResurrectionDialogue()
        {
            return L10n.T(DragonDescendantResurrectionCN, DragonDescendantResurrectionEN);
        }
        
        /// <summary>
        /// 注入龙套装本地化（龙头 + 龙甲）
        /// </summary>
        public static void InjectDragonSetLocalization()
        {
            InjectDragonHelmLocalization(0);
            InjectDragonArmorLocalization(0);
        }
        
        /// <summary>
        /// 注入龙王套装本地化（龙王之冕 + 龙王鳞铠 + Boss名称）
        /// </summary>
        public static void InjectDragonKingSetLocalization()
        {
            InjectDragonKingBossLocalization();
            InjectDragonKingHelmLocalization(0);
            InjectDragonKingArmorLocalization(0);
            InjectDragonKingBossGunLocalization();
        }
        
        /// <summary>
        /// 注入焚天龙皇Boss名称本地化
        /// </summary>
        public static void InjectDragonKingBossLocalization()
        {
            try
            {
                string displayName = L10n.T(DragonKingBossNameCN, DragonKingBossNameEN);
                
                // 注入Boss名称键（与DragonKingConfig.BossNameKey一致）
                LocalizationHelper.InjectLocalization(DragonKingConfig.BossNameKey, displayName);
                LocalizationHelper.InjectLocalization("Characters_" + DragonKingConfig.BossNameKey, displayName);
                LocalizationHelper.InjectLocalization("DragonKing_Preset", displayName);
                LocalizationHelper.InjectLocalization(DragonKingConfig.BossNameCN, displayName);
                LocalizationHelper.InjectLocalization(DragonKingConfig.BossNameEN, displayName);
                // 也注入旧名称键以保持兼容
                LocalizationHelper.InjectLocalization("龙王", displayName);
                LocalizationHelper.InjectLocalization("Dragon King", displayName);
                
                ModBehaviour.DevLog("[EquipmentLocalization] 焚天龙皇Boss本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入焚天龙皇Boss本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取焚天龙皇Boss显示名称
        /// </summary>
        public static string GetDragonKingBossName()
        {
            return L10n.T(DragonKingBossNameCN, DragonKingBossNameEN);
        }

        /// <summary>
        /// 注入幽灵女巫Boss名称本地化
        /// </summary>
        public static void InjectPhantomWitchBossLocalization()
        {
            try
            {
                string displayName = L10n.T(PhantomWitchBossNameCN, PhantomWitchBossNameEN);

                LocalizationHelper.InjectLocalization(PhantomWitchConfig.BossNameKey, displayName);
                LocalizationHelper.InjectLocalization("Characters_" + PhantomWitchConfig.BossNameKey, displayName);
                LocalizationHelper.InjectLocalization("PhantomWitch_Preset", displayName);
                LocalizationHelper.InjectLocalization(PhantomWitchConfig.BossNameCN, displayName);
                LocalizationHelper.InjectLocalization(PhantomWitchConfig.BossNameEN, displayName);

                ModBehaviour.DevLog("[EquipmentLocalization] 幽灵女巫Boss本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入幽灵女巫Boss本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙头本地化
        /// </summary>
        /// <param name="typeId">物品 TypeID，用于注入 Item_xxx 键（0 表示不注入）</param>
        public static void InjectDragonHelmLocalization(int typeId)
        {
            try
            {
                string displayName = L10n.T(DragonHelmNameCN, DragonHelmNameEN);
                string description = L10n.T(DragonHelmDescCN, DragonHelmDescEN);
                
                // 注入原始键（Item 的 displayName 字段值为 "龙头"）
                LocalizationHelper.InjectLocalization("龙头", displayName);
                LocalizationHelper.InjectLocalization("龙头_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                ModBehaviour.DevLog("[EquipmentLocalization] 赤龙首本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入赤龙首本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙甲本地化
        /// </summary>
        /// <param name="typeId">物品 TypeID，用于注入 Item_xxx 键（0 表示不注入）</param>
        public static void InjectDragonArmorLocalization(int typeId)
        {
            try
            {
                string displayName = L10n.T(DragonArmorNameCN, DragonArmorNameEN);
                string description = L10n.T(DragonArmorDescCN, DragonArmorDescEN);
                
                // 注入原始键（Item 的 displayName 字段值为 "龙甲"）
                LocalizationHelper.InjectLocalization("龙甲", displayName);
                LocalizationHelper.InjectLocalization("龙甲_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                ModBehaviour.DevLog("[EquipmentLocalization] 焰鳞甲本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入焰鳞甲本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙王之冕本地化
        /// </summary>
        /// <param name="typeId">物品 TypeID，用于注入 Item_xxx 键（0 表示不注入）</param>
        public static void InjectDragonKingHelmLocalization(int typeId)
        {
            try
            {
                string displayName = L10n.T(DragonKingHelmNameCN, DragonKingHelmNameEN);
                string description = L10n.T(DragonKingHelmDescCN, DragonKingHelmDescEN);
                
                // 注入原始键（Unity Prefab 中 displayName 字段值）
                LocalizationHelper.InjectLocalization("dragonking_Helmet_Item", displayName);
                LocalizationHelper.InjectLocalization("dragonking_Helmet_Item_Desc", description);
                // 也注入中文键以备用
                LocalizationHelper.InjectLocalization("龙王之冕", displayName);
                LocalizationHelper.InjectLocalization("龙王之冕_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙王之冕本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙王之冕本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙王鳞铠本地化
        /// </summary>
        /// <param name="typeId">物品 TypeID，用于注入 Item_xxx 键（0 表示不注入）</param>
        public static void InjectDragonKingArmorLocalization(int typeId)
        {
            try
            {
                string displayName = L10n.T(DragonKingArmorNameCN, DragonKingArmorNameEN);
                string description = L10n.T(DragonKingArmorDescCN, DragonKingArmorDescEN);
                
                // 注入原始键（Unity Prefab 中 displayName 字段值）
                LocalizationHelper.InjectLocalization("dragonking_Armor_Item", displayName);
                LocalizationHelper.InjectLocalization("dragonking_Armor_Item_Desc", description);
                // 也注入中文键以备用
                LocalizationHelper.InjectLocalization("龙王鳞铠", displayName);
                LocalizationHelper.InjectLocalization("龙王鳞铠_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙王鳞铠本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙王鳞铠本地化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注入焚天龙铳本地化
        /// </summary>
        public static void InjectDragonKingBossGunLocalization()
        {
            try
            {
                DragonKingBossGunConfig.InjectLocalization();
                ModBehaviour.DevLog("[EquipmentLocalization] 焚天龙铳本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入焚天龙铳本地化失败: " + e.Message);
            }
        }
        
        #endregion
        
        #region 辅助方法 - 获取本地化文本
        
        /// <summary>
        /// 获取赤龙首显示名称
        /// </summary>
        public static string GetDragonHelmName()
        {
            return L10n.T(DragonHelmNameCN, DragonHelmNameEN);
        }
        
        /// <summary>
        /// 获取赤龙首描述
        /// </summary>
        public static string GetDragonHelmDescription()
        {
            return L10n.T(DragonHelmDescCN, DragonHelmDescEN);
        }
        
        /// <summary>
        /// 获取焰鳞甲显示名称
        /// </summary>
        public static string GetDragonArmorName()
        {
            return L10n.T(DragonArmorNameCN, DragonArmorNameEN);
        }
        
        /// <summary>
        /// 获取焰鳞甲描述
        /// </summary>
        public static string GetDragonArmorDescription()
        {
            return L10n.T(DragonArmorDescCN, DragonArmorDescEN);
        }
        
        /// <summary>
        /// 获取龙王之冕显示名称
        /// </summary>
        public static string GetDragonKingHelmName()
        {
            return L10n.T(DragonKingHelmNameCN, DragonKingHelmNameEN);
        }
        
        /// <summary>
        /// 获取龙王之冕描述
        /// </summary>
        public static string GetDragonKingHelmDescription()
        {
            return L10n.T(DragonKingHelmDescCN, DragonKingHelmDescEN);
        }
        
        /// <summary>
        /// 获取龙王鳞铠显示名称
        /// </summary>
        public static string GetDragonKingArmorName()
        {
            return L10n.T(DragonKingArmorNameCN, DragonKingArmorNameEN);
        }
        
        /// <summary>
        /// 获取龙王鳞铠描述
        /// </summary>
        public static string GetDragonKingArmorDescription()
        {
            return L10n.T(DragonKingArmorDescCN, DragonKingArmorDescEN);
        }
        
        #endregion
        
        #region 龙息武器本地化方法
        
        /// <summary>
        /// 注入龙息武器本地化
        /// </summary>
        public static void InjectDragonBreathWeaponLocalization()
        {
            try
            {
                string displayName = L10n.T(DragonBreathNameCN, DragonBreathNameEN);
                string description = L10n.T(DragonBreathDescCN, DragonBreathDescEN);
                
                // 注入原始键（Item 的 displayName 字段值为 "龙息"）
                LocalizationHelper.InjectLocalization("龙息", displayName);
                LocalizationHelper.InjectLocalization("龙息_Desc", description);
                
                // 注入 DragonBreathConfig 中定义的本地化键
                LocalizationHelper.InjectLocalization(DragonBreathConfig.LOC_KEY_WEAPON_NAME, displayName);
                LocalizationHelper.InjectLocalization(DragonBreathConfig.LOC_KEY_WEAPON_DESC, description);
                
                // 注入物品 ID 键（TypeID = 500005）
                int typeId = DragonBreathConfig.WEAPON_TYPE_ID;
                string itemKey = "Item_" + typeId;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙息武器本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙息武器本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取龙息武器显示名称
        /// </summary>
        public static string GetDragonBreathName()
        {
            return L10n.T(DragonBreathNameCN, DragonBreathNameEN);
        }
        
        /// <summary>
        /// 获取龙息武器描述
        /// </summary>
        public static string GetDragonBreathDescription()
        {
            return L10n.T(DragonBreathDescCN, DragonBreathDescEN);
        }
        
        #endregion
        
        #region 龙焰灼烧Buff本地化方法
        
        /// <summary>
        /// 注入龙焰灼烧Buff本地化
        /// </summary>
        public static void InjectDragonBurnBuffLocalization()
        {
            try
            {
                string displayName = L10n.T(DragonBurnNameCN, DragonBurnNameEN);
                string description = L10n.T(DragonBurnDescCN, DragonBurnDescEN);
                
                // 注入 DragonBreathConfig 中定义的本地化键
                LocalizationHelper.InjectLocalization(DragonBreathConfig.LOC_KEY_BUFF_NAME, displayName);
                LocalizationHelper.InjectLocalization(DragonBreathConfig.LOC_KEY_BUFF_DESC, description);
                
                // 注入原始键（Buff 的 displayName 字段值）
                LocalizationHelper.InjectLocalization("龙焰灼烧", displayName);
                LocalizationHelper.InjectLocalization("龙焰灼烧_Desc", description);
                
                // 注入 Buff ID 键（BuffID = 500006）
                int buffId = DragonBreathConfig.BUFF_ID;
                string buffKey = "Buff_" + buffId;
                LocalizationHelper.InjectLocalization(buffKey, displayName);
                LocalizationHelper.InjectLocalization(buffKey + "_Desc", description);
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙焰灼烧Buff本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙焰灼烧Buff本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取龙焰灼烧Buff显示名称
        /// </summary>
        public static string GetDragonBurnName()
        {
            return L10n.T(DragonBurnNameCN, DragonBurnNameEN);
        }
        
        /// <summary>
        /// 获取龙焰灼烧Buff描述
        /// </summary>
        public static string GetDragonBurnDescription()
        {
            return L10n.T(DragonBurnDescCN, DragonBurnDescEN);
        }
        
        #endregion

        #region 冰霜套装本地化数据

        // 霜冠（冰霜头盔）
        private static readonly string FrostHelmNameCN = "霜冠";
        private static readonly string FrostHelmNameEN = "Frost Crown";
        private static readonly string FrostHelmDescCN = "冠沿结着白霜，隔着手套也冷。\n<color=#87CEEB>【寒冰之护】穿齐2件生效</color>\n<color=#AADDFF>冰抗+50%，所受冰伤的50%转为治疗。\n击杀触发冰葬：尸体处4.5米霜爆，20冰伤并冻结。冷却1.5秒。\n受击：30%概率冻结5米内的攻击者。冷却5秒。</color>\n<color=#BBBBBB>来源：「???」Boss / 叮当的小店（好感6级）</color>";
        private static readonly string FrostHelmDescEN = "Frost rims the crown. Cold even through gloves.\n<color=#87CEEB>[Frost Ward] Requires 2 pieces</color>\n<color=#AADDFF>Ice Resist +50%. Half of ice damage taken heals you.\nOn kill: Frost Nova bursts 4.5m from the corpse, 20 ice damage and freeze. Cooldown: 1.5s.\nOn hit: 30% chance to freeze the attacker within 5m. Cooldown: 5s.</color>\n<color=#BBBBBB>Source: \"???\" boss / Dingdang's Shop (affinity 6)</color>";

        // 寒冰铠甲
        private static readonly string FrostArmorNameCN = "寒冰铠甲";
        private static readonly string FrostArmorNameEN = "Ice Armor";
        private static readonly string FrostArmorDescCN = "铠甲内侧凝着冰，走动时簌簌作响。\n<color=#87CEEB>【寒冰之护】穿齐2件生效</color>\n<color=#AADDFF>冰抗+50%，所受冰伤的50%转为治疗。\n击杀触发冰葬：尸体处4.5米霜爆，20冰伤并冻结。冷却1.5秒。\n受击：30%概率冻结5米内的攻击者。冷却5秒。</color>\n<color=#BBBBBB>来源：「???」Boss / 叮当的小店（好感6级）</color>";
        private static readonly string FrostArmorDescEN = "Ice lines the armor, crackling with each step.\n<color=#87CEEB>[Frost Ward] Requires 2 pieces</color>\n<color=#AADDFF>Ice Resist +50%. Half of ice damage taken heals you.\nOn kill: Frost Nova bursts 4.5m from the corpse, 20 ice damage and freeze. Cooldown: 1.5s.\nOn hit: 30% chance to freeze the attacker within 5m. Cooldown: 5s.</color>\n<color=#BBBBBB>Source: \"???\" boss / Dingdang's Shop (affinity 6)</color>";

        // 冰冻 Buff
        private static readonly string FrostBuffNameCN = "冰冻";
        private static readonly string FrostBuffNameEN = "Frozen";
        private static readonly string FrostBuffDescCN = "被寒冰之力冻结，移动速度大幅降低。";
        private static readonly string FrostBuffDescEN = "Frozen by ice power, movement speed greatly reduced.";

        #endregion

        #region 雷霆套装本地化数据

        // 雷神之角（雷霆头盔）
        private static readonly string ThunderHelmNameCN = "雷神之角";
        private static readonly string ThunderHelmNameEN = "Thunder Horn";
        private static readonly string ThunderHelmDescCN = "角尖不时跳出电弧。\n<color=#FFD700>【雷霆之怒】穿齐2件生效</color>\n<color=#FFEE88>电抗+50%，所受电伤的50%转为治疗。\n击杀：引雷术攻击6米内最多3名敌人。35电伤，最多连跳3次。\n受击：6米内攻击者有25%概率触发雷霆反震。\n反震造成4米范围30电伤，不伤自己。冷却3秒。</color>\n<color=#BBBBBB>来源：风暴区Boss / 叮当的小店（好感6级）</color>";
        private static readonly string ThunderHelmDescEN = "Arcs flicker between the horns.\n<color=#FFD700>[Thunder's Wrath] Requires 2 pieces</color>\n<color=#FFEE88>Elec Resist +50%. Half of shock damage taken heals you.\nOn kill: lightning hits up to 3 foes within 6m. 35 shock damage, up to 3 jumps.\nOn hit: attackers within 6m have a 25% chance to trigger a counter-shock.\nThe shock deals 30 damage within 4m and cannot hurt you. Cooldown: 3s.</color>\n<color=#BBBBBB>Source: Storm Zone boss / Dingdang's Shop (affinity 6)</color>";

        // 雷霆战甲
        private static readonly string ThunderArmorNameCN = "雷霆战甲";
        private static readonly string ThunderArmorNameEN = "Thunder Armor";
        private static readonly string ThunderArmorDescCN = "甲片间藏着电光，碰一下就麻手。\n<color=#FFD700>【雷霆之怒】穿齐2件生效</color>\n<color=#FFEE88>电抗+50%，所受电伤的50%转为治疗。\n击杀：引雷术攻击6米内最多3名敌人。35电伤，最多连跳3次。\n受击：6米内攻击者有25%概率触发雷霆反震。\n反震造成4米范围30电伤，不伤自己。冷却3秒。</color>\n<color=#BBBBBB>来源：风暴区Boss / 叮当的小店（好感6级）</color>";
        private static readonly string ThunderArmorDescEN = "Sparks hide between the plates. A touch numbs your fingers.\n<color=#FFD700>[Thunder's Wrath] Requires 2 pieces</color>\n<color=#FFEE88>Elec Resist +50%. Half of shock damage taken heals you.\nOn kill: lightning hits up to 3 foes within 6m. 35 shock damage, up to 3 jumps.\nOn hit: attackers within 6m have a 25% chance to trigger a counter-shock.\nThe shock deals 30 damage within 4m and cannot hurt you. Cooldown: 3s.</color>\n<color=#BBBBBB>Source: Storm Zone boss / Dingdang's Shop (affinity 6)</color>";

        #endregion

        #region 冰霜套装本地化注入

        /// <summary>
        /// 注入冰霜套装本地化
        /// </summary>
        public static void InjectFrostSetLocalization()
        {
            try
            {
                // 霜冠
                string frostHelmName = L10n.T(FrostHelmNameCN, FrostHelmNameEN);
                string frostHelmDesc = L10n.T(FrostHelmDescCN, FrostHelmDescEN);
                LocalizationHelper.InjectLocalization("BossRush_FrostCrown", frostHelmName);
                LocalizationHelper.InjectLocalization("BossRush_FrostCrown_Desc", frostHelmDesc);
                LocalizationHelper.InjectLocalization("霜冠", frostHelmName);
                LocalizationHelper.InjectLocalization("霜冠_Desc", frostHelmDesc);
                LocalizationHelper.InjectLocalization("Item_500053", frostHelmName);
                LocalizationHelper.InjectLocalization("Item_500053_Desc", frostHelmDesc);

                // 寒冰铠甲
                string frostArmorName = L10n.T(FrostArmorNameCN, FrostArmorNameEN);
                string frostArmorDesc = L10n.T(FrostArmorDescCN, FrostArmorDescEN);
                LocalizationHelper.InjectLocalization("BossRush_IceArmor", frostArmorName);
                LocalizationHelper.InjectLocalization("BossRush_IceArmor_Desc", frostArmorDesc);
                LocalizationHelper.InjectLocalization("寒冰铠甲", frostArmorName);
                LocalizationHelper.InjectLocalization("寒冰铠甲_Desc", frostArmorDesc);
                LocalizationHelper.InjectLocalization("Item_500054", frostArmorName);
                LocalizationHelper.InjectLocalization("Item_500054_Desc", frostArmorDesc);

                // 冰冻 Buff
                string frostBuffName = L10n.T(FrostBuffNameCN, FrostBuffNameEN);
                string frostBuffDesc = L10n.T(FrostBuffDescCN, FrostBuffDescEN);
                LocalizationHelper.InjectLocalization("FrostSet_Freeze", frostBuffName);
                LocalizationHelper.InjectLocalization("FrostSet_Freeze_Desc", frostBuffDesc);

                ModBehaviour.DevLog("[EquipmentLocalization] 冰霜套装本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入冰霜套装本地化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取霜冠显示名称
        /// </summary>
        public static string GetFrostHelmName()
        {
            return L10n.T(FrostHelmNameCN, FrostHelmNameEN);
        }

        /// <summary>
        /// 获取寒冰铠甲显示名称
        /// </summary>
        public static string GetFrostArmorName()
        {
            return L10n.T(FrostArmorNameCN, FrostArmorNameEN);
        }

        #endregion

        #region 雷霆套装本地化注入

        /// <summary>
        /// 注入雷霆套装本地化
        /// </summary>
        public static void InjectThunderSetLocalization()
        {
            try
            {
                // 雷神之角
                string thunderHelmName = L10n.T(ThunderHelmNameCN, ThunderHelmNameEN);
                string thunderHelmDesc = L10n.T(ThunderHelmDescCN, ThunderHelmDescEN);
                LocalizationHelper.InjectLocalization("BossRush_ThunderHorn", thunderHelmName);
                LocalizationHelper.InjectLocalization("BossRush_ThunderHorn_Desc", thunderHelmDesc);
                LocalizationHelper.InjectLocalization("雷神之角", thunderHelmName);
                LocalizationHelper.InjectLocalization("雷神之角_Desc", thunderHelmDesc);
                LocalizationHelper.InjectLocalization("Item_500055", thunderHelmName);
                LocalizationHelper.InjectLocalization("Item_500055_Desc", thunderHelmDesc);

                // 雷霆战甲
                string thunderArmorName = L10n.T(ThunderArmorNameCN, ThunderArmorNameEN);
                string thunderArmorDesc = L10n.T(ThunderArmorDescCN, ThunderArmorDescEN);
                LocalizationHelper.InjectLocalization("BossRush_ThunderArmor", thunderArmorName);
                LocalizationHelper.InjectLocalization("BossRush_ThunderArmor_Desc", thunderArmorDesc);
                LocalizationHelper.InjectLocalization("雷霆战甲", thunderArmorName);
                LocalizationHelper.InjectLocalization("雷霆战甲_Desc", thunderArmorDesc);
                LocalizationHelper.InjectLocalization("Item_500056", thunderArmorName);
                LocalizationHelper.InjectLocalization("Item_500056_Desc", thunderArmorDesc);

                ModBehaviour.DevLog("[EquipmentLocalization] 雷霆套装本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入雷霆套装本地化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取雷神之角显示名称
        /// </summary>
        public static string GetThunderHelmName()
        {
            return L10n.T(ThunderHelmNameCN, ThunderHelmNameEN);
        }

        /// <summary>
        /// 获取雷霆战甲显示名称
        /// </summary>
        public static string GetThunderArmorName()
        {
            return L10n.T(ThunderArmorNameCN, ThunderArmorNameEN);
        }

        #endregion
    }
}
