using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>群岛手记与装置交互；重复可打开，是否提交由持久剧情服务判断。</summary>
    public sealed class SkyIslandSearchPoint : BossRushBuildingInteractableBase
    {
        private Action searched;
        private string title;
        protected override string InteractNameKey
        {
            get
            {
                string key = "BossRush_SkyIsland_Search_" + name;
                LocalizationHelper.InjectLocalization(key, title ?? L10n.T("查看群岛见闻", "Read island notes"));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIsland] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIsland]"; } }
        protected override bool IsBuildingInteractable() { return searched != null; }
        internal void Bind(Action callback, string label = null) { searched = callback; title = label; }
        protected override void OnInteractCompleted()
        {
            if (searched != null) searched();
            Debug.Log("[SkyIsland] SEARCH_COMPLETE point=" + name);
        }
    }
}
