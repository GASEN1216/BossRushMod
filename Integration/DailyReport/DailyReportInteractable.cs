// ============================================================================
// DailyReportInteractable.cs - 报箱交互组件
// ============================================================================
// 骨架（交互名注入、碰撞体启用、交互组初始化、base.* 隔离、完成后回调）
// 自 2026-09-06 起只有一份：Interactables/BossRushBuildingInteractableBase.cs。
// 本文件只声明报箱自己的几项：交互名 key、交互组标签、可交互条件、打开日报面板。
// ============================================================================

namespace BossRush
{
    /// <summary>基地报箱的交互组件。</summary>
    public class DailyReportInteractable : BossRushBuildingInteractableBase
    {
        /// <summary>交互名的本地化 key（由 DailyReportLocalization 注入）。</summary>
        protected override string InteractNameKey { get { return DailyReportTuning.LocalizationPrefix + "Interact"; } }

        protected override string LogPrefix { get { return DailyReportTuning.LogPrefix; } }

        protected override string InteractionGroupLabel { get { return "[DailyReport]"; } }

        protected override float InteractMarkerHeight { get { return 1.2f; } }

        protected override bool IsBuildingInteractable()
        {
            ModBehaviour owner = ModBehaviour.Instance;
            return owner != null && owner.IsDailyReportConfiguredEnabled();
        }

        protected override void OnInteractCompleted()
        {
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.OpenDailyReportUI();
            }
        }
    }
}
