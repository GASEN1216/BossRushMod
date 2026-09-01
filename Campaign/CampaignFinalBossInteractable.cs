// ============================================================================
// CampaignFinalBossInteractable.cs - 竞技场内的「决战」召唤石
// ============================================================================
// 为什么要一个显式交互物，而不是进竞技场就自动开战：
//   玩家接了终章契约之后，仍然可能只是想跑一局普通竞技场。进场即刷 Boss 会直接
//   抢掉那一局——而且标准模式要等玩家点路牌才置 bossRushArenaActive，
//   自动开战恰好会卡在「还没开始正常流程」的窗口里，把人堵死。
//   给一块石头让玩家自己决定什么时候开打，是唯一不干扰既有玩法的做法。
//
// 交互体本身随场景销毁；召唤石由 CampaignFinalBoss 的维护逻辑按需重建，
// 因此这里不做任何持久化。
// ============================================================================

using System;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>终章决战召唤石的交互组件。</summary>
    public class CampaignFinalBossInteractable : InteractableBase
    {
        /// <summary>交互名的本地化键（由 CampaignLocalization 注入）。</summary>
        private const string InteractNameKey = "BossRush_Campaign_FinalBoss_Interact";

        protected override void Awake()
        {
            ApplyInteractName("awake");

            try
            {
                this.interactCollider = GetComponent<Collider>();
                this.interactMarkerOffset = new Vector3(0f, 1.2f, 0f);
                NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[CampaignFinalBoss]");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战交互体绑定失败: " + e.Message);
            }

            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] base.Awake 异常: " + e.Message);
            }

            try
            {
                if (this.interactCollider != null) this.interactCollider.enabled = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战交互体启用失败: " + e.Message);
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
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] base.Start 异常: " + e.Message);
            }

            ApplyInteractName("start");
        }

        private void ApplyInteractName(string stage)
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = InteractNameKey;
                this.InteractName = InteractNameKey;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix
                    + "[WARNING] 决战交互名设置失败(" + stage + "): " + e.Message);
            }
        }

        protected override bool IsInteractable()
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                return owner != null && owner.CanStartCampaignFinalBoss();
            }
            catch (Exception)
            {
                // 每帧靠近都会跑，失败时静默禁用，不打日志免得刷屏
                return false;
            }
        }

        protected override void OnTimeOut()
        {
            try
            {
                base.OnTimeOut();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] base.OnTimeOut 异常: " + e.Message);
            }

            try
            {
                ModBehaviour.Instance?.StartCampaignFinalBoss();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "决战触发异常: " + e.Message);
            }
        }
    }
}
