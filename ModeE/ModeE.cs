// ============================================================================
// ModeE.cs - 划地为营模式核心逻辑
// ============================================================================
// 模块说明：
//   Mode E（划地为营）是 BossRush 的多阵营沙盒混战模式。
//   玩家裸装携带"营旗"进入竞技场，系统根据营旗类型分配阵营，
//   地图刷怪点平均分配给所有参战阵营，每个阵营在各自领地一次性生成 Boss。
//   同阵营实体互不伤害，不同阵营自动敌对交战。
//   每当某阵营有单位阵亡，该阵营存活单位属性提升 5%（各阵营独立计算）。
//
// 主要功能：
//   - 入场条件检测（营旗 + 裸装）
//   - 阵营分配（随机/指定）
//   - 阵营气泡显示
//   - 模式启动与结束
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// Mode E（划地为营）：多阵营沙盒混战模式
    /// <para>玩家裸装+营旗入场，分配阵营，Boss一次性生成，按阵营动态缩放</para>
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 状态变量

        /// <summary>是否处于 Mode E 模式</summary>
        private bool modeEActive = false;

        /// <summary>玩家被分配的阵营</summary>
        private Teams modeEPlayerFaction = Teams.player;

        /// <summary>当前所有存活的 Mode E 敌人（跨阵营）</summary>
        private readonly List<CharacterMainControl> modeEAliveEnemies = new List<CharacterMainControl>();

        /// <summary>各阵营死亡计数，用于计算独立缩放倍率</summary>
        private Dictionary<Teams, int> modeEFactionDeathCount = new Dictionary<Teams, int>();

        /// <summary>
        /// [P4性能优化] 按阵营维护的独立存活敌人列表，避免缩放时全量遍历 modeEAliveEnemies
        /// Key = 阵营, Value = 该阵营的存活敌人列表
        /// </summary>
        private readonly Dictionary<Teams, List<CharacterMainControl>> modeEFactionAliveMap
            = new Dictionary<Teams, List<CharacterMainControl>>();

        #endregion

        #region Mode E 配置

        /// <summary>Mode E 可用阵营池（排除 player/middle/all）</summary>
        private static readonly Teams[] ModeEAvailableFactions = new Teams[]
        {
            Teams.scav,   // 拾荒者
            Teams.usec,   // USEC雇佣兵
            Teams.bear,   // BEAR雇佣兵
            Teams.lab,    // 实验室
            Teams.wolf    // 狼群
        };

        /// <summary>随机营旗 TypeID（引用 FactionFlagConfig 常量）</summary>
        private int modeERandomFlagTypeId = FactionFlagConfig.RANDOM_FLAG_TYPE_ID;

        /// <summary>指定阵营营旗 TypeID → Teams 映射（引用 FactionFlagConfig 常量）</summary>
        private Dictionary<int, Teams> modeEFactionFlagMap = new Dictionary<int, Teams>
        {
            { FactionFlagConfig.SCAV_FLAG_TYPE_ID, Teams.scav },     // 拾荒者营旗
            { FactionFlagConfig.USEC_FLAG_TYPE_ID, Teams.usec },     // USEC营旗
            { FactionFlagConfig.BEAR_FLAG_TYPE_ID, Teams.bear },     // BEAR营旗
            { FactionFlagConfig.LAB_FLAG_TYPE_ID,  Teams.lab },      // 实验室营旗
            { FactionFlagConfig.WOLF_FLAG_TYPE_ID, Teams.wolf },     // 狼群营旗
            { FactionFlagConfig.PLAYER_FLAG_TYPE_ID, Teams.player }  // 爷的营旗（独立阵营，敌对所有Boss）
        };

        #endregion

        #region Mode E 公共属性

        /// <summary>是否处于 Mode E 模式</summary>
        public bool IsModeEActive { get { return modeEActive; } }

        /// <summary>玩家当前所属阵营</summary>
        public Teams ModeEPlayerFaction { get { return modeEPlayerFaction; } }

        /// <summary>当前所有存活的 Mode E 敌人列表（只读访问，供龙王等系统查找攻击目标）</summary>
        public List<CharacterMainControl> ModeEAliveEnemies { get { return modeEAliveEnemies; } }

        #endregion

        #region Mode E 核心方法

        /// <summary>
        /// 检测玩家背包中是否存在营旗物品
        /// 返回阵营和对应的 Item 引用；未找到则返回 (null, null)
        /// </summary>
        public (Teams? faction, Item flagItem) DetectFactionFlag()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return (null, null);

                Item characterItem = main.CharacterItem;
                if (characterItem == null) return (null, null);

                Inventory inventory = characterItem.Inventory;
                if (inventory == null || inventory.Content == null) return (null, null);

                // 遍历背包，匹配营旗 TypeID
                for (int i = 0; i < inventory.Content.Count; i++)
                {
                    Item item = inventory.Content[i];
                    if (item == null) continue;

                    int typeId = -1;
                    try { typeId = item.TypeID; } catch { continue; }

                    // 检查是否为随机营旗
                    if (modeERandomFlagTypeId > 0 && typeId == modeERandomFlagTypeId)
                    {
                        // 随机营旗：从可用阵营池中随机选择
                        Teams randomFaction = ModeEAvailableFactions[UnityEngine.Random.Range(0, ModeEAvailableFactions.Length)];
                        DevLog("[ModeE] 检测到随机营旗 (TypeID=" + typeId + ")，随机分配阵营: " + randomFaction);
                        return (randomFaction, item);
                    }

                    // 检查是否为指定阵营营旗
                    Teams assignedFaction;
                    if (modeEFactionFlagMap.TryGetValue(typeId, out assignedFaction))
                    {
                        DevLog("[ModeE] 检测到指定营旗 (TypeID=" + typeId + ")，阵营: " + assignedFaction);
                        return (assignedFaction, item);
                    }
                }

                return (null, null);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] DetectFactionFlag 失败: " + e.Message);
                return (null, null);
            }
        }

        /// <summary>
        /// 消耗（销毁）指定的营旗物品
        /// </summary>
        private void ConsumeFactionFlag(Item flagItem)
        {
            try
            {
                if (flagItem == null)
                {
                    DevLog("[ModeE] [WARNING] ConsumeFactionFlag: flagItem 为 null，跳过消耗");
                    return;
                }

                flagItem.Detach();
                flagItem.DestroyTree();
                DevLog("[ModeE] 营旗已消耗");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ConsumeFactionFlag 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 检查并尝试启动 Mode E（在进入竞技场时调用）
        /// </summary>
        public bool TryStartModeE()
        {
            try
            {
                // 互斥保护：Mode D 已激活时不启动 Mode E
                if (modeDActive)
                {
                    DevLog("[ModeE] Mode D 已激活，跳过 Mode E 启动");
                    return false;
                }

                // 检测营旗
                var (faction, flagItem) = DetectFactionFlag();
                if (!faction.HasValue || flagItem == null)
                {
                    DevLog("[ModeE] 未检测到营旗，不启动 Mode E");
                    return false;
                }

                // 检查裸装条件（复用 Mode D 的裸装检测）
                if (!IsPlayerNaked())
                {
                    DevLog("[ModeE] 玩家不满足裸装条件，拒绝启动");
                    ShowMessage(L10n.T(
                        "划地为营模式需要裸装入场！请清空所有装备后重试。",
                        "Faction Battle requires naked entry! Please remove all equipment."
                    ));
                    return false;
                }

                // 消耗营旗
                ConsumeFactionFlag(flagItem);

                // 启动 Mode E
                StartModeE(faction.Value);
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] TryStartModeE 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 启动 Mode E 模式
        /// </summary>
        private void StartModeE(Teams faction)
        {
            try
            {
                DevLog("[ModeE] 启动 Mode E 模式，阵营: " + faction);

                modeEActive = true;
                modeEPlayerFaction = faction;
                modeEAliveEnemies.Clear();
                modeEFactionDeathCount.Clear();
                modeEFactionAliveMap.Clear();

                // 重置龙裔/龙王全局限制标记
                modeEDragonDescendantSpawned = false;
                modeEDragonKingSpawned = false;

                // 初始化各阵营死亡计数
                for (int i = 0; i < ModeEAvailableFactions.Length; i++)
                {
                    modeEFactionDeathCount[ModeEAvailableFactions[i]] = 0;
                }

                // 设置玩家阵营
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    // 爷的营旗：玩家保持 player 阵营，不需要 SetTeam
                    if (faction != Teams.player)
                    {
                        player.SetTeam(faction);
                        DevLog("[ModeE] 玩家阵营已设置为: " + faction);
                    }
                    else
                    {
                        DevLog("[ModeE] 爷的营旗：玩家保持 player 阵营，所有Boss均为敌对");
                    }
                }

                // 显示阵营气泡
                ShowFactionBubble(faction);

                // 初始化物品池和敌人池（复用 Mode D 逻辑）
                InitializeModeDItemPools();
                InitializeModeDEnemyPools();

                // 前置构建全局掉落池（避免战斗中首次调用时卡顿）
                EnsureModeDGlobalItemPool();

                // Mode E 不激活 BossRush 运行时状态（IsActive 保持 false）
                // 仅订阅龙息Buff处理器，确保龙裔遗族Boss的龙息能触发龙焰灼烧
                DragonBreathBuffHandler.Subscribe();

                // 分配刷怪点给各阵营（优先使用地图配置的 Mode E 专用刷怪点，无配置时兜底使用原地图 spawner 位置）
                AllocateSpawnPoints();

                // 传送玩家到安全位置（远离Boss的安全点）
                TeleportPlayerToSafePosition();

                // 发放初始装备（复用 Mode D 的 Starter Kit）
                GivePlayerStarterKit();

                // 零度挑战地图：额外发放保暖装备（头盔 ID:1312 + 护甲 ID:1307）
                ModeEGiveColdWeatherGear();

                // 独狼阵营：额外发放补给物品（3个id=881 + 3个id=660）
                if (faction == Teams.player)
                {
                    ModeEGiveLoneWolfSupplies();
                }

                // 一次性生成所有阵营的 Boss（UniTaskVoid fire-and-forget，抑制 CS4014 警告）
                #pragma warning disable CS4014
                ModeESpawnAllBosses();
                #pragma warning restore CS4014

                // 生成神秘商人 NPC（fire-and-forget）
                #pragma warning disable CS4014
                SpawnModeEMerchant();
                #pragma warning restore CS4014

                // 在玩家出生点生成快递员阿稳（站在原地不移动）
                SpawnCourierNPC();

                ShowMessage(L10n.T(
                    "划地为营模式已激活！阵营：" + GetFactionDisplayName(faction),
                    "Faction Battle activated! Faction: " + faction.ToString()
                ));
                ShowBigBanner(L10n.T(
                    "欢迎来到 <color=red>划地为营</color>！",
                    "Welcome to <color=red>Faction Battle</color>!"
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] StartModeE 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 在玩家头顶显示阵营气泡（"阵营：xxx"）
        /// </summary>
        private void ShowFactionBubble(Teams faction)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null)
                {
                    DevLog("[ModeE] [WARNING] ShowFactionBubble: 玩家或 transform 为 null");
                    return;
                }

                string factionName = GetFactionDisplayName(faction);
                string bubbleText = L10n.T("阵营：" + factionName, "Faction: " + faction.ToString());

                // 使用游戏原版 DialogueBubblesManager 显示气泡，时长 3 秒
                DialogueBubblesManager.Show(bubbleText, player.transform, 2.5f, false, false, -1f, 3f);
                DevLog("[ModeE] 显示阵营气泡: " + bubbleText);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ShowFactionBubble 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 结束 Mode E 模式
        /// </summary>
        public void EndModeE()
        {
            try
            {
                if (!modeEActive) return;

                DevLog("[ModeE] 结束 Mode E 模式");

                // 先置 modeEActive = false，防止后续 Hurt() 触发的 OnModeEEnemyDeath
                // 回调中再对即将死亡的敌人执行无意义的 ApplyFactionDeathScaling
                modeEActive = false;

                // 恢复玩家阵营
                try
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null)
                    {
                        player.SetTeam(Teams.player);
                        DevLog("[ModeE] 玩家阵营已恢复为 player");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeE] [WARNING] 恢复玩家阵营失败: " + e.Message);
                }

                // 清理所有存活的 Mode E 敌人（优先使用游戏API触发正常死亡流程）
                // [L4修复] 清理前先阻止所有敌人掉落战利品箱子，防止模式结束时友军Boss掉落一堆箱子
                for (int i = modeEAliveEnemies.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        CharacterMainControl enemy = modeEAliveEnemies[i];
                        if (enemy != null && enemy.gameObject != null)
                        {
                            // 阻止掉落战利品箱子（模式结束清理，不应产生掉落物）
                            enemy.dropBoxOnDead = false;

                            // 使用 Health.Hurt() 造成致命伤害，触发正常死亡流程（动画等）
                            Health health = enemy.Health;
                            if (health != null && !health.IsDead)
                            {
                                DamageInfo dmgInfo = new DamageInfo();
                                dmgInfo.damageValue = health.MaxHealth * 10f;
                                dmgInfo.ignoreArmor = true;
                                health.Hurt(dmgInfo);
                            }
                            else
                            {
                                // Health 不可用时回退到直接销毁
                                UnityEngine.Object.Destroy(enemy.gameObject);
                            }
                        }
                    }
                    catch { }
                }

                // 清理神秘商人 NPC
                CleanupModeEMerchant();

                // 清理快递员阿稳 NPC
                DestroyCourierNPC();

                // 重置所有状态（modeEActive 已在清理前置为 false）
                modeEPlayerFaction = Teams.player;
                modeEAliveEnemies.Clear();
                modeEFactionDeathCount.Clear();
                modeEFactionAliveMap.Clear();
                modeEScalingModifiers.Clear();
                modeEPendingScalingFactions.Clear();
                modeEScalingBatchTimer = 0f;
                modeESpawnAllocation = null;
                modeECachedSpawnerPositions = null;
                modeEIntegrityTimer = 0f;

                // 重置龙裔/龙王全局限制标记
                modeEDragonDescendantSpawned = false;
                modeEDragonKingSpawned = false;

                // 重置狼阵营 Boss 计数
                modeEWolfBossCount = 0;
                modeEWolfBossAssigned = 0;

                // 清理虚拟 CharacterSpawnerRoot（BossLiveMapMod 集成）
                CleanupModeEVirtualSpawnerRoot();

                // 清理龙息Buff处理器（防止非 BossRush 场景中意外触发龙焰灼烧）
                DragonBreathBuffHandler.Cleanup();

                ShowMessage(L10n.T(
                    "划地为营模式已结束！",
                    "Faction Battle ended!"
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] EndModeE 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 自检机制

        /// <summary>
        /// Mode E 存活敌人列表自检：清理已死亡/已销毁的敌人引用，补偿丢失的死亡事件
        /// <para>防止敌人死亡事件丢失（瞬杀、事件触发时机等）导致列表残留和缩放计算不准确</para>
        /// </summary>
        private void ModeEIntegrityCheck()
        {
            try
            {
                if (!modeEActive) return;

                int removedCount = 0;
                for (int i = modeEAliveEnemies.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl enemy = modeEAliveEnemies[i];
                    if (enemy == null || enemy.gameObject == null || enemy.Health == null || enemy.Health.IsDead)
                    {
                        // 补偿丢失的死亡事件：递增该阵营死亡计数
                        // [修复] 使用 try-catch 保护 Team 访问，防止 Unity 已销毁对象的假 non-null 引用
                        try
                        {
                            if (enemy != null)
                            {
                                Teams faction = enemy.Team;
                                if (modeEFactionDeathCount.ContainsKey(faction))
                                {
                                    modeEFactionDeathCount[faction]++;
                                }
                                // 从阵营独立列表中移除
                                RemoveFromFactionAliveList(faction, enemy);
                            }
                        }
                        catch { /* Unity 已销毁对象，无法读取 Team，跳过计数补偿 */ }

                        modeEAliveEnemies.RemoveAt(i);
                        modeEScalingModifiers.Remove(enemy);
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    DevLog("[ModeE] 自检清理了 " + removedCount + " 个已死亡/已销毁的敌人引用");
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEIntegrityCheck 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 辅助方法

        /// <summary>
        /// 零度挑战地图专用：发放保暖装备（头盔 + 护甲）
        /// 仅在 Level_ChallengeSnow 场景下生效，硬编码物品ID
        /// </summary>
        private void ModeEGiveColdWeatherGear()
        {
            try
            {
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                // 零度挑战地图和37号实验区都需要发放保暖装备
                if (currentScene != "Level_ChallengeSnow" && currentScene != "Level_SnowMilitaryBase") return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                DevLog("[ModeE] 零度挑战地图：发放保暖装备...");

                // 头盔 ID:1312
                Item helmet = ItemAssetsCollection.InstantiateSync(1312);
                if (helmet != null)
                {
                    bool equipped = main.CharacterItem.TryPlug(helmet, true, null, 0);
                    if (!equipped) ItemUtilities.SendToPlayerCharacterInventory(helmet, false);
                    DevLog("[ModeE] 发放保暖头盔: " + helmet.DisplayName);
                }

                // 护甲 ID:1307
                Item armor = ItemAssetsCollection.InstantiateSync(1307);
                if (armor != null)
                {
                    bool equipped = main.CharacterItem.TryPlug(armor, true, null, 0);
                    if (!equipped) ItemUtilities.SendToPlayerCharacterInventory(armor, false);
                    DevLog("[ModeE] 发放保暖护甲: " + armor.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEGiveColdWeatherGear 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 独狼阵营专属补给：发放3个id=881和3个id=660的物品到玩家背包
        /// </summary>
        private void ModeEGiveLoneWolfSupplies()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                DevLog("[ModeE] 独狼阵营：发放专属补给物品...");

                // 发放3个 id=881 的物品
                for (int i = 0; i < 3; i++)
                {
                    Item item881 = ItemAssetsCollection.InstantiateSync(881);
                    if (item881 != null)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(item881, false);
                        DevLog("[ModeE] 独狼补给：发放物品 881 - " + item881.DisplayName);
                    }
                }

                // 发放3个 id=660 的物品
                for (int i = 0; i < 3; i++)
                {
                    Item item660 = ItemAssetsCollection.InstantiateSync(660);
                    if (item660 != null)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(item660, false);
                        DevLog("[ModeE] 独狼补给：发放物品 660 - " + item660.DisplayName);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEGiveLoneWolfSupplies 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取阵营的中文显示名称
        /// </summary>
        private string GetFactionDisplayName(Teams faction)
        {
            switch (faction)
            {
                case Teams.scav:    return L10n.T("拾荒者", "Scav");
                case Teams.usec:    return L10n.T("USEC", "USEC");
                case Teams.bear:    return L10n.T("BEAR", "BEAR");
                case Teams.lab:     return L10n.T("实验室", "Lab");
                case Teams.wolf:    return L10n.T("狼群", "Wolf");
                case Teams.player:  return L10n.T("独狼", "Lone Wolf");
                default:            return faction.ToString();
            }
        }

        /// <summary>
        /// 获取阵营后缀字符串（供 Harmony 补丁在 HealthBar 名字后追加）
        /// 格式：" - 阵营名"，非 Mode E 阵营返回 null
        /// </summary>
        public string GetModeEFactionSuffix(Teams faction)
        {
            string name = GetFactionDisplayName(faction);
            if (string.IsNullOrEmpty(name)) return null;
            return " - " + name;
        }

        #region Mode E 阵营存活列表管理（P4性能优化）

        /// <summary>
        /// 将敌人添加到阵营独立存活列表
        /// </summary>
        private void AddToFactionAliveList(Teams faction, CharacterMainControl enemy)
        {
            List<CharacterMainControl> list;
            if (!modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                list = new List<CharacterMainControl>();
                modeEFactionAliveMap[faction] = list;
            }
            list.Add(enemy);
        }

        /// <summary>
        /// 从阵营独立存活列表中移除敌人
        /// </summary>
        private void RemoveFromFactionAliveList(Teams faction, CharacterMainControl enemy)
        {
            List<CharacterMainControl> list;
            if (modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                list.Remove(enemy);
            }
        }

        /// <summary>
        /// 获取指定阵营的存活敌人列表（只读访问，用于缩放遍历）
        /// </summary>
        private List<CharacterMainControl> GetFactionAliveList(Teams faction)
        {
            List<CharacterMainControl> list;
            if (modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                return list;
            }
            return null;
        }

        #endregion

        #endregion
    }
}
