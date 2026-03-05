// ============================================================================
// INPCController.cs - NPC控制器通用接口
// ============================================================================
// 模块说明：
//   所有NPC控制器（GoblinNPCController、NurseNPCController等）的通用接口。
//   NPCInteractableBase 及服务类通过此接口与控制器交互，
//   避免硬编码具体NPC控制器类型。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// NPC控制器通用接口
    /// </summary>
    public interface INPCController
    {
        /// <summary>NPC标识符</summary>
        string NpcId { get; }

        /// <summary>NPC的Transform</summary>
        Transform NpcTransform { get; }

        /// <summary>开始对话（NPC进入待机状态）</summary>
        void StartDialogue();

        /// <summary>结束对话并在原地停留指定时间</summary>
        void EndDialogueWithStay(float stayDuration, bool showFarewell = false);

        /// <summary>显示冒爱心气泡特效</summary>
        void ShowLoveHeartBubble();

        /// <summary>显示心碎气泡特效</summary>
        void ShowBrokenHeartBubble();
    }
}
