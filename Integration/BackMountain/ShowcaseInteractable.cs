// ============================================================================
// ShowcaseInteractable.cs - 战利品展示柜交互组件
// ============================================================================
// 骨架（交互名注入、碰撞体启用、交互组初始化、base.* 隔离、完成后回调）
// 自 2026-09-06 起只有一份：Interactables/BossRushBuildingInteractableBase.cs。
// 本文件只声明展示柜自己的几项：交互名 key、交互组标签、可交互条件（含设施解锁）、
// 打开展示柜面板。
// ============================================================================

namespace BossRush
{
    /// <summary>基地战利品展示柜的交互组件。</summary>
    public class ShowcaseInteractable : BossRushBuildingInteractableBase
    {
        /// <summary>交互名的本地化键（由 BackMountainLocalization 注入）。</summary>
        protected override string InteractNameKey { get { return "BossRush_BackMountain_Showcase_Interact"; } }

        protected override string LogPrefix { get { return BackMountainConfig.LogPrefix; } }

        protected override string InteractionGroupLabel { get { return "[BackMountainShowcase]"; } }

        protected override float InteractMarkerHeight { get { return 1.3f; } }

        protected override bool IsBuildingInteractable()
        {
            ModBehaviour owner = ModBehaviour.Instance;
            if (owner == null || !owner.IsBackMountainConfiguredEnabled()) return false;
            return BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Showcase);
        }

        protected override void OnInteractCompleted()
        {
            ModBehaviour.Instance?.OpenShowcaseUI();
        }
    }
}
