// ============================================================================
// SkyIslandSessionQuestBridge.cs - 官方任务桥从岛上会话只读取事实的几个口子
// ============================================================================
// 从 SkyIslandSession.cs 拆出来单独放：会话主文件卡在行数预算上（DebugAndTools/SkyIsland/AGENTS.md §4）。
// 只读：不写剧情、不改居民显隐、不碰官方任务系统符号（那些全在 SkyIslandOfficialQuestGivers / Bridge）。
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
        internal bool HasResident(string id) { return residents != null && residents.IsSpawned(id); }

        /// <summary>居民 owner 已经把整队生成完（成功与否都算）；在此之前不判「谁缺席」。只读。</summary>
        internal bool ResidentsSettled { get { return residents != null && residents.SpawnFinished; } }

        /// <summary>按标记名找岛上的装置交互体（`Search_B` 委托板等），给兜底给予者分组用。只读。</summary>
        internal InteractableBase FindDeviceInteractable(string marker)
        {
            if (string.IsNullOrEmpty(marker) || root == null) return null;
            UnityEngine.Transform point = root.transform.Find(marker);
            return point == null ? null : point.GetComponent<SkyIslandSearchPoint>();
        }
    }
}
