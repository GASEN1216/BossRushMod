// BossRushEquipmentRescue.cs - 读档时没装上的 Mod 装备：放回原槽，或原样保住记录
//
// 官方 ItemTreeData.InstantiateAsync（ItemStatsSystem.dll 里的状态机，2026-11-26 用 ikdasm 核对 IL）：
// 查不到 prefab 的节点先造一个保留 TypeID 的 FallbackItem_<id> 空壳；往父物品挂槽时 Slot.Plug 的返回值
// 直接丢弃，插不进去只打一句 "Found Unplugged Item while Loading Item Tree!"。装备槽要求标签，空壳插不进去，
// 这件装备连同配件、它背包里的东西就成了不在任何物品树里的孤儿。官方下次 SaveMainCharacter
// （撤离、死亡、切场景前，反编译源 LevelManager.cs）只写实际挂在角色身上的东西，这条记录就没了。
//
// 什么时候会走到这里：
// 1. 本 Mod 激活得比读档晚。创意工坊 Mod 要等 Steam 查询回调才激活（Duckov/Modding/SteamWorkshopManager.cs），
//    网络慢时玩家已经点了「继续」进档，读档时没有任何 Mod 补丁在场。
// 2. 本 Mod 在场，但读档时某件装备一时注册不上（DynamicItemRegistrationPatchSupport.NeedsFallback 为真）。
//
// 怎么处理：只看主角。读出存档里那份还没被覆盖的角色记录（Item/MainCharacterItemData），按槽位逐层对照
// 活着的角色，找出「记录里有 Mod 装备、现在那个槽是空的」的位置。必须同时找得到读档留下的同 TypeID 空壳孤儿：
// 孤儿只可能来自读档失败，以它为证据，避免把玩家本局主动卸下、卖掉的装备再变出一份。
// 然后用官方 ItemTreeData.InstantiateAsync 从记录重建整棵子树（耐久、配件、背包内容都在记录里）。重建要等几帧，
// 等完先核对存档槽、关卡、主角与角色物品都没变，变了就销毁重建品、整轮作废，不把旧档装备送给新档角色。插回原槽；
// 插不回就进背包，再不行进仓库。资源仍注册不上时，重建出的仍是空壳：送进仓库（官方 PlayerStorage 会把它
// 整棵序列化保存），原记录随存档保住，下次资源就绪读档即恢复。旧孤儿随即销毁，避免一份记录两份实物。
//
// 成本：正常读档（全部注册成功、Mod 按时激活）一次都不跑；只在上面两种情况各跑一次，先查记录，
// 有候选才扫一遍已加载场景里的 Item（不含预制体资源）。
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal static class BossRushEquipmentRescue
    {
        /// <summary>官方 LevelManager.MainCharacterItemSaveKey 与 ItemSavesUtilities 的 "Item/" 前缀。</summary>
        private const string CharacterItemSavePrefix = "Item/";
        private const string CharacterItemSaveKey = "MainCharacterItemData";
        private const string FallbackNamePrefix = "FallbackItem_";
        /// <summary>等官方关卡初始化完成的帧数上限；读条再慢也不该超过这个量级，超了就放弃本次。</summary>
        private const int LevelWaitFrameLimit = 3600;
        private const int MaxSlotDepth = 8;

        private sealed class Candidate
        {
            internal Item LiveParent;
            internal string SlotKey;
            internal ItemTreeData.DataEntry Entry;
        }

        private static ModBehaviour owner;
        private static bool levelSubscribed;
        private static bool loadFailureNoted;
        private static bool running;
        private static int generation;

        /// <summary>
        /// 宿主 Awake 收尾时调用。characterAlreadyLoaded = 激活时已经身处关卡场景（迟到激活，关卡还在加载也算）：
        /// 读档可能在补丁装上之前就失败了，不会留下 NoteUnresolvedLoad 的记号，所以等关卡初始化完成后无条件查一次。
        /// </summary>
        internal static void Attach(ModBehaviour host, bool characterAlreadyLoaded)
        {
            owner = host;
            if (!levelSubscribed)
            {
                try
                {
                    LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized;
                    levelSubscribed = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BossRush] 装备抢救订阅关卡事件失败: " + e.Message);
                }
            }
            if (characterAlreadyLoaded) Schedule("late-activation");
        }

        /// <summary>读档路径上有 Mod 物品注册失败、只能交出空壳时调用；本关卡初始化完成后检查一次。</summary>
        internal static void NoteUnresolvedLoad()
        {
            loadFailureNoted = true;
        }

        internal static void Shutdown()
        {
            if (levelSubscribed)
            {
                try { LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized; }
                catch (Exception e) { ModBehaviour.DevLog("[BossRush] 装备抢救退订失败: " + e.Message); }
                levelSubscribed = false;
            }
            owner = null;
            loadFailureNoted = false;
            running = false;
            generation++;
        }

        private static void OnAfterLevelInitialized()
        {
            if (!loadFailureNoted) return;
            loadFailureNoted = false;
            Schedule("load-unresolved");
        }

        private static void Schedule(string reason)
        {
            if (owner == null || running) return;
            running = true;
            RunAsync(owner, ++generation, reason).Forget();
        }

        private static bool Alive(ModBehaviour host, int gen)
        {
            return host != null && ReferenceEquals(owner, host) && gen == generation;
        }

        private static async UniTaskVoid RunAsync(ModBehaviour host, int gen, string reason)
        {
            try
            {
                for (int i = 0; i < LevelWaitFrameLimit && !LevelManager.LevelInited; i++)
                {
                    await UniTask.NextFrame();
                    if (!Alive(host, gen)) return;
                }
                // 再让一帧：关卡初始化回调与本 Mod 的场景补装都在这一帧收尾。
                await UniTask.NextFrame();
                if (!Alive(host, gen) || !LevelManager.LevelInited) return;
                await RescueMainCharacterAsync(host, gen, reason);
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] 装备抢救异常 reason=" + reason + ": " + e);
            }
            finally
            {
                if (gen == generation) running = false;
            }
        }

        private static async UniTask RescueMainCharacterAsync(ModBehaviour host, int gen, string reason)
        {
            if (!LevelConfig.SaveCharacter) return;
            int slot = SavesSystem.CurrentSlot;
            LevelManager level = LevelManager.Instance;
            CharacterMainControl main = level != null ? level.MainCharacter : null;
            Item root = main != null ? main.CharacterItem : null;
            if (root == null) return;

            ItemTreeData stored = null;
            try { stored = SavesSystem.Load<ItemTreeData>(CharacterItemSavePrefix, CharacterItemSaveKey); }
            catch (Exception e) { Debug.LogWarning("[BossRush] 装备抢救读取角色记录失败: " + e.Message); }
            if (stored == null || stored.entries == null) return;
            ItemTreeData.DataEntry storedRoot = stored.RootData;
            if (storedRoot == null || storedRoot.typeID != root.TypeID) return;

            Dictionary<int, ItemTreeData.DataEntry> lookup = IndexEntries(stored);
            List<Candidate> candidates = new List<Candidate>();
            CollectMissing(lookup, storedRoot, root, candidates, 0);
            if (candidates.Count == 0) return;

            Dictionary<int, List<Item>> orphans = FindOrphanShells();
            int restored = 0, preserved = 0, skipped = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                List<Item> pool;
                if (!orphans.TryGetValue(candidate.Entry.typeID, out pool) || pool.Count == 0)
                {
                    skipped++;
                    continue;
                }
                Item orphan = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);

                Item rebuilt = null;
                try { rebuilt = await ItemTreeData.InstantiateAsync(ExtractSubtree(lookup, candidate.Entry)); }
                catch (Exception e) { Debug.LogWarning("[BossRush] 装备抢救重建失败 TypeID=" + candidate.Entry.typeID + ": " + e.Message); }
                // 重建要等几帧：期间换了存档槽、关卡或主角，这件装备就不属于眼前这个角色了，整轮作废。
                if (!Alive(host, gen) || !SameContext(slot, level, main, root, candidate.LiveParent))
                {
                    if (rebuilt != null) DestroyQuietly(rebuilt);
                    Debug.LogWarning("[BossRush] 装备抢救中止 reason=" + reason + "：重建期间存档槽、关卡或主角已变");
                    return;
                }
                if (rebuilt == null) { skipped++; continue; }

                DestroyQuietly(orphan);
                bool real = !IsFallbackShell(rebuilt);
                if (real && (TryPlug(candidate.LiveParent, candidate.SlotKey, rebuilt)
                    || ItemUtilities.SendToPlayerCharacterInventory(rebuilt, true)))
                {
                    restored++;
                    continue;
                }
                // 空壳或放不回身上：进仓库。仓库满时官方转进快递缓冲，同样按 ItemTreeData 整棵保存。
                ItemUtilities.SendToPlayerStorage(rebuilt, false);
                if (real) restored++; else preserved++;
            }

            Debug.Log("[BossRush] 装备抢救 reason=" + reason + " candidates=" + candidates.Count
                + " restored=" + restored + " preserved=" + preserved + " skipped=" + skipped);
            if (restored > 0)
                NotificationText.Push(string.Format(L10n.T("读档时没装上的 {0} 件 BossRush 装备已经放回。",
                    "{0} BossRush item(s) that failed to load have been put back."), restored));
            if (preserved > 0)
                NotificationText.Push(string.Format(L10n.T("有 {0} 件 BossRush 装备这次读不出来，已原样放进仓库，下次进档会恢复。",
                    "{0} BossRush item(s) couldn't load this time. They were kept in storage as-is and will come back next time you load."), preserved));
        }

        /// <summary>开始重建时的存档槽、关卡、主角与角色物品都没变，原装备槽的父物品也还在。</summary>
        private static bool SameContext(int slot, LevelManager level, CharacterMainControl main, Item root, Item liveParent)
        {
            if (SavesSystem.CurrentSlot != slot) return false;
            if (level == null || !ReferenceEquals(LevelManager.Instance, level)) return false;
            if (main == null || !ReferenceEquals(level.MainCharacter, main)) return false;
            if (root == null || !ReferenceEquals(main.CharacterItem, root)) return false;
            return liveParent != null;
        }

        private static Dictionary<int, ItemTreeData.DataEntry> IndexEntries(ItemTreeData data)
        {
            Dictionary<int, ItemTreeData.DataEntry> lookup = new Dictionary<int, ItemTreeData.DataEntry>();
            for (int i = 0; i < data.entries.Count; i++)
            {
                ItemTreeData.DataEntry entry = data.entries[i];
                if (entry != null && !lookup.ContainsKey(entry.instanceID)) lookup.Add(entry.instanceID, entry);
            }
            return lookup;
        }

        /// <summary>
        /// 按槽位键逐层对照记录与活着的物品树。槽里是同 TypeID 就往下走；槽里换了别的东西说明玩家本局动过，不碰；
        /// 槽空着而记录里是 Mod 物品，就是候选。
        /// </summary>
        private static void CollectMissing(Dictionary<int, ItemTreeData.DataEntry> lookup, ItemTreeData.DataEntry storedParent,
            Item liveParent, List<Candidate> output, int depth)
        {
            if (storedParent.slotContents == null || liveParent == null || depth > MaxSlotDepth) return;
            for (int i = 0; i < storedParent.slotContents.Count; i++)
            {
                ItemTreeData.SlotInstanceIDPair pair = storedParent.slotContents[i];
                ItemTreeData.DataEntry child;
                if (pair == null || string.IsNullOrEmpty(pair.slot) || !lookup.TryGetValue(pair.instanceID, out child) || child == null) continue;
                Slot slot = liveParent.Slots != null ? liveParent.Slots[pair.slot] : null;
                Item liveChild = slot != null ? slot.Content : null;
                if (liveChild != null)
                {
                    if (liveChild.TypeID == child.typeID) CollectMissing(lookup, child, liveChild, output, depth + 1);
                    continue;
                }
                if (slot != null && BossRushDynamicItemRegistry.IsBossRushDynamicItemType(child.typeID))
                    output.Add(new Candidate { LiveParent = liveParent, SlotKey = pair.slot, Entry = child });
            }
        }

        /// <summary>从记录里摘出以 entry 为根的整棵子树（槽位与背包里的后代都带上），交给官方加载器重建。</summary>
        private static ItemTreeData ExtractSubtree(Dictionary<int, ItemTreeData.DataEntry> lookup, ItemTreeData.DataEntry entry)
        {
            ItemTreeData sub = new ItemTreeData { rootInstanceID = entry.instanceID };
            HashSet<int> visited = new HashSet<int>();
            Stack<ItemTreeData.DataEntry> pending = new Stack<ItemTreeData.DataEntry>();
            pending.Push(entry);
            while (pending.Count > 0)
            {
                ItemTreeData.DataEntry current = pending.Pop();
                if (current == null || !visited.Add(current.instanceID)) continue;
                sub.entries.Add(current);
                if (current.slotContents != null)
                {
                    for (int i = 0; i < current.slotContents.Count; i++)
                    {
                        ItemTreeData.DataEntry child;
                        if (current.slotContents[i] != null && lookup.TryGetValue(current.slotContents[i].instanceID, out child))
                            pending.Push(child);
                    }
                }
                if (current.inventory != null)
                {
                    for (int i = 0; i < current.inventory.Count; i++)
                    {
                        ItemTreeData.DataEntry child;
                        if (current.inventory[i] != null && lookup.TryGetValue(current.inventory[i].instanceID, out child))
                            pending.Push(child);
                    }
                }
            }
            return sub;
        }

        /// <summary>读档留下的 Mod 空壳孤儿：名字是官方 FallbackItem_&lt;TypeID&gt;，不在任何槽位或背包里。</summary>
        private static Dictionary<int, List<Item>> FindOrphanShells()
        {
            Dictionary<int, List<Item>> result = new Dictionary<int, List<Item>>();
            // 只扫已加载场景里的对象（含未激活），不连同物品预制体资源一起过一遍；不排序。
            Item[] all = UnityEngine.Object.FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Item item = all[i];
                if (item == null || !item.gameObject.scene.IsValid()) continue;
                if (!BossRushDynamicItemRegistry.IsBossRushDynamicItemType(item.TypeID) || !IsFallbackShell(item)) continue;
                if (item.ParentObject != null || item.InInventory != null || item.PluggedIntoSlot != null) continue;
                List<Item> pool;
                if (!result.TryGetValue(item.TypeID, out pool))
                {
                    pool = new List<Item>();
                    result.Add(item.TypeID, pool);
                }
                pool.Add(item);
            }
            return result;
        }

        private static bool IsFallbackShell(Item item)
        {
            return item != null && item.name == FallbackNamePrefix + item.TypeID;
        }

        private static bool TryPlug(Item parent, string slotKey, Item item)
        {
            if (parent == null || parent.Slots == null || item == null) return false;
            Slot slot = parent.Slots[slotKey];
            if (slot == null || slot.Content != null || !slot.CanPlug(item)) return false;
            Item unplugged;
            return slot.Plug(item, out unplugged) && ReferenceEquals(slot.Content, item);
        }

        private static void DestroyQuietly(Item item)
        {
            try
            {
                if (item != null) item.DestroyTree();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 装备抢救销毁旧对象失败: " + e.Message);
            }
        }
    }
}
