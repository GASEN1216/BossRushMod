// ============================================================================
// DiamondRingConfig.cs - 钻石戒指物品配置
// ============================================================================
// 模块说明：
//   定义钻石戒指物品的配置常量和初始化逻辑
//   钻石戒指是一种特殊礼物，只有当NPC好感度达到10级时才能赠送
//   参考星露谷物语的结婚戒指设计
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 钻石戒指物品配置
    /// </summary>
    public static class DiamondRingConfig
    {
        // ============================================================================
        // 物品基础配置
        // ============================================================================
        
        /// <summary>
        /// 物品 TypeID（需要与 Unity 预制体中的 typeID 一致）
        /// </summary>
        public const int TYPE_ID = 500029;
        
        /// <summary>
        /// AssetBundle 名称
        /// </summary>
        public const string BUNDLE_NAME = "diamond_ring";
        
        /// <summary>
        /// 图标资源名称
        /// </summary>
        public const string ICON_NAME = "DiamondRing";
        
        /// <summary>
        /// 解锁所需好感度等级
        /// </summary>
        public const int UNLOCK_LEVEL = 7;
        
        /// <summary>
        /// 赠送所需好感度等级（必须达到10级才能赠送）
        /// </summary>
        public const int GIFT_REQUIRED_LEVEL = 10;
        
        // ============================================================================
        // 本地化配置
        // ============================================================================
        
        /// <summary>
        /// 物品本地化键
        /// </summary>
        public const string LOC_KEY_DISPLAY = "BossRush_DiamondRing";
        
        /// <summary>
        /// 物品显示名称（中文）
        /// </summary>
        public const string DISPLAY_NAME_CN = "钻石戒指";
        
        /// <summary>
        /// 物品显示名称（英文）
        /// </summary>
        public const string DISPLAY_NAME_EN = "Diamond Ring";
        
        /// <summary>
        /// 物品描述（中文）
        /// </summary>
        public const string DESCRIPTION_CN = "一枚镶嵌着璀璨钻石的精致戒指。据说将它送给心仪的人，就能表达最真挚的心意。";
        
        /// <summary>
        /// 物品描述（英文）
        /// </summary>
        public const string DESCRIPTION_EN = "An exquisite ring set with a brilliant diamond. It's said that giving this to someone special expresses your deepest feelings.";
        
        // ============================================================================
        // 拒绝礼物对话配置
        // ============================================================================
        
        /// <summary>
        /// NPC拒绝接受钻石戒指的对话（中文）
        /// </summary>
        public static readonly string[] REJECT_DIALOGUES_CN = new string[]
        {
            "这...不太好吧",
            "是不是有点太快了...",
            "太突然了，我...还没准备好"
        };
        
        /// <summary>
        /// NPC拒绝接受钻石戒指的对话（英文）
        /// </summary>
        public static readonly string[] REJECT_DIALOGUES_EN = new string[]
        {
            "This... isn't quite right",
            "Isn't this a bit too fast...",
            "This is too sudden, I... I'm not ready yet"
        };
        
        // ============================================================================
        // 好感度配置
        // ============================================================================
        
        /// <summary>
        /// 成功赠送钻石戒指增加的好感度
        /// </summary>
        public const int AFFINITY_BONUS = 500;
        
        // ============================================================================
        // 辅助方法
        // ============================================================================
        
        /// <summary>
        /// 获取本地化的物品名称
        /// </summary>
        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }
        
        /// <summary>
        /// 获取本地化的物品描述
        /// </summary>
        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }
        
        /// <summary>
        /// 获取随机的拒绝对话
        /// </summary>
        public static string GetRandomRejectDialogue()
        {
            int index = UnityEngine.Random.Range(0, REJECT_DIALOGUES_CN.Length);
            return L10n.T(REJECT_DIALOGUES_CN[index], REJECT_DIALOGUES_EN[index]);
        }
        
        /// <summary>
        /// 检查是否可以将钻石戒指赠送给指定NPC
        /// </summary>
        /// <param name="npcId">NPC标识符</param>
        /// <returns>是否可以赠送</returns>
        public static bool CanGiftToNPC(string npcId)
        {
            int level = AffinityManager.GetLevel(npcId);
            return level >= GIFT_REQUIRED_LEVEL;
        }
        
        // ============================================================================
        // 物品配置
        // ============================================================================
        
        /// <summary>
        /// 配置钻石戒指物品（由 ItemFactory 调用）
        /// </summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            
            try
            {
                // 设置本地化键（最重要，游戏通过这个查找物品名称）
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                
                // 设置物品为可堆叠（礼物通常可堆叠）
                // 注意：这个属性由 Unity 预制体控制，这里只是确保
                
                ModBehaviour.DevLog("[DiamondRingConfig] 钻石戒指物品配置完成: TypeID=" + TYPE_ID + ", DisplayNameRaw=" + LOC_KEY_DISPLAY);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DiamondRingConfig] 配置物品失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注册配置器到 ItemFactory
        /// </summary>
        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[DiamondRingConfig] 已注册物品配置器");
        }
    }
}
