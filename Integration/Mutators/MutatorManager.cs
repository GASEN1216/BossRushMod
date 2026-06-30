// ============================================================================
// MutatorManager.cs - 每局变异词条核心管理器
// ============================================================================
// 模块说明：
//   负责词条的随机抽取、应用、清理。
//   提供全局静态修改器供掉落/流血系统读取。
//   提供 ApplyToEnemy 供敌人生成后调用。
//   提供 Boss 回血协程。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 变异词条核心管理器（静态单例模式）
    /// </summary>
    public static class MutatorManager
    {
        // ═══════════════════════════════════════════
        // 当前局状态
        // ═══════════════════════════════════════════

        /// <summary>当前局生效的词条列表</summary>
        private static readonly List<MutatorDefinition> _activeMutators = new List<MutatorDefinition>();

        /// <summary>当前局的运行时上下文</summary>
        private static MutatorContext _currentContext;

        /// <summary>词条系统是否处于激活状态</summary>
        public static bool IsActive { get; private set; }

        // ═══════════════════════════════════════════
        // 全局修改器（供外部系统读取）
        // ═══════════════════════════════════════════

        /// <summary>掉落品质偏移（正数=提升，负数=降低）</summary>
        public static int LootQualityOffset { get; set; }

        /// <summary>掉落数量倍率（默认1）</summary>
        public static int LootQuantityMultiplier { get; set; } = 1;

        /// <summary>掉落类型过滤（null=不过滤，"melee"=只掉近战）</summary>
        public static string LootTypeFilter { get; set; }

        /// <summary>流血速率倍率（默认1.0）</summary>
        public static float BleedRateMultiplier { get; set; } = 1.0f;

        /// <summary>Boss 回血是否启用</summary>
        public static bool BossRegenEnabled { get; set; }

        // Boss 回血计时器
        private static float _bossRegenTimer;
        private const float BossRegenInterval = 10f;
        private const float BossRegenPercent = 0.05f;

        // ═══════════════════════════════════════════
        // 核心方法
        // ═══════════════════════════════════════════

        /// <summary>
        /// 模式启动时调用：随机抽取并应用词条。
        /// 支持传入种子，便于需要确定性随机的模式复用。
        /// </summary>
        /// <param name="player">玩家角色引用</param>
        /// <param name="count">抽取词条数量（默认2）</param>
        /// <param name="seed">随机种子（null=随机）</param>
        /// <param name="modeTag">当前模式标签，用于过滤只在特定模式生效的词条</param>
        public static void RollAndApply(CharacterMainControl player, int count = 2, int? seed = null, string modeTag = null)
        {
            // 先清理上一局残留（防御性）
            RemoveAll();

            if (player == null)
            {
                ModBehaviour.DevLog("[Mutator] RollAndApply: player 为 null，跳过");
                return;
            }

            var rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            // 从词条池随机抽取（不重复），并剔除当前模式无法实际生效的词条
            var pool = new List<MutatorDefinition>();
            for (int i = 0; i < MutatorPool.All.Length; i++)
            {
                MutatorDefinition definition = MutatorPool.All[i];
                if (definition == null) continue;
                if (!definition.AllowsMode(modeTag)) continue;
                pool.Add(definition);
            }

            if (pool.Count == 0)
            {
                ModBehaviour.DevLog("[Mutator] RollAndApply: 当前模式无可用词条，modeTag=" + (modeTag ?? "null"));
                return;
            }

            int actualCount = Mathf.Min(count, pool.Count);

            _activeMutators.Clear();
            for (int i = 0; i < actualCount; i++)
            {
                int idx = rng.Next(pool.Count);
                _activeMutators.Add(pool[idx]);
                pool.RemoveAt(idx);
            }

            // 创建上下文
            _currentContext = new MutatorContext { Player = player };

            // 逐条应用
            for (int i = 0; i < _activeMutators.Count; i++)
            {
                try
                {
                    _activeMutators[i].OnApply?.Invoke(_currentContext);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[Mutator] 应用词条 " + _activeMutators[i].Id + " 失败: " + e.Message);
                }
            }

            IsActive = true;
            _bossRegenTimer = 0f;

            ModBehaviour.DevLog("[Mutator] 已抽取并应用 " + _activeMutators.Count +
                      " 条变异词条，modeTag=" + (string.IsNullOrEmpty(modeTag) ? "Default" : modeTag));
        }

        /// <summary>
        /// 模式结束时调用：清理所有词条效果。
        /// 必须覆盖所有退出路径（正常通关、死亡、手动退出）。
        /// </summary>
        public static void RemoveAll()
        {
            if (!IsActive && _activeMutators.Count == 0)
            {
                return; // 没有需要清理的
            }

            // 1. 集中清理敌人身上的 Modifier（针对仍然存活的敌人）
            if (_currentContext != null)
            {
                for (int i = 0; i < _currentContext.EnemyAppliedModifiers.Count; i++)
                {
                    var pair = _currentContext.EnemyAppliedModifiers[i];
                    if (pair.stat == null || pair.modifier == null) continue;
                    try { pair.stat.RemoveModifier(pair.modifier); } catch  { /* best-effort fallback intentionally ignored */ }
                }
                _currentContext.EnemyAppliedModifiers.Clear();

                // 2. 集中清理玩家身上的 Modifier
                for (int i = 0; i < _currentContext.PlayerAppliedModifiers.Count; i++)
                {
                    var pair = _currentContext.PlayerAppliedModifiers[i];
                    if (pair.stat == null || pair.modifier == null) continue;
                    try { pair.stat.RemoveModifier(pair.modifier); } catch  { /* best-effort fallback intentionally ignored */ }
                }
                _currentContext.PlayerAppliedModifiers.Clear();
            }

            // 3. 调用每个词条自己的 OnRemove（重置静态变量等）
            for (int i = 0; i < _activeMutators.Count; i++)
            {
                try
                {
                    _activeMutators[i].OnRemove?.Invoke(_currentContext);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[Mutator] 清理词条 " + _activeMutators[i].Id + " 失败: " + e.Message);
                }
            }

            _activeMutators.Clear();
            _currentContext = null;

            // 4. 强制重置所有全局修改器（双保险）
            LootQualityOffset = 0;
            LootQuantityMultiplier = 1;
            LootTypeFilter = null;
            BleedRateMultiplier = 1.0f;
            BossRegenEnabled = false;
            _bossRegenTimer = 0f;

            IsActive = false;

            ModBehaviour.DevLog("[Mutator] 所有变异词条已清理");
        }

        /// <summary>
        /// 给新生成的敌人应用所有已启用的 EnemyBuff 类词条效果。
        /// 接入点：在敌人 CharacterMainControl 创建并激活后调用。
        /// </summary>
        public static void ApplyToEnemy(CharacterMainControl enemy)
        {
            if (!IsActive) return;
            if (enemy == null) return;
            if (_currentContext == null) return;
            if (_currentContext.EnemySpawnCallbacks.Count == 0) return;

            for (int i = 0; i < _currentContext.EnemySpawnCallbacks.Count; i++)
            {
                try
                {
                    _currentContext.EnemySpawnCallbacks[i]?.Invoke(enemy);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[Mutator] ApplyToEnemy 回调失败: " + e.Message);
                }
            }
        }

        /// <summary>
        /// 获取当前生效的词条列表（只读，供 UI 展示）
        /// </summary>
        public static IReadOnlyList<MutatorDefinition> GetActiveMutators()
        {
            return _activeMutators;
        }

        // ═══════════════════════════════════════════
        // Boss 回血 Tick
        // ═══════════════════════════════════════════

        /// <summary>
        /// 在 Update 中调用：处理 Boss 回血逻辑。
        /// 仅在 BossRegenEnabled 时生效，每 10 秒回复 5% 最大血量。
        /// </summary>
        /// <param name="deltaTime">Time.deltaTime</param>
        /// <param name="aliveBosses">当前存活的 Boss 列表（可为 null）</param>
        public static void TickBossRegen(float deltaTime, List<MonoBehaviour> aliveBosses)
        {
            if (!BossRegenEnabled) return;
            if (aliveBosses == null || aliveBosses.Count == 0) return;

            _bossRegenTimer += deltaTime;
            if (_bossRegenTimer < BossRegenInterval) return;

            _bossRegenTimer -= BossRegenInterval;

            // 给所有存活 Boss 回血
            for (int i = 0; i < aliveBosses.Count; i++)
            {
                if (aliveBosses[i] == null) continue;
                try
                {
                    CharacterMainControl boss = aliveBosses[i] as CharacterMainControl;
                    if (boss == null || boss.Health == null) continue;
                    if (boss.Health.IsDead) continue;

                    float healAmount = boss.Health.MaxHealth * BossRegenPercent;
                    boss.Health.AddHealth(healAmount);
                }
                catch  { /* best-effort fallback intentionally ignored */ }
            }
        }
    }
}
