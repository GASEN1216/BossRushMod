using System;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void InitializeAchievementRuntime()
        {
            InitializeAchievementSystem();
            AchievementView.EnsureInstance();
            SteamAchievementPopup.EnsureInstance();
            BossRushEventBus.Subscribe<BossRushAchievementUnlockedEvent>(OnBossRushAchievementUnlockedEvent);
            Health.OnHurt += OnPlayerHurtForAchievement;
        }

        internal void TickAchievementRuntime(float deltaTime, float unscaledDeltaTime)
        {
            try
            {
                UnityEngine.KeyCode achievementKey = UnityEngine.KeyCode.L;
                if (config != null && config.achievementHotkey > 0)
                {
                    achievementKey = (UnityEngine.KeyCode)config.achievementHotkey;
                }

                if (UnityEngine.Input.GetKeyDown(achievementKey))
                {
                    if (Duckov.UI.View.ActiveView == null)
                    {
                        AchievementView.Instance.Toggle();
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 成就界面快捷键处理失败: " + e.Message);
            }
        }

        internal void CleanupAchievementRuntime()
        {
            Health.OnHurt -= OnPlayerHurtForAchievement;
            BossRushEventBus.Unsubscribe<BossRushAchievementUnlockedEvent>(OnBossRushAchievementUnlockedEvent);
            UnsubscribeAchievementEvents();
        }

        private void OnBossRushAchievementUnlockedEvent(BossRushAchievementUnlockedEvent eventData)
        {
            SteamAchievementPopup.Show(eventData.Achievement);
        }
    }
}
