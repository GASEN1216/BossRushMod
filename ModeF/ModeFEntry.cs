using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F 入口

        /// <summary>
        /// 检测玩家背包中是否存在血猎收发器
        /// </summary>
        private Item DetectBloodhuntTransponder()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return null;

                Item characterItem = main.CharacterItem;
                if (characterItem == null) return null;

                Inventory inventory = characterItem.Inventory;
                if (inventory == null || inventory.Content == null) return null;

                for (int i = 0; i < inventory.Content.Count; i++)
                {
                    Item item = inventory.Content[i];
                    if (item == null) continue;

                    int typeId = -1;
                    try { typeId = item.TypeID; } catch { continue; }

                    if (typeId == BloodhuntTransponderConfig.TYPE_ID)
                    {
                        DevLog("[ModeF] 检测到血猎收发器 (TypeID=" + typeId + ")");
                        return item;
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] DetectBloodhuntTransponder 失败: " + e.Message);
                return null;
            }
        }

        private Item DetectBossRushTicketItem()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || main.CharacterItem == null)
                {
                    return null;
                }

                Inventory inventory = main.CharacterItem.Inventory;
                if (inventory == null || inventory.Content == null)
                {
                    return null;
                }

                int allowedTicketTypeId = bossRushTicketTypeId > 0 ? bossRushTicketTypeId : 868;
                for (int i = 0; i < inventory.Content.Count; i++)
                {
                    Item item = inventory.Content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    int typeId = -1;
                    try { typeId = item.TypeID; } catch { }
                    if (typeId == allowedTicketTypeId)
                    {
                        return item;
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] DetectBossRushTicketItem 失败: " + e.Message);
                return null;
            }
        }

        private bool IsPlayerNakedForModeF()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || main.CharacterItem == null)
                {
                    return false;
                }

                Item characterItem = main.CharacterItem;
                string[] equipmentSlots = new string[]
                {
                    "Armor",
                    "Helmat",
                    "FaceMask",
                    "Backpack",
                    "Headset",
                    "Totem1",
                    "Totem2"
                };
                for (int i = 0; i < equipmentSlots.Length; i++)
                {
                    try
                    {
                        var slot = characterItem.Slots.GetSlot(equipmentSlots[i]);
                        if (slot != null && slot.Content != null)
                        {
                            return false;
                        }
                    }
                    catch { }
                }

                string[] weaponSlots = new string[] { "PrimaryWeapon", "SecondaryWeapon", "MeleeWeapon" };
                for (int i = 0; i < weaponSlots.Length; i++)
                {
                    try
                    {
                        var slot = characterItem.Slots.GetSlot(weaponSlots[i]);
                        if (slot != null && slot.Content != null)
                        {
                            return false;
                        }
                    }
                    catch { }
                }

                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    int allowedTicketTypeId = bossRushTicketTypeId > 0 ? bossRushTicketTypeId : 868;
                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        Item item = inventory.Content[i];
                        if (item == null)
                        {
                            continue;
                        }

                        int typeId = -1;
                        try { typeId = item.TypeID; } catch { }

                        if (typeId == allowedTicketTypeId || typeId == BloodhuntTransponderConfig.TYPE_ID)
                        {
                            continue;
                        }

                        return false;
                    }
                }

                try
                {
                    Inventory petInventory = PetProxy.PetInventory;
                    if (petInventory != null && petInventory.Content != null)
                    {
                        for (int i = 0; i < petInventory.Content.Count; i++)
                        {
                            if (petInventory.Content[i] != null)
                            {
                                return false;
                            }
                        }
                    }
                }
                catch (Exception petEx)
                {
                    DevLog("[ModeF] [WARNING] 无法检查狗子背包: " + petEx.Message);
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] IsPlayerNakedForModeF 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 检测并尝试启动 Mode F
        /// 条件：裸装 + 船票 + 血猎收发器 + 无营旗
        /// </summary>
        public bool TryStartModeF()
        {
            try
            {
                if (modeDActive || modeEActive)
                {
                    DevLog("[ModeF] Mode D 或 Mode E 已激活，跳过 Mode F 启动");
                    return false;
                }

                Item ticket = DetectBossRushTicketItem();
                Item transponder = DetectBloodhuntTransponder();
                if (ticket == null || transponder == null)
                {
                    DevLog("[ModeF] 未检测到船票或血猎收发器，不启动 Mode F");
                    return false;
                }

                var (faction, flagItem) = DetectFactionFlag();
                if (faction.HasValue || flagItem != null)
                {
                    DevLog("[ModeF] 检测到营旗，按优先级不进入 Mode F");
                    return false;
                }

                if (!IsPlayerNakedForModeF())
                {
                    DevLog("[ModeF] 玩家不满足裸装条件，拒绝启动");
                    ShowMessage(L10n.T(
                        "血猎追击模式需要裸装入场！请清空所有装备后重试。",
                        "Bloodhunt mode requires naked entry! Please remove all equipment."
                    ));
                    return false;
                }

                bool transponderConsumed = false;
                try
                {
                    transponder.Detach();
                    transponder.DestroyTree();
                    transponderConsumed = true;
                    DevLog("[ModeF] 血猎收发器已消耗");
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] [WARNING] 消耗血猎收发器失败: " + e.Message);
                }

                bool ticketConsumed = false;
                try
                {
                    ticket.Detach();
                    ticket.DestroyTree();
                    ticketConsumed = true;
                    DevLog("[ModeF] 船票已消耗");
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] [WARNING] 消耗船票失败: " + e.Message);
                }

                if (!transponderConsumed || !ticketConsumed)
                {
                    ShowMessage(L10n.T(
                        "血猎追击模式启动失败：入场道具消耗异常。",
                        "Bloodhunt start failed: unable to consume the entry items."
                    ));
                    return false;
                }

                StartModeF();
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] TryStartModeF 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 启动 Mode F 模式
        /// </summary>
        private void StartModeF()
        {
            try
            {
                DevLog("[ModeF] 启动 Mode F 血猎追击模式");

                modeFActive = true;
                modeFState.Reset();
                modeFState.IsActive = true;
                ClearEnemyRecoveryMonitorState();

                // 初始化物品池和敌人池（复用 Mode D 逻辑）
                InitializeModeDItemPools();
                InitializeModeDEnemyPools();
                EnsureModeDGlobalItemPool();

                // 订阅龙息Buff处理器
                DragonBreathBuffHandler.Subscribe();

                // 分配刷怪点（复用 Mode E 逻辑）
                PreCacheMapSpawnerPositions();
                AllocateSpawnPoints();

                // 传送玩家到安全位置
                TeleportPlayerToSafePosition();

                // 发放初始装备（复用 Mode D 的 Starter Kit）
                GivePlayerStarterKit();

                // 零度挑战地图：额外发放保暖装备
                ModeEGiveColdWeatherGear();

                // 额外发放折叠掩体包 x1
                try
                {
                    Item coverPack = ItemAssetsCollection.InstantiateSync(FoldableCoverPackConfig.TYPE_ID);
                    if (coverPack != null)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(coverPack, false);
                        DevLog("[ModeF] 发放折叠掩体包 x1");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] [WARNING] 发放折叠掩体包失败: " + e.Message);
                }

                // 快照初始最大生命值
                try
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && player.Health != null)
                    {
                        modeFState.InitialMaxHealthSnapshot = player.Health.MaxHealth;
                        DevLog("[ModeF] 初始最大生命快照: " + modeFState.InitialMaxHealthSnapshot);
                    }
                }
                catch { }

                // 清除原始撤离点
                ClearOriginalExtractionPoints();

                // 一次性生成所有 Boss（复用 Mode E 逻辑）
                #pragma warning disable CS4014
                ModeESpawnAllBosses();
                #pragma warning restore CS4014

                // 生成神秘商人 NPC
                #pragma warning disable CS4014
                SpawnModeEMerchant();
                #pragma warning restore CS4014

                // 生成快递员
                SpawnCourierNPC();

                // 启动状态机
                StartModeFRun();

                ShowMessage(L10n.T(
                    "血猎追击模式已激活！持续掉血，击杀Boss回血续命！",
                    "Bloodhunt mode activated! You're bleeding out - kill bosses to survive!"
                ));
                ShowBigBanner(L10n.T(
                    "欢迎来到 <color=red>血猎追击</color>！",
                    "Welcome to <color=red>Bloodhunt</color>!"
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] StartModeF 失败: " + e.Message);
            }
        }

        #endregion
    }
}
