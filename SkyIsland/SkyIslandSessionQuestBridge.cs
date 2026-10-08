// ============================================================================
// SkyIslandSessionQuestBridge.cs - 官方任务桥从岛上会话只读取事实的几个口子
// ============================================================================
// 从 SkyIslandSession.cs 拆出来单独放：会话主文件卡在行数预算上（SkyIsland/AGENTS.md §4）。
// 任务查询只读；配偶的剧情交互转发给本趟 worldStory。官方任务符号仍只在 Givers / Bridge。
// ============================================================================

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        /// <summary>
        /// 岛上这一趟的权威剧情门面。官方任务桥在岛上以它为事实源（基地那份由 SkyIslandPreludeFlow 提供）。
        /// 会话未就绪、正在返航或已关闭时为 null：桥读到 null 就早退，不清也不建投影。
        /// </summary>
        internal SkyIslandStoryService OfficialQuestStory { get { return IsSessionValid() ? story : null; } }

        /// <summary>某位居民这一趟是不是真的在岛上（决定要不要给装置挂兜底的官方给予者）。只读。</summary>
        internal bool HasResident(string id) { return FindResidentQuestOwner(id) != null; }

        internal InteractableBase FindResidentQuestOwner(string id)
        {
            InteractableBase owner = residents == null ? null : residents.FindQuestInteractionOwner(id);
            if (owner != null) return owner;
            CharacterMainControl spouse = PermanentDuckNpcRegistry.GetInstance(id);
            return spouse != null && player != null && spouse.gameObject.scene == player.gameObject.scene
                ? spouse.GetComponentInChildren<PermanentDuckNpcInteractable>(true) : null;
        }

        /// <summary>
        /// 去找某位居民接 / 交任务时，地图圈和风标罗盘指向这位居民本人（只读）。
        /// 兜底装置（渡口工台 / 委托板 / 钟庭留言板）离居民站的地方 31–41 米，而地图圈半径只有 12 米：
        /// 以前圈画在装置上，人站在圈外（owner 2026-10-02「圈与实际位置不符」）。
        /// 这位居民这一趟不在岛上就返回 null，调用方退回装置标记——那时任务也确实挂在装置上。
        /// </summary>
        internal UnityEngine.Transform GiverAnchor(int giverId)
        {
            string resident = SkyIslandOfficialQuestTable.ResidentOfGiver(giverId);
            if (resident == null || !IsSessionValid()) return null;
            InteractableBase owner = FindResidentQuestOwner(resident);
            CharacterMainControl npc = owner == null ? null : owner.GetComponentInParent<CharacterMainControl>();
            return npc != null ? npc.transform : null;
        }

        /// <summary>随行配偶也走本趟的战斗门、服务和剧情 owner。</summary>
        internal void TalkToResident(string id, UnityEngine.Transform speaker)
        {
            if (IsSessionValid() && worldStory != null) worldStory.Talk(id, speaker);
        }

        /// <summary>居民 owner 已经把整队生成完（成功与否都算）；在此之前不判「谁缺席」。只读。</summary>
        internal bool ResidentsSettled { get { return residentsFailed || (residents != null && residents.SpawnFinished); } }

        /// <summary>按标记名找岛上的装置交互体（`Search_B` 委托板等），给兜底给予者分组用。只读。</summary>
        internal InteractableBase FindDeviceInteractable(string marker)
        {
            if (string.IsNullOrEmpty(marker) || root == null) return null;
            UnityEngine.Transform point = root.transform.Find(marker);
            return point == null ? null : point.GetComponent<SkyIslandSearchPoint>();
        }

        /// <summary>
        /// 航标守卫清了一伙（2026-09-29 引导复核）：字幕说这伙算进哪盏灯、还差哪一伙在哪儿。
        /// 以前只报「航路已清理 · 悬根林」，玩家不知道这一伙和「点亮两端航标」有什么关系，更不知道还差另一伙。
        /// 放在本 partial 而不是会话主文件：主文件贴着 1200 行预算。文案唯一来源是 <see cref="SkyIslandStoryRules.GuardProgressCaption"/>。
        /// 只读剧情事实、不写存档。
        /// </summary>
        private void AnnounceGuardProgress(string id)
        {
            if (story == null || !story.Current.EncounterCleared(id)) return;
            string caption = SkyIslandStoryRules.GuardProgressCaption(story.Current, id);
            if (caption != null) Announce(caption, false);
        }
    }
}
