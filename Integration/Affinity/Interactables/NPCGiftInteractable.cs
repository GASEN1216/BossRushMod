// ============================================================================
// NPCGiftInteractable.cs - 通用NPC礼物赠送交互组件
// ============================================================================
// 通用的NPC礼物赠送交互组件，支持任意配置了好感度系统的NPC。
// 通过 npcId 参数化，无需为每个NPC创建单独的交互组件。
// 使用容器式UI（LootView）让玩家放入礼物进行赠送。
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using Duckov.UI;
using BossRush.UI;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 通用NPC礼物赠送交互组件
    /// </summary>
    public class NPCGiftInteractable : NPCInteractableBase
    {
        // ============================================================================
        // 交互设置
        // ============================================================================

        protected override Vector3 GetDefaultInteractMarkerOffset()
        {
            return new Vector3(0f, 0.15f, 0f);
        }

        protected override bool ShouldHideInteractMarker()
        {
            return true;
        }
        
        protected override void SetupInteractName()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                string giftText = L10n.T("赠送礼物", "Give Gift");
                LocalizationHelper.InjectLocalization("BossRush_GiveGift", giftText);
                LocalizationHelper.InjectLocalization(giftText, giftText);

                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_GiveGift";
                this.InteractName = "BossRush_GiveGift";
            }, "NPCGiftInteractable.SetupInteractName");
        }
        
        protected override bool IsInteractable()
        {
            // 始终可交互，即使今日已赠送（会显示不同的对话）
            return isInitialized && !string.IsNullOrEmpty(npcId);
        }

        // ============================================================================
        // 交互逻辑
        // ============================================================================

        protected override void DoInteract(CharacterMainControl character)
        {
            ModBehaviour.DevLog("[NPCGift] 玩家选择赠送礼物给: " + npcId);

            // 让NPC进入对话状态
            StartNPCDialogue();

            // 检查今日是否已赠送（已婚玩家仍可打开容器以赠送钻石戒指）
            if (!NPCGiftSystem.CanGiftToday(npcId))
            {
                ShowAlreadyGiftedDialogue();
                return;
            }

            // 获取NPC配置
            var config = GetNPCConfig();
            var containerConfig = config as INPCGiftContainerConfig;

            // 打开容器式UI
            ModBehaviour.DevLog("[NPCGift] 打开礼物容器UI，NPC: " + npcId);
            NPCGiftContainerService.OpenService(npcId, npcController?.NpcTransform, containerConfig, npcController);
        }

        /// <summary>
        /// 显示今日已赠送礼物的对话
        /// </summary>
        private void ShowAlreadyGiftedDialogue()
        {
            string dialogue = NPCGiftSystem.GetAlreadyGiftedDialogue(npcId);
            NPCDialogueSystem.ShowDialogue(npcId, npcController?.NpcTransform, dialogue);

            ModBehaviour.DevLog("[NPCGift] 今日已赠送，显示对话: " + dialogue);
            EndNPCDialogue();
        }
    }

    /// <summary>
    /// 通用配偶跟随交互组件。
    /// 仅当前配偶、好感度达到要求、驻留在婚礼教堂且未处于跟随状态时显示。
    /// </summary>
    public class NPCSpouseFollowInteractable : NPCInteractableBase
    {
        private const string FollowKey = "BossRush_SpouseFollow";

        protected override Vector3 GetDefaultInteractMarkerOffset()
        {
            return new Vector3(0f, 0.15f, 0f);
        }

        protected override bool ShouldHideInteractMarker()
        {
            return true;
        }

        protected override void Awake()
        {
            base.Awake();
            RefreshVisibility();
        }

        protected override void Start()
        {
            base.Start();
            RefreshVisibility();
        }

        protected virtual void OnEnable()
        {
            RefreshVisibility();
        }

        protected override void SetupInteractName()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                string followText = L10n.T("跟随", "Follow");
                LocalizationHelper.InjectLocalization(FollowKey, followText);
                LocalizationHelper.InjectLocalization(followText, followText);

                this.overrideInteractName = true;
                this._overrideInteractNameKey = FollowKey;
                this.InteractName = FollowKey;
            }, "NPCSpouseFollowInteractable.SetupInteractName");
        }

        protected override bool IsInteractable()
        {
            if (!base.IsInteractable()) return false;
            if (ModBehaviour.Instance == null) return false;
            Transform npcTransform = npcController?.NpcTransform ?? transform.root;
            return ModBehaviour.Instance.ShouldShowSpouseFollowOption(npcId, npcTransform);
        }

        protected override void DoInteract(CharacterMainControl character)
        {
            ModBehaviour.DevLog("[NPCFollow] 玩家请求配偶跟随: npc=" + npcId);

            StartNPCDialogue();
            bool started = ModBehaviour.Instance != null && ModBehaviour.Instance.TryStartSpouseFollowingPlayer(npcId);
            CloseCurrentInteraction(started ? 0.05f : 0.2f);
        }

        public void RefreshVisibility()
        {
            bool shouldBeVisible = false;
            if (ModBehaviour.Instance != null)
            {
                Transform npcTransform = npcController?.NpcTransform ?? transform.root;
                shouldBeVisible = ModBehaviour.Instance.ShouldShowSpouseFollowOption(npcId, npcTransform);
            }

            if (gameObject.activeSelf != shouldBeVisible)
            {
                gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    /// <summary>
    /// 通用NPC离婚交互组件。
    /// 仅当前配偶、未处于跟随状态、驻留在婚礼教堂时显示。
    /// </summary>
    public class NPCDivorceInteractable : NPCInteractableBase
    {
        private const string DivorceKey = "BossRush_Divorce";

        protected override Vector3 GetDefaultInteractMarkerOffset()
        {
            return new Vector3(0f, 0.15f, 0f);
        }

        protected override bool ShouldHideInteractMarker()
        {
            return true;
        }

        protected override void Awake()
        {
            base.Awake();
            RefreshVisibility();
        }

        protected override void Start()
        {
            base.Start();
            RefreshVisibility();
        }

        protected virtual void OnEnable()
        {
            RefreshVisibility();
        }

        protected override void SetupInteractName()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                string divorceText = L10n.T("离婚", "Divorce");
                LocalizationHelper.InjectLocalization(DivorceKey, divorceText);
                LocalizationHelper.InjectLocalization(divorceText, divorceText);

                this.overrideInteractName = true;
                this._overrideInteractNameKey = DivorceKey;
                this.InteractName = DivorceKey;
            }, "NPCDivorceInteractable.SetupInteractName");
        }

        protected override bool IsInteractable()
        {
            if (!base.IsInteractable()) return false;
            if (ModBehaviour.Instance == null) return false;
            Transform npcTransform = npcController?.NpcTransform ?? transform.root;
            return ModBehaviour.Instance.ShouldShowSpouseDivorceOption(npcId, npcTransform);
        }

        protected override void DoInteract(CharacterMainControl character)
        {
            ModBehaviour.DevLog("[NPCDivorce] 玩家请求离婚: npc=" + npcId);
            StartNPCDialogue();
            NPCMarriageSystem.HandleDivorceRequested(npcId, npcController?.NpcTransform, npcController);
        }

        public void RefreshVisibility()
        {
            bool shouldBeVisible = false;
            if (ModBehaviour.Instance != null)
            {
                Transform npcTransform = npcController?.NpcTransform ?? transform.root;
                shouldBeVisible = ModBehaviour.Instance.ShouldShowSpouseDivorceOption(npcId, npcTransform);
            }

            if (gameObject.activeSelf != shouldBeVisible)
            {
                gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    /// <summary>
    /// 通用配偶回家交互组件。
    /// 仅当前配偶、处于玩家跟随状态、且当前实例为跟随实例时显示。
    /// </summary>
    public class NPCSpouseHomeInteractable : NPCInteractableBase
    {
        private const string GoHomeKey = "BossRush_GoHome";

        protected override Vector3 GetDefaultInteractMarkerOffset()
        {
            return new Vector3(0f, 0.15f, 0f);
        }

        protected override bool ShouldHideInteractMarker()
        {
            return true;
        }

        protected override void Awake()
        {
            base.Awake();
            RefreshVisibility();
        }

        protected override void Start()
        {
            base.Start();
            RefreshVisibility();
        }

        protected virtual void OnEnable()
        {
            RefreshVisibility();
        }

        protected override void SetupInteractName()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                string goHomeText = L10n.T("回家", "Go Home");
                LocalizationHelper.InjectLocalization(GoHomeKey, goHomeText);
                LocalizationHelper.InjectLocalization(goHomeText, goHomeText);

                this.overrideInteractName = true;
                this._overrideInteractNameKey = GoHomeKey;
                this.InteractName = GoHomeKey;
            }, "NPCSpouseHomeInteractable.SetupInteractName");
        }

        protected override bool IsInteractable()
        {
            if (!base.IsInteractable()) return false;
            if (ModBehaviour.Instance == null) return false;
            Transform npcTransform = npcController?.NpcTransform ?? transform.root;
            return ModBehaviour.Instance.ShouldShowSpouseHomeOption(npcId, npcTransform);
        }

        protected override void DoInteract(CharacterMainControl character)
        {
            ModBehaviour.DevLog("[NPCHome] 玩家请求配偶回家: npc=" + npcId);
            StartNPCDialogue();

            bool sentHome = false;
            if (ModBehaviour.Instance != null)
            {
                sentHome = ModBehaviour.Instance.SendSpouseHome(npcId);
            }

            CloseCurrentInteraction(sentHome ? 0.05f : 0.2f);
        }

        public void RefreshVisibility()
        {
            bool shouldBeVisible = false;
            if (ModBehaviour.Instance != null)
            {
                Transform npcTransform = npcController?.NpcTransform ?? transform.root;
                shouldBeVisible = ModBehaviour.Instance.ShouldShowSpouseHomeOption(npcId, npcTransform);
            }

            if (gameObject.activeSelf != shouldBeVisible)
            {
                gameObject.SetActive(shouldBeVisible);
            }
        }
    }
}
