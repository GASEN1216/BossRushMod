// 新内容建筑的宿主桥。旧场景装配/早期恢复/清理入口保持不变，具体状态归各模块所有。
// 不新增生命周期或 MonoBehaviour；协程与基地重绘仍由创建该模块的宿主执行。
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private DailyReportMailboxBuilder _dailyReportMailboxBuilder;
        private CampaignBoardBuilder _campaignBoardBuilder;
        private ShowcaseBuildingBuilder _showcaseBuildingBuilder;
        private PetNestBuilder _petNestBuilder;

        private DailyReportMailboxBuilder DailyReportMailbox
        {
            get { return _dailyReportMailboxBuilder ?? (_dailyReportMailboxBuilder = new DailyReportMailboxBuilder(this)); }
        }

        private CampaignBoardBuilder CampaignBoard
        {
            get { return _campaignBoardBuilder ?? (_campaignBoardBuilder = new CampaignBoardBuilder(this)); }
        }

        private ShowcaseBuildingBuilder ShowcaseBuilding
        {
            get { return _showcaseBuildingBuilder ?? (_showcaseBuildingBuilder = new ShowcaseBuildingBuilder(this)); }
        }

        private PetNestBuilder PetNestBuilding
        {
            get { return _petNestBuilder ?? (_petNestBuilder = new PetNestBuilder(this)); }
        }

        // 报箱只借已有模型；加载、缓存和卸载继续由许愿台的原生命周期负责。
        internal GameObject StarwishBuildingModelPrefab { get { return starwishModelPrefab; } }

        public void InitDailyReportMailbox() { DailyReportMailbox.InitDailyReportMailbox(); }
        internal void TryInitializeDailyReportMailboxEarly() { DailyReportMailbox.TryInitializeDailyReportMailboxEarly(); }
        public void RestoreDailyReportMailboxes() { DailyReportMailbox.RestoreDailyReportMailboxes(); }
        public void CleanupDailyReportMailbox() { DailyReportMailbox.CleanupDailyReportMailbox(); }

        public void InitCampaignBoardBuilding() { CampaignBoard.InitCampaignBoardBuilding(); }
        internal void TryInitializeCampaignBoardEarly() { CampaignBoard.TryInitializeCampaignBoardEarly(); }
        internal void RegisterCampaignNotesForScene() { CampaignBoard.RegisterCampaignNotesForScene(); }
        public void CleanupCampaignBoardBuilding() { CampaignBoard.CleanupCampaignBoardBuilding(); }

        public void InitBackMountainShowcase() { ShowcaseBuilding.InitBackMountainShowcase(); }
        internal void TryInitializeBackMountainShowcaseEarly() { ShowcaseBuilding.TryInitializeBackMountainShowcaseEarly(); }
        internal void NotifyShowcaseSlotChanged() { ShowcaseBuilding.NotifyShowcaseSlotChanged(); }
        public void CleanupBackMountainShowcase() { ShowcaseBuilding.CleanupBackMountainShowcase(); }

        public void InitPetNestBuilding() { PetNestBuilding.InitPetNestBuilding(); }
        private void TryInitializePetNestEarly() { PetNestBuilding.TryInitializePetNestEarly(); }
        public void RestorePetNestBuildings() { PetNestBuilding.RestorePetNestBuildings(); }
        public void CleanupPetNestBuilding() { PetNestBuilding.CleanupPetNestBuilding(); }
    }
}
