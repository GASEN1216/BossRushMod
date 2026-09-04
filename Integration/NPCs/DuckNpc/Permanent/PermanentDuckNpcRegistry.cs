// ============================================================================
// PermanentDuckNpcRegistry.cs - 永久捏脸 NPC 的蓝图与实例登记
// ============================================================================
// 模块说明：
//   两个职责：
//     1. 从 DuckNpcRegistry 里筛出 isPermanent 的蓝图，供模块层和好感度配置查询；
//     2. 维护「npcId → 当前场上实例」的表，供**婚姻系统反查**。
//
//   第 2 条是本文件存在的主要理由。婚姻系统对可婚 NPC 有 6 处
//   `if (npcId == 叮当) ... else if (羽织) ...` 硬编码：
//     WeddingModBehaviourBridge  教堂点生成 / 取实例 / 设站桩 / 跟随准备
//     NPCMarriageSystem          结婚后移走
//     WeddingBuildingInjector    教堂被拆时清理
//   照那个模式走，每加一只永久 NPC 就要改 6 个文件。
//
//   本注册表让那 6 处各加**一条**泛化分支就能接住**所有**捏脸永久 NPC，
//   此后新增 NPC 零改动。不动现有叮当/羽织分支。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 永久捏脸 NPC 登记表。
    /// </summary>
    internal static class PermanentDuckNpcRegistry
    {
        private const string LogPrefix = "[PermanentDuckNpc]";

        /// <summary>npcId → 当前场上实例。只有活着的才在表里。</summary>
        private static readonly Dictionary<string, CharacterMainControl> _instances =
            new Dictionary<string, CharacterMainControl>(StringComparer.Ordinal);

        // ====================================================================
        // 蓝图查询
        // ====================================================================

        /// <summary>该 npcId 是不是一只永久捏脸 NPC。</summary>
        /// <remarks>
        /// 婚姻系统的泛化分支判据。必须无副作用、可在任意时机调用。
        /// </remarks>
        internal static bool IsPermanentDuckNpc(string npcId)
        {
            DuckNpcBlueprint blueprint;
            return TryGetBlueprint(npcId, out blueprint);
        }

        /// <summary>按 npcId 取永久蓝图。非永久蓝图不会被返回。</summary>
        internal static bool TryGetBlueprint(string npcId, out DuckNpcBlueprint blueprint)
        {
            blueprint = null;
            if (string.IsNullOrEmpty(npcId))
            {
                return false;
            }

            try
            {
                DuckNpcBlueprint candidate;
                if (!DuckNpcRegistry.TryGet(npcId, out candidate) || candidate == null)
                {
                    return false;
                }
                if (!candidate.isPermanent || candidate.permanent == null)
                {
                    return false;
                }
                blueprint = candidate;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 查永久蓝图失败 " + npcId + ": " + e.Message);
                return false;
            }
        }

        /// <summary>全部永久蓝图。</summary>
        internal static List<DuckNpcBlueprint> GetAllPermanent()
        {
            List<DuckNpcBlueprint> result = new List<DuckNpcBlueprint>();
            try
            {
                IList<DuckNpcBlueprint> all = DuckNpcRegistry.All;
                for (int i = 0; i < all.Count; i++)
                {
                    DuckNpcBlueprint blueprint = all[i];
                    if (blueprint != null && blueprint.isPermanent && blueprint.permanent != null)
                    {
                        result.Add(blueprint);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 枚举永久蓝图失败: " + e.Message);
            }
            return result;
        }

        // ====================================================================
        // 实例登记
        // ====================================================================

        /// <summary>登记一只已生成的永久 NPC。</summary>
        internal static void RegisterInstance(string npcId, CharacterMainControl npc)
        {
            if (string.IsNullOrEmpty(npcId) || npc == null)
            {
                return;
            }
            _instances[npcId] = npc;
        }

        /// <summary>注销登记。销毁路径必须调，否则表里会留 destroyed 引用。</summary>
        internal static void UnregisterInstance(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return;
            }
            _instances.Remove(npcId);
        }

        /// <summary>
        /// 取当前场上实例。已被销毁的会顺手清出表并返回 null。
        /// </summary>
        /// <remarks>
        /// Unity 的"假 null"：被 Destroy 的对象 != null 为 false，但引用还在字典里。
        /// 这里用 `npc == null` 走 Unity 的重载判定，能正确识别已销毁对象。
        /// </remarks>
        internal static CharacterMainControl GetInstance(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return null;
            }

            CharacterMainControl npc;
            if (!_instances.TryGetValue(npcId, out npc))
            {
                return null;
            }

            if (npc == null)
            {
                _instances.Remove(npcId);
                return null;
            }

            return npc;
        }

        /// <summary>当前登记在场的永久 NPC 数量（会顺手剔除已销毁的）。</summary>
        internal static int AliveCount
        {
            get
            {
                List<string> dead = null;
                foreach (KeyValuePair<string, CharacterMainControl> pair in _instances)
                {
                    if (pair.Value == null)
                    {
                        if (dead == null)
                        {
                            dead = new List<string>();
                        }
                        dead.Add(pair.Key);
                    }
                }

                if (dead != null)
                {
                    for (int i = 0; i < dead.Count; i++)
                    {
                        _instances.Remove(dead[i]);
                    }
                }

                return _instances.Count;
            }
        }

        /// <summary>
        /// 清空实例表。Mod 卸载时由 AlwaysOnRuntimeHooks 调用 ——
        /// 表里握着 CharacterMainControl 引用，不清会把旧程序集的引用留到下次加载。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            _instances.Clear();
        }
    }
}
