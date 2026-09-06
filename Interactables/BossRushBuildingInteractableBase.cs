// ============================================================================
// BossRushBuildingInteractableBase.cs - 「靠近建筑 -> 交互完成 -> 打开面板」的共享交互体基类
// ============================================================================
// 为什么需要它（2026-09-06，D-2）：报箱 / 征程公告板 / 展示柜 / 许愿台 / 终章召唤石
//   五个交互体此前是同一份约 130 行的逐字复制（公告板与报箱归一化后只差 27 行）。
//   现在骨架只有这一份，子类只声明「交互名 key、日志前缀、交互组标签、标记高度、
//   可交互条件、交互完成后做什么」。
//
// 两条纪律（照 Integration/WishFountain/WishFountainInteractable.cs 的既有防御式写法）：
//   1. base.Awake() / base.Start() 必须单独包 try/catch：其他 Mod 可能 patch 了
//      InteractableBase，它们的异常不能把我们的建筑一起拖挂。
//   2. catch 里保留 DevLog，不做空吞。这里是**一次性初始化路径**不是每帧热路径，
//      按 AGENTS.md 4.7 属于「可以补日志」的那一类；空吞会让交互点装配失败变成
//      玩家侧的「建筑按不动」而日志里什么都没有。IsInteractable 是每次靠近都跑的
//      例外：失败时静默禁用，不打日志免得刷屏。
//
// 时序（由 tests/LatestPlayerLogRegressionGuard.py 守卫）：otherInterablesInGroup 必须在
//   base.Awake() 之前初始化，否则官方基类会在分组交互上空引用。
// ============================================================================

using System;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 建筑交互体共享骨架：交互名注入（Awake 与 Start 各一次，官方 Start 可能把
    /// InteractName 覆盖回 prefab 上的值）、碰撞体启用、交互组初始化、完成后回调。
    /// </summary>
    public abstract class BossRushBuildingInteractableBase : InteractableBase
    {
        /// <summary>交互名的本地化 key（由各子系统的 *Localization 注入）。</summary>
        protected abstract string InteractNameKey { get; }

        /// <summary>日志前缀（含尾随空格）。</summary>
        protected abstract string LogPrefix { get; }

        /// <summary>
        /// 交互组标签，例如 "[DailyReport]"；返回 null 表示不初始化交互组
        /// （许愿台沿用它上线以来的行为）。
        /// </summary>
        protected virtual string InteractionGroupLabel { get { return null; } }

        /// <summary>交互标记相对建筑根的高度。</summary>
        protected virtual float InteractMarkerHeight { get { return 1.2f; } }

        /// <summary>可交互条件。每次靠近都会跑；抛异常一律按不可交互处理。</summary>
        protected abstract bool IsBuildingInteractable();

        /// <summary>交互完成（OnTimeOut）后的动作，通常是打开面板。</summary>
        protected abstract void OnInteractCompleted();

        protected override void Awake()
        {
            ApplyInteractName("awake");

            try
            {
                this.interactCollider = GetComponent<Collider>();
                this.interactMarkerOffset = new Vector3(0f, InteractMarkerHeight, 0f);
                string groupLabel = InteractionGroupLabel;
                if (!string.IsNullOrEmpty(groupLabel))
                {
                    NPCInteractionGroupHelper.GetOrCreateGroupList(this, groupLabel);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 交互体绑定失败: " + e.Message);
            }

            // 官方基类：其他 Mod 的 patch 可能在这里抛，必须隔离
            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] base.Awake 异常: " + e.Message);
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
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 交互体启用失败: " + e.Message);
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
                ModBehaviour.DevLog(LogPrefix + "[WARNING] base.Start 异常: " + e.Message);
            }

            // 再设一遍：官方 Start 可能把 InteractName 覆盖回 prefab 上的值
            ApplyInteractName("start");
        }

        /// <summary>设置交互名。Awake 与 Start 各调一次。</summary>
        private void ApplyInteractName(string stage)
        {
            try
            {
                string key = InteractNameKey;
                this.overrideInteractName = true;
                this._overrideInteractNameKey = key;
                this.InteractName = key;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 交互名设置失败(" + stage + "): " + e.Message);
            }
        }

        protected override bool IsInteractable()
        {
            try
            {
                return IsBuildingInteractable();
            }
            catch (Exception)
            {
                // 每次靠近都会跑，失败时静默禁用即可，不打日志免得刷屏
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
                ModBehaviour.DevLog(LogPrefix + "[WARNING] base.OnTimeOut 异常: " + e.Message);
            }

            try
            {
                OnInteractCompleted();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "交互触发异常: " + e.Message);
            }
        }
    }
}
