// ============================================================================
// CampaignBoardInteractable.cs - 征程公告板交互组件
// ============================================================================
// 骨架（交互名注入、碰撞体启用、交互组初始化、base.* 隔离、完成后回调）
// 自 2026-09-06 起只有一份：Interactables/BossRushBuildingInteractableBase.cs。
// 本文件只声明公告板自己的几项：交互名 key、交互组标签、可交互条件、打开公告板面板。
// ============================================================================

namespace BossRush
{
    /// <summary>基地征程公告板的交互组件。</summary>
    public class CampaignBoardInteractable : BossRushBuildingInteractableBase
    {
        /// <summary>交互名的本地化 key（由 CampaignLocalization 注入）。</summary>
        protected override string InteractNameKey { get { return "BossRush_Campaign_Board_Interact"; } }

        protected override string LogPrefix { get { return CampaignTuning.LogPrefix; } }

        protected override string InteractionGroupLabel { get { return "[CampaignBoard]"; } }

        protected override float InteractMarkerHeight { get { return 1.4f; } }

        protected override bool IsBuildingInteractable()
        {
            ModBehaviour owner = ModBehaviour.Instance;
            return owner != null && owner.IsCampaignConfiguredEnabled();
        }

        protected override void OnInteractCompleted()
        {
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.OpenCampaignBoardUI();
            }
        }
    }
}
