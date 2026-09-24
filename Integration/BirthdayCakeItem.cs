// ============================================================================
// BirthdayCakeItem.cs - 生日蛋糕物品
// ============================================================================
// 模块说明：
//   管理生日蛋糕物品的加载、配置和注册，包括：
//   - 从 AssetBundle 加载生日蛋糕预制体
//   - 动态添加食物功能（FoodDrink）
//   - 动态添加 Buff 效果（高兴）
//   - 动态添加 Tag（食物、收藏品）
//   - 本地化注入
//   - 商店注入
// ============================================================================


using ItemStatsSystem;

namespace BossRush
{
    /// <summary>生日蛋糕的旧宿主入口，转发给集成运行时 owner。</summary>
    public partial class ModBehaviour
    {
        private void InitializeBirthdayCakeItem()
        {
            bossRushIntegrationRuntime.InitializeBirthdayCakeItem();
        }

        private void InjectBirthdayCakeLocalization()
        {
            bossRushIntegrationRuntime.InjectBirthdayCakeLocalization();
        }

        private void AddTagsToItem(Item itemPrefab, string[] tagNames)
        {
            bossRushIntegrationRuntime.AddTagsToItem(itemPrefab, tagNames);
        }

        public void DebugGiveBirthdayCake()
        {
            bossRushIntegrationRuntime.DebugGiveBirthdayCake();
        }
    }
}
