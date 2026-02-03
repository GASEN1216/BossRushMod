// ============================================================================
// INPCShopConfig.cs - NPC商店配置接口
// ============================================================================
// 模块说明：
//   定义NPC商店系统的配置接口，支持自定义商品和解锁条件。
//   实现此接口的NPC可以使用通用商店系统 NPCShopSystem。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 商店商品条目
    /// </summary>
    public class ShopItemEntry
    {
        /// <summary>物品TypeID</summary>
        public int TypeID { get; set; }
        
        /// <summary>最大库存</summary>
        public int MaxStock { get; set; } = 5;
        
        /// <summary>解锁所需好感度等级</summary>
        public int RequiredLevel { get; set; } = 0;
        
        /// <summary>基础价格因子（1.0 = 原价）</summary>
        public float BasePriceFactor { get; set; } = 1.0f;
        
        /// <summary>出现概率（0.0 ~ 1.0）</summary>
        public float Possibility { get; set; } = 1.0f;
        
        /// <summary>
        /// 创建商品条目
        /// </summary>
        public ShopItemEntry(int typeId, int requiredLevel = 0, int maxStock = 5)
        {
            TypeID = typeId;
            RequiredLevel = requiredLevel;
            MaxStock = maxStock;
        }
    }
    
    /// <summary>
    /// NPC商店配置接口
    /// <para>实现此接口以定义NPC的商店功能</para>
    /// </summary>
    public interface INPCShopConfig
    {
        // ============================================================================
        // 商店基础配置
        // ============================================================================
        
        /// <summary>
        /// 是否启用商店功能
        /// </summary>
        bool ShopEnabled { get; }
        
        /// <summary>
        /// 商店解锁所需的最低好感度等级
        /// </summary>
        int ShopUnlockLevel { get; }
        
        /// <summary>
        /// 商店名称（显示在UI上）
        /// </summary>
        string ShopName { get; }
        
        // ============================================================================
        // 商品配置
        // ============================================================================
        
        /// <summary>
        /// 获取商店商品列表
        /// </summary>
        List<ShopItemEntry> GetShopItems();
        
        // ============================================================================
        // 折扣配置
        // ============================================================================
        
        /// <summary>
        /// 获取指定等级的折扣率
        /// </summary>
        /// <param name="level">好感度等级</param>
        /// <returns>折扣率（如0.1表示10%折扣）</returns>
        float GetDiscountForLevel(int level);
    }
}
