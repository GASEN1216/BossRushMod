using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 额外死亡掉落抑制身份表（设计提案 §19.5、§25.2）。
    ///
    /// 契约：
    /// - 命中时只让 Patches/Combat/CharacterOnDeadPatch 的 Prefix 跳过本 Mod 的两个额外掉落
    ///   handler（霜之哀伤、幽灵女巫镰刀），**不得**返回 false、不得跳过或改写原版 OnDead、
    ///   Health.OnDead 以及 Mode H 自己的死亡遥测；
    /// - clone preset 必须在 CreateCharacterAsync 调用**之前**登记，角色引用在创建返回后补登记；
    /// - 解除顺序固定为“角色引用 -> preset 引用 -> 销毁 clone”；
    /// - 查询是 O(1) 引用身份比较，未激活时零分配快路径；异常保持 fail-open 并限频告警。
    /// </summary>
    public static class ModeHDeathSuppressionRegistry
    {
        #region 状态

        private static readonly object _lock = new object();
        private static readonly HashSet<int> _presetIds = new HashSet<int>();
        private static readonly HashSet<int> _healthIds = new HashSet<int>();
        private static readonly HashSet<int> _characterIds = new HashSet<int>();
        private static int _activeCount;

        #endregion

        #region 快路径

        /// <summary>
        /// 抑制表是否激活。零分配 bool 快路径：Mode H 未生成任何角色时恒 false，
        /// 死亡热路径直接返回，不建集合、不分配。
        /// </summary>
        public static bool IsSuppressionArmed
        {
            get
            {
                try { return _activeCount > 0; }
                catch (Exception) { return false; }
            }
        }

        #endregion

        #region 登记

        /// <summary>创建调用之前登记 clone preset。</summary>
        public static void RegisterPreset(CharacterRandomPreset preset)
        {
            if (preset == null) return;
            lock (_lock)
            {
                if (_presetIds.Add(preset.GetInstanceID()))
                {
                    _activeCount++;
                }
            }
        }

        /// <summary>创建返回后补登记角色与 Health 引用。</summary>
        public static void RegisterCharacter(Health health, CharacterMainControl character)
        {
            lock (_lock)
            {
                if (health != null) _healthIds.Add(health.GetInstanceID());
                if (character != null) _characterIds.Add(character.GetInstanceID());
            }
        }

        /// <summary>解除角色引用登记（回收第一步）。</summary>
        public static void UnregisterCharacter(Health health)
        {
            if (health == null) return;
            lock (_lock)
            {
                _healthIds.Remove(health.GetInstanceID());
                try
                {
                    CharacterMainControl character = health.TryGetCharacter();
                    if (character != null) _characterIds.Remove(character.GetInstanceID());
                }
                catch (Exception)
                {
                    // 角色引用解析失败时只移除 Health 登记
                }
            }
        }

        /// <summary>解除 preset 登记（回收第二步，随后才销毁 clone）。</summary>
        public static void UnregisterPreset(CharacterRandomPreset preset)
        {
            if (preset == null) return;
            lock (_lock)
            {
                if (_presetIds.Remove(preset.GetInstanceID()) && _activeCount > 0)
                {
                    _activeCount--;
                }
            }
        }

        #endregion

        #region 查询

        /// <summary>
        /// 该 Health 对应的死亡是否属于 Mode H 临时角色。
        /// 依次用 Health 引用、角色引用和 staging preset 引用做 O(1) 身份比较。
        /// </summary>
        public static bool IsModeHOnDeadSuppressionActive(Health deadHealth)
        {
            if (deadHealth == null) return false;
            try
            {
                if (_activeCount <= 0) return false;
                lock (_lock)
                {
                    if (_healthIds.Contains(deadHealth.GetInstanceID())) return true;

                    CharacterMainControl character = deadHealth.TryGetCharacter();
                    if (character != null)
                    {
                        if (_characterIds.Contains(character.GetInstanceID())) return true;
                        CharacterRandomPreset preset = character.characterPreset;
                        if (preset != null && _presetIds.Contains(preset.GetInstanceID())) return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                // fail-open：抑制表故障不得拖崩宿主死亡流程
                return false;
            }
        }

        #endregion

        #region 清理

        /// <summary>清空全部登记（run 结束、技术中止、Mod 卸载）。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _presetIds.Clear();
                _healthIds.Clear();
                _characterIds.Clear();
                _activeCount = 0;
            }
        }

        #endregion
    }
}
