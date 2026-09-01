// ============================================================================
// CampaignBoardInteractable.cs - 征程公告板交互组件
// ============================================================================
// 形态照 Integration/DailyReport/DailyReportInteractable.cs：
// 继承官方 InteractableBase，交互完成（OnTimeOut）后打开公告板面板。
//
// 两条纪律：
//   1. base.Awake() / base.Start() 必须单独包 try/catch：其他 Mod 可能 patch 了
//      InteractableBase，它们的异常不能把我们的建筑一起拖挂。
//   2. catch 里保留 DevLog，不做空吞。这里是一次性初始化路径不是每帧热路径，
//      按 AGENTS.md 4.7 属于「可以补日志」的那一类；空吞会让装配失败变成
//      玩家侧的「公告板按不动」而日志里什么都没有。
// ============================================================================

using System;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>基地征程公告板的交互组件。</summary>
    public class CampaignBoardInteractable : InteractableBase
    {
        /// <summary>交互名的本地化 key（由 CampaignLocalization 注入）。</summary>
        private const string InteractNameKey = "BossRush_Campaign_Board_Interact";

        protected override void Awake()
        {
            ApplyInteractName("awake");

            try
            {
                this.interactCollider = GetComponent<Collider>();
                this.interactMarkerOffset = new Vector3(0f, 1.4f, 0f);
                NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[CampaignBoard]");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 交互体绑定失败: " + e.Message);
            }

            // 官方基类：其他 Mod 的 patch 可能在这里抛，必须隔离
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
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 交互体启用失败: " + e.Message);
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

            // 再设一遍：官方 Start 可能把 InteractName 覆盖回 prefab 上的值
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
                    + "[WARNING] 交互名设置失败(" + stage + "): " + e.Message);
            }
        }

        protected override bool IsInteractable()
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                return owner != null && owner.IsCampaignConfiguredEnabled();
            }
            catch (Exception)
            {
                // 每次靠近公告板都会跑，失败时静默禁用即可，不打日志免得刷屏
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
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.OpenCampaignBoardUI();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "交互触发异常: " + e.Message);
            }
        }
    }
}
