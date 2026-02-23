// ============================================================================
// ModeERespawnItems.cs - 刷怪消耗品使用效果与自动发放
// ============================================================================
// 模块说明：
//   实现挑衅烟雾弹（Taunt Smoke）和混沌引爆器（Chaos Detonator）的使用效果逻辑。
//   挑衅烟雾弹：在最近10个刷怪点重新生成随机阵营Boss
//   混沌引爆器：在全图所有刷怪点重新生成随机阵营Boss
//   自动发放：每击杀10个Boss自动发放一个挑衅烟雾弹
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// Mode E 刷怪消耗品使用效果模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 刷怪消耗品字段

        /// <summary>Mode E 中累计 Boss 击杀数，每10次触发自动发放挑衅烟雾弹</summary>
        private int modeERespawnKillCounter = 0;

        /// <summary>5个有效NPC阵营，用于重生Boss时随机选择</summary>
        private static readonly Teams[] RespawnFactions = new Teams[]
        {
            Teams.scav,
            Teams.usec,
            Teams.bear,
            Teams.lab,
            Teams.wolf
        };

        #endregion

        #region 刷怪点收集方法

        /// <summary>
        /// 获取距离玩家最近的N个刷怪点
        /// 从 modeESpawnAllocation 合并所有阵营刷怪点，按距离玩家排序，取前 count 个
        /// 不足 count 个时返回全部
        /// </summary>
        private List<Vector3> GetNearestSpawnPoints(int count)
        {
            // 合并所有阵营的刷怪点
            List<Vector3> allPoints = GetAllSpawnPoints();
            if (allPoints.Count == 0) return allPoints;

            // 获取玩家位置
            Vector3 playerPos = Vector3.zero;
            CharacterMainControl playerRef = CharacterMainControl.Main;
            if (playerRef != null) playerPos = playerRef.transform.position;

            // 按距离玩家由近到远排序
            allPoints.Sort((a, b) =>
            {
                float distA = Vector3.SqrMagnitude(a - playerPos);
                float distB = Vector3.SqrMagnitude(b - playerPos);
                return distA.CompareTo(distB);
            });

            // 不足 count 个时返回全部
            if (allPoints.Count <= count) return allPoints;

            // 取前 count 个
            return allPoints.GetRange(0, count);
        }

        /// <summary>
        /// 获取全图所有刷怪点
        /// 从 modeESpawnAllocation 合并所有阵营刷怪点
        /// </summary>
        private List<Vector3> GetAllSpawnPoints()
        {
            List<Vector3> allPoints = new List<Vector3>();

            if (modeESpawnAllocation == null) return allPoints;

            foreach (var kvp in modeESpawnAllocation)
            {
                List<Vector3> factionPoints = kvp.Value;
                if (factionPoints != null)
                {
                    allPoints.AddRange(factionPoints);
                }
            }

            return allPoints;
        }

        #endregion

        #region 烟雾VFX

        /// <summary>
        /// 在玩家位置触发原版烟雾弹效果
        /// 从原版烟雾弹(TypeID=660)的 Skill_Grenade 获取 Grenade prefab，
        /// 再从 Grenade.createOnExlode 获取 FowSmoke prefab 并实例化
        /// </summary>
        private void PlaySmokeVFX()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;

                // 实例化原版烟雾弹物品（TypeID=660）以获取其 Grenade prefab
                Item smokeItem = ItemAssetsCollection.InstantiateSync(660);
                if (smokeItem == null)
                {
                    DevLog("[ModeE] [WARNING] 无法实例化原版烟雾弹(660)，跳过VFX");
                    return;
                }

                // 从物品上获取 Skill_Grenade 组件（可能在子对象上）
                Skill_Grenade skillGrenade = smokeItem.GetComponentInChildren<Skill_Grenade>();
                if (skillGrenade == null)
                {
                    DevLog("[ModeE] [WARNING] 原版烟雾弹(660)无 Skill_Grenade 组件");
                    UnityEngine.Object.Destroy(smokeItem.gameObject);
                    return;
                }

                // 获取 Grenade prefab
                Grenade grenadePfb = skillGrenade.grenadePfb;
                if (grenadePfb == null)
                {
                    DevLog("[ModeE] [WARNING] 原版烟雾弹(660) Skill_Grenade.grenadePfb 为 null");
                    UnityEngine.Object.Destroy(smokeItem.gameObject);
                    return;
                }

                // 从 Grenade prefab 获取 createOnExlode（FowSmoke prefab）并在玩家位置实例化
                if (grenadePfb.createOnExlode != null)
                {
                    Vector3 playerPos = player.transform.position;
                    UnityEngine.Object.Instantiate(grenadePfb.createOnExlode, playerPos, Quaternion.identity);
                    DevLog("[ModeE] 已在玩家位置实例化原版烟雾效果(FowSmoke)");
                }
                else
                {
                    DevLog("[ModeE] [WARNING] Grenade.createOnExlode 为 null，无烟雾效果");
                }

                // 销毁临时烟雾弹物品对象
                UnityEngine.Object.Destroy(smokeItem.gameObject);
            }
            catch (Exception e)
            {
                // VFX 播放失败不影响核心逻辑
                DevLog("[ModeE] [WARNING] PlaySmokeVFX 失败: " + e.Message);
            }
        }

        #endregion

        #region Boss 重生逻辑

        /// <summary>
        /// 在指定刷怪点列表生成随机阵营Boss（异步，每次生成间加延迟避免帧率卡顿）
        /// 遍历刷怪点，随机选择阵营，调用 SpawnSingleModeEBoss
        /// 新 Boss 自动注册到 modeEAliveEnemies 和 modeEFactionAliveMap（由 OnModeEEnemySpawned 处理）
        /// </summary>
        private async UniTaskVoid RespawnBossesAtPoints(List<Vector3> points)
        {
            try
            {
                if (points == null || points.Count == 0)
                {
                    DevLog("[ModeE] RespawnBossesAtPoints: 刷怪点列表为空，跳过");
                    return;
                }

                DevLog("[ModeE] 开始重生Boss，刷怪点数量: " + points.Count);

                for (int i = 0; i < points.Count; i++)
                {
                    if (!modeEActive) break; // 模式已结束，停止生成

                    // 随机选择阵营
                    Teams faction = RespawnFactions[UnityEngine.Random.Range(0, RespawnFactions.Length)];

                    // 复用现有 SpawnSingleModeEBoss 逻辑生成Boss
                    // SpawnSingleModeEBoss 内部会处理：
                    //   - Boss预设匹配
                    //   - 注册到 modeEAliveEnemies 和 modeEFactionAliveMap
                    //   - 注册死亡事件（OnModeEEnemyDeath）
                    //   - 应用当前阵营死亡缩放倍率
                    SpawnSingleModeEBoss(faction, points[i]);

                    // 每次生成间加延迟，避免帧率卡顿（与 ModeESpawnAllBosses 保持一致）
                    if (i + 1 < points.Count)
                    {
                        await UniTask.Delay(250);
                    }
                }

                DevLog("[ModeE] Boss 重生任务完成");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] RespawnBossesAtPoints 失败: " + e.Message);
            }
        }

        #endregion

        #region 物品使用效果

        /// <summary>
        /// 挑衅烟雾弹使用效果：在最近10个刷怪点重新生成随机阵营Boss
        /// 由 RespawnItemUsage.OnUse 调用（Mode E 激活检查已在 CanBeUsed 中完成）
        /// </summary>
        public void UseTauntSmoke()
        {
            try
            {
                // 检查刷怪点分配数据
                if (modeESpawnAllocation == null)
                {
                    ShowMessage(L10n.T(
                        "刷怪点数据异常，无法使用！",
                        "Spawn point data error, cannot use!"
                    ));
                    DevLog("[ModeE] UseTauntSmoke: modeESpawnAllocation 为 null");
                    return;
                }

                // 获取最近10个刷怪点
                List<Vector3> nearestPoints = GetNearestSpawnPoints(10);
                if (nearestPoints.Count == 0)
                {
                    ShowMessage(L10n.T(
                        "无可用刷怪点！",
                        "No available spawn points!"
                    ));
                    DevLog("[ModeE] UseTauntSmoke: 无可用刷怪点");
                    return;
                }

                DevLog("[ModeE] 使用挑衅烟雾弹，将在 " + nearestPoints.Count + " 个最近刷怪点重生Boss");

                // 播放烟雾VFX
                PlaySmokeVFX();

                // 在刷怪点重生Boss（fire-and-forget）
                RespawnBossesAtPoints(nearestPoints).Forget();

                ShowBigBanner(L10n.T(
                    "<color=yellow>挑衅烟雾弹</color> 已激活！<color=red>" + nearestPoints.Count + "</color> 个Boss正在赶来...",
                    "<color=yellow>Taunt Smoke</color> activated! <color=red>" + nearestPoints.Count + "</color> Bosses incoming..."
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] UseTauntSmoke 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 混沌引爆器使用效果：在全图所有刷怪点重新生成随机阵营Boss
        /// 由 RespawnItemUsage.OnUse 调用（Mode E 激活检查已在 CanBeUsed 中完成）
        /// </summary>
        public void UseChaosDetonator()
        {
            try
            {
                // 检查刷怪点分配数据
                if (modeESpawnAllocation == null)
                {
                    ShowMessage(L10n.T(
                        "刷怪点数据异常，无法使用！",
                        "Spawn point data error, cannot use!"
                    ));
                    DevLog("[ModeE] UseChaosDetonator: modeESpawnAllocation 为 null");
                    return;
                }

                // 获取全图所有刷怪点
                List<Vector3> allPoints = GetAllSpawnPoints();
                if (allPoints.Count == 0)
                {
                    ShowMessage(L10n.T(
                        "无可用刷怪点！",
                        "No available spawn points!"
                    ));
                    DevLog("[ModeE] UseChaosDetonator: 无可用刷怪点");
                    return;
                }

                DevLog("[ModeE] 使用混沌引爆器，将在全图 " + allPoints.Count + " 个刷怪点重生Boss");

                // 播放烟雾VFX
                PlaySmokeVFX();

                // 在全图刷怪点重生Boss（fire-and-forget）
                RespawnBossesAtPoints(allPoints).Forget();

                ShowBigBanner(L10n.T(
                    "<color=red>混沌引爆器</color> 已引爆！全图 <color=red>" + allPoints.Count + "</color> 个Boss正在涌来...",
                    "<color=red>Chaos Detonator</color> activated! <color=red>" + allPoints.Count + "</color> Bosses spawning across the map..."
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] UseChaosDetonator 失败: " + e.Message);
            }
        }

        #endregion

        #region 自动发放机制

        /// <summary>
        /// Boss击杀计数 + 自动发放检查
        /// 递增 modeERespawnKillCounter，每达到10的倍数时向玩家背包添加一个挑衅烟雾弹
        /// 背包满时在玩家位置掉落物品
        /// 在 OnModeEEnemyDeath 中调用
        /// </summary>
        private void CheckRespawnItemAutoGrant()
        {
            try
            {
                if (!modeEActive) return;

                modeERespawnKillCounter++;
                DevLog("[ModeE] Boss击杀计数: " + modeERespawnKillCounter);

                // 每10次击杀发放一个挑衅烟雾弹
                if (modeERespawnKillCounter % 10 == 0)
                {
                    Item tauntSmoke = ItemAssetsCollection.InstantiateSync(RespawnItemConfig.TAUNT_SMOKE_TYPE_ID);
                    if (tauntSmoke != null)
                    {
                        // 尝试放入玩家背包，失败则在玩家位置掉落
                        bool added = ItemUtilities.SendToPlayerCharacterInventory(tauntSmoke, false);
                        if (!added)
                        {
                            // 背包满，在玩家位置掉落物品
                            CharacterMainControl player = CharacterMainControl.Main;
                            if (player != null)
                            {
                                tauntSmoke.Drop(player, true);
                                DevLog("[ModeE] 背包已满，挑衅烟雾弹掉落在玩家位置");
                            }
                        }

                        DevLog("[ModeE] 自动发放挑衅烟雾弹（累计击杀: " + modeERespawnKillCounter + "）");

                        // 使用横幅提示
                        ShowBigBanner(L10n.T(
                            "击杀 <color=yellow>" + modeERespawnKillCounter + "</color> 个Boss！获得 <color=yellow>挑衅烟雾弹</color> ×1",
                            "Killed <color=yellow>" + modeERespawnKillCounter + "</color> Bosses! Received <color=yellow>Taunt Smoke</color> ×1"
                        ));
                    }
                    else
                    {
                        DevLog("[ModeE] [WARNING] 自动发放挑衅烟雾弹失败：ItemAssetsCollection.InstantiateSync 返回 null");
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] CheckRespawnItemAutoGrant 失败: " + e.Message);
            }
        }

        #endregion
    }
}
