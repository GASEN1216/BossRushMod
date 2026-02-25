// ============================================================================
// InfiniteHellCashMagnet.cs - 无间炼狱现金磁铁吸附系统
// ============================================================================
// 模块说明：
//   在无间炼狱模式下，持续扫描玩家周围 2 米范围内的现金掉落物，
//   使其以加速曲线平滑飞向玩家，到达后自动拾取。
//   不修改现有掉落逻辑，仅新增磁铁检测和飞行动画。
//
// 主要功能：
//   - UpdateCashMagnet: 磁铁主更新（在 Update 中调用）
//   - DetectNearbyCashPickups: 检测范围内现金 pickup
//   - UpdateFlyingCashPickups: 更新飞行中 pickup 的位置并拾取
//   - ClearCashMagnetState: 清理飞行列表
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // ============================================================================
        // 现金磁铁常量
        // ============================================================================

        /// <summary>磁铁检测半径（米）</summary>
        private const float CashMagnetRadius = 2.0f;

        /// <summary>拾取判定距离（米），低于此距离触发自动拾取</summary>
        private const float CashMagnetPickupDistance = 0.3f;

        /// <summary>现金物品 TypeID（= EconomyManager.CashItemID）</summary>
        private const int CashItemTypeID = 451;

        /// <summary>飞行初始速度（米/秒）</summary>
        private const float CashMagnetBaseSpeed = 2.0f;

        /// <summary>飞行加速因子（米/秒²）</summary>
        private const float CashMagnetAcceleration = 8.0f;

        // ============================================================================
        // 现金磁铁状态字段
        // ============================================================================

        /// <summary>正在飞行中的现金 pickup 集合（避免重复检测）</summary>
        private HashSet<InteractablePickup> cashMagnetFlyingPickups = new HashSet<InteractablePickup>();

        /// <summary>每个飞行 pickup 的已飞行时间（用于加速曲线计算）</summary>
        private Dictionary<InteractablePickup, float> cashMagnetFlyTimes = new Dictionary<InteractablePickup, float>();

        /// <summary>5秒窗口内吸附的现金总额</summary>
        private long cashMagnetAbsorbedTotal = 0L;

        /// <summary>吸附气泡窗口计时器（秒）</summary>
        private float cashMagnetBubbleTimer = 0f;

        /// <summary>吸附气泡窗口时长（秒）</summary>
        private const float CashMagnetBubbleWindow = 5.0f;

        // ============================================================================
        // 现金磁铁核心方法
        // ============================================================================

        /// <summary>
        /// 磁铁主更新方法，在 Update() 中调用。
        /// 仅在无间炼狱模式激活时执行检测和飞行逻辑。
        /// </summary>
        private void UpdateCashMagnet()
        {
            try
            {
                // 仅在无间炼狱模式下生效
                if (!infiniteHellMode) return;

                // 获取玩家角色，为 null 则跳过本帧
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;

                Vector3 playerPos = player.transform.position;

                // 更新吸附气泡计时器
                if (cashMagnetBubbleTimer > 0f)
                {
                    cashMagnetBubbleTimer -= Time.deltaTime;
                    if (cashMagnetBubbleTimer <= 0f)
                    {
                        // 窗口过期，重置累计
                        cashMagnetAbsorbedTotal = 0L;
                        cashMagnetBubbleTimer = 0f;
                    }
                }

                // 检测范围内的现金 pickup
                DetectNearbyCashPickups(playerPos);

                // 更新飞行中 pickup 的位置并处理拾取
                UpdateFlyingCashPickups(player);
            }
            catch (Exception e)
            {
                DevLog("[CashMagnet] UpdateCashMagnet 异常: " + e.Message);
            }
        }

        /// <summary>
        /// 检测玩家周围 2m 范围内的现金 pickup，加入飞行列表。
        /// 使用 Physics.OverlapSphere 检测碰撞体，过滤 InteractablePickup 且 TypeID == 451。
        /// </summary>
        /// <param name="playerPos">玩家当前位置</param>
        private void DetectNearbyCashPickups(Vector3 playerPos)
        {
            try
            {
                // 球形检测范围内所有碰撞体
                Collider[] colliders = Physics.OverlapSphere(playerPos, CashMagnetRadius);
                if (colliders == null || colliders.Length == 0) return;

                for (int i = 0; i < colliders.Length; i++)
                {
                    try
                    {
                        Collider col = colliders[i];
                        if (col == null) continue;

                        // 获取 InteractablePickup 组件
                        InteractablePickup pickup = col.GetComponent<InteractablePickup>();
                        if (pickup == null) continue;

                        // 已在飞行列表中则跳过
                        if (cashMagnetFlyingPickups.Contains(pickup)) continue;

                        // 检查 ItemAgent 和 Item 是否有效
                        if (pickup.ItemAgent == null || pickup.ItemAgent.Item == null) continue;

                        // 仅吸附现金物品（TypeID == 451）
                        if (pickup.ItemAgent.Item.TypeID != CashItemTypeID) continue;

                        // 加入飞行列表，初始飞行时间为 0
                        cashMagnetFlyingPickups.Add(pickup);
                        cashMagnetFlyTimes[pickup] = 0f;
                    }
                    catch (Exception e)
                    {
                        DevLog("[CashMagnet] DetectNearbyCashPickups 处理碰撞体异常: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[CashMagnet] DetectNearbyCashPickups 异常: " + e.Message);
            }
        }

        /// <summary>
        /// 更新所有飞行中 pickup 的位置，到达玩家后自动拾取并销毁。
        /// 使用加速曲线：speed = baseSpeed + acceleration * flyTime
        /// </summary>
        /// <param name="player">玩家角色引用</param>
        private void UpdateFlyingCashPickups(CharacterMainControl player)
        {
            try
            {
                if (cashMagnetFlyingPickups.Count == 0) return;

                Vector3 playerPos = player.transform.position;
                float deltaTime = Time.deltaTime;

                // 使用临时列表遍历，避免遍历中修改集合
                var pickupsToRemove = new List<InteractablePickup>();

                foreach (var pickup in cashMagnetFlyingPickups)
                {
                    try
                    {
                        // 检查 pickup 是否已被销毁
                        if (pickup == null || pickup.gameObject == null)
                        {
                            pickupsToRemove.Add(pickup);
                            continue;
                        }

                        // 累加飞行时间
                        float flyTime = 0f;
                        cashMagnetFlyTimes.TryGetValue(pickup, out flyTime);
                        flyTime += deltaTime;
                        cashMagnetFlyTimes[pickup] = flyTime;

                        // 计算加速速度：baseSpeed + acceleration * flyTime
                        float speed = CashMagnetBaseSpeed + CashMagnetAcceleration * flyTime;

                        // 移动 pickup 向玩家位置
                        Vector3 currentPos = pickup.transform.position;
                        Vector3 newPos = Vector3.MoveTowards(currentPos, playerPos, speed * deltaTime);
                        pickup.transform.position = newPos;

                        // 判断是否到达拾取距离
                        float distance = Vector3.Distance(newPos, playerPos);
                        if (distance < CashMagnetPickupDistance)
                        {
                            // 触发拾取并记录吸附金额
                            try
                            {
                                if (pickup.ItemAgent != null && pickup.ItemAgent.Item != null)
                                {
                                    // 记录现金数量用于气泡显示
                                    long cashAmount = 0L;
                                    try
                                    {
                                        cashAmount = (long)pickup.ItemAgent.Item.StackCount;
                                    }
                                    catch {}

                                    player.PickupItem(pickup.ItemAgent.Item);

                                    // 累加吸附金额并刷新气泡
                                    if (cashAmount > 0)
                                    {
                                        cashMagnetAbsorbedTotal += cashAmount;
                                        cashMagnetBubbleTimer = CashMagnetBubbleWindow;
                                        try
                                        {
                                            string bubbleText = "吸附现金：<color=red>" + cashMagnetAbsorbedTotal.ToString("N0") + "</color>";
                                            Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(bubbleText, player.transform, -1f, false, false, -1f, 3f);
                                        }
                                        catch {}
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                DevLog("[CashMagnet] PickupItem 调用异常: " + e.Message);
                            }

                            // 销毁 pickup GameObject
                            try
                            {
                                UnityEngine.Object.Destroy(pickup.gameObject);
                            }
                            catch (Exception e)
                            {
                                DevLog("[CashMagnet] Destroy pickup 异常: " + e.Message);
                            }

                            pickupsToRemove.Add(pickup);
                        }
                    }
                    catch (Exception e)
                    {
                        // pickup 处理异常，标记移除
                        pickupsToRemove.Add(pickup);
                        DevLog("[CashMagnet] UpdateFlyingCashPickups 处理单个 pickup 异常: " + e.Message);
                    }
                }

                // 清理已完成或异常的 pickup
                for (int i = 0; i < pickupsToRemove.Count; i++)
                {
                    cashMagnetFlyingPickups.Remove(pickupsToRemove[i]);
                    cashMagnetFlyTimes.Remove(pickupsToRemove[i]);
                }
            }
            catch (Exception e)
            {
                DevLog("[CashMagnet] UpdateFlyingCashPickups 异常: " + e.Message);
            }
        }

        /// <summary>
        /// 清理现金磁铁状态（模式结束或场景切换时调用）。
        /// 清空飞行列表和计时器。
        /// </summary>
        private void ClearCashMagnetState()
        {
            try
            {
                cashMagnetFlyingPickups.Clear();
                cashMagnetFlyTimes.Clear();
                cashMagnetAbsorbedTotal = 0L;
                cashMagnetBubbleTimer = 0f;
            }
            catch (Exception e)
            {
                DevLog("[CashMagnet] ClearCashMagnetState 异常: " + e.Message);
            }
        }
    }
}
