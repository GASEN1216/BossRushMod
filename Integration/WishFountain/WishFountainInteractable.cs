// ============================================================================
// WishFountainInteractable.cs - 布满了灰尘的星愿许愿台交互组件
// ============================================================================
// 模块说明：
//   为布满了灰尘的星愿许愿台建筑提供"许愿"交互选项。
//   玩家靠近并触发交互后，打开运行时创建的许愿 View。
//   当心愿正在发送时，交互会被暂时禁用，避免重复打开和重复提交。
//
// 骨架（交互名注入、碰撞体启用、base.* 隔离、完成后回调）自 2026-09-06 起只有一份：
// Interactables/BossRushBuildingInteractableBase.cs（它就是照本文件的防御式写法抽出来的）。
// 许愿台上线以来不初始化交互组，这里沿用（InteractionGroupLabel 保持默认 null）。
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 布满了灰尘的星愿许愿台交互组件
    /// </summary>
    public class WishFountainInteractable : BossRushBuildingInteractableBase
    {
        protected override string InteractNameKey { get { return "BossRush_StarWish_Interact"; } }

        protected override string LogPrefix { get { return "[WishFountain] "; } }

        protected override float InteractMarkerHeight { get { return 1.5f; } }

        protected override bool IsBuildingInteractable()
        {
            // 发送中时不允许再次交互
            return !WishFountainService.IsSending;
        }

        protected override void OnInteractCompleted()
        {
            // 打开许愿 UI
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.OpenWishFountainUI();
            }
        }
    }
}
