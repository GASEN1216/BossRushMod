// ============================================================================
// PetNestPetProxyBridge.cs - 遗种巢「捡漏背包」官方 PetProxy 借席桥
// ============================================================================
// 全案唯一的反射写点（契约面登记见 docs/contracts.md，版本升级检查单同步）：
//   AccessTools.Field(typeof(LevelManager), "petCharacter")
//
// 为什么要写它：
//   官方 PetProxy.Update（PetProxy.cs:110-127）被 LevelManager.PetCharacter 门控，
//   命中后做两件事——把 PetProxy 自身 transform 贴到宠物位置、每秒把 Inventory 容量
//   同步成玩家的 PetCapcity。PetCharacter 的 backing field 是
//   `private CharacterMainControl petCharacter;`（LevelManager.cs:785），
//   只有 `public CharacterMainControl PetCharacter { get; }`（:157），无 setter。
//
// 【实装期实测修正 · 与实施计划原文不同，须 owner 知悉】
//   实施计划写「petCharacter 全源码无可见赋值点」，据此定下「官方宠物在场则 mod 让位」。
//   实际核实（.codex_tmp/core_decomp/LevelManager.cs:402，ILSpy 现代语法反编译还原了被
//   旧反编译器吞掉的 async 状态机）：
//       petCharacter = await petPreset.CreateCharacterAsync(
//           mainCharacter.transform.position + Vector3.one * 99f, ...);
//   即**每一张图的关卡初始化都会创建官方宠物并占席**，席位永不为空。
//   若照字面执行「在场就让位」，捡漏背包将永远不激活、整条功能死掉。
//   因此本桥实现的是「借席不夺席」：
//     - 只在非基地图借席（基地由官方 PetHouse 驱动宠物驻留位，不碰）；
//     - 借席前记录原占位者，离场/死亡/切图必然还原；
//     - 还席时若席位已不是我们的随从（官方或他方收回了席位），一律不覆盖；
//     - 反射解析失败、LevelManager 缺失、任何异常 -> fail-closed：随从无背包，不崩。
//   官方宠物本身不受影响：它仍然存在、仍然跟随玩家，只是借席期间 PetProxy 的
//   位置耦合指向随从。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 官方 PetProxy 席位借用桥。占席 / 让席 / 还原三态，全程 no-throw。
    /// </summary>
    internal static class PetNestPetProxyBridge
    {
        #region 反射解析（唯一反射写点）

        /// <summary>反射目标字段名。版本升级检查单据此复查。</summary>
        internal const string PetCharacterFieldName = "petCharacter";

        private static FieldInfo _petCharacterField;
        private static bool _fieldResolveAttempted;

        /// <summary>
        /// 惰性解析 LevelManager.petCharacter 字段。解析失败只记一次日志，
        /// 之后恒返回 null（fail-closed：随从无背包，不崩）。
        /// </summary>
        private static FieldInfo ResolvePetCharacterField()
        {
            if (_fieldResolveAttempted) return _petCharacterField;
            _fieldResolveAttempted = true;
            try
            {
                _petCharacterField = AccessTools.Field(typeof(LevelManager), PetCharacterFieldName);
                if (_petCharacterField == null)
                {
                    ModBehaviour.DevLog("[PetNest] [WARNING] 未找到 LevelManager." + PetCharacterFieldName
                        + " 字段，捡漏背包 fail-closed 关闭");
                }
                else if (_petCharacterField.FieldType != typeof(CharacterMainControl))
                {
                    ModBehaviour.DevLog("[PetNest] [WARNING] LevelManager." + PetCharacterFieldName
                        + " 字段类型已变更，捡漏背包 fail-closed 关闭");
                    _petCharacterField = null;
                }
            }
            catch (Exception e)
            {
                _petCharacterField = null;
                ModBehaviour.DevLog("[PetNest] [WARNING] 解析 LevelManager." + PetCharacterFieldName
                    + " 失败: " + e.Message);
            }
            return _petCharacterField;
        }

        #endregion

        #region 状态

        private static readonly object _lock = new object();
        private static CharacterMainControl _previousOccupant;
        private static CharacterMainControl _borrowedFor;
        private static bool _seatBorrowed;
        private static string _lastYieldReason;

        /// <summary>当前是否持有席位。</summary>
        internal static bool HasBorrowedSeat { get { return _seatBorrowed; } }

        /// <summary>最后一次让席原因（诊断用；null 表示未让席）。</summary>
        internal static string LastYieldReason { get { return _lastYieldReason; } }

        #endregion

        #region 借席 / 还席

        /// <summary>
        /// 为随从借用官方宠物席位。成功返回 true；让席或失败返回 false 并写 yieldReason。
        /// 幂等：同一随从重复调用直接返回 true。
        /// </summary>
        internal static bool TryBorrowSeat(CharacterMainControl companion, out string yieldReason)
        {
            yieldReason = null;
            if (companion == null)
            {
                yieldReason = "companion_null";
                _lastYieldReason = yieldReason;
                return false;
            }

            FieldInfo field = ResolvePetCharacterField();
            if (field == null)
            {
                yieldReason = "reflection_unavailable";
                _lastYieldReason = yieldReason;
                return false;
            }

            LevelManager level = null;
            try { level = LevelManager.Instance; }
            catch (Exception) { level = null; }
            if (level == null)
            {
                yieldReason = "level_manager_missing";
                _lastYieldReason = yieldReason;
                return false;
            }

            // 让位规则一：基地图由官方 PetHouse 驱动宠物驻留位，mod 一律不借席。
            try
            {
                if (level.IsBaseLevel)
                {
                    yieldReason = "base_level_official_pet_priority";
                    _lastYieldReason = yieldReason;
                    return false;
                }
            }
            catch (Exception)
            {
                yieldReason = "base_level_query_failed";
                _lastYieldReason = yieldReason;
                return false;
            }

            lock (_lock)
            {
                if (_seatBorrowed && _borrowedFor == companion)
                {
                    return true;
                }

                CharacterMainControl current = null;
                try { current = field.GetValue(level) as CharacterMainControl; }
                catch (Exception e)
                {
                    yieldReason = "seat_read_failed:" + e.GetType().Name;
                    _lastYieldReason = yieldReason;
                    return false;
                }

                // 让位规则二：席位已经被另一只遗种巢随从占着（单席契约被破坏）时不抢。
                if (current != null && current != companion
                    && PetNestCompanionAgent.IsCompanionCharacter(current))
                {
                    yieldReason = "seat_held_by_other_companion";
                    _lastYieldReason = yieldReason;
                    return false;
                }

                try
                {
                    field.SetValue(level, companion);
                }
                catch (Exception e)
                {
                    yieldReason = "seat_write_failed:" + e.GetType().Name;
                    _lastYieldReason = yieldReason;
                    return false;
                }

                // 借席不夺席：原占位者（官方宠物）引用留存，离场时原样还回去。
                _previousOccupant = current;
                _borrowedFor = companion;
                _seatBorrowed = true;
                _lastYieldReason = null;
            }

            ModBehaviour.DevLog("[PetNest] 已借用官方宠物席位，捡漏背包随随从移动");
            return true;
        }

        /// <summary>
        /// 还席。只在席位仍然是我们借出去的那只随从时才写回原占位者；
        /// 否则说明官方或他方已经收回席位，一律不覆盖。幂等。
        /// </summary>
        internal static void ReleaseSeat()
        {
            lock (_lock)
            {
                if (!_seatBorrowed)
                {
                    _previousOccupant = null;
                    _borrowedFor = null;
                    return;
                }

                try
                {
                    FieldInfo field = ResolvePetCharacterField();
                    LevelManager level = null;
                    try { level = LevelManager.Instance; }
                    catch (Exception) { level = null; }

                    if (field != null && level != null)
                    {
                        CharacterMainControl current = null;
                        try { current = field.GetValue(level) as CharacterMainControl; }
                        catch (Exception) { current = null; }

                        if (current == _borrowedFor)
                        {
                            field.SetValue(level, _previousOccupant);
                        }
                        else
                        {
                            ModBehaviour.DevLog("[PetNest] 还席时席位已被他方接管，保持现状不覆盖");
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[PetNest] [WARNING] 还席失败: " + e.Message);
                }
                finally
                {
                    _seatBorrowed = false;
                    _previousOccupant = null;
                    _borrowedFor = null;
                }
            }
        }

        #endregion

        #region 容量天赋 Modifier（挂玩家，不挂宠物）

        /// <summary>
        /// 容量 Modifier 的 source tag。source-tagged 模式（照 EndowmentEntry.ApplyModifiers），
        /// 离场时用 Item.RemoveAllModifiersFrom(source) 整组摘除，不逐条记账。
        /// </summary>
        internal static readonly object CapacityModifierSource = new object();

        private static bool _capacityBonusApplied;

        /// <summary>当前挂着的额外格子数（重复应用时保持幂等）。</summary>
        private static int _capacityBonusSlots;
        private static Item _capacityOwner;
        private static Inventory _capacityInventory;

        /// <summary>当前是否挂着容量加成。</summary>
        internal static bool HasCapacityBonus { get { return _capacityBonusApplied; } }

        /// <summary>
        /// 给玩家挂随从带来的额外背包格子。
        ///
        /// 关键：官方 PetProxy 每秒读的是 **CharacterMainControl.Main.PetCapcity**
        /// （PetProxy.cs:122-125），也就是**玩家**角色 Item 上的 stat，不是宠物的。
        /// 因此 Modifier 必须挂在玩家身上，挂宠物完全无效。
        ///
        /// 另外 PetCapcity 是格子数（官方 Mathf.RoundToInt），必须用 ModifierType.Add
        /// 常量加；用百分比在基础值为 0 时永远加不出格子。
        /// </summary>
        internal static bool ApplyCapacityBonus(CharacterMainControl player, int extraSlots)
        {
            if (player == null || extraSlots <= 0) return false;
            try
            {
                Item characterItem = player.CharacterItem;
                if (characterItem == null) return false;
                Inventory inventory = PetProxy.PetInventory;
                if (inventory == null || inventory.Loading) return false;
                if (_capacityBonusApplied && _capacityOwner == characterItem
                    && _capacityInventory == inventory && _capacityBonusSlots == extraSlots)
                {
                    inventory.SetCapacity(player.PetCapcity);
                    return true;
                }
                if (_capacityBonusApplied)
                    RemoveCapacityBonus(player);

                // 幂等：先摘干净再挂，避免切图重挂叠加
                characterItem.RemoveAllModifiersFrom(CapacityModifierSource);

                Modifier modifier = new Modifier(ModifierType.Add, extraSlots, CapacityModifierSource);
                if (!characterItem.AddModifier(PetCapacityStatKey, modifier))
                {
                    Debug.LogWarning("[PetNest] 玩家没有 " + PetCapacityStatKey
                        + " stat，捡漏背包容量加成未生效");
                    return false;
                }

                _capacityBonusApplied = true;
                _capacityBonusSlots = extraSlots;
                _capacityOwner = characterItem;
                _capacityInventory = inventory;
                // 官方 PetProxy 按秒同步。这里即时同步同一份 Inventory，让打开中的
                // 官方背包也当帧重铺格子，随后继续由官方按玩家 PetCapcity 维护。
                inventory.SetCapacity(player.PetCapcity);
                Debug.Log("[PetNest] 捡漏背包已同步：额外 " + extraSlots
                    + " 格，官方宠物背包总容量 " + inventory.Capacity);
                return true;
            }
            catch (Exception e)
            {
                RemoveCapacityBonus(player);
                Debug.LogWarning("[PetNest] 挂容量 Modifier 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>摘除容量加成。幂等；player 为空时也把标记清掉。</summary>
        internal static void RemoveCapacityBonus(CharacterMainControl player)
        {
            if (!_capacityBonusApplied)
            {
                return;
            }
            Item characterItem = _capacityOwner;
            Inventory inventory = _capacityInventory;
            // 官方切图先保存安全箱、再销毁旧主角和背包。新场景可能已经从该快照
            // 读出额外槽物品；等加载完成，按新主角真实容量回收超出的部分。
            // 不用旧 extraSlots 去减新图容量，也不删除仍合法的格子。
            if (inventory == null)
            {
                if (!LevelManager.AfterInit || player == null || player.CharacterItem == null
                    || PetProxy.PetInventory == null || PetProxy.PetInventory.Loading) return;
                if (characterItem != null) characterItem.RemoveAllModifiersFrom(CapacityModifierSource);
                characterItem = player.CharacterItem;
                inventory = PetProxy.PetInventory;
            }
            _capacityBonusApplied = false;
            _capacityOwner = null;
            _capacityInventory = null;

            // 先摘属性取得真实目标容量，再清溢出，最后收缩同一份背包。
            // Inventory.SetCapacity 只改 defaultCapacity、**不动 content**：
            // 物品没被销毁，但超出新容量后在宠物保险箱 UI 里既看不见也取不出，
            // 玩家会当成东西丢了。交官方溢出缓冲区是既有解法（同 ModeH 押品满仓那条出路）。
            try
            {
                if (characterItem == null) return;
                characterItem.RemoveAllModifiersFrom(CapacityModifierSource);
                // 按卸下本来源后真实的 stat 取容量，避免官方尚未同步或其他装备
                // 同时变动时，用当前背包容量减 extraSlots 算错溢出范围。
                int targetCapacity = Math.Max(0, Mathf.RoundToInt(characterItem.GetStatValue(PetCapacityStatKey.GetHashCode())));
                RescuePetInventoryOverflow(inventory, targetCapacity);
                if (inventory != null) inventory.SetCapacity(targetCapacity);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 摘容量 Modifier 失败: " + e.Message);
            }
            finally { _capacityBonusSlots = 0; }
        }

        /// <summary>
        /// 把宠物背包里超出「摘掉容量加成后」那部分槽位的物品捞出来，交官方溢出缓冲区。
        ///
        /// 判据用摘除后的真实 stat 容量；切图后的恢复只处理超出新主角真实容量的物品。
        /// no-throw：捞不出来也不阻断离场流程，最坏是回到原来的「看不见」状态。
        /// </summary>
        private static void RescuePetInventoryOverflow(Inventory petInventory, int targetCapacity)
        {
            try
            {
                if (petInventory == null) return;

                List<Item> content = petInventory.Content;
                if (content == null) return;

                int rescued = 0;
                for (int i = content.Count - 1; i >= targetCapacity; i--)
                {
                    if (i >= content.Count) continue;
                    Item item = content[i];
                    if (item == null) continue;
                    int countBefore = PlayerStorage.IncomingItemBuffer.Count;
                    try
                    {
                        // Push 先记入缓冲，再 Detach。不要先手动 RemoveItem：官方
                        // 容器通知抛错时，先移出会留下没有任何容器持有的孤儿物品。
                        PlayerStorage.Push(item, true);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[PetNest] [WARNING] 捡漏背包越界物品回收失败: " + e.Message);
                    }
                    if (PlayerStorage.IncomingItemBuffer.Count <= countBefore) continue;
                    rescued++;
                    // 缓冲已接收而 Detach 的通知抛错时，完成剩余清理，不再推第二份。
                    if (item != null)
                    {
                        try { item.Detach(); } catch (Exception) { }
                        try { item.DestroyTree(); } catch (Exception) { }
                    }
                }
                if (rescued > 0)
                {
                    ModBehaviour.DevLog(
                        "[PetNest] 随从离场，" + rescued + " 件越界物品已转入仓库待收取缓冲区");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 捡漏背包越界检查失败: " + e.Message);
            }
        }

        /// <summary>
        /// 官方 stat key。注意官方拼写就是 PetCapcity（少一个 a），
        /// 全库唯一拼法，改成 PetCapacity 会静默失效。
        /// </summary>
        internal const string PetCapacityStatKey = "PetCapcity";

        #endregion

        #region 诊断

        /// <summary>
        /// 席位现状快照（PoC 探针与诊断页使用）。不写任何状态。
        /// </summary>
        internal static string DescribeSeat()
        {
            try
            {
                FieldInfo field = ResolvePetCharacterField();
                if (field == null) return "petCharacter 字段不可用（fail-closed）";

                LevelManager level = null;
                try { level = LevelManager.Instance; }
                catch (Exception) { level = null; }
                if (level == null) return "LevelManager 未就绪";

                CharacterMainControl current = field.GetValue(level) as CharacterMainControl;
                string who;
                if (current == null)
                {
                    who = "空";
                }
                else if (PetNestCompanionAgent.IsCompanionCharacter(current))
                {
                    who = "遗种巢随从(" + current.gameObject.name + ")";
                }
                else
                {
                    who = "官方宠物(" + current.gameObject.name + ")";
                }

                string proxy;
                try
                {
                    proxy = PetProxy.PetInventory != null
                        ? ("容量=" + PetProxy.PetInventory.Capacity)
                        : "PetInventory 为空";
                }
                catch (Exception)
                {
                    proxy = "PetInventory 读取失败";
                }

                return "席位=" + who
                    + "，借席中=" + (_seatBorrowed ? "是" : "否")
                    + "，" + proxy
                    + "，SavePet=" + SafeSavePet();
            }
            catch (Exception e)
            {
                return "席位快照失败: " + e.Message;
            }
        }

        private static string SafeSavePet()
        {
            try { return LevelConfig.SavePet ? "true" : "false"; }
            catch (Exception) { return "unknown"; }
        }

        #endregion

        #region 清理

        /// <summary>清空静态状态（Mod 卸载 / 宿主重建 / 静态缓存重置）。</summary>
        internal static void ResetStaticCaches()
        {
            RemoveCapacityBonus(SafeMainCharacter());
            _capacityBonusApplied = false;
            _capacityBonusSlots = 0;
            _capacityOwner = null;
            _capacityInventory = null;
            lock (_lock)
            {
                _seatBorrowed = false;
                _previousOccupant = null;
                _borrowedFor = null;
                _lastYieldReason = null;
            }
        }

        private static CharacterMainControl SafeMainCharacter()
        {
            try { return CharacterMainControl.Main; }
            catch (Exception) { return null; }
        }

        #endregion
    }
}
