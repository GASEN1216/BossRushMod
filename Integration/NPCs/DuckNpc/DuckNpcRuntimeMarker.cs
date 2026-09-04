// ============================================================================
// DuckNpcRuntimeMarker.cs - 捏脸 NPC 的运行时身份标记
// ============================================================================
// 模块说明：
//   捏脸 NPC 的本体是官方 CharacterMainControl，和原图敌人长得一模一样。
//   本组件是它唯一的身份凭证，承担三件事：
//
//   1. **清场豁免**。Mode H 竞技场隔离（ModeHArenaIsolationLease）在
//      ShouldPreserveNativeCharacter 里检查 GetComponentInChildren<INPCController>()，
//      挂了本组件就自动被保留，不需要每个模式再各加一处白名单。
//      丧尸模式隔离走的是「非敌对阵营即保留」，由 Team 覆盖。
//
//   2. **反查入口**。给交互组件、好感度、对话系统一个从 GameObject 找回 NPC 身份的口子，
//      不需要靠 gameObject.name 猜。
//
//   3. **待机/对话状态**。实现 INPCController 后，现有 NPCInteractableBase 及其
//      子类可以直接挂到捏脸 NPC 上，不用改一行现有代码。
//
//   本组件**不实现移动**，但负责把对话状态转达给移动组件：
//   StartDialogue → DuckNpcMovement.Hold()，EndDialogueWithStay → Release(stay)。
//   站桩 NPC 身上没有 DuckNpcMovement，那两处是 no-op。
//   真正的移动在 DuckNpcMovement（走官方 AI_PathControl，不引入战斗 AI）。
// ============================================================================

using System;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 捏脸 NPC 运行时标记。挂在 CharacterMainControl 的 GameObject 上。
    /// </summary>
    internal sealed class DuckNpcRuntimeMarker : MonoBehaviour, INPCController
    {
        /// <summary>爱心/碎心气泡的默认高度，与现有 NPC 视觉基本一致。</summary>
        private const float BubbleHeight = 1.6f;
        private const float BubbleAnimationHeight = 0.5f;
        private const float BubbleDuration = 2f;

        private const string LogPrefix = "[DuckNpc]";

        private string npcId;
        private CharacterMainControl character;
        private bool dialogueLatched;
        private float dialogueReleaseTime = -1f;
        private DuckNpcMovement movement;
        private bool movementResolved;

        // ====================================================================
        // 身份
        // ====================================================================

        /// <summary>蓝图 ID（与 NPC ID 同义）。</summary>
        public string NpcId
        {
            get { return npcId; }
        }

        public Transform NpcTransform
        {
            get { return transform; }
        }

        /// <summary>本体角色引用。可能为 null（角色已销毁）。</summary>
        internal CharacterMainControl Character
        {
            get { return character; }
        }

        /// <summary>
        /// 当前是否处于对话/站桩锁定。
        /// </summary>
        /// <remarks>
        /// 惰性求值而不是每帧 Update 推进：捏脸 NPC 是常驻对象，
        /// 为了一个布尔计时器给每只 NPC 挂一个每帧回调不值当（AGENTS.md 4.12）。
        /// </remarks>
        internal bool IsInDialogue
        {
            get
            {
                if (!dialogueLatched)
                {
                    return false;
                }

                if (dialogueReleaseTime >= 0f && Time.time >= dialogueReleaseTime)
                {
                    dialogueLatched = false;
                    dialogueReleaseTime = -1f;
                }

                return dialogueLatched;
            }
        }

        /// <summary>
        /// 由 DuckNpcFactory 在生成后立即调用。幂等。
        /// </summary>
        internal void Bind(string id, CharacterMainControl owner)
        {
            npcId = id;
            character = owner;
        }

        // ====================================================================
        // INPCController
        // ====================================================================

        public void StartDialogue()
        {
            dialogueLatched = true;
            dialogueReleaseTime = -1f;
            FacePlayer();

            DuckNpcMovement move = ResolveMovement();
            if (move != null)
            {
                move.Hold();
            }
        }

        public void EndDialogueWithStay(float stayDuration, bool showFarewell = false)
        {
            if (stayDuration > 0f)
            {
                dialogueReleaseTime = Time.time + stayDuration;
            }
            else
            {
                dialogueLatched = false;
                dialogueReleaseTime = -1f;
            }

            // 会走动的 NPC（永久 NPC）在这里解除挂起，并按 stayDuration 站一会儿再走。
            // 站桩 NPC 没有 movement，ResolveMovement 返回 null，这里是 no-op。
            DuckNpcMovement move = ResolveMovement();
            if (move != null)
            {
                move.Release(stayDuration);
            }
        }

        /// <summary>
        /// 惰性解析移动组件。永久 NPC 的 DuckNpcMovement 是生成后才 AddComponent 的，
        /// 所以不能在 Bind 时一次性取；解析一次后缓存（含"确实没有"这个结论）。
        /// </summary>
        private DuckNpcMovement ResolveMovement()
        {
            if (movementResolved)
            {
                return movement;
            }

            try
            {
                movement = GetComponent<DuckNpcMovement>();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 解析移动组件失败: " + e.Message);
                movement = null;
            }

            movementResolved = true;
            return movement;
        }

        public void ShowLoveHeartBubble()
        {
            NPCHeartBubbleHelper.ShowLoveHeart(
                transform, BubbleHeight, BubbleAnimationHeight, BubbleDuration, LogPrefix);
        }

        public void ShowBrokenHeartBubble()
        {
            NPCHeartBubbleHelper.ShowBrokenHeart(
                transform, BubbleHeight, BubbleAnimationHeight, BubbleDuration, LogPrefix);
        }

        // ====================================================================
        // 朝向
        // ====================================================================

        /// <summary>
        /// 让 NPC 面向玩家。走官方 SetAimPoint，而不是直接改 transform.rotation ——
        /// 官方 CharacterMainControl 每帧会按 aimPoint 推朝向，手改 rotation 会被推平。
        /// </summary>
        internal void FacePlayer()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || character == null)
                {
                    return;
                }
                character.SetAimPoint(main.transform.position);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 面向玩家失败: " + e.Message);
            }
        }

        // ====================================================================
        // 反查
        // ====================================================================

        /// <summary>
        /// 判断一个角色是否是捏脸 NPC。供清场、统计、掉落等路径做豁免判断。
        /// </summary>
        internal static bool IsDuckNpc(CharacterMainControl candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            try
            {
                return candidate.GetComponent<DuckNpcRuntimeMarker>() != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
