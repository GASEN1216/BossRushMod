// 鸭王征程旧入口与宿主依赖；决战和模式观察状态归同一 CampaignRuntimeModule。
namespace BossRush
{
    public partial class ModBehaviour
    {
        private bool campaignFinalBossActive { get { return campaignRuntime.IsCampaignFinalBossActive; } }
        internal bool IsCampaignFinalBossActive { get { return campaignRuntime.IsCampaignFinalBossActive; } }
        internal int CampaignFinalBossDeathPresentationCount { get { return campaignRuntime.CampaignFinalBossDeathPresentationCount; } }
        internal CharacterMainControl CampaignFinalBossInstanceForValidation { get { return campaignRuntime.CampaignFinalBossInstanceForValidation; } }
        internal bool CanStartCampaignFinalBoss() { return campaignRuntime.CanStartCampaignFinalBoss(); }
        internal void TickCampaignFinalBossAltar() { campaignRuntime.TickCampaignFinalBossAltar(); }
        internal void StartCampaignFinalBoss() { campaignRuntime.StartCampaignFinalBoss(); }
        internal bool DebugStartCampaignFinalBossForValidation() { return campaignRuntime.DebugStartCampaignFinalBossForValidation(); }
        internal void TickCampaignFinalBossYield() { campaignRuntime.TickCampaignFinalBossYield(); }
        internal void CleanupCampaignFinalBoss(bool destroyBoss) { campaignRuntime.CleanupCampaignFinalBoss(destroyBoss); }
        internal void TickCampaignModeBridge(float deltaTime) { campaignRuntime.TickCampaignModeBridge(deltaTime); }
        internal string ResolveCampaignCurrentMode() { return campaignRuntime.ResolveCampaignCurrentMode(); }
        internal int GetCampaignCurrentWave() { return campaignRuntime.GetCampaignCurrentWave(); }
        internal bool HasCampaignBountyMark(CharacterMainControl boss) { return campaignRuntime.HasCampaignBountyMark(boss); }
        internal bool ConsumeCampaignBountyMark(CharacterMainControl boss) { return campaignRuntime.ConsumeCampaignBountyMark(boss); }
        internal void NotifyCampaignStandardCleared() { campaignRuntime.NotifyCampaignStandardCleared(); }
        internal void NotifyCampaignModeDWaveComplete(int waveIndex) { campaignRuntime.NotifyCampaignModeDWaveComplete(waveIndex); }
        internal void NotifyCampaignModeFExtracted() { campaignRuntime.NotifyCampaignModeFExtracted(); }
        internal void NotifyCampaignZombieExtracted() { campaignRuntime.NotifyCampaignZombieExtracted(); }

        internal bool CampaignArenaActiveForRuntime { get { return bossRushArenaActive; } }
        internal bool CampaignModeDActiveForRuntime { get { return modeDActive; } }
        internal bool CampaignModeEActiveForRuntime { get { return modeEActive; } }
        internal bool CampaignModeFActiveForRuntime { get { return modeFActive; } }
        internal bool CampaignModeGActiveForRuntime { get { return modeGActive; } }
        internal bool CampaignInfiniteHellForRuntime { get { return infiniteHellMode; } }
        internal int CampaignInfiniteHellWaveForRuntime { get { return infiniteHellWaveIndex; } }
        internal int CampaignEnemyIndexForRuntime { get { return currentEnemyIndex; } }
        internal ZombieModeRunState CampaignZombieRunForRuntime { get { return zombieModeRunState; } }
        internal ModeFState CampaignModeFStateForRuntime { get { return modeFState; } }
        internal bool HasCampaignBountyKillLatchForRuntime(int victimId) { return HasModeFPlayerBountyKillLatch(victimId); }
        internal bool ConsumeCampaignBountyKillLatchForRuntime(int victimId) { return ConsumeModeFPlayerBountyKillLatch(victimId); }
        internal void ClearCampaignBossRandomLootTrackingForRuntime(CharacterMainControl boss) { ClearBossRandomLootTracking(boss); }
        internal void ApplyCampaignBossStatMultiplierForRuntime(CharacterMainControl boss, float multiplier) { ApplyBossStatMultiplier(boss, multiplier); }
    }
}
