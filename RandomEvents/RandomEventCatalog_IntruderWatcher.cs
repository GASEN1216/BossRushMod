// ============================================================================
// RandomEventCatalog_IntruderWatcher.cs — E3 Boss 乱入的逐实例死亡监听
// ============================================================================
// 从 RandomEventCatalog.cs 原样提取（行为逐字不变），只为控制单文件行数预算（LargeFileBudgetGuard）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Duckov.Economy;

namespace BossRush
{
    /// <summary>
    /// 乱入 Boss 的逐实例死亡监听。
    /// 用**命名方法 + 缓存委托实例**订阅官方 UnityEvent，保证 RemoveListener 能精确摘掉；
    /// 回调只置一个标志位，真正的剪枝在事件 Tick 里做，避免在事件派发过程中改动集合。
    /// </summary>
    internal sealed class RandomEventIntruderDeathWatcher
    {
        private readonly CharacterMainControl _boss;
        private Health _health;
        private UnityAction<DamageInfo> _handler;
        private bool _subscribed;
        private bool _dead;

        internal RandomEventIntruderDeathWatcher(CharacterMainControl boss)
        {
            _boss = boss;
        }

        /// <summary>监听目标已死亡或已失效。</summary>
        internal bool IsFinished
        {
            get
            {
                if (_dead || _boss == null)
                {
                    return true;
                }
                try { return _boss.Health == null || _boss.Health.IsDead; }
                catch (Exception) { return true; }
            }
        }

        internal bool Subscribe()
        {
            if (_subscribed || _boss == null)
            {
                return false;
            }

            try
            {
                _health = _boss.Health;
                if (_health == null || _health.OnDeadEvent == null)
                {
                    return false;
                }

                _handler = new UnityAction<DamageInfo>(HandleDead);
                _health.OnDeadEvent.AddListener(_handler);
                _subscribed = true;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 订阅乱入 Boss 死亡失败: " + e.Message);
                _subscribed = false;
                return false;
            }
        }

        /// <summary>成对退订。幂等。</summary>
        internal void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }
            _subscribed = false;

            try
            {
                if (_health != null && _health.OnDeadEvent != null && _handler != null)
                {
                    _health.OnDeadEvent.RemoveListener(_handler);
                }
            }
            catch (Exception) { }

            _handler = null;
            _health = null;
        }

        private void HandleDead(DamageInfo damageInfo)
        {
            _dead = true;
        }
    }
}
