using System;
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
        private NPCDivorceInteractable divorceInteractable;
        private bool handledDialogueEndThisInteraction;

        protected override void Awake()
        {
            try
            {
                interactableGroup = true;
                overrideInteractName = true;

                string chatText = L10n.T("聊天", "Chat");
                LocalizationHelper.InjectLocalization("BossRush_Chat", chatText);

                _overrideInteractNameKey = "BossRush_Chat";
                InteractName = "BossRush_Chat";
                interactMarkerOffset = new Vector3(0f, 1f, 0f);
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

                NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[NurseNPC]");
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

            CreateSubInteractables();
        }

        private void CreateSubInteractables()
        {
            try
            {
                var groupList = NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[NurseNPC]");
                if (groupList == null)
                {
                    return;
                }

                giftInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                    transform,
                    "GiftInteractable",
                    groupList,
                    (NPCGiftInteractable component) => component.NpcId = NurseAffinityConfig.NPC_ID);

                healInteractable = NPCInteractionGroupHelper.AddSubInteractable<NurseHealInteractable>(
                    transform,
                    "HealInteractable",
                    groupList);

                if (ShouldAddDivorceOption())
                {
                    divorceInteractable = NPCInteractionGroupHelper.AddSubInteractable(
                        transform,
                        "DivorceInteractable",
                        groupList,
                        (NPCDivorceInteractable component) => component.NpcId = NurseAffinityConfig.NPC_ID);
                }

                ModBehaviour.DevLog("[NurseNPC] 已注入护士交互组选项，总数: " + groupList.Count);
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 创建子交互失败: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        private bool ShouldAddDivorceOption()
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

            if (ModBehaviour.Instance == null)
            {
                return false;
            }

            Transform npcTransform = controller != null ? controller.NpcTransform : transform;
            return ModBehaviour.Instance.IsWeddingNpcInstance(npcTransform);
        }

        protected override bool IsInteractable()
        {
            return true;
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
