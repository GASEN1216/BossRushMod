// ============================================================================
// ModeHItemBetStake.cs - 鸭王杯「押背包物品」的物品侧（2026-09-24 owner 拍板）
// ============================================================================
// owner：「让玩家自己选择押注多少背包里的物品吧，或者直接砸钱也行」。押钱已在 ModeHCashBetService；
// 这里是押物品的物品侧，账本（押了什么、按什么赔、输赢统计）仍记在同一本押钱账本里。
//
// owner（第三轮）：「押上的物品不要有限制，只是其品质和价钱会影响到再次给予其奖品的品质和价钱」。
// 2026-09-25 owner明确要求任何物品与穿戴装备均可押；零估值物品可押但不产生凭空利润。
//
// Mode H 玩家资产访问白名单的一条（ModeHIsolationGuard 按文件放行 Inventory 符号）：
//   - 从主角色背包（CharacterItem.Inventory）列出能押的物品；不依赖出击图中不存在的仓库；
//   - 押上的物品**不离开背包**：锁盘盖持久身份，记录引用、估值和品质；身份随主角物品树与账本同存；
//   - 输了才收走：只 Detach + DestroyTree 仍在玩家身上的那几件；被挪走、用掉、合并掉的部分按估值
//     记成「找不到」，由账本从余额里扣（扣到 0 为止）；堆叠被合并变多时只扣回押上的数量；
//   - 赢了：押上的东西一件不动，另发奖品。奖品从共享的品质候选池（BossRushQualityItemPool，全表按品质、
//     过掉落黑名单）按押上物品的加权品质挑，经 ModeHRewardItemPool.TryInstantiate（空壳门禁）实例化，
//     先把奖品身份与清单记成可恢复计划，再用 SendToPlayerCharacterInventory(prize, true) 不合并地发进背包；
//     满包或投递失败保留欠账；已到账凭据、主角物品树与剩余义务同存，全部完成后才结清现金与统计；
//   - 退回：物品一件不动，只丢掉引用。
// 为什么不先拿走托管：比赛在出击地图上打，托管在内存里的物品会随进程崩溃消失；原地押上的物品崩溃后
// 仍在官方存档里；收走和交付则必须与账本一起采集快照，防止恢复到互不对应的资产与结算状态。
// 为什么结算时还要核对：官方背包键不看 InputManager 的输入禁用（CharacterInputControl.OnUIInventoryInput），
// 看台上也能打开背包把押上的东西挪走。
// 读档后内存引用没了：只按 TypeID + 持久身份唯一认领；旧记录缺身份或身份重复都不猜，沿用缺失估值补偿。
// 估值按官方商人收购口径（ModeHConfig.ItemBetValuePermille），押物品不会比卖掉更划算。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>选择页上的一件候选物品（界面只拿这份快照，不拿官方 Item）。</summary>
    internal sealed class ModeHItemBetCandidate
    {
        public int Key;
        public string Name;
        public int Count;
        public long Value;
        public int Quality;
        public Sprite Icon;
        public bool Selected;
        public bool Equipped;
        /// <summary>是装着东西的容器时里面有几件（押了连里面的一起押）。</summary>
        public int Contents;
    }

    /// <summary>押背包物品的物品侧。全部入口 no-throw，只在 Unity 主线程调用。</summary>
    internal static class ModeHItemBetStake
    {
        // 官方 ItemTreeData 会复制 Variables；Unity GetInstanceID 只在本进程内有效，不能进恢复账本。
        internal const string IdentityKey = "BossRush_ModeHBetIdentity";

        private sealed class LockedEntry
        {
            public Item Item;
            public int TypeId;
            public int Count;
            public long Value;
            public int Quality;
            public string Name;
        }

        /// <summary>玩家在选择页勾上的物品（下一次锁盘押它们；锁盘后清空，押物品只管一场）。</summary>
        private static readonly List<Item> _selected = new List<Item>();
        /// <summary>本场押上的物品（锁盘时的快照）。</summary>
        private static readonly List<LockedEntry> _locked = new List<LockedEntry>();
        private static bool _forfeited;
        private static long _forfeitMissing;

        #region 估值与候选

        /// <summary>一件（整组）物品的估值：官方总价（含配件、按耐久折算）× 收购折算，向下取整。</summary>
        internal static long ValueOf(Item item)
        {
            if (item == null) return 0;
            try
            {
                long raw = item.GetTotalRawValue();
                return raw <= 0 ? 0 : raw * ModeHConfig.ItemBetValuePermille / 1000;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static Item PlayerCharacterItem()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            return player != null ? player.CharacterItem : null;
        }

        private static bool IsOnPlayer(Item item, Item character)
        {
            if (item == null || character == null || item.IsBeingDestroyed) return false;
            try { return ReferenceEquals(item.GetCharacterItem(), character); }
            catch (Exception) { return false; }
        }

        internal static bool CanSnapshotPlayer()
        {
            Item character = PlayerCharacterItem();
            return !SavesSystem.IsSaving && SavesSystem.CurrentSlot >= 0
                && character != null && !character.IsBeingDestroyed && character.Inventory != null;
        }

        /// <summary>仅采集主角物品树；出击图没有 PlayerStorage，不能使用要求基地仓库的资产门。</summary>
        internal static bool CollectPlayerSnapshot(int slot)
        {
            if (SavesSystem.CurrentSlot != slot || !CanSnapshotPlayer()) return false;
            try { PlayerCharacterItem().Save("MainCharacterItemData"); return true; }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 押物品资产快照顺延: " + e.Message);
                return false;
            }
        }

        /// <summary>每次锁盘重新盖章，旧凭据不会认领下一次押注。读取时从不补造身份。</summary>
        private static string StampIdentity(Item item)
        {
            string identity = Guid.NewGuid().ToString("N");
            item.SetString(IdentityKey, identity, true);
            if (!string.Equals(item.GetString(IdentityKey, string.Empty), identity, StringComparison.Ordinal))
                throw new InvalidOperationException("item_bet_identity_write_failed");
            return identity;
        }

        private static Item FindIdentity(List<Item> pool, ModeHItemBetEntry entry, out bool ambiguous)
        {
            ambiguous = false;
            if (string.IsNullOrEmpty(entry.Identity)) return null;
            Item match = null;
            for (int i = 0; i < pool.Count; i++)
            {
                Item item = pool[i];
                if (item == null || item.IsBeingDestroyed || item.TypeID != entry.TypeId
                    || !string.Equals(item.GetString(IdentityKey, string.Empty), entry.Identity, StringComparison.Ordinal)) continue;
                if (match != null) { ambiguous = true; return null; }
                match = item;
            }
            return match;
        }

        /// <summary>
        /// 能不能押：任何有效物品都可押，包括穿戴、Sticky及零估值物品。
        /// 装着东西的容器也能押，估值连里面的一起算，输了连里面的一起收走（选择页写明「连里面的」）。
        /// </summary>
        private static bool IsBettable(Item item)
        {
            if (item == null || item.IsBeingDestroyed) return false;
            try
            {
                return item.TypeID > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>容器里装着几件东西（选择页提示「连里面的」用）。</summary>
        private static int ContentCountOf(Item item)
        {
            int count = 0;
            try
            {
                if (item.Inventory == null) return 0;
                foreach (Item child in item.Inventory)
                {
                    if (child != null) count++;
                }
            }
            catch (Exception)
            {
                return count;
            }
            return count;
        }

        private static int QualityOf(Item item)
        {
            try { return item.Quality; }
            catch (Exception) { return ModeHConfig.MinGameQuality; }
        }

        private static string NameOf(Item item)
        {
            try
            {
                string name = item.DisplayName;
                return string.IsNullOrEmpty(name) ? "#" + item.TypeID : name;
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static int CountOf(Item item)
        {
            try { return Math.Max(1, item.StackCount); }
            catch (Exception) { return 1; }
        }

        /// <summary>背包里能押的物品，按估值从高到低，全部列出（选择页可以滚动）。</summary>
        internal static List<ModeHItemBetCandidate> ListCandidates()
        {
            List<ModeHItemBetCandidate> result = new List<ModeHItemBetCandidate>();
            try
            {
                PruneSelection();
                Item character = PlayerCharacterItem();
                // GetAllChildren(false, true) 同时包含背包直系物品与所有已装备槽位。
                // 旧实现只枚举 CharacterItem.Inventory，穿在身上的头盔、护甲、背包、面具、耳机
                // 因而永远不会出现在押注页，和“什么都能押”不符；不递归容器内部，避免把容器和
                // 容器里的内容重复列出（容器仍按整棵物品树估值并一并承担风险）。
                if (character == null) return result;
                List<Item> items = new List<Item>();
                List<Item> owned = character.GetAllChildren(false, true);
                for (int i = 0; i < owned.Count; i++)
                {
                    Item item = owned[i];
                    if (IsBettable(item)) items.Add(item);
                }
                items.Sort(delegate (Item a, Item b)
                {
                    int byValue = ValueOf(b).CompareTo(ValueOf(a));
                    return byValue != 0 ? byValue : string.CompareOrdinal(NameOf(a), NameOf(b));
                });
                for (int i = 0; i < items.Count; i++)
                {
                    Item item = items[i];
                    bool selected = _selected.Contains(item);
                    Sprite icon = null;
                    try { icon = item.Icon; } catch (Exception) { icon = null; }
                    result.Add(new ModeHItemBetCandidate
                    {
                        Key = item.GetInstanceID(),
                        Name = NameOf(item),
                        Count = CountOf(item),
                        Value = ValueOf(item),
                        Quality = QualityOf(item),
                        Icon = icon,
                        Selected = selected,
                        Equipped = item.PluggedIntoSlot != null,
                        Contents = ContentCountOf(item),
                    });
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 列出可押物品失败: " + e.Message);
            }
            return result;
        }

        #endregion

        #region 选择（锁盘前）

        internal static bool HasSelection
        {
            get { PruneSelection(); return _selected.Count > 0; }
        }

        internal static int SelectedCount
        {
            get { PruneSelection(); return _selected.Count; }
        }

        internal static long SelectedValue
        {
            get
            {
                PruneSelection();
                long sum = 0;
                for (int i = 0; i < _selected.Count; i++) sum += ValueOf(_selected[i]);
                return sum;
            }
        }

        /// <summary>勾上 / 取下一件。被拒时给出玩家看的原因（写在按钮带上方）。</summary>
        internal static bool Toggle(int key, out string failureText)
        {
            failureText = null;
            try
            {
                PruneSelection();
                for (int i = 0; i < _selected.Count; i++)
                {
                    if (_selected[i].GetInstanceID() != key) continue;
                    _selected.RemoveAt(i);
                    return true;
                }
                Item character = PlayerCharacterItem();
                Item found = null;
                if (character != null)
                {
                    List<Item> owned = character.GetAllChildren(false, true);
                    for (int i = 0; i < owned.Count; i++)
                    {
                        Item item = owned[i];
                        if (item != null && item.GetInstanceID() == key) { found = item; break; }
                    }
                }
                if (found == null || !IsBettable(found))
                {
                    failureText = L10n.T("这件已经不在身上了。", "That item is no longer on your character.");
                    return false;
                }
                _selected.Add(found);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 勾选押注物品失败: " + e.Message);
                failureText = L10n.T("这件现在押不了。", "That item can't be bet right now.");
                return false;
            }
        }

        internal static void ClearSelection()
        {
            _selected.Clear();
        }

        /// <summary>「名字 ×n、名字」：给页脚说明与结算行用。</summary>
        internal static string DescribeSelection()
        {
            PruneSelection();
            List<ModeHItemBetEntry> entries = new List<ModeHItemBetEntry>();
            for (int i = 0; i < _selected.Count; i++)
            {
                entries.Add(new ModeHItemBetEntry { Name = NameOf(_selected[i]), Count = CountOf(_selected[i]) });
            }
            return ModeHItemBetEntry.Describe(entries);
        }

        /// <summary>丢掉已经不在玩家身上、已被销毁或不再能押的勾选。</summary>
        private static void PruneSelection()
        {
            if (_selected.Count == 0) return;
            Item character = PlayerCharacterItem();
            for (int i = _selected.Count - 1; i >= 0; i--)
            {
                Item item = _selected[i];
                if (!IsOnPlayer(item, character) || !IsBettable(item)) _selected.RemoveAt(i);
            }
        }

        #endregion

        #region 锁盘、收走与认领

        internal static bool HasLocked
        {
            get { return _locked.Count > 0; }
        }

        /// <summary>
        /// 锁盘：把勾上的物品记成本场押注（引用 + 估值、品质快照）。物品一件不动；勾选留着，
        /// 账本记成功后由调用方清空（押物品只管这一场），记不成时玩家的勾选不丢。押多少都不限。
        /// </summary>
        internal static bool TryLock(out long value, out List<ModeHItemBetEntry> entries, out string failureText)
        {
            value = 0;
            entries = new List<ModeHItemBetEntry>();
            failureText = null;
            ReleaseLocked();
            try
            {
                PruneSelection();
                if (_selected.Count == 0)
                {
                    failureText = L10n.T("勾上的物品都不在身上了，这一场没押。", "The items you picked are no longer on your character; no bet this match.");
                    return false;
                }
                for (int i = 0; i < _selected.Count; i++)
                {
                    Item item = _selected[i];
                    string identity = StampIdentity(item);
                    LockedEntry entry = new LockedEntry
                    {
                        Item = item, TypeId = item.TypeID, Count = CountOf(item), Value = ValueOf(item),
                        Quality = QualityOf(item), Name = NameOf(item),
                    };
                    _locked.Add(entry);
                    value += entry.Value;
                    entries.Add(new ModeHItemBetEntry
                    {
                        TypeId = entry.TypeId, Count = entry.Count, Value = entry.Value, Quality = entry.Quality, Name = entry.Name,
                        Identity = identity,
                    });
                }
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 押物品锁盘失败: " + e.Message);
                failureText = L10n.T("押物品没押成，这一场没押。", "The item bet did not go through; no bet this match.");
                ReleaseLocked();
                entries.Clear();
                value = 0;
                return false;
            }
        }

        /// <summary>
        /// 读档后只按随物品保存的唯一身份认领。数量变化由收走路径处理，绝不用同型号物品顶替。
        /// 旧账本没有身份、身份重复或认领不到的留空，输了时走既有的缺失估值补偿。已有本场引用时不动。
        /// </summary>
        internal static void RebindFromLedger(List<ModeHItemBetEntry> entries)
        {
            if (_locked.Count > 0 || entries == null || entries.Count == 0) return;
            _forfeited = false;
            _forfeitMissing = 0;
            List<Item> pool = new List<Item>();
            try
            {
                Item character = PlayerCharacterItem();
                if (character != null) pool = character.GetAllChildren(true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 认领押注物品时读背包失败: " + e.Message);
            }
            for (int i = 0; i < entries.Count; i++)
            {
                ModeHItemBetEntry entry = entries[i];
                if (entry == null) continue;
                bool ambiguous;
                Item match = FindIdentity(pool, entry, out ambiguous);
                if (match != null) pool.Remove(match);
                _locked.Add(new LockedEntry
                {
                    Item = match, TypeId = entry.TypeId, Count = entry.Count, Value = entry.Value, Quality = entry.Quality, Name = entry.Name,
                });
            }
        }

        /// <summary>
        /// 输了：收走仍在玩家身上的押注物品，返回「找不到」部分的估值（由账本从余额里扣）。幂等：
        /// 第二次调用不再动物品，只返回第一次算出的数（账本落盘顺延时会再结算一次）。
        /// </summary>
        internal static long ForfeitLocked()
        {
            if (_forfeited) return _forfeitMissing;
            long missing = 0;
            Item character = PlayerCharacterItem();
            for (int i = 0; i < _locked.Count; i++)
            {
                LockedEntry entry = _locked[i];
                Item item = entry.Item;
                bool present = false;
                try { present = IsOnPlayer(item, character) && item.TypeID == entry.TypeId; }
                catch (Exception) { present = false; }
                if (!present)
                {
                    missing += entry.Value;
                    continue;
                }
                try
                {
                    int now = CountOf(item);
                    if (now > entry.Count)
                    {
                        // 比赛期间又合并进来的不算押注：只扣回押上的数量
                        item.StackCount = now - entry.Count;
                        continue;
                    }
                    if (now < entry.Count && entry.Count > 0)
                    {
                        missing += entry.Value * (entry.Count - now) / entry.Count;
                    }
                    item.Detach();
                    item.DestroyTree();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeH] 收走押注物品失败，按估值扣钱: " + e.Message);
                    missing += entry.Value;
                }
            }
            _forfeited = true;
            _forfeitMissing = missing;
            return missing;
        }

        #endregion

        #region 奖品（赢了）

        /// <summary>把实际可生成的奖品固定成可恢复计划；试生成物不交付，由这里负责清理。</summary>
        internal static List<ModeHItemBetEntry> PreparePrizePlan(long budget, int quality, int slots,
            long runSeed, string txKey, out long prizeValue, out string summary)
        {
            List<Item> prizes = PreparePrizes(budget, quality, slots, runSeed, txKey, out prizeValue, out summary);
            List<ModeHItemBetEntry> plan = new List<ModeHItemBetEntry>();
            try
            {
                for (int i = 0; i < prizes.Count; i++)
                {
                    Item item = prizes[i];
                    plan.Add(new ModeHItemBetEntry { TypeId = item.TypeID, Count = CountOf(item), Value = ValueOf(item),
                        Quality = QualityOf(item), Name = NameOf(item), Identity = Guid.NewGuid().ToString("N") });
                }
                return plan;
            }
            finally { DiscardPrizes(prizes); }
        }

        /// <summary>
        /// 备好奖品（实例化、不发）：共 <paramref name="slots"/> 件，每件的目标价值是「剩下的奖品价值 ÷ 剩下的件数」，
        /// 在 <paramref name="quality"/> 品质里挑价值落在目标值 50%–100% 之间的一件（种子确定，同一场重放挑到同一件），
        /// 区间里没有就取不超过目标值的最贵那件，这一档一件都放不下就降一档品质再找。
        /// 返回备好的奖品；<paramref name="prizeValue"/> 是它们按同一口径算的估值合计（不超过 <paramref name="budget"/>）。
        /// </summary>
        internal static List<Item> PreparePrizes(long budget, int quality, int slots, long runSeed, string txKey,
            out long prizeValue, out string summary)
        {
            List<Item> prizes = new List<Item>();
            prizeValue = 0;
            summary = string.Empty;
            if (budget <= 0 || slots <= 0) return prizes;
            long remaining = budget;
            List<ModeHItemBetEntry> described = new List<ModeHItemBetEntry>();
            for (int slot = 0; slot < slots && remaining > 0; slot++)
            {
                long target = remaining / (slots - slot);
                if (target <= 0) break;
                int typeId = PickPrizeTypeId(quality, target, runSeed, txKey, slot);
                if (typeId <= 0) continue;
                string failure;
                Item prize = ModeHRewardItemPool.TryInstantiate(typeId, out failure);
                if (prize == null)
                {
                    ModBehaviour.DevLog("[ModeH] 押物品奖品实例化失败: " + (failure ?? "unknown"));
                    continue;
                }
                long value = ValueOf(prize);
                if (value <= 0 || value > remaining)
                {
                    ModeHRewardItemPool.DestroyUngranted(prize);
                    continue;
                }
                prizes.Add(prize);
                prizeValue += value;
                remaining -= value;
                described.Add(new ModeHItemBetEntry { Name = NameOf(prize), Count = CountOf(prize) });
            }
            summary = ModeHItemBetEntry.Describe(described);
            return prizes;
        }

        /// <summary>按品质挑一件价值合适的奖品 typeId；这一档放不下就降一档，都不行返回 0（这一件的价值折成钱）。</summary>
        private static int PickPrizeTypeId(int quality, long target, long runSeed, string txKey, int slot)
        {
            int top = Math.Max(ModeHConfig.MinGameQuality, Math.Min(ModeHConfig.MaxGameQuality, quality));
            long low = target * ModeHConfig.ItemBetPrizeBandLowPermille / 1000;
            for (int q = top; q >= ModeHConfig.MinGameQuality; q--)
            {
                int[] candidates = BossRushQualityItemPool.GetCandidates(q);
                if (candidates == null || candidates.Length == 0) continue;
                List<int> band = new List<int>();
                int best = 0;
                long bestValue = 0;
                for (int i = 0; i < candidates.Length; i++)
                {
                    long value = PrefabValueOf(candidates[i]);
                    if (value <= 0 || value > target) continue;
                    if (value >= low) band.Add(candidates[i]);
                    if (value > bestValue) { bestValue = value; best = candidates[i]; }
                }
                if (band.Count > 0)
                {
                    ModeHSeedStream stream = ModeHSeedStream.Create(runSeed, "modeh_item_bet_prize|" + (txKey ?? string.Empty) + "|" + q, slot);
                    return band[stream.NextInt(band.Count)];
                }
                if (best > 0) return best;
            }
            return 0;
        }

        /// <summary>官方模板的估值（与押上物品同一口径）；取不到时为 0（不参与挑选）。</summary>
        private static long PrefabValueOf(int typeId)
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                return prefab != null ? ValueOf(prefab) : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 交付固定计划，返回未到账的条目。仅进背包且不合并：空间不足时留欠账，不能把不随存档保存的落地物算到账。
        /// 已在角色物品树中的同一凭据只确认一次，覆盖送达后通知异常与回执写入重试。
        /// </summary>
        internal static List<ModeHItemBetEntry> DeliverPrizes(List<ModeHItemBetEntry> prizes)
        {
            List<ModeHItemBetEntry> remaining = new List<ModeHItemBetEntry>();
            Item character = PlayerCharacterItem();
            List<Item> pool = character != null ? character.GetAllChildren(true, true) : new List<Item>();
            for (int i = 0; prizes != null && i < prizes.Count; i++)
            {
                ModeHItemBetEntry entry = prizes[i];
                bool ambiguous;
                if (FindIdentity(pool, entry, out ambiguous) != null) continue;
                if (ambiguous || string.IsNullOrEmpty(entry.Identity) || !CanSnapshotPlayer()
                    || character.Inventory.GetFirstEmptyPosition() < 0)
                {
                    remaining.Add(entry);
                    continue;
                }
                Item prize = null;
                try
                {
                    string failure;
                    prize = ModeHRewardItemPool.TryInstantiate(entry.TypeId, out failure);
                    if (prize != null)
                    {
                        prize.StackCount = entry.Count;
                        prize.SetString(IdentityKey, entry.Identity, true);
                        if (prize.GetString(IdentityKey, string.Empty) != entry.Identity)
                            throw new InvalidOperationException("item_prize_identity_write_failed");
                        ItemUtilities.SendToPlayerCharacterInventory(prize, true);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeH] 押物品奖品送达异常，核对实物后保留欠账: " + e.Message);
                }
                if (IsOnPlayer(prize, character)) pool.Add(prize);
                else
                {
                    remaining.Add(entry);
                    ModeHRewardItemPool.DestroyUngranted(prize);
                }
            }
            return remaining;
        }

        /// <summary>清理尚未交付的试生成奖品；真正的交付由已保存的计划驱动。</summary>
        internal static void DiscardPrizes(List<Item> prizes)
        {
            for (int i = 0; prizes != null && i < prizes.Count; i++) ModeHRewardItemPool.DestroyUngranted(prizes[i]);
        }

        #endregion

        #region 引用

        /// <summary>赢了、退回或结算完：丢掉本场押注的引用，物品一件不动。</summary>
        internal static void ReleaseLocked()
        {
            _locked.Clear();
            _forfeited = false;
            _forfeitMissing = 0;
        }

        #endregion

        /// <summary>模块销毁：丢掉全部引用（§4.6）。</summary>
        internal static void ResetStaticCaches()
        {
            _selected.Clear();
            ReleaseLocked();
        }
    }
}
