// ============================================================================
// CourierNPC partial source - extracted from CourierNPC.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Duckov.Utilities;
using Pathfinding;
using Cysharp.Threading.Tasks;
using Saves;
using ItemStatsSystem;
using Dialogues;
using NodeCanvas.DialogueTrees;
using SodaCraft.Localizations;
using BossRush.Utils;

namespace BossRush
{
    public class CourierStorageInteractable : InteractableBase
    {
        private CourierNPCController controller;
        private bool isInitialized = false;

        protected override void Awake()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_StorageService";
                this.InteractName = "BossRush_StorageService";
            }, "CourierStorageInteractable.Awake.SetupInteractName", false);

            NPCExceptionHandler.TryExecute(
                () => this.interactMarkerOffset = new Vector3(0f, 0.15f, 0f),
                "CourierStorageInteractable.Awake.SetMarkerOffset",
                false);

            NPCExceptionHandler.TryExecute(
                () => base.Awake(),
                "CourierStorageInteractable.Awake.BaseAwake",
                false);

            NPCExceptionHandler.TryExecute(
                () => controller = GetComponentInParent<CourierNPCController>(),
                "CourierStorageInteractable.Awake.GetController",
                false);

            NPCExceptionHandler.TryExecute(
                () => this.MarkerActive = false,
                "CourierStorageInteractable.Awake.SetMarkerActive",
                false);

            isInitialized = true;
        }

        protected override void Start()
        {
            NPCExceptionHandler.TryExecute(
                () => base.Start(),
                "CourierStorageInteractable.Start.BaseStart",
                false);
        }

        protected override bool IsInteractable()
        {
            return isInitialized;
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                ModBehaviour.DevLog("[CourierNPC] 玩家选择寄存服务");

                // 调用寄存服务
                StorageDepositService.OpenService(controller?.transform);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 寄存服务交互出错: " + e.Message);
            }
        }

        protected override void OnInteractStop()
        {
            NPCExceptionHandler.TryExecute(
                () => base.OnInteractStop(),
                "CourierStorageInteractable.OnInteractStop.BaseStop",
                false);
        }
    }

    public class CourierPaidLootSweepInteractable : InteractableBase
    {
        private CourierNPCController controller;
        private CourierMovement movement;
        private bool isInitialized = false;
        private string lastInjectedText = string.Empty;

        protected override void Awake()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                overrideInteractName = true;
                _overrideInteractNameKey = CourierPaidLootSweepService.PaidSweepInteractKey;
                InteractName = CourierPaidLootSweepService.PaidSweepInteractKey;
                interactMarkerOffset = new Vector3(0f, 0.15f, 0f);
                UpdateOptionName();
            }, "CourierPaidLootSweepInteractable.Awake.SetupInteractName", false);

            NPCExceptionHandler.TryExecute(
                () => base.Awake(),
                "CourierPaidLootSweepInteractable.Awake.BaseAwake",
                false);

            NPCExceptionHandler.TryExecute(
                () => controller = GetComponentInParent<CourierNPCController>(),
                "CourierPaidLootSweepInteractable.Awake.GetController",
                false);

            NPCExceptionHandler.TryExecute(
                () => movement = GetComponentInParent<CourierMovement>(),
                "CourierPaidLootSweepInteractable.Awake.GetMovement",
                false);

            NPCExceptionHandler.TryExecute(
                () => MarkerActive = false,
                "CourierPaidLootSweepInteractable.Awake.SetMarkerActive",
                false);

            isInitialized = true;
        }

        protected override void Start()
        {
            NPCExceptionHandler.TryExecute(
                () => base.Start(),
                "CourierPaidLootSweepInteractable.Start.BaseStart",
                false);

            UpdateOptionName();
        }

        private void OnEnable()
        {
            UpdateOptionName();
        }

        public void UpdateOptionName()
        {
            try
            {
                string optionText = CourierPaidLootSweepService.GetInteractText();
                if (!string.Equals(lastInjectedText, optionText, StringComparison.Ordinal))
                {
                    LocalizationHelper.InjectLocalization(CourierPaidLootSweepService.PaidSweepInteractKey, optionText);
                    lastInjectedText = optionText;
                }

                _overrideInteractNameKey = CourierPaidLootSweepService.PaidSweepInteractKey;
                InteractName = CourierPaidLootSweepService.PaidSweepInteractKey;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 更新付费扫箱选项失败: " + e.Message);
            }
        }

        protected override bool IsInteractable()
        {
            UpdateOptionName();

            ModBehaviour mod = ModBehaviour.Instance;
            return isInitialized && mod != null && mod.CanUseAwenLootSweepInCurrentMode();
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                ModBehaviour.DevLog("[CourierNPC] 玩家选择付费扫箱");

                if (controller == null)
                {
                    controller = GetComponentInParent<CourierNPCController>();
                }

                if (controller != null)
                {
                    controller.StartTalking(false);
                }

                if (movement != null)
                {
                    movement.SetInService(true);
                }

                bool started = CourierPaidLootSweepService.TryRunPaidSweep(controller != null ? controller.transform : transform, interactCharacter);
                if (!started)
                {
                    if (controller != null)
                    {
                        controller.StopTalking();
                    }

                    if (movement != null)
                    {
                        movement.SetInService(false);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 付费扫箱交互出错: " + e.Message);
            }
        }

        protected override void OnInteractStop()
        {
            NPCExceptionHandler.TryExecute(
                () => base.OnInteractStop(),
                "CourierPaidLootSweepInteractable.OnInteractStop.BaseStop",
                false);
        }
    }

    public class CourierServiceOptionInteractable : InteractableBase
    {
        private CourierNPCController controller;
        private bool isInitialized = false;

        protected override void Awake()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                overrideInteractName = true;
                _overrideInteractNameKey = "BossRush_CourierService";
                InteractName = "BossRush_CourierService";
                interactMarkerOffset = new Vector3(0f, 0.15f, 0f);
            }, "CourierServiceOptionInteractable.Awake.SetupInteractName", false);

            NPCExceptionHandler.TryExecute(
                () => base.Awake(),
                "CourierServiceOptionInteractable.Awake.BaseAwake",
                false);

            NPCExceptionHandler.TryExecute(
                () => controller = GetComponentInParent<CourierNPCController>(),
                "CourierServiceOptionInteractable.Awake.GetController",
                false);

            NPCExceptionHandler.TryExecute(
                () => MarkerActive = false,
                "CourierServiceOptionInteractable.Awake.SetMarkerActive",
                false);

            isInitialized = true;
        }

        protected override void Start()
        {
            NPCExceptionHandler.TryExecute(
                () => base.Start(),
                "CourierServiceOptionInteractable.Start.BaseStart",
                false);
        }

        protected override bool IsInteractable()
        {
            return isInitialized;
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                ModBehaviour.DevLog("[CourierNPC] 玩家选择快递服务（子选项）");

                if (controller == null)
                {
                    controller = GetComponentInParent<CourierNPCController>();
                }

                if (controller != null)
                {
                    controller.StartTalking(false);
                }

                CourierService.OpenService(controller != null ? controller.transform : transform);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 快递服务子交互出错: " + e.Message);
            }
        }

        protected override void OnInteractStop()
        {
            NPCExceptionHandler.TryExecute(
                () => base.OnInteractStop(),
                "CourierServiceOptionInteractable.OnInteractStop.BaseStop",
                false);
        }
    }

    /// <summary>
    /// 快递员主交互组件。
    /// 主交互处理扫箱，快递服务/寄存服务通过原生 grouped interaction 提供。
    /// </summary>
    public class CourierInteractable : InteractableBase
    {
        private CourierServiceOptionInteractable serviceInteractable;
        private CourierStorageInteractable storageInteractable;

        protected override void Awake()
        {
            try
            {
                // 设置主交互名称
                this.overrideInteractName = true;
                this._overrideInteractNameKey = CourierPaidLootSweepService.PaidSweepInteractKey;
                this.InteractName = CourierPaidLootSweepService.PaidSweepInteractKey;

                // 设置交互标记偏移（显示在人物中间）
                this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f);

                NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(this, "[CourierNPC]");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] CourierInteractable.Awake 设置属性失败: " + e.Message);
            }

            // 确保有 Collider
            try
            {
                Collider col = GetComponent<Collider>();
                if (col == null)
                {
                    CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
                    capsule.height = 2f;
                    capsule.radius = 0.8f;
                    capsule.center = new Vector3(0f, 1f, 0f);
                    capsule.isTrigger = false;  // 不是触发器，是实体碰撞器
                    this.interactCollider = capsule;
                }
                else
                {
                    this.interactCollider = col;
                }

                // 设置 Layer 为 Interactable（让玩家能检测到交互点）
                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    gameObject.layer = interactableLayer;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] CourierInteractable 设置 Collider 失败: " + e.Message);
            }

            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                // 捕获可能的异常，确保 Mod 能继续运行
                ModBehaviour.DevLog("[CourierNPC] [WARNING] CourierInteractable base.Awake 异常: " + e.Message);
            }

            ModBehaviour.DevLog("[CourierNPC] CourierInteractable.Awake 完成");
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] CourierInteractable base.Start 异常: " + e.Message);
            }

            EnsureGroupedInteractionOptions();
        }

        /// <summary>
        /// 创建组内附加服务选项。
        /// </summary>
        private void EnsureGroupedInteractionOptions()
        {
            List<InteractableBase> groupList = NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[CourierNPC]");
            if (groupList == null)
            {
                return;
            }

            RemoveLegacyPaidSweepSubOption(groupList);

            if (serviceInteractable == null)
            {
                serviceInteractable = NPCInteractionGroupHelper.AddSubInteractable<CourierServiceOptionInteractable>(
                    transform,
                    "CourierServiceOption",
                    groupList);
            }

            if (storageInteractable == null)
            {
                storageInteractable = NPCInteractionGroupHelper.AddSubInteractable<CourierStorageInteractable>(
                    transform,
                    "StorageOption",
                    groupList);
            }

            if (serviceInteractable != null)
            {
                groupList.Remove(serviceInteractable);
            }

            if (storageInteractable != null)
            {
                groupList.Remove(storageInteractable);
            }

            if (serviceInteractable != null)
            {
                groupList.Insert(0, serviceInteractable);
            }

            if (storageInteractable != null)
            {
                groupList.Add(storageInteractable);
            }
        }

        private void RemoveLegacyPaidSweepSubOption(List<InteractableBase> groupList)
        {
            if (groupList != null)
            {
                for (int i = groupList.Count - 1; i >= 0; i--)
                {
                    if (groupList[i] is CourierPaidLootSweepInteractable)
                    {
                        groupList.RemoveAt(i);
                    }
                }
            }

            Transform legacyChild = transform.Find("PaidLootSweepOption");
            if (legacyChild != null)
            {
                UnityEngine.Object.Destroy(legacyChild.gameObject);
            }
        }

        protected override bool IsInteractable()
        {
            // 快递员始终可交互
            return true;
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            base.OnInteractStart(interactCharacter);
            ModBehaviour.DevLog("[CourierNPC] 玩家开始与快递员交互");
        }

        protected override void OnTimeOut()
        {
            // 主交互选项"扫箱"被选中
            try
            {
                ModBehaviour.DevLog("[CourierNPC] 玩家选择付费扫箱（主交互）");

                var controller = GetComponent<CourierNPCController>();
                if (controller != null)
                {
                    controller.StartTalking(false);
                }

                CourierMovement movement = GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetInService(true);
                }

                bool started = CourierPaidLootSweepService.TryRunPaidSweep(controller != null ? controller.transform : transform, CharacterMainControl.Main);
                if (!started)
                {
                    if (controller != null)
                    {
                        controller.StopTalking();
                    }

                    if (movement != null)
                    {
                        movement.SetInService(false);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 扫箱主交互出错: " + e.Message);
            }
        }
    }
}
