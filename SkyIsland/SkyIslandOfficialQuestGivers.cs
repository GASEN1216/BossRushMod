using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush.Utils;
using Duckov.Quests;
using Duckov.Quests.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 岛上的官方任务给予者：给发任务的居民（苇白 / 浮舟 / 钟守）身上挂一个官方 <see cref="QuestGiver"/>，
    /// 与「聊聊航路」同在一个交互组里；居民不在岛上的那一趟（婚后不上岛、生成失败）改挂到对应装置
    /// （委托板 / 渡口工台 / 钟庭装置）上兜底。官方任务系统的符号集中在本文件与桥，居民与剧情 owner 不碰。
    ///
    /// 给予者 id 用官方 enum 之外的整数（5901–5903）：官方 UI 不显示给予者名字、
    /// <c>Quest.Compare</c> 只做整数减法、<c>GetAllQuestsByQuestGiverID</c> 只做相等比较，
    /// 而 <c>Quest.SaveData.questGiverID</c> 随整条记录被桥从官方快照剥掉，卸载后不留野枚举值。
    ///
    /// 时序：<c>NPCInteractionGroupHelper.AddSubInteractable</c> 先把子物体 SetActive(false) 再 setup 再激活，
    /// 所以 <c>questGiverID</c> 在官方 Awake 之前写好，Awake 挂出来的头顶标记绑的才是正确的给予者。
    /// 官方 <c>QuestGiverView</c> 不在场时不挂（fail-closed）：那会变成一个点了没反应的选项，而自绘面板路径不受影响。
    /// </summary>
    internal static class SkyIslandOfficialQuestGivers
    {
        private const string ChildName = "IslandQuestGiver";
        private const string InteractNameKey = "BossRush_SkyIsland_QuestGiver";
        private const int FallbackAttemptLimit = 40;

        private static readonly FieldInfo GiverIdField = AccessTools.Field(typeof(QuestGiver), "questGiverID");
        private static readonly List<QuestGiver> attached = new List<QuestGiver>();
        private static readonly HashSet<int> fallbackDone = new HashSet<int>();
        private static int fallbackAttempts;
        private static bool viewMissingLogged, fieldMissingLogged;

        /// <summary>居民生成落地时由 <c>SkyIslandResidents.AttachStoryInteraction</c> 调一次；不发任务的居民直接返回。</summary>
        internal static void AttachResident(Transform parent, List<InteractableBase> group, string residentId)
        {
            int giverId = SkyIslandOfficialQuestTable.GiverIdOfResident(residentId);
            if (giverId == 0) return;
            Attach(parent, group, giverId);
        }

        /// <summary>
        /// 居民缺席那一趟的装置兜底。只在居民 owner 已经把整队生成完之后判断，避免居民还没落地就把兜底挂到装置上；
        /// 有界重试，装置不在或官方任务页不在场就放弃这一趟。
        /// </summary>
        internal static void EnsureDeviceFallback(SkyIslandSession session)
        {
            IList<SkyIslandOfficialQuestDefinition> island = SkyIslandOfficialQuestTable.Island;
            if (session == null || !session.ResidentsSettled) return;
            // 婚礼 / 送配偶回家会连同子给予者销毁 NPC。成功记录只在实例仍存活时有效；
            // 必须先检查失效，再检查重试上限，否则本趟曾经耗尽预算后仍无法接管。
            for (int i = 0; i < island.Count; i++)
            {
                int giverId = island[i].GiverId;
                if (fallbackDone.Contains(giverId) && !HasAttachedGiver(giverId))
                {
                    fallbackDone.Remove(giverId);
                    fallbackAttempts = 0;
                }
            }
            if (fallbackDone.Count >= island.Count || fallbackAttempts >= FallbackAttemptLimit) return;
            fallbackAttempts++;
            for (int i = 0; i < island.Count; i++)
            {
                int giverId = island[i].GiverId;
                if (fallbackDone.Contains(giverId)) continue;
                string resident = SkyIslandOfficialQuestTable.ResidentOfGiver(giverId);
                if (resident == null || HasAttachedGiver(giverId)) { fallbackDone.Add(giverId); continue; }
                // 居民先于官方 UI 就绪时，首次 Attach 会失败；有人在岛上不等于他已经能发任务。
                // 按同一有界重试补到居民原交互组，只有居民缺席才使用装置。
                InteractableBase device = session.HasResident(resident)
                    ? session.FindResidentQuestOwner(resident)
                    : session.FindDeviceInteractable(SkyIslandOfficialQuestTable.FallbackMarkerOfGiver(giverId));
                if (device == null) continue;
                List<InteractableBase> group = NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(device, "[SkyIslandQuest]");
                if (Attach(device.transform, group, giverId))
                {
                    fallbackDone.Add(giverId);
                    ModBehaviour.DevLog("[SkyIslandQuest] 任务给予者补齐: " + resident + " -> " + device.name);
                }
            }
        }

        private static bool HasAttachedGiver(int giverId)
        {
            for (int i = 0; i < attached.Count; i++)
                if (attached[i] != null && (int)attached[i].ID == giverId) return true;
            return false;
        }

        private static bool Attach(Transform parent, List<InteractableBase> group, int giverId)
        {
            if (parent == null || group == null) return false;
            if (GiverIdField == null)
            {
                if (!fieldMissingLogged)
                {
                    fieldMissingLogged = true;
                    ModBehaviour.CriticalLog("sky-island-quest-giver-field", "[SkyIslandQuest] 官方 QuestGiver.questGiverID 字段签名已变化，岛上不挂任务给予者。");
                }
                return false;
            }
            if (QuestGiverView.Instance == null)
            {
                if (!viewMissingLogged)
                {
                    viewMissingLogged = true;
                    ModBehaviour.DevLog("[SkyIslandQuest] 当前场景没有官方 QuestGiverView，岛上不挂任务给予者（自绘面板照常）。");
                }
                return false;
            }
            Transform existing = parent.Find(ChildName);
            if (existing != null) return existing.GetComponent<QuestGiver>() != null;
            LocalizationHelper.InjectLocalization(InteractNameKey, L10n.T("航路任务", "Route quests"));
            QuestGiver giver = NPCInteractionGroupHelper.AddSubInteractable<QuestGiver>(parent, ChildName, group, component =>
            {
                GiverIdField.SetValue(component, (QuestGiverID)giverId);
                component.spawnPOI = false;
                component.overrideInteractName = true;
                component._overrideInteractNameKey = InteractNameKey;
                component.interactMarkerOffset = new Vector3(0f, 0.1f, 0f);
            });
            if (giver == null) return false;
            attached.Add(giver);
            return true;
        }

        /// <summary>剧情事实变了：让每位在场给予者重算头顶标记（官方只在任务列表变化时自己刷）。</summary>
        internal static void RefreshMarkers()
        {
            for (int i = attached.Count - 1; i >= 0; i--)
            {
                if (attached[i] == null) { attached.RemoveAt(i); continue; }
                RefreshMarker(attached[i]);
            }
        }

        /// <summary>头顶标记的反射刷新统一在 <see cref="OfficialQuestGiverLocator"/>（全仓库唯一一处）。</summary>
        internal static void RefreshMarker(QuestGiver giver)
        {
            OfficialQuestGiverLocator.RefreshMarker(giver);
        }

        /// <summary>离岛：这一趟的兜底记录作废（下一趟居民可能又在了）。给予者对象随居民 / 场景一起销毁。</summary>
        internal static void ClearSessionState()
        {
            fallbackDone.Clear();
            fallbackAttempts = 0;
            attached.RemoveAll(giver => giver == null);
        }

        internal static void ResetStaticCaches()
        {
            attached.Clear();
            fallbackDone.Clear();
            fallbackAttempts = 0;
            viewMissingLogged = fieldMissingLogged = false;
        }
    }

    /// <summary>
    /// 官方任务桥的事实源解析：岛上会话优先（<see cref="SkyIslandSession.OfficialQuestStory"/>），其次基地的序章故事门面；
    /// 都不可用返回 null，桥读到 null 就早退（不清不建）。
    /// </summary>
    internal static class SkyIslandOfficialQuestStory
    {
        private static SkyIslandStoryService baseSource;

        /// <summary><see cref="SkyIslandPreludeFlow"/> 打开 / 关闭基地故事时调用。</summary>
        internal static void SetBaseSource(SkyIslandStoryService service) { baseSource = service; }

        internal static SkyIslandStoryService Resolve(ModBehaviour host, out bool onIsland)
        {
            SkyIslandSession session = host == null ? null : host.GetComponent<SkyIslandSession>();
            SkyIslandStoryService island = session == null ? null : session.OfficialQuestStory;
            if (island != null && island.IsCurrentSlot) { onIsland = true; return island; }
            onIsland = false;
            return baseSource != null && baseSource.IsCurrentSlot ? baseSource : null;
        }

        internal static SkyIslandOfficialQuestContext Capture(ModBehaviour host)
        {
            var context = new SkyIslandOfficialQuestContext();
            try { context.Slot = Saves.SavesSystem.CurrentSlot; }
            catch (Exception) { context.Slot = -1; }
            bool onIsland;
            SkyIslandStoryService story = Resolve(host, out onIsland);
            context.OnIsland = onIsland;
            context.StoryReady = story != null && story.Current != null;
            context.Data = story == null ? null : story.Current;
            context.CanWrite = story != null && story.CanWrite;
            context.InBaseHub = !onIsland && SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name);
            context.BundleDeployed = SkyIslandPreludeFlow.BundleDeployed;
            string mode;
            context.NoConflictingMode = host == null || !host.ValidationHasActiveMode(out mode);
            return context;
        }

        internal static void ResetStaticCaches() { baseSource = null; }
    }
}
