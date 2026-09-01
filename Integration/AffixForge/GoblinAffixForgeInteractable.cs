// ============================================================================
// GoblinAffixForgeInteractable.cs - 哥布林"词缀锻造"子交互组件
// ============================================================================
// 模块说明：
//   为哥布林 NPC 追加一个"词缀锻造"子交互选项，玩家选择后以词缀模式打开
//   共享的锻造 UI（复用官方分解界面，与重铸走同一条 View 与同一条关闭路径）。
//   形态逐字照 GoblinReforgeInteractable，不新建任何 UI 根、不新增 Harmony patch。
//
// 硬约束：
//   1. InteractName 走 _overrideInteractNameKey = "BossRush_AffixForge"，
//      该本地化键必须由 AffixForgeLocalization 注入，否则游戏里会显示 *星号*。
//   2. 开关关闭时本选项不可交互（IsInteractable 返回 false）。
//   3. 丧尸模式的临时哥布林不提供词缀锻造（与重铸的口径分开：那是局内临时 NPC）。
//   4. 组件创建由 GoblinInteractable.EnsureGroupedInteractionOptions 统一负责，
//      本文件不自己往 group 里塞东西。
// ============================================================================

using System;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 哥布林词缀锻造交互组件。作为哥布林的子交互选项，选中后打开词缀模式锻造 UI。
    /// </summary>
    public class GoblinAffixForgeInteractable : InteractableBase
    {
        private GoblinNPCController controller;
        private bool isInitialized = false;

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_AffixForge";
                this.InteractName = "BossRush_AffixForge";
            }
            catch { }

            try { this.interactMarkerOffset = new Vector3(0f, 0.15f, 0f); } catch { }

            try { NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[AffixForge]"); } catch { }

            try { base.Awake(); } catch { }

            try { controller = GetComponentInParent<GoblinNPCController>(); } catch { }

            try { this.MarkerActive = false; } catch { }

            isInitialized = true;
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
        }

        protected override bool IsInteractable()
        {
            if (!isInitialized)
            {
                return false;
            }

            try
            {
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod == null || !mod.IsAffixForgeConfiguredEnabled())
                {
                    return false;
                }

                // 丧尸模式的临时哥布林不提供词缀锻造服务
                if (controller != null && mod.IsZombieModeTemporaryRealNpc(controller))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                ModBehaviour.DevLog("[GoblinNPC] 玩家选择词缀锻造服务");

                BossRushAudioManager.Instance?.PlayGoblinInteractSFX();

                if (controller != null)
                {
                    controller.StartDialogue();
                }

                ReforgeUIManager.OpenAffixForgeUI(controller);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 词缀锻造交互出错: " + e.Message);
            }
        }

        protected override void OnInteractStop()
        {
            try { base.OnInteractStop(); } catch { }
        }
    }
}
