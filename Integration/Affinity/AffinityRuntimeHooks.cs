using System;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 初始化好感度系统
        /// </summary>
        private void InitializeAffinitySystem()
        {
            try
            {
                // 初始化好感度管理器
                AffinityManager.Initialize();

                // 通过 NPC 模块注册中心统一注册可用 NPC 好感度配置
                int affinityNpcCount = NPCModuleRegistry.RegisterAffinityConfigs();
                DevLog("[BossRush] NPC 好感度配置注册完成，数量: " + affinityNpcCount);

                // 订阅好感度变化事件（显示UI动画）
                AffinityManager.OnAffinityChanged += OnAffinityChanged;

                // 订阅等级提升事件（显示通知）
                AffinityManager.OnLevelUp += OnAffinityLevelUp;

                DevLog("[BossRush] 好感度系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 好感度系统初始化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 好感度变化事件处理
        /// </summary>
        private void OnAffinityChanged(string npcId, int oldPoints, int newPoints)
        {
            int delta = newPoints - oldPoints;
            AffinityUIManager.ShowAffinityChange(npcId, delta);

            if (!string.IsNullOrEmpty(npcId))
            {
                HandleSpouseFollowAffinityLoss(npcId);
                RefreshSpouseInteractionOptionsForNpc(npcId);
            }
        }

        /// <summary>
        /// 好感度等级提升事件处理
        /// </summary>
        private void OnAffinityLevelUp(string npcId, int newLevel)
        {
            AffinityUIManager.ShowLevelUpNotification(npcId, newLevel);
        }
    }
}
