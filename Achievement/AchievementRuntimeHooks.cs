using Duckov.Economy;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void InitializeAchievementRuntime() { achievementRuntime.InitializeAchievementRuntime(); }
        internal void TickAchievementRuntime(float deltaTime, float unscaledDeltaTime) { achievementRuntime.TickAchievementRuntime(deltaTime, unscaledDeltaTime); }
        internal void CleanupAchievementRuntime() { achievementRuntime.CleanupAchievementRuntime(); }
        private void BeginAchievementSession(string sessionName) { achievementRuntime.BeginAchievementSession(sessionName); }
        private void CheckModeDFlawlessAchievement() { achievementRuntime.CheckModeDFlawlessAchievement(); }
        private void CheckClearAchievements() { achievementRuntime.CheckClearAchievements(); }
        private void CheckInfiniteHellAchievements(int waveNumber) { achievementRuntime.CheckInfiniteHellAchievements(waveNumber); }
        private void CheckModeDClearAchievements() { achievementRuntime.CheckModeDClearAchievements(); }
        private bool CheckBossKillAchievementsOnce(CharacterMainControl bossMain, string bossTypeOverride = null) { return achievementRuntime.CheckBossKillAchievementsOnce(bossMain, bossTypeOverride); }
        internal void BeginModeGAchievementSession() { achievementRuntime.BeginModeGAchievementSession(); }
        internal void EndModeGAchievementSession() { achievementRuntime.EndModeGAchievementSession(); }
        internal void ReportModeGBossKillAchievement(int token, string bossType, bool wasFlawlessAtDeath) { achievementRuntime.ReportModeGBossKillAchievement(token, bossType, wasFlawlessAtDeath); }
        private void InjectAchievementMedalLocalization() { achievementRuntime.InjectAchievementMedalLocalization(); }
        internal bool TryInjectAchievementMedalIntoShop(StockShop shop) { return achievementRuntime.TryInjectAchievementMedalIntoShop(shop); }
        private void InjectAchievementMedalIntoShops(string targetSceneName = null) { achievementRuntime.InjectAchievementMedalIntoShops(targetSceneName); }
    }
}
