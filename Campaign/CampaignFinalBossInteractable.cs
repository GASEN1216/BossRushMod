// ============================================================================
// CampaignFinalBossInteractable.cs - 竞技场内的「决战」召唤石
// ============================================================================
// 为什么要一个显式交互物，而不是进竞技场就自动开战：
//   玩家接了终章契约之后，仍然可能只是想跑一局普通竞技场。进场即刷 Boss 会直接
//   抢掉那一局——而且标准模式要等玩家点路牌才置 bossRushArenaActive，
//   自动开战恰好会卡在「还没开始正常流程」的窗口里，把人堵死。
//   给一块石头让玩家自己决定什么时候开打，是唯一不干扰既有玩法的做法。
//
// 交互体本身随场景销毁；召唤石由 CampaignFinalBoss 的维护逻辑按需重建，
// 因此这里不做任何持久化。
//
// 骨架（交互名注入、碰撞体启用、交互组初始化、base.* 隔离、完成后回调）
// 自 2026-09-06 起只有一份：Interactables/BossRushBuildingInteractableBase.cs。
// ============================================================================

namespace BossRush
{
    /// <summary>终章决战召唤石的交互组件。</summary>
    public class CampaignFinalBossInteractable : BossRushBuildingInteractableBase
    {
        /// <summary>交互名的本地化键（由 CampaignLocalization 注入）。</summary>
        protected override string InteractNameKey { get { return "BossRush_Campaign_FinalBoss_Interact"; } }

        protected override string LogPrefix { get { return CampaignTuning.LogPrefix; } }

        protected override string InteractionGroupLabel { get { return "[CampaignFinalBoss]"; } }

        protected override float InteractMarkerHeight { get { return 1.2f; } }

        protected override bool IsBuildingInteractable()
        {
            ModBehaviour owner = ModBehaviour.Instance;
            return owner != null && owner.CanStartCampaignFinalBoss();
        }

        protected override void OnInteractCompleted()
        {
            ModBehaviour.Instance?.StartCampaignFinalBoss();
        }
    }
}
