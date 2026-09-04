// ============================================================================
// RandomEventModels.cs - 随机事件的枚举、运行期上下文与事件基类（方案二 步骤 2）
// ============================================================================
// 职责：
//   - RandomEventId / RandomEventPhase / RandomEventEndReason 三个稳定枚举；
//   - RandomEventContext：单次事件的运行期上下文（作废判据 + 生成物回收作用域）；
//   - RandomEventBase：单个事件的实现基类，事件目录里的 8 个事件全部继承它。
//
// 硬约束：
//   - RandomEventId 的数值进 F3 调试菜单与日志，**只增不改、不复用**；
//   - 所有生成物（GameObject / 协程 / 还原动作）一律注册进 ctx.Scope，
//     由 RuntimeScope.Clear 统一回收；事件自己额外持有的实例引用必须在 OnCleanup 里清干净；
//   - 覆写的 CanTrigger / OnTrigger / OnTick / OnCleanup 必须 no-throw（实现内部自带 try/catch），
//     异常不得穿透到调度器，更不得拖崩宿主；
//   - OnCleanup 必须**幂等**：到时、局结束、切场景、关开关、宿主销毁五条路径共用同一份清理；
//   - 本文件不得引用任何波次状态机符号（tests/RandomEventsWaveIsolationGuard.py 守卫）。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>随机事件稳定 id。数值进日志与 F3 菜单，只增不改、不复用。</summary>
    internal enum RandomEventId
    {
        /// <summary>无事件（空值哨兵）。</summary>
        None = 0,
        /// <summary>E1 空投补给。</summary>
        AirdropSupply = 1,
        /// <summary>E2 血月凶兆。</summary>
        BloodMoon = 2,
        /// <summary>E3 Boss 乱入。</summary>
        BossIntrusion = 3,
        /// <summary>E4 神秘商人路过。</summary>
        WanderingMerchant = 4,
        /// <summary>E5 声东击西。</summary>
        Feint = 5,
        /// <summary>E6 鸭王的烟花。</summary>
        Fireworks = 6,
        /// <summary>E7 金鸭雨。</summary>
        GoldenDuckRain = 7,
        /// <summary>E8 鸭群巡游。</summary>
        DuckParade = 8
    }

    /// <summary>调度器状态机相位。</summary>
    internal enum RandomEventPhase
    {
        /// <summary>不在允许的局内：零成本常驻态。</summary>
        Dormant = 0,
        /// <summary>已就绪，等待触发计时归零。</summary>
        Armed = 1,
        /// <summary>有事件正在运行（并发恒 1）。</summary>
        EventActive = 2,
        /// <summary>事件间冷却。</summary>
        Cooldown = 3
    }

    /// <summary>事件结束原因。进日志，也决定是否播「撤退/收摊」文案。</summary>
    internal enum RandomEventEndReason
    {
        /// <summary>自然到时。</summary>
        Expired = 0,
        /// <summary>局结束（通关/失败/撤离/换局）。</summary>
        RunEnded = 1,
        /// <summary>场景切换。</summary>
        SceneChanged = 2,
        /// <summary>玩家运行期关闭了入口开关。</summary>
        SwitchDisabled = 3,
        /// <summary>宿主销毁。</summary>
        HostDestroyed = 4,
        /// <summary>F3 调试强制结束/强制换事件。</summary>
        DebugForced = 5,
        /// <summary>触发失败的半成品回滚。</summary>
        TriggerFailed = 6
    }

    /// <summary>F3 实机验收查询事件副作用是否真正完成，而不只看 OnTrigger 是否返回 true。</summary>
    internal enum RandomEventValidationOutcome
    {
        Pending = 0,
        Passed = 1,
        Failed = 2
    }

    /// <summary>
    /// 单次事件的运行期上下文。由调度器创建，OnCleanup 之后即丢弃，不做复用。
    /// </summary>
    internal sealed class RandomEventContext
    {
        /// <summary>宿主。协程、桥接方法与播报都经它。</summary>
        internal ModBehaviour Owner;

        /// <summary>创建时的调度器 generation；异步续作回来时必须比对，不等即作废。</summary>
        internal int Generation;

        /// <summary>创建时的 run signature；不等即说明已经换局，异步续作作废。</summary>
        internal int RunSignature;

        /// <summary>创建时的 activeScene.buildIndex；切图后异步续作作废。</summary>
        internal int SceneBuildIndex;

        /// <summary>已运行秒数（scaled deltaTime 累加，带单帧保险帽）。</summary>
        internal float ElapsedSeconds;

        /// <summary>本事件的总时长（秒）。</summary>
        internal float DurationSeconds;

        /// <summary>本事件生成的全部 GameObject / 协程 / 还原动作统一挂这里。</summary>
        internal RuntimeScope Scope;

        /// <summary>事件锚点（播报方向、生成中心）。</summary>
        internal Vector3 AnchorPosition;

        /// <summary>剩余秒数（HUD 用，恒非负）。</summary>
        internal float RemainingSeconds
        {
            get { return Mathf.Max(0f, DurationSeconds - ElapsedSeconds); }
        }

        // 【IsStillValid(director) 已于 2026-09-03 移除】零调用点，且是冗余的第二套判据。
        // 各事件的异步续作走自己的 IsSpawnStillValid（`!_cleanedUp && 场景 buildIndex 相同`），
        // 而 _cleanedUp 已经覆盖了 generation / runSignature 变化：
        // RandomEventDirector.HandleRunSignatureChanged 在自增 _generation 的同一句之后
        // 就调 EndActiveEvent(RunEnded)，进而触发 OnCleanup 把 _cleanedUp 置真。
        // 换句话说"换局"必然先关掉在跑的事件，事件侧不可能观察到 generation 已变而自己还活着。
        // 留两套判据只会让人以为其中一套没接上。
    }

    /// <summary>
    /// 单个随机事件的实现基类。所有覆写必须 no-throw（实现内部自带 try/catch）。
    /// </summary>
    internal abstract class RandomEventBase
    {
        /// <summary>稳定 id。</summary>
        internal abstract RandomEventId Id { get; }

        /// <summary>播报与 HUD 用的显示名，实现里用 L10n.T(中, 英)。</summary>
        internal abstract string DisplayName { get; }

        /// <summary>时长（秒），取自 RandomEventsTuning。</summary>
        internal abstract float DurationSeconds { get; }

        /// <summary>抽取权重，取自 RandomEventsTuning；&lt;= 0 表示本次不入池。</summary>
        internal abstract float Weight { get; }

        /// <summary>
        /// 额外可触发条件（例如 E4 需要商人预设可解析）。默认恒 true。no-throw。
        /// </summary>
        internal virtual bool CanTrigger(RandomEventContext ctx)
        {
            return true;
        }

        /// <summary>
        /// 触发：播报 + 生效。
        /// 返回 false 表示触发失败，调度器会回滚 Scope 并直接回 Cooldown，且**不计入单局上限**。
        /// </summary>
        internal abstract bool OnTrigger(RandomEventContext ctx);

        /// <summary>运行期 tick（scaled deltaTime）。默认空实现；实现内禁止每帧日志与每帧分配。</summary>
        internal virtual void OnTick(RandomEventContext ctx, float deltaTime)
        {
        }

        /// <summary>
        /// Dev 验收探针。同步事件在 OnTrigger 成功后即可视为完成；有异步生成物的事件
        /// 必须覆写并等到生成回调收敛，避免“调度成功、实际空转”被误报为 PASS。
        /// </summary>
        internal virtual RandomEventValidationOutcome GetValidationOutcome(out string metrics)
        {
            metrics = "trigger_completed";
            return RandomEventValidationOutcome.Passed;
        }

        /// <summary>
        /// 清理：必须幂等，五条路径（到时/局结束/切图/关开关/宿主销毁）共用同一份实现。
        /// </summary>
        internal abstract void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason);
    }
}
