// ============================================================================
// PetNestInteractable.cs - 遗种巢建筑交互点（实施计划 步骤 9）
// ============================================================================
// 最小化修改：全案只加**一个**建筑，巢 / 孵化 / 远征 / 博物馆四个功能都挂在这一个
// 交互点上，走 NPCInteractionGroupHelper 的多选项交互菜单（先例：护士 NPC 五选项、
// 婚礼教堂宿主 + 子选项）。
//
// 时序纪律（照既有派生类）：
//   - PrepareGroupedInteractionOwner 必须在 base.Awake() **之前**调；
//   - 子选项在 Start() 里、base.Start() **之后**建；
//   - 子选项靠 SetActive 显隐，不从 group list 里删（官方 GetInteractableList 按
//     activeInHierarchy 过滤）。
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>遗种巢建筑的宿主交互点。</summary>
    public class PetNestInteractable : InteractableBase
    {
        /// <summary>宿主交互名的本地化 key（打开巢）。</summary>
        internal const string HostInteractKey = "BossRush_PetNest_Interact";

        private PetNestHatchInteractable hatchOption;
        private PetNestExpeditionInteractable expeditionOption;
        private PetNestMuseumInteractable museumOption;
        private bool optionsInjected;

        protected override void Awake()
        {
            try
            {
                this.interactMarkerOffset = new Vector3(0f, 1.5f, 0f);
            }
            catch (Exception)
            {
                // 标记偏移失败不影响交互本身
            }

            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = HostInteractKey;
                this.InteractName = HostInteractKey;
            }
            catch (Exception)
            {
                // 名字覆盖失败时官方会显示默认名
            }

            try
            {
                // 必须在 base.Awake() 之前：它会初始化分组列表
                NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(this, "[PetNest]");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 分组交互初始化失败: " + e.Message);
            }

            try
            {
                if (this.interactCollider == null)
                {
                    this.interactCollider = GetComponent<Collider>();
                }
                int layer = LayerMask.NameToLayer("Interactable");
                if (layer != -1) gameObject.layer = layer;
            }
            catch (Exception)
            {
                // 碰撞体/层设置失败由官方 InteractableBase.Awake 兜底
            }

            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 交互点 Awake 失败: " + e.Message);
            }

            try
            {
                if (this.interactCollider != null) this.interactCollider.enabled = true;
            }
            catch (Exception)
            {
                // 同上
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
                ModBehaviour.DevLog("[PetNest] 交互点 Start 失败: " + e.Message);
            }

            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = HostInteractKey;
                this.InteractName = HostInteractKey;
            }
            catch (Exception)
            {
                // 名字被官方覆盖时不致命
            }

            if (!optionsInjected)
            {
                EnsureGroupedInteractionOptions();
            }
        }

        private void EnsureGroupedInteractionOptions()
        {
            try
            {
                List<InteractableBase> groupList =
                    NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[PetNest]");
                if (groupList == null) return;

                if (hatchOption == null)
                {
                    hatchOption = NPCInteractionGroupHelper.AddSubInteractable<PetNestHatchInteractable>(
                        transform, "HatchOption", groupList);
                }
                if (expeditionOption == null)
                {
                    expeditionOption = NPCInteractionGroupHelper.AddSubInteractable<PetNestExpeditionInteractable>(
                        transform, "ExpeditionOption", groupList);
                }
                if (museumOption == null)
                {
                    museumOption = NPCInteractionGroupHelper.AddSubInteractable<PetNestMuseumInteractable>(
                        transform, "MuseumOption", groupList);
                }

                optionsInjected = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 子选项装配失败: " + e.Message);
            }
        }

        protected override bool IsInteractable()
        {
            return PetNestUIBridge.IsPetNestUsable();
        }

        protected override void OnTimeOut()
        {
            try
            {
                base.OnTimeOut();
            }
            catch (Exception)
            {
                // 基类超时处理失败不阻断打开面板
            }
            PetNestUIBridge.OpenPage(PetNestUIPage.Nest);
        }
    }

    /// <summary>子选项：孵化。</summary>
    public class PetNestHatchInteractable : PetNestSubInteractableBase
    {
        protected override string InteractKey { get { return "BossRush_PetNest_Interact_Hatch"; } }
        protected override PetNestUIPage TargetPage { get { return PetNestUIPage.Hatch; } }
    }

    /// <summary>子选项：天灾远征。</summary>
    public class PetNestExpeditionInteractable : PetNestSubInteractableBase
    {
        protected override string InteractKey { get { return "BossRush_PetNest_Interact_Expedition"; } }
        protected override PetNestUIPage TargetPage { get { return PetNestUIPage.Expedition; } }
    }

    /// <summary>子选项：遗种博物馆。</summary>
    public class PetNestMuseumInteractable : PetNestSubInteractableBase
    {
        protected override string InteractKey { get { return "BossRush_PetNest_Interact_Museum"; } }
        protected override PetNestUIPage TargetPage { get { return PetNestUIPage.Museum; } }
    }

    /// <summary>
    /// 子选项基类：纯逻辑交互体，不显示标记、碰撞体只占位。
    /// 形态照 Interactables/BossRushInteractables.cs 的 BossRushInteractable。
    /// </summary>
    public abstract class PetNestSubInteractableBase : InteractableBase
    {
        /// <summary>本地化 key。</summary>
        protected abstract string InteractKey { get; }

        /// <summary>点开后跳转的页面。</summary>
        protected abstract PetNestUIPage TargetPage { get; }

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = InteractKey;
                this.InteractName = InteractKey;
            }
            catch (Exception)
            {
                // 名字覆盖失败时官方显示默认名
            }

            try
            {
                // 只为把自己的分组列表初始化成空表，避免基类解引用 null
                NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[PetNest]");
            }
            catch (Exception)
            {
                // 分组列表初始化失败不致命
            }

            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 子选项 Awake 失败: " + e.Message);
            }

            try
            {
                this.MarkerActive = false;
            }
            catch (Exception)
            {
                // 子选项不该显示世界标记，失败也只是多一个标记
            }
        }

        protected override bool IsInteractable()
        {
            return PetNestUIBridge.IsPetNestUsable();
        }

        protected override void OnTimeOut()
        {
            try
            {
                base.OnTimeOut();
            }
            catch (Exception)
            {
                // 同宿主
            }
            PetNestUIBridge.OpenPage(TargetPage);
        }
    }
}
