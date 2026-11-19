// ============================================================================
// CollectionSeriesBonus.cs - 集齐系列的永久小加成（挂在主角的官方属性上）
// ============================================================================
// 每次主角换人（进场景、复活）就把已集齐系列的加成重挂一遍：先摘掉上一次挂的，再按当前事实挂，
// 不会叠加。每帧只比较一次主角引用和事实版本，O(1) 早返（AGENTS.md §4.12）。
// 用的是运行时 Modifier（RuntimeStatModifierTracker），不进装备词条、不写存档。
// ============================================================================

using System.Collections.Generic;
using ItemStatsSystem.Stats;

namespace BossRush
{
    internal sealed class CollectionSeriesBonus
    {
        private readonly List<BossRushStatModifierRecord> records = new List<BossRushStatModifierRecord>();
        private CharacterMainControl appliedTo;
        private CollectionBookData appliedData;

        /// <summary>主角或事实变了才重挂。<paramref name="data"/> 为 null（事实未就绪）时什么都不做。</summary>
        internal void Tick(CollectionBookData data)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == appliedTo && ReferenceEquals(data, appliedData)) return;
            Remove();
            appliedTo = player;
            appliedData = data;
            if (player == null || player.CharacterItem == null || data == null) return;
            foreach (int index in data.PaidSeries)
            {
                CollectibleTuning.SeriesBonus bonus = CollectibleTuning.BonusOf((CollectibleSeries)index);
                if (bonus == null) continue;
                RuntimeStatModifierTracker.TryAdd(player, bonus.Stat, bonus.Amount, this, records, "CollectionSeries",
                    bonus.Percent ? ModifierType.PercentageAdd : ModifierType.Add);
            }
        }

        internal void Remove()
        {
            RuntimeStatModifierTracker.RemoveAll(records, "CollectionSeries");
            appliedTo = null;
            appliedData = null;
        }
    }
}
