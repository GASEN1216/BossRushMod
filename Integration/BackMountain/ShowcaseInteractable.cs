// ============================================================================
// ShowcaseInteractable.cs - 战利品展示柜交互组件
// ============================================================================
// 骨架（交互名注入、碰撞体启用、交互组初始化、base.* 隔离、完成后回调）
// 自 2026-09-06 起只有一份：Interactables/BossRushBuildingInteractableBase.cs。
// 本文件只声明展示柜自己的几项：交互名 key、交互组标签、可交互条件、互动后的提示。
// 自建柜已退役（2026-09-22，陈列改接官方陈列柜 / 枪械展示架 / 假人）：老档已建的保留，互动只提示去官方柜摆放。
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
            return owner != null && owner.IsBackMountainConfiguredEnabled();
        }

        protected override void OnInteractCompleted()
        {
            try
            {
                Duckov.UI.NotificationText.Push(L10n.T(
                    "登记这一套不用了。战利品现在直接摆进陈列柜、枪械展示架或假人就算数，加成照给。",
                    "No more registering. Trophies count as soon as you put them in a display cabinet, on a weapon display rack or on a dummy. The bonus still applies."));
            }
            catch (System.Exception) { }
        }
    }
}
