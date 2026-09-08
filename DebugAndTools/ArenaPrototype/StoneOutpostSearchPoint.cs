using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>实验探索目标：搜索记录只属于本次会话，不生成物品或写玩家存档。</summary>
    public sealed class StoneOutpostSearchPoint : BossRushBuildingInteractableBase
    {
        private Action searched;
        private bool collected;
        protected override string InteractNameKey
        {
            get
            {
                const string key = "BossRush_StoneOutpost_Search";
                LocalizationHelper.InjectLocalization(key, L10n.T("搜索前哨记录", "Search outpost records"));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[StoneOutpost] "; } }
        protected override string InteractionGroupLabel { get { return "[StoneOutpost]"; } }
        protected override bool IsBuildingInteractable() { return !collected && searched != null; }
        internal void Bind(Action callback) { searched = callback; }
        protected override void OnInteractCompleted()
        {
            if (collected) return;
            collected = true;
            if (searched != null) searched();
            Debug.Log("[StoneOutpost] SEARCH_COMPLETE point=" + name);
        }
    }
}
