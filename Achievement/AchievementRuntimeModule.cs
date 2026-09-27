using System;
using Duckov.Economy;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class AchievementRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private Func<bool> isLegacyActive;
        private Func<bool> isModeDActive;
        private Func<bool> isInfiniteHellMode;
        private Func<int> getBossesPerWave;
        private Func<bool> hasAchievementConfiguration;
        private Func<int> getAchievementHotkey;
        private Func<StockShop, bool> isBaseHubNormalMerchantShop;
        private string achievementBaseSceneName;
        private bool achievementRuntimeCleaned;
        private bool medalStockEventsSubscribed;

        public override string ModuleName { get { return "Achievement"; } }

        internal void BindRuntimeQueries(Func<bool> legacyActive, Func<bool> modeDActive,
            Func<bool> infiniteHellMode, Func<int> bossesPerWave,
            Func<bool> hasConfiguration, Func<int> achievementHotkey)
        {
            isLegacyActive = legacyActive;
            isModeDActive = modeDActive;
            isInfiniteHellMode = infiniteHellMode;
            getBossesPerWave = bossesPerWave;
            hasAchievementConfiguration = hasConfiguration;
            getAchievementHotkey = achievementHotkey;
        }

        internal void BindMedalShopQueries(Func<StockShop, bool> baseHubNormalMerchantShop, string baseSceneName)
        {
            isBaseHubNormalMerchantShop = baseHubNormalMerchantShop;
            achievementBaseSceneName = baseSceneName;
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (owner == null) return;
            TickAchievementRuntime(deltaTime, unscaledDeltaTime);
        }

        // 宿主在原 Awake / 销毁槽位调用；模块注册回调不提前初始化或延后实际退订。
        internal void InitializeAchievementRuntime()
        {
            achievementRuntimeCleaned = false;
            InitializeAchievementRuntimeCore();
        }

        internal void CleanupAchievementRuntime()
        {
            if (achievementRuntimeCleaned) return;
            achievementRuntimeCleaned = true;
            CleanupAchievementRuntimeCore();
        }

        internal void SubscribeMedalStockEvents()
        {
            if (medalStockEventsSubscribed) return;
            medalStockEventsSubscribed = true;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_MedalStock;
            SavesSystem.OnSetFile += OnSetFile_MedalStock;
        }

        internal void UnsubscribeMedalStockEvents()
        {
            if (!medalStockEventsSubscribed) return;
            medalStockEventsSubscribed = false;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_MedalStock;
            SavesSystem.OnSetFile -= OnSetFile_MedalStock;
        }

        public override void OnDestroy()
        {
            CleanupAchievementRuntime();
            UnsubscribeMedalStockEvents();
            owner = null;
        }

        private void InitializeAchievementRuntimeCore()
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
                if (hasAchievementConfiguration() && getAchievementHotkey() > 0)
                {
                    achievementKey = (UnityEngine.KeyCode)getAchievementHotkey();
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
                ModBehaviour.DevLog("[BossRush] 成就界面快捷键处理失败: " + e.Message);
            }
        }

        private void CleanupAchievementRuntimeCore()
        {
            Health.OnHurt -= OnPlayerHurtForAchievement;
            BossRushEventBus.Unsubscribe<BossRushAchievementUnlockedEvent>(OnBossRushAchievementUnlockedEvent);
            ResetAchievementBossKillTracking();
            UnsubscribeAchievementEvents();
            SafeRuntime.Run("AchievementView.Shutdown", AchievementView.Shutdown);
            SafeRuntime.Run("SteamAchievementPopup.Shutdown", SteamAchievementPopup.Shutdown);
        }

        private void OnBossRushAchievementUnlockedEvent(BossRushAchievementUnlockedEvent eventData)
        {
            SteamAchievementPopup.Show(eventData.Achievement);
        }
    }
}
