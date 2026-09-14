using System;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：头目 / 岛主「每次都穿全套，死后只掉其中一件」（owner 2026-09-14 拍板，龙王式一格专属掉落 + 原版掉落）。
    ///
    /// 【为什么挂在 BeforeCharacterSpawnLootOnDead】官方 `CharacterMainControl.OnDead` 对非主角先发这个**实例事件**，
    /// 紧接着按 `dropBoxOnDead` 调 `InteractableLootbox.CreateFromItem` 收走「全部槽位 + 全部背包」。
    /// 在这里按权重留一件、把没抽中的配装卸下销毁，箱子里就只剩一格专属装备，其余照原版掉：
    /// - 不另建箱子、不搬运物品（遭遇 owner 守卫明令「官方死亡拥有掉落」）；
    /// - 不经过 BossRush 奖励箱，所以不需要 defer 协议，也不存在「三条消费通道各摇一次」；
    /// - 只在建箱前摇一次（<see cref="resolved"/> 挡住重入）。
    ///
    /// 抽中的那件补满耐久（与龙系专属掉落口径一致）：Boss 身上那件可能刚被打穿，拿到手不该是废的。
    /// 抽中的那件如果配装阶段就没穿上（资源缺失、插槽拒绝），补一件新的进背包，箱子里照样有这一格。
    /// </summary>
    internal sealed class SkyIslandBossLoot : MonoBehaviour
    {
        private CharacterMainControl boss;
        private SkyIslandBossProfile profile;
        private System.Random random;
        private bool subscribed, resolved;

        /// <summary>死亡时抽中的专属装备 TypeID；-1 表示没掉或还没死。只读，给 F3 演练核对箱子内容。</summary>
        internal int ChosenTypeId { get; private set; }

        /// <summary>结算结果：alive / slot / inventory / no_drop / missing / character_item_missing / error。</summary>
        internal string Outcome { get; private set; }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, int seed)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            boss = character;
            profile = value;
            random = new System.Random(seed);
            ChosenTypeId = -1;
            Outcome = "alive";
            boss.BeforeCharacterSpawnLootOnDead += OnBeforeLoot;
            subscribed = true;
        }

        private void OnBeforeLoot(DamageInfo damage)
        {
            if (resolved || boss == null || profile == null) return;
            resolved = true;
            try { Resolve(random.NextDouble()); }
            catch (Exception e)
            {
                Outcome = "error";
                Debug.LogWarning("[SkyIslandBoss] 专属掉落结算失败（其余原版掉落照常）：" + e.Message);
            }
        }

#if BOSSRUSH_DEV
        /// <summary>
        /// Dev 演练 SKY_DRILL_BOSS_LOADOUT 专用：不走死亡，按给定抽样值结算一次，结算完与死亡时同样上闩。
        /// 不死就不建官方尸体箱、不记官方击杀计数、不派发首杀事件——演练因此不碰任何存档。返回 <see cref="Outcome"/>。
        /// </summary>
        internal string DevResolveForDrill(double roll)
        {
            if (resolved || boss == null || profile == null) return "not_bound_or_already_resolved";
            resolved = true;
            Resolve(roll);
            return Outcome;
        }
#endif

        private void Resolve(double roll)
        {
            Item characterItem = boss.CharacterItem;
            if (characterItem == null) { Outcome = "character_item_missing"; return; }
            int chosen = SkyIslandBossRules.RollDrop(profile, roll);
            SkyIslandBossGearPiece[] gear = profile.Gear;
            bool chosenWorn = false;
            for (int i = 0; i < gear.Length; i++)
            {
                Slot slot = SkyIslandBossForge.FindSlot(characterItem, gear[i].Slot);
                Item worn = slot == null ? null : slot.Content;
                if (worn == null || worn.TypeID != gear[i].TypeId) continue;
                if (i == chosen)
                {
                    if (worn.UseDurability) worn.Durability = worn.MaxDurability;
                    chosenWorn = true;
                    continue;
                }
                Item removed = slot.Unplug();
                if (removed != null) removed.DestroyTree();
            }
            if (chosen < 0) { Outcome = "no_drop"; return; }
            ChosenTypeId = gear[chosen].TypeId;
            if (chosenWorn) { Outcome = "slot"; return; }
            Outcome = TryAddFresh(characterItem, ChosenTypeId) ? "inventory" : "missing";
        }

        /// <summary>补一件新的进背包。先问 prefab：缺资源时官方给的空壳带着同一个 TypeID，回读分辨不出来（contracts §7.1）。</summary>
        private static bool TryAddFresh(Item characterItem, int typeId)
        {
            Item created = null;
            try
            {
                if (characterItem.Inventory == null || ItemAssetsCollection.GetPrefab(typeId) == null) return false;
                created = ItemAssetsCollection.InstantiateSync(typeId);
                if (created == null || created.TypeID != typeId) return false;
                if (created.UseDurability) created.Durability = created.MaxDurability;
                if (!characterItem.Inventory.AddItem(created)) return false;
                created = null;
                return true;
            }
            finally
            {
                if (created != null) created.DestroyTree();
            }
        }

        private void OnDestroy()
        {
            if (subscribed && boss != null) boss.BeforeCharacterSpawnLootOnDead -= OnBeforeLoot;
            subscribed = false;
            boss = null;
            profile = null;
        }
    }
}
