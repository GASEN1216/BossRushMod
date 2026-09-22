using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Quests;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 官方给予者（基地的 Jeff 等）的唯一查找口：全仓库只有这里一处 <c>FindObjectsOfType&lt;QuestGiver&gt;</c>。
    /// 按场景代数缓存一次扫描的全部命中，天空岛序章与鸭王征程共享同一次扫描、同一份有界预算；
    /// 调用方自己再做有界重试也无妨（序章保留原来的 12 次 / 1 秒）。
    /// 头顶标记刷新走官方私有 <c>RefreshInspectionIndicator</c>（官方只在任务列表变化时自己刷，我们的事实变化不经那些事件）。
    /// </summary>
    internal static class OfficialQuestGiverLocator
    {
        private const int ScanBudgetPerScene = 12;
        private const float ScanInterval = 1f;

        private static readonly MethodInfo RefreshIndicatorMethod = AccessTools.Method(typeof(QuestGiver), "RefreshInspectionIndicator");
        private static readonly Dictionary<int, QuestGiver> found = new Dictionary<int, QuestGiver>();
        private static int generation, cachedGeneration = -1, scans;
        private static float nextScan;

        /// <summary>场景切换：缓存作废、预算重置。</summary>
        internal static void NotifySceneChanged()
        {
            generation++;
        }

        /// <summary>
        /// 找一位官方给予者。命中缓存直接返回；否则在预算内扫描一次并缓存全部命中。
        /// 预算耗尽或本秒已扫过时返回 false，由调用方按自己的节奏重试。
        /// </summary>
        internal static bool TryFind(QuestGiverID id, out QuestGiver giver)
        {
            giver = null;
            if (cachedGeneration != generation)
            {
                cachedGeneration = generation;
                found.Clear();
                scans = 0;
                nextScan = 0f;
            }
            QuestGiver cached;
            if (found.TryGetValue((int)id, out cached))
            {
                if (cached != null) { giver = cached; return true; }
                found.Remove((int)id);
            }
            if (scans >= ScanBudgetPerScene || Time.unscaledTime < nextScan) return false;
            scans++;
            nextScan = Time.unscaledTime + ScanInterval;
            QuestGiver[] candidates;
            try { candidates = UnityEngine.Object.FindObjectsOfType<QuestGiver>(true); }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OfficialQuest] [WARNING] 扫描官方给予者失败: " + e.Message);
                return false;
            }
            for (int i = 0; i < candidates.Length; i++)
            {
                QuestGiver candidate = candidates[i];
                if (candidate == null) continue;
                // 按官方公开枚举识别，不猜名字或层级；同一 id 多个实例时保留先扫到的那个。
                int key = (int)candidate.ID;
                if (!found.ContainsKey(key)) found[key] = candidate;
            }
            return found.TryGetValue((int)id, out giver) && giver != null;
        }

        /// <summary>让一位给予者重算头顶标记（反射官方私有方法，失败只记日志）。</summary>
        internal static void RefreshMarker(QuestGiver giver)
        {
            if (giver == null || RefreshIndicatorMethod == null) return;
            try { RefreshIndicatorMethod.Invoke(giver, null); }
            catch (Exception e) { ModBehaviour.DevLog("[OfficialQuest] [WARNING] 任务标记刷新失败: " + e.Message); }
        }

        /// <summary>按给予者整数刷标记（客户端目录不引用官方类型时用）。找不到返回 false。</summary>
        internal static bool RefreshMarkerFor(int giverId)
        {
            QuestGiver giver;
            if (!TryFind((QuestGiverID)giverId, out giver)) return false;
            RefreshMarker(giver);
            return true;
        }

        internal static void ResetStaticCaches()
        {
            found.Clear();
            cachedGeneration = -1;
            generation = 0;
            scans = 0;
            nextScan = 0f;
        }
    }
}
