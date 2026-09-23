using System;
using System.Collections.Generic;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    public class NurseInteractable : InteractableBase
    {
        private const float InteractStopFallbackStayDuration = 0.05f;

        private NurseNPCController controller;
        private NPCGiftInteractable giftInteractable;
        private NurseHealInteractable healInteractable;
        private NPCSpouseFollowInteractable spouseFollowInteractable;
        private NPCDivorceInteractable divorceInteractable;
        private NPCSpouseHomeInteractable spouseHomeInteractable;
        private bool handledDialogueEndThisInteraction;

        protected override void Awake()
        {
            try
            {
                overrideInteractName = true;

                string chatText = L10n.T("聊天", "Chat");
                LocalizationHelper.InjectLocalization("BossRush_Chat", chatText);

                _overrideInteractNameKey = "BossRush_Chat";
                InteractName = "BossRush_Chat";
                interactMarkerOffset = new Vector3(0f, 1f, 0f);

                NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(this, "[NurseNPC]");
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] NurseInteractable.Awake setup failed: " + ex.Message);
            }

            try
            {
                Collider existingCollider = GetComponent<Collider>();
                if (existingCollider == null)
                {
                    CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
                    capsule.height = 2f;
                    capsule.radius = 0.6f;
                    capsule.center = new Vector3(0f, 1f, 0f);
                    capsule.isTrigger = false;
                    interactCollider = capsule;
                }
                else
                {
                    interactCollider = existingCollider;
                }

                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    gameObject.layer = interactableLayer;
                }

            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] NurseInteractable collider setup failed: " + ex.Message);
            }

            try
            {
                base.Awake();
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] NurseInteractable base.Awake failed: " + ex.Message);
            }

            controller = GetComponent<NurseNPCController>();
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] NurseInteractable base.Start failed: " + ex.Message);
            }

            if (controller == null)
            {
                controller = GetComponent<NurseNPCController>();
            }

            EnsureGroupedInteractionOptions();
            RefreshMarriageOptionVisibility();
        }

        private void EnsureGroupedInteractionOptions()
        {
            List<InteractableBase> groupList = NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[NurseNPC]");
            if (groupList == null)
            {
                return;
            }

            if (giftInteractable == null)
            {
                giftInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform,
                    "GiftOption",
                    groupList,
                    (NPCGiftInteractable component) => component.NpcId = NurseAffinityConfig.NPC_ID);
            }

            if (healInteractable == null)
            {
                healInteractable = NPCInteractionGroupHelper.AddSubInteractable<NurseHealInteractable>(
                    transform,
                    "HealOption",
                    groupList);
            }

            if (spouseFollowInteractable == null)
            {
                spouseFollowInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform,
                    "MarriageFollowOption",
                    groupList,
                    (NPCSpouseFollowInteractable component) => component.NpcId = NurseAffinityConfig.NPC_ID);
            }

            if (divorceInteractable == null)
            {
                divorceInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform,
                    "MarriageDivorceOption",
                    groupList,
                    (NPCDivorceInteractable component) => component.NpcId = NurseAffinityConfig.NPC_ID);
            }

            if (spouseHomeInteractable == null)
            {
                spouseHomeInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform,
                    "MarriageHomeOption",
                    groupList,
                    (NPCSpouseHomeInteractable component) => component.NpcId = NurseAffinityConfig.NPC_ID);
            }
        }

        private bool ShouldAddMarriageOptions()
        {
            if (!AffinityManager.IsMarriedToPlayer(NurseAffinityConfig.NPC_ID))
            {
                return false;
            }

            string currentSpouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            if (string.IsNullOrEmpty(currentSpouseNpcId) || currentSpouseNpcId != NurseAffinityConfig.NPC_ID)
            {
                return false;
            }

            return true;
        }

        public void RefreshMarriageOptionVisibility()
        {
            EnsureGroupedInteractionOptions();

            bool shouldAddMarriageOptions = ShouldAddMarriageOptions();
            if (!shouldAddMarriageOptions || ModBehaviour.Instance == null)
            {
                SetMarriageOptionActive(spouseFollowInteractable, false);
                SetMarriageOptionActive(divorceInteractable, false);
                SetMarriageOptionActive(spouseHomeInteractable, false);
                return;
            }

            Transform npcTransform = controller != null ? controller.NpcTransform : transform;
            bool showFollow = ModBehaviour.Instance.ShouldShowSpouseFollowOption(NurseAffinityConfig.NPC_ID, npcTransform);
            bool showDivorce = ModBehaviour.Instance.ShouldShowSpouseDivorceOption(NurseAffinityConfig.NPC_ID, npcTransform);
            bool showHome = ModBehaviour.Instance.ShouldShowSpouseHomeOption(NurseAffinityConfig.NPC_ID, npcTransform);

            SetMarriageOptionActive(spouseFollowInteractable, showFollow);
            SetMarriageOptionActive(divorceInteractable, showDivorce);
            SetMarriageOptionActive(spouseHomeInteractable, showHome);
        }

        private static void SetMarriageOptionActive(Behaviour interactable, bool active)
        {
            if (interactable == null || interactable.gameObject == null)
            {
                return;
            }

            if (interactable.gameObject.activeSelf != active)
            {
                interactable.gameObject.SetActive(active);
            }
        }

        protected override bool IsInteractable()
        {
            RefreshHealOptionWhenApproached();
            return true;
        }

        /// <summary>治疗选项显隐的刷新间隔（秒）。玩家贴着护士时官方每帧都来问 IsInteractable。</summary>
        private const float HealOptionRefreshInterval = 0.25f;
        private float nextHealOptionRefreshTime;

        /// <summary>
        /// 玩家走近、护士即将成为交互主体时刷新「治疗」选项显隐（满血无减益不挂，见 NurseHealInteractable.RefreshOptionVisibility）。
        ///
        /// 【菜单正显示本护士时不改列表】官方 InteractHUD 只在交互主体变化时重建选项，中途增删成员会让
        /// 高亮项与实际交互目标错位（按「送礼」却触发了别的）。所以只在官方扫描候选、主体还不是本护士时刷新；
        /// 一次交互结束后官方会重建一次菜单，那一刻由治疗选项自己的 OnInteractStop 刷新。
        /// </summary>
        private void RefreshHealOptionWhenApproached()
        {
            if (healInteractable == null)
            {
                return;
            }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.interactAction != null
                    && player.interactAction.MasterInteractableAround == this)
                {
                    return;
                }

                float now = Time.unscaledTime;
                if (now < nextHealOptionRefreshTime)
                {
                    return;
                }
                nextHealOptionRefreshTime = now + HealOptionRefreshInterval;
                healInteractable.RefreshOptionVisibility();
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] 刷新治疗选项显隐失败: " + ex.Message);
            }
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            base.OnInteractStart(interactCharacter);
            handledDialogueEndThisInteraction = false;

            if (controller != null)
            {
                controller.StartDialogue();
            }
        }

        protected override void OnTimeOut()
        {
            try
            {
                BossRushAudioManager.Instance?.PlayNPCInteractSFX(NurseAffinityConfig.NPC_ID);

                if (controller == null)
                {
                    controller = GetComponent<NurseNPCController>();
                }

                if (controller != null)
                {
                    controller.StartDialogue();
                }

                DoChat();
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 聊天交互失败: " + ex.Message);
            }
        }

        private void DoChat()
        {
            try
            {
                Transform dialogueTarget = controller != null ? controller.transform : null;
                if (NPCAffinityInteractionHelper.TryHandleSpouseCheatingRebuke(
                    NurseAffinityConfig.NPC_ID,
                    dialogueTarget,
                    () =>
                    {
                        if (controller != null)
                        {
                            controller.ShowBrokenHeartBubble();
                        }
                    },
                    "[NurseNPC]"))
                {
                    EndDialogueWithMark(10f);
                    return;
                }

                bool isMarriedToNurse = AffinityManager.IsMarriedToPlayer(NurseAffinityConfig.NPC_ID);

                int level = AffinityManager.GetLevel(NurseAffinityConfig.NPC_ID);
                if (controller != null)
                {
                    if (level >= 3 && !AffinityManager.HasTriggeredStory(NurseAffinityConfig.NPC_ID, 3))
                    {
                        controller.TriggerStoryDialogue(3);
                        return;
                    }

                    if (level >= 5 && !AffinityManager.HasTriggeredStory(NurseAffinityConfig.NPC_ID, 5))
                    {
                        controller.TriggerStoryDialogue(5);
                        return;
                    }

                    if (level >= 8 && !AffinityManager.HasTriggeredStory(NurseAffinityConfig.NPC_ID, 8))
                    {
                        controller.TriggerStoryDialogue(8);
                        return;
                    }

                    if (level >= 10 && !AffinityManager.HasTriggeredStory(NurseAffinityConfig.NPC_ID, 10))
                    {
                        controller.TriggerStoryDialogue(10);
                        return;
                    }
                }

                bool dailyChatGranted;
                NPCAffinityInteractionHelper.ProcessChatAffinityAndFeedback(
                    NurseAffinityConfig.NPC_ID,
                    30,
                    8,
                    () =>
                    {
                        if (controller != null)
                        {
                            controller.ShowLoveHeartBubble();
                        }
                    },
                    "[NurseNPC]",
                    out dailyChatGranted);

                if (isMarriedToNurse && dailyChatGranted && controller != null)
                {
                    string successBanner = L10n.T("羽织递给了你一瓶安神滴剂", "Yu Zhi gives you a bottle of Calming Drops");
                    string fullInventoryBanner = L10n.T("背包已满，安神滴剂掉落在羽织脚边。", "Inventory full. The Calming Drops were dropped at Yu Zhi's feet.");
                    controller.TryGiveRewardItem(CalmingDropsConfig.TYPE_ID, 1, successBanner, fullInventoryBanner);
                }

                string dialogue = NPCDialogueSystem.GetDialogue(NurseAffinityConfig.NPC_ID, DialogueCategory.Greeting);
                NPCDialogueSystem.ShowDialogue(NurseAffinityConfig.NPC_ID, dialogueTarget, dialogue);

                if (controller != null)
                {
                    EndDialogueWithMark(10f);
                }
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 聊天流程失败: " + ex.Message);
                EndDialogueWithMark(10f);
            }
        }

        private void EndDialogueWithMark(float stayDuration)
        {
            if (controller == null)
            {
                controller = GetComponent<NurseNPCController>();
            }

            if (controller != null)
            {
                handledDialogueEndThisInteraction = true;
                controller.EndDialogueWithStay(stayDuration);
            }
        }

        private void EnsureDialogueEndedOnStop()
        {
            if (controller == null)
            {
                controller = GetComponent<NurseNPCController>();
            }

            if (controller != null && !controller.IsInStoryDialogue)
            {
                controller.EndDialogueWithStay(InteractStopFallbackStayDuration);
            }
        }

        protected override void OnInteractStop()
        {
            NPCExceptionHandler.TryExecute(
                () => base.OnInteractStop(),
                "NurseInteractable.OnInteractStop.BaseStop",
                false);

            if (!handledDialogueEndThisInteraction)
            {
                NPCExceptionHandler.TryExecute(
                    EnsureDialogueEndedOnStop,
                    "NurseInteractable.OnInteractStop.EnsureDialogueEnded",
                    false);
            }

            handledDialogueEndThisInteraction = false;
        }
    }
}
