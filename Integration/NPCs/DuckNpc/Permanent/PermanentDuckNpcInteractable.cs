// ============================================================================
// PermanentDuckNpcInteractable.cs - 永久捏脸 NPC 的根交互
// ============================================================================
// 模块说明：
//   对标 NurseInteractable，但有一条**根本性差异**：
//
//     羽织/叮当的本体是 AssetBundle 里的裸 GameObject，交互可以直接挂根节点。
//     捏脸 NPC 的本体是官方 CharacterMainControl —— **绝不能挂根节点**。
//
//   原因在官方 InteractableBase.Awake()
//   （鸭科夫源码/TeamSoda.Duckov.Core/InteractableBase.cs:167-179）：
//     1. `GetComponent<Collider>()` —— 抓走同 GameObject 上的第一个 Collider；
//     2. `interactCollider.gameObject.layer = "Interactable"` —— 强行改层，拦不住。
//
//   角色根节点那个 Collider 是 **ECM2 的移动胶囊**，层是 `Character`(9)。
//   挂根节点会把角色的移动碰撞体征用成交互体、把层翻成 Interactable，连带打坏
//   Zone / DoorTrigger / OnTriggerEnterEvent 里的 `layer != Character` 提前 return，
//   以及 ECM2 的层碰撞矩阵。**而且全程不会有任何报错。**
//
//   所以本组件由 Attach() 挂到一个**专用子物体** `InteractRoot` 上，
//   让 Awake 去改那个子物体的层。这也正是官方自己的做法
//   （官方可交互 NPC 的交互挂在 AISpecialAttachment_Shop.shop 子物体上），
//   以及 ModeEMerchant 的做法（子选项 SetParent(mainInteract.transform)）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 永久捏脸 NPC 的根交互（"聊天"）+ 子选项组（送礼 / 婚姻三件套）。
    /// </summary>
    internal sealed class PermanentDuckNpcInteractable : InteractableBase
    {
        private const string LogPrefix = "[PermanentDuckNpc]";

        /// <summary>交互体尺寸。官方 CA_Interact 的搜索球只有 0.3m，太小会点不到。</summary>
        private const float ColliderHeight = 2f;
        private const float ColliderRadius = 0.6f;

        /// <summary>达到该等级后聊天会冒爱心。</summary>
        private const int LoveHeartLevelThreshold = 5;

        private string _npcId;
        private DuckNpcRuntimeMarker _marker;

        private NPCGiftInteractable _giftInteractable;
        private NPCSpouseFollowInteractable _spouseFollowInteractable;
        private NPCDivorceInteractable _divorceInteractable;
        private NPCSpouseHomeInteractable _spouseHomeInteractable;

        // ====================================================================
        // 装配
        // ====================================================================

        /// <summary>
        /// 在角色下建 InteractRoot 子物体并挂上交互。返回 null 表示失败。
        /// </summary>
        /// <remarks>
        /// 用 SetActive(false) → 设 npcId → SetActive(true) 的顺序，
        /// 把 Awake 推迟到 npcId 已经写好之后 ——
        /// 与 NPCInteractionGroupHelper.AddSubInteractable 同一个时序技巧。
        /// </remarks>
        internal static PermanentDuckNpcInteractable Attach(CharacterMainControl npc, string npcId)
        {
            if (npc == null || string.IsNullOrEmpty(npcId))
            {
                return null;
            }

            try
            {
                GameObject root = new GameObject("InteractRoot");
                root.SetActive(false);
                root.transform.SetParent(npc.transform, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;

                PermanentDuckNpcInteractable interactable = root.AddComponent<PermanentDuckNpcInteractable>();
                interactable._npcId = npcId;

                root.SetActive(true);
                return interactable;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 挂载交互失败 " + npcId + ": " + e.Message);
                return null;
            }
        }

        protected override void Awake()
        {
            try
            {
                overrideInteractName = true;

                // 复用全局通用的"聊天"key，不给每只 NPC 再注一个
                string chatText = L10n.T("聊天", "Chat");
                LocalizationHelper.InjectLocalization("BossRush_Chat", chatText);
                _overrideInteractNameKey = "BossRush_Chat";
                InteractName = "BossRush_Chat";
                interactMarkerOffset = new Vector3(0f, 1f, 0f);

                // 必须在 base.Awake() 之前：官方 Awake 会遍历 otherInterablesInGroup，
                // 运行时 AddComponent 时它是 null，直接 NRE。
                NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(this, LogPrefix);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [ERROR] 交互名设置失败: " + e.Message);
            }

            SetupCollider();

            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] base.Awake 失败: " + e.Message);
            }

            _marker = GetComponentInParent<DuckNpcRuntimeMarker>();
            if (string.IsNullOrEmpty(_npcId) && _marker != null)
            {
                _npcId = _marker.NpcId;
            }
        }

        /// <summary>
        /// 在**本子物体**上建交互碰撞体，并与角色自身的碰撞体互相忽略。
        /// </summary>
        /// <remarks>
        /// 用非 trigger（与羽织一致）而不是 trigger：官方 CA_Interact 走
        /// `Physics.OverlapSphereNonAlloc` 的 4 参重载，是否命中 trigger 取决于
        /// 全局 `Physics.queriesHitTriggers`。用实体碰撞体不受那个全局开关影响。
        ///
        /// 代价是它会与角色自己的移动胶囊物理接触，所以显式 IgnoreCollision 掉 ——
        /// 否则 NPC 可能被自己的交互体顶住或抖动。
        /// </remarks>
        private void SetupCollider()
        {
            try
            {
                CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
                capsule.height = ColliderHeight;
                capsule.radius = ColliderRadius;
                capsule.center = new Vector3(0f, ColliderHeight * 0.5f, 0f);
                capsule.isTrigger = false;
                interactCollider = capsule;

                // 层由 base.Awake 改成 Interactable；这里显式写一次，
                // 让"这个子物体的层会变"这件事在本文件里可见。
                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    gameObject.layer = interactableLayer;
                }

                IgnoreCollisionWithOwner(capsule);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [ERROR] 交互碰撞体设置失败: " + e.Message);
            }
        }

        private void IgnoreCollisionWithOwner(Collider interact)
        {
            try
            {
                Transform ownerRoot = transform.parent;
                if (ownerRoot == null)
                {
                    return;
                }

                Collider[] ownerColliders = ownerRoot.GetComponentsInChildren<Collider>(true);
                if (ownerColliders == null)
                {
                    return;
                }

                for (int i = 0; i < ownerColliders.Length; i++)
                {
                    Collider other = ownerColliders[i];
                    if (other == null || other == interact)
                    {
                        continue;
                    }
                    Physics.IgnoreCollision(interact, other, true);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 忽略自身碰撞失败: " + e.Message);
            }
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] base.Start 失败: " + e.Message);
            }

            EnsureGroupedInteractionOptions();
            RefreshMarriageOptionVisibility();
        }

        // ====================================================================
        // 子选项
        // ====================================================================

        private void EnsureGroupedInteractionOptions()
        {
            if (string.IsNullOrEmpty(_npcId))
            {
                return;
            }

            List<InteractableBase> groupList =
                NPCInteractionGroupHelper.GetOrCreateGroupList(this, LogPrefix);
            if (groupList == null)
            {
                return;
            }

            string npcId = _npcId;

            if (_giftInteractable == null)
            {
                _giftInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform, "GiftOption", groupList,
                    (NPCGiftInteractable component) => component.NpcId = npcId);
            }

            if (_spouseFollowInteractable == null)
            {
                _spouseFollowInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform, "MarriageFollowOption", groupList,
                    (NPCSpouseFollowInteractable component) => component.NpcId = npcId);
            }

            if (_divorceInteractable == null)
            {
                _divorceInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform, "MarriageDivorceOption", groupList,
                    (NPCDivorceInteractable component) => component.NpcId = npcId);
            }

            if (_spouseHomeInteractable == null)
            {
                _spouseHomeInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform, "MarriageHomeOption", groupList,
                    (NPCSpouseHomeInteractable component) => component.NpcId = npcId);
            }

            // 注意：这里没有商店选项。PermanentDuckNpcAffinityConfig 不实现 INPCShopConfig，
            // 即使挂了 NPCShopInteractable 也会自己 SetActive(false)。
            // 将来要开服务时在这里加子选项，并让配置实现对应接口。
        }

        /// <summary>婚姻三件套的显隐。未婚时全部隐藏。</summary>
        internal void RefreshMarriageOptionVisibility()
        {
            EnsureGroupedInteractionOptions();

            try
            {
                bool married = !string.IsNullOrEmpty(_npcId)
                    && AffinityManager.IsMarriedToPlayer(_npcId);

                if (!married || ModBehaviour.Instance == null)
                {
                    SetOptionActive(_spouseFollowInteractable, false);
                    SetOptionActive(_divorceInteractable, false);
                    SetOptionActive(_spouseHomeInteractable, false);
                    return;
                }

                Transform npcTransform = _marker != null ? _marker.NpcTransform : transform;
                SetOptionActive(_spouseFollowInteractable,
                    ModBehaviour.Instance.ShouldShowSpouseFollowOption(_npcId, npcTransform));
                SetOptionActive(_divorceInteractable,
                    ModBehaviour.Instance.ShouldShowSpouseDivorceOption(_npcId, npcTransform));
                SetOptionActive(_spouseHomeInteractable,
                    ModBehaviour.Instance.ShouldShowSpouseHomeOption(_npcId, npcTransform));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 刷新婚姻选项失败: " + e.Message);
            }
        }

        private static void SetOptionActive(InteractableBase option, bool active)
        {
            if (option == null || option.gameObject == null)
            {
                return;
            }
            // 官方 GetInteractableList 只收 activeInHierarchy 的成员，
            // 所以选项显隐就是 SetActive。
            if (option.gameObject.activeSelf != active)
            {
                option.gameObject.SetActive(active);
            }
        }

        // ====================================================================
        // 聊天
        // ====================================================================

        /// <summary>
        /// 主交互触发点。
        /// </summary>
        /// <remarks>
        /// 用 OnTimeOut 而不是 OnInteractStart：`InteractableBase.interactTime` 是
        /// private SerializeField，运行时 AddComponent 恒为 0，
        /// 于是首帧就 OnTimeOut → finishWhenTimeOut → FinishInteract。
        /// 羽织/叮当的主交互也是这么接的。
        /// </remarks>
        protected override void OnTimeOut()
        {
            try
            {
                if (string.IsNullOrEmpty(_npcId))
                {
                    return;
                }

                BossRushAudioManager.Instance?.PlayNPCInteractSFX(_npcId);

                if (_marker != null)
                {
                    _marker.StartDialogue();
                }

                DoChat();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [ERROR] 聊天交互失败: " + e.Message);
            }
        }

        private void DoChat()
        {
            Transform target = _marker != null ? _marker.NpcTransform : transform;

            // 花心谴责优先：玩家给别人送过戒指的话，配偶见面先算这笔账
            if (NPCAffinityInteractionHelper.TryHandleSpouseCheatingRebuke(
                    _npcId, target,
                    () => { if (_marker != null) { _marker.ShowBrokenHeartBubble(); } },
                    LogPrefix))
            {
                EndDialogue(10f);
                return;
            }

            int dailyChat = 30;
            INPCAffinityConfig config = AffinityManager.GetNPCConfig(_npcId);
            INPCGiftConfig giftConfig = config as INPCGiftConfig;
            if (giftConfig != null)
            {
                dailyChat = giftConfig.DailyChatAffinity;
            }

            NPCAffinityInteractionHelper.ProcessChatAffinityAndFeedback(
                _npcId,
                dailyChat,
                LoveHeartLevelThreshold,
                () => { if (_marker != null) { _marker.ShowLoveHeartBubble(); } },
                LogPrefix);

            TryTriggerStoryMilestone();

            int level = AffinityManager.GetLevel(_npcId);
            string line = NPCDialogueSystem.GetDialogue(_npcId, DialogueCategory.Greeting, level);
            if (!string.IsNullOrEmpty(line))
            {
                NPCDialogueSystem.ShowDialogue(_npcId, target, line);
            }

            EndDialogue(6f);
        }

        /// <summary>
        /// 剧情里程碑打标。
        /// </summary>
        /// <remarks>
        /// **10 级那次是 load-bearing 的**：婚礼教堂的解锁判据是
        /// `AffinityManager.HasAnyNPCEverReachedMaxLevel()`，而它查的是
        /// `hasTriggeredStory10` 标记，**不是当前点数**。
        /// 不打这个标，这只 NPC 自己永远解锁不了教堂，玩家也就永远结不了婚。
        /// </remarks>
        private void TryTriggerStoryMilestone()
        {
            try
            {
                int level = AffinityManager.GetLevel(_npcId);
                int[] milestones = new int[] { 3, 5, 8, 10 };

                for (int i = 0; i < milestones.Length; i++)
                {
                    int milestone = milestones[i];
                    if (level >= milestone && !AffinityManager.HasTriggeredStory(_npcId, milestone))
                    {
                        AffinityManager.MarkStoryTriggered(_npcId, milestone);
                        ModBehaviour.DevLog(LogPrefix + " " + _npcId + " 触发剧情里程碑 Lv." + milestone);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 剧情里程碑打标失败: " + e.Message);
            }
        }

        private void EndDialogue(float stay)
        {
            try
            {
                if (_marker != null)
                {
                    _marker.EndDialogueWithStay(stay);
                }
                RefreshMarriageOptionVisibility();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 结束对话失败: " + e.Message);
            }
        }
    }
}
