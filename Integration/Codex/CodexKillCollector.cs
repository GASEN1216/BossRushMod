// ============================================================================
// CodexKillCollector.cs - 鸭皇图鉴击杀采集器
// ============================================================================
// 职责：
//   把「玩家亲手击杀 Boss」这件事翻译成图鉴条目的一次累加，并顺带结算本次战斗
//   耗时（最快击杀）。本文件只提供两个**命名静态 handler**，事件的 += / -=
//   由 Utilities/PlayerLifecycleRuntimeHooks.cs 成对挂接，本文件绝不自己订阅
//   （AGENTS.md 4.6：静态事件订阅禁 lambda，且必须成对退订）。
//
// 硬约束：
//   - **热路径零分配零日志**：OnGlobalDead / OnGlobalHurt 会被每一次敌人受伤与
//     死亡触达，这里不做字符串拼接、不 new、不 DevLog（AGENTS.md 4.7 明确要求
//     每帧热路径不加噪声日志）。诊断只暴露 TrackedFightCount 这样的只读计数。
//   - **本文件严禁触碰任何物理落盘 API**：采集只调 CodexPersistence.Store()
//     入队，写盘唯一发生在 CodexSaveCoordinator。采集器在战斗帧里落盘会造成
//     明显卡顿，也会与官方存档流程抢锁。
//   - **丧尸 marker 只在丧尸模式激活时才 GetComponent**（AGENTS.md 4.12）：
//     否则每一次普通击杀都要白付一次组件查找。
//   - 官方派发顺序（Health.cs:428-459）：致命一击先 OnDead 后 OnHurt，且
//     isDead 已置位。因此 OnGlobalHurt 里的 !IsDead 早返是**必须**的，
//     否则会给已死目标重新开计时，造成计时表泄漏。
//   - 全部状态都提供成对清理：ClearRunScoped（切场景）/ NotifySlotChanged
//     （切档删档）/ ResetStaticCaches（Mod 卸载、宿主重建）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>图鉴击杀采集器。静态，无 MonoBehaviour。热路径零分配零日志。</summary>
    internal static class CodexKillCollector
    {
        #region 状态

        private static readonly object _lock = new object();

        /// <summary>Health.GetInstanceID() -&gt; 玩家首次造成伤害的 Time.time。</summary>
        private static readonly Dictionary<int, float> _fightStart =
            new Dictionary<int, float>(CodexTuning.MaxFightStartTracked);

        /// <summary>
        /// 已计入的 CharacterMainControl.GetInstanceID()。存 int 而不是对象引用：
        /// 存引用会把已销毁的角色钉在内存里，也会让 HashSet 的相等比较走 Unity
        /// 的重载 ==（对已销毁对象有额外开销）。
        /// </summary>
        private static readonly HashSet<int> _countedInstances = new HashSet<int>();

        /// <summary>去重集合的软上限。同一场景里死过的角色不会再死第二次，
        /// 因此满了直接整表清空是安全的，只是防止极长局内的无界增长。</summary>
        private const int MaxCountedInstances = 512;

        #endregion

        #region 只读诊断

        /// <summary>诊断用：当前计时表条目数。</summary>
        internal static int TrackedFightCount
        {
            get
            {
                lock (_lock)
                {
                    return _fightStart.Count;
                }
            }
        }

        #endregion

        #region 全局事件 handler（热路径）

        /// <summary>
        /// 全局死亡事件。由 PlayerLifecycleRuntimeHooks 转发（命名 handler）。
        /// 过滤序是刻意排布的：越便宜越靠前，最贵的组件查找压到最后。
        /// </summary>
        internal static void OnGlobalDead(Health target, DamageInfo info)
        {
            // 1) 开关早返：两次 bool + 一次 no-throw getter，关闭时几乎零成本
            if (!IsActive()) return;

            try
            {
                // 2) 空目标
                if (target == null) return;

                // 2.5) 排 Mode H（owner 2026-09-03 定：观战模式的击杀不计入图鉴）。
                //      必须在 fromCharacter 判定之前：ERROR 完整互换期间官方会把
                //      fromCharacter 改写成主角（见 ModeHCombatControl 的互换注释与
                //      ModeHEventRouter.SetErrorSwapControlledParticipant），
                //      否则那一次击杀会伪装成"玩家亲手击杀"混进鸭皇图鉴。
                if (ModBehaviour.IsModeHRunInProgressSafe()) return;

                // 3) 排玩家死亡（玩家自己倒下不进图鉴）
                if (target.IsMainCharacterHealth) return;

                // 4) 只记主角亲手击杀：随从、雇佣兵、环境伤害都不算
                if (info.fromCharacter == null || !info.fromCharacter.IsMainCharacter) return;

                // 5) 排遗种巢随从（自家崽被误伤致死不该进图鉴）
                if (PetNestCompanionAgent.IsCompanionHealth(target)) return;

                CharacterMainControl victim = target.TryGetCharacter();
                if (victim == null) return;

                // 6) 排友军：宠物、雇佣兵、临时同伴都是 Teams.player
                if (victim.Team == Teams.player) return;

                // 7) 排基地场景：基地里的靶子/演示角色不该计入战绩
                if (IsBaseLevelSafe()) return;

                // 8) 身份归属（内含 Boss 判定）。非 Boss 一律返回 null，
                //    因此杂兵不会污染去重集合。
                string key = ResolveBossKey(victim);
                if (string.IsNullOrEmpty(key)) return;

                // 9) 实例去重：同一个角色实例只计一次
                int victimId = victim.GetInstanceID();
                lock (_lock)
                {
                    if (_countedInstances.Count >= MaxCountedInstances)
                    {
                        _countedInstances.Clear();
                    }
                    if (!_countedInstances.Add(victimId)) return;
                }

                // 10) 结算战斗耗时。计时表未命中给 -1f，表示"本次没有有效计时"
                float seconds = -1f;
                int healthId = target.GetInstanceID();
                lock (_lock)
                {
                    float start;
                    if (_fightStart.TryGetValue(healthId, out start))
                    {
                        _fightStart.Remove(healthId);
                        float elapsed = Time.time - start;
                        if (elapsed > 0f) seconds = elapsed;
                    }
                }

                RecordKill(key, seconds);
            }
            catch (Exception)
            {
                // 热路径静默：采集失败绝不能拖崩宿主的死亡派发链
            }
        }

        /// <summary>
        /// 全局受伤事件。只用于最快击杀计时的开表，不做任何写入。
        /// </summary>
        internal static void OnGlobalHurt(Health target, DamageInfo info)
        {
            if (!IsActive()) return;

            try
            {
                // 致命一击的 OnHurt 在 OnDead **之后**派发且 isDead 已置位，
                // 这条早返把它挡在门外，否则会给死人重新开计时造成表泄漏。
                if (target == null || target.IsDead) return;
                // Mode H 的击杀不进图鉴，配套的最快击杀计时也不能开表——
                // 否则计时条目只进不出，会白占 MaxFightStartTracked 的额度。
                if (ModBehaviour.IsModeHRunInProgressSafe()) return;
                if (info.fromCharacter == null || !info.fromCharacter.IsMainCharacter) return;
                if (target.IsMainCharacterHealth) return;
                if (info.finalDamage <= 0f) return;

                int id = target.GetInstanceID();
                lock (_lock)
                {
                    // 只记玩家造成的**首次**伤害，后续伤害不刷新起点
                    if (_fightStart.ContainsKey(id)) return;
                    if (_fightStart.Count >= CodexTuning.MaxFightStartTracked)
                    {
                        // 满表整清而不是逐条淘汰：这里不能付排序/遍历的代价
                        _fightStart.Clear();
                    }
                    _fightStart[id] = Time.time;
                }
            }
            catch (Exception)
            {
                // 热路径静默
            }
        }

        #endregion

        #region 清理入口

        /// <summary>
        /// 清 run 级状态：实例去重集与最快击杀计时表。
        /// 切场景时调用（角色实例全部作废，继续留着只是内存垃圾）。
        /// </summary>
        internal static void ClearRunScoped()
        {
            try
            {
                lock (_lock)
                {
                    _fightStart.Clear();
                    _countedInstances.Clear();
                }
            }
            catch (Exception)
            {
                // 清理失败不得抛出：调用点在场景回调里
            }
        }

        /// <summary>切场景：清实例去重集与计时表。</summary>
        internal static void NotifySceneChanged()
        {
            ClearRunScoped();
        }

        /// <summary>切档 / 删档：同 NotifySceneChanged，并丢弃本地待写标记。</summary>
        internal static void NotifySlotChanged()
        {
            ClearRunScoped();
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            ClearRunScoped();
        }

        #endregion

        #region 私有

        /// <summary>入口总开关门控。O(1)、零分配、no-throw。</summary>
        private static bool IsActive()
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                return owner != null && owner.IsCodexConfiguredEnabled();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 身份归属。返回 null 表示"不是应当入图鉴的 Boss"。
        ///
        /// 支路（丧尸）先于主路：丧尸模式的 Boss 是运行时改造出来的，
        /// 它的 characterPreset 可能仍是杂兵 preset，只能认 marker。
        /// </summary>
        private static string ResolveBossKey(CharacterMainControl victim)
        {
            // 支路：只在丧尸模式激活时才付 GetComponent 的代价（AGENTS.md 4.12）
            ModBehaviour owner = ModBehaviour.Instance;
            if (owner != null && owner.IsZombieModeActive)
            {
                ZombieModeEnemyRuntimeMarker marker = victim.GetComponent<ZombieModeEnemyRuntimeMarker>();
                if (marker != null)
                {
                    // 带 marker 的一律由 marker 定性：不是 Boss 就是丧尸杂兵，直接出局
                    return marker.IsBoss ? CodexBossCatalog.BuildZombieBossKey(marker.BossKind) : null;
                }
            }

            // 主路：官方/自定义 preset 的 nameKey。三个自定义 Boss 在所有生成路径
            // 上都会被盖成 canonical key，因此这里不需要额外分支。
            if (!victim.isBossCharacter) return null;

            CharacterRandomPreset preset = victim.characterPreset;
            return preset != null ? preset.nameKey : null;
        }

        /// <summary>当前模式 id。只经公开门面读取，全程 no-throw。</summary>
        private static string ResolveCurrentModeId()
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner == null) return CodexTuning.ModeIdRaid;

                if (owner.IsZombieModeActive) return CodexTuning.ModeIdZombie;
                if (ModBehaviour.IsModeGRunInProgressSafe()) return CodexTuning.ModeIdModeG;
                if (ModBehaviour.IsModeHRunInProgressSafe()) return CodexTuning.ModeIdModeH;
                if (owner.IsModeDActive) return CodexTuning.ModeIdModeD;
                if (owner.IsModeEActive) return CodexTuning.ModeIdModeE;
                if (owner.IsModeFActive) return CodexTuning.ModeIdModeF;
                if (owner.IsActive || owner.IsBossRushArenaActive)
                {
                    return owner.IsCodexInfiniteHellActive()
                        ? CodexTuning.ModeIdHell
                        : CodexTuning.ModeIdArena;
                }
                return CodexTuning.ModeIdRaid;
            }
            catch (Exception)
            {
                return CodexTuning.ModeIdRaid;
            }
        }

        /// <summary>
        /// 写入一次击杀。**只入队，不落盘**——物理写盘唯一发生在
        /// CodexSaveCoordinator.FlushBatch。
        /// fightSeconds &lt;= 0 表示本次没有有效计时，不参与最快击杀。
        /// </summary>
        private static void RecordKill(string key, float fightSeconds)
        {
            CodexData data = CodexPersistence.Current;
            if (data == null) return;

            CodexEntry entry = data.GetOrCreate(key, CodexBossCatalog.ResolveDisplayName(key));
            // 达到条目上限：fail-closed，不新增条目（已有条目照常累计）
            if (entry == null) return;

            bool firstUnlock = entry.Kills <= 0;
            entry.Kills++;
            if (firstUnlock)
            {
                entry.FirstKillTicks = DateTime.UtcNow.Ticks;
                entry.FirstMode = ResolveCurrentModeId();
            }

            if (fightSeconds > 0f
                && (entry.FastestKillSeconds <= 0f || fightSeconds < entry.FastestKillSeconds))
            {
                entry.FastestKillSeconds = fightSeconds;
            }

            // 入队成功后必须显式请求一次落盘，否则协调器的 deferred 重试永远不会被点着：
            // _deferredFlushPending 只在 FlushBatch 里置位，而 Tick() 在它为 false 时 O(1) 早返，
            // 少了这一行整条「回基地补写」链路都是死代码，图鉴只能靠官方存盘顺带带走。
            // 形态与 DailyReportService.Persist / PetNestService 完全一致。
            // 击杀必然发生在非基地（过滤序第 7 步已排掉基地），因此这里只会登记 deferred，
            // 不会在交火帧上真的 SaveFile。
            if (CodexPersistence.Store(data))
            {
                CodexSaveCoordinator.RequestFlush();
            }

            CodexMilestones.Evaluate(data, fightSeconds);
        }

        /// <summary>是否在基地场景。no-throw。</summary>
        private static bool IsBaseLevelSafe()
        {
            try
            {
                return LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion
    }
}
