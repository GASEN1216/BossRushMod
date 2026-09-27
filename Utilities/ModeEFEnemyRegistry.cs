using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    // E/F share tracking and subscription ownership. Gameplay callbacks stay in their mode.
    internal sealed class ModeEFEnemyRegistry
    {
        private Action<CharacterMainControl, DamageInfo> OnModeEEnemyDeath;

        internal void BindDeathCallback(Action<CharacterMainControl, DamageInfo> onDeath)
        {
            OnModeEEnemyDeath = onDeath;
        }

        internal void AttachLootHandler(CharacterMainControl enemy, Action<DamageInfo> handler)
        {
            modeEEnemyLootHandlers[enemy] = handler;
            enemy.BeforeCharacterSpawnLootOnDead += handler;
        }

        // The old public List API retains the same collection identity.
        internal List<CharacterMainControl> LegacyAliveEnemies { get { return modeEAliveEnemies; } }
        internal bool IsTracked(CharacterMainControl enemy) { return modeEAliveEnemySet.Contains(enemy); }
        internal bool TryGetTrackedFaction(CharacterMainControl enemy, out Teams faction)
        { return modeEAliveEnemyFactionMap.TryGetValue(enemy, out faction); }
        internal void RemoveNullAliveSlotAt(int index) { modeEAliveEnemies.RemoveAt(index); }
        internal void RemoveFactionSlotAt(Teams faction, int index) { modeEFactionAliveMap[faction].RemoveAt(index); }
        internal void ClearAliveEnemies() { modeEAliveEnemies.Clear(); }
        internal void ClearAliveEnemySet() { modeEAliveEnemySet.Clear(); }
        internal void ClearFactionLookup() { modeEAliveEnemyFactionMap.Clear(); }
        internal void ClearFactionAliveLists() { modeEFactionAliveMap.Clear(); }
        internal void ClearDeathHandlers() { modeEEnemyDeathHandlers.Clear(); }
        internal void ClearLootHandlers() { modeEEnemyLootHandlers.Clear(); }

        /// <summary>当前所有存活的 Mode E 敌人（跨阵营）</summary>
        private readonly List<CharacterMainControl> modeEAliveEnemies = new List<CharacterMainControl>();

        /// <summary>Mode E 存活敌人的去重集合，避免重复注册导致列表和扫描路径膨胀。</summary>
        private readonly HashSet<CharacterMainControl> modeEAliveEnemySet = new HashSet<CharacterMainControl>();

        /// <summary>BossRegen 专用缓存，避免变异词条开启时每帧重建存活列表。</summary>
        private readonly List<MonoBehaviour> modeEBossRegenCache = new List<MonoBehaviour>(32);

        private bool modeEBossRegenCacheDirty = true;

        /// <summary>Mode E 存活敌人的阵营缓存，避免清理路径回退到全阵营扫描。</summary>
        private readonly Dictionary<CharacterMainControl, Teams> modeEAliveEnemyFactionMap
            = new Dictionary<CharacterMainControl, Teams>();

        /// <summary>
        /// [P4性能优化] 按阵营维护的独立存活敌人列表，避免缩放时全量遍历 modeEAliveEnemies
        /// Key = 阵营, Value = 该阵营的存活敌人列表
        /// </summary>
        private readonly Dictionary<Teams, List<CharacterMainControl>> modeEFactionAliveMap
            = new Dictionary<Teams, List<CharacterMainControl>>();

        /// <summary>缓存死亡事件句柄，避免对象复用或重复注册导致 UnityEvent 持续膨胀。</summary>
        private readonly Dictionary<CharacterMainControl, UnityAction<DamageInfo>> modeEEnemyDeathHandlers
            = new Dictionary<CharacterMainControl, UnityAction<DamageInfo>>();

        /// <summary>缓存掉落拦截句柄，确保 Mode E 结束或对象复用时可以对称取消订阅。</summary>
        private readonly Dictionary<CharacterMainControl, Action<DamageInfo>> modeEEnemyLootHandlers
            = new Dictionary<CharacterMainControl, Action<DamageInfo>>();

        internal void MarkModeEBossRegenCacheDirty()
        {
            modeEBossRegenCacheDirty = true;
        }

        internal void ClearModeEBossRegenCache()
        {
            modeEBossRegenCache.Clear();
            modeEBossRegenCacheDirty = false;
        }

        internal List<MonoBehaviour> GetModeEBossRegenCache()
        {
            if (!modeEBossRegenCacheDirty)
            {
                return modeEBossRegenCache;
            }

            modeEBossRegenCache.Clear();
            for (int i = 0; i < modeEAliveEnemies.Count; i++)
            {
                CharacterMainControl boss = modeEAliveEnemies[i];
                if (boss != null)
                {
                    modeEBossRegenCache.Add(boss);
                }
            }

            modeEBossRegenCacheDirty = false;
            return modeEBossRegenCache;
        }

        internal void AddToFactionAliveList(Teams faction, CharacterMainControl enemy)
        {
            List<CharacterMainControl> list;
            if (!modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                list = new List<CharacterMainControl>(8);
                modeEFactionAliveMap[faction] = list;
            }

            list.Add(enemy);
        }

        internal void RemoveFromFactionAliveList(Teams faction, CharacterMainControl enemy)
        {
            List<CharacterMainControl> list;
            if (modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (object.ReferenceEquals(list[i], enemy))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        internal void TrackModeEAliveEnemy(CharacterMainControl enemy, Teams faction)
        {
            if (enemy == null)
            {
                return;
            }

            if (!modeEAliveEnemySet.Add(enemy))
            {
                return;
            }

            modeEAliveEnemies.Add(enemy);
            modeEAliveEnemyFactionMap[enemy] = faction;
            AddToFactionAliveList(faction, enemy);
            MarkModeEBossRegenCacheDirty();
        }

        internal void UntrackModeEAliveEnemy(CharacterMainControl enemy, Teams? faction = null)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            bool removedFromSet = modeEAliveEnemySet.Remove(enemy);
            bool removedFromList = modeEAliveEnemies.Remove(enemy);
            if (removedFromSet || removedFromList)
            {
                MarkModeEBossRegenCacheDirty();
            }

            Teams trackedFaction;
            if (!faction.HasValue && modeEAliveEnemyFactionMap.TryGetValue(enemy, out trackedFaction))
            {
                faction = trackedFaction;
            }

            modeEAliveEnemyFactionMap.Remove(enemy);

            if (faction.HasValue)
            {
                RemoveFromFactionAliveList(faction.Value, enemy);
                return;
            }

            foreach (KeyValuePair<Teams, List<CharacterMainControl>> kvp in modeEFactionAliveMap)
            {
                RemoveFromFactionAliveList(kvp.Key, enemy);
            }
        }

        internal List<CharacterMainControl> GetFactionAliveList(Teams faction)
        {
            List<CharacterMainControl> list;
            if (modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                return list;
            }
            return null;
        }

        internal void RegisterModeEEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                if (enemy == null)
                {
                    return;
                }

                UnregisterModeEEnemyDeath(enemy);

                Health health = enemy.GetComponent<Health>();
                if (health != null)
                {
                    CharacterMainControl capturedEnemy = enemy;
                    UnityAction<DamageInfo> handler = null;
                    handler = (dmgInfo) =>
                    {
                        UnregisterModeEEnemyDeath(capturedEnemy);
                        OnModeEEnemyDeath(capturedEnemy, dmgInfo);
                    };
                    modeEEnemyDeathHandlers[enemy] = handler;
                    health.OnDeadEvent.AddListener(handler);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] RegisterModeEEnemyDeath 失败: " + e.Message);
            }
        }

        internal void UnregisterModeEEnemyDeath(CharacterMainControl enemy)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            UnityAction<DamageInfo> handler;
            if (!modeEEnemyDeathHandlers.TryGetValue(enemy, out handler))
            {
                return;
            }

            try
            {
                if (!(enemy == null))
                {
                    Health health = enemy.GetComponent<Health>();
                    if (health != null)
                    {
                        health.OnDeadEvent.RemoveListener(handler);
                    }
                }
            }
            catch { }

            modeEEnemyDeathHandlers.Remove(enemy);
        }

        internal void UnregisterModeEEnemyLootHandler(CharacterMainControl enemy)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            Action<DamageInfo> handler;
            if (!modeEEnemyLootHandlers.TryGetValue(enemy, out handler))
            {
                return;
            }

            try
            {
                if (!(enemy == null))
                {
                    enemy.BeforeCharacterSpawnLootOnDead -= handler;
                }
            }
            catch { }

            modeEEnemyLootHandlers.Remove(enemy);
        }
    }
}
