using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>群岛手记与装置交互；重复可打开，是否提交由持久剧情服务判断。</summary>
    public sealed class SkyIslandSearchPoint : BossRushBuildingInteractableBase
    {
        private Action searched;
        protected override string InteractNameKey
        {
            get { return LocalizationKey(name); }
        }
        protected override string LogPrefix { get { return "[SkyIsland] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIsland]"; } }
        protected override bool IsBuildingInteractable() { return searched != null; }
        internal void Bind(Action callback, string label = null)
        {
            searched = callback;
            LocalizationHelper.InjectLocalization(LocalizationKey(name), label ?? SkyIslandPointText.Name(name));
            ApplyInteractName("bind");
        }

        private static string LocalizationKey(string pointName) { return "BossRush_SkyIsland_Search_" + pointName; }

        /// <summary>复用见闻目录，语言切换时刷新固定搜索点的 key；不用查找场景对象或各点订阅事件。</summary>
        internal static void InjectLocalizations()
        {
            string[][] chapters = SkyIslandJournal.Chapters;
            for (int chapter = 0; chapter < chapters.Length; chapter++)
                for (int point = 0; point < chapters[chapter].Length; point++)
                {
                    string marker = chapters[chapter][point];
                    LocalizationHelper.InjectLocalization(LocalizationKey(marker), SkyIslandPointText.Name(marker));
                }
        }
        protected override void OnInteractCompleted()
        {
            if (searched != null) searched();
            Debug.Log("[SkyIsland] SEARCH_COMPLETE point=" + name);
        }
    }
}
