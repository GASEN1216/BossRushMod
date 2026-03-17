using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using UnityEngine.Events;
using Duckov.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F 撤离与结算

        /// <summary>
        /// 清除地图原有撤离点
        /// </summary>
        private void ClearOriginalExtractionPoints()
        {
            try
            {
                CountDownArea[] areas = UnityEngine.Object.FindObjectsOfType<CountDownArea>();
                if (areas == null || areas.Length == 0)
                {
                    DevLog("[ModeF] ClearOriginalExtractionPoints: 未找到撤离点");
                    return;
                }

                int cleared = 0;
                for (int i = 0; i < areas.Length; i++)
                {
                    try
                    {
                        // 跳过 Mode F 自己生成的撤离点
                        if (modeFState.ActiveExtractionArea != null && areas[i] == modeFState.ActiveExtractionArea)
                            continue;

                        areas[i].gameObject.SetActive(false);
                        cleared++;
                    }
                    catch { }
                }

                DevLog("[ModeF] 已清除 " + cleared + " 个原始撤离点");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] ClearOriginalExtractionPoints 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 生成最终撤离点
        /// </summary>
        private void SpawnFinalExtractionPoint()
        {
            try
            {
                if (modeFState.ExtractionPointSpawned)
                {
                    DevLog("[ModeF] 撤离点已生成，跳过");
                    return;
                }

                // 选择撤离点位置（从刷怪点中选一个）
                Vector3 extractionPos = FindSpawnPointAwayFromPlayer(30f);

                // 尝试使用官方 exitPrefab
                GameObject exitObj = null;
                try
                {
                    var levelManager = UnityEngine.Object.FindObjectOfType<LevelManager>();
                    if (levelManager != null && levelManager.ExitCreator != null && levelManager.ExitCreator.exitPrefab != null)
                    {
                        exitObj = UnityEngine.Object.Instantiate(levelManager.ExitCreator.exitPrefab, extractionPos, Quaternion.identity);
                        DevLog("[ModeF] 使用官方 exitPrefab 创建撤离点");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] [WARNING] 获取官方 exitPrefab 失败: " + e.Message);
                }

                // 回退：手动创建
                if (exitObj == null)
                {
                    exitObj = new GameObject("ModeF_ExtractionPoint");
                    exitObj.transform.position = extractionPos;

                    // 添加触发器碰撞体
                    SphereCollider collider = exitObj.AddComponent<SphereCollider>();
                    collider.radius = 3f;
                    collider.isTrigger = true;

                    DevLog("[ModeF] 手动创建撤离点");
                }

                // 配置 CountDownArea
                CountDownArea countDown = exitObj.GetComponent<CountDownArea>();
                if (countDown == null)
                {
                    countDown = exitObj.AddComponent<CountDownArea>();
                }

                countDown.enabled = true;

                try
                {
                    var requiredTimeField = typeof(CountDownArea).GetField("requiredExtrationTime",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (requiredTimeField != null)
                    {
                        requiredTimeField.SetValue(countDown, 15f);
                    }

                    var countDownField = typeof(CountDownArea).GetField("countDownTime",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (countDownField != null)
                    {
                        countDownField.SetValue(countDown, 15f);
                    }
                }
                catch { }

                if (countDown.onCountDownSucceed == null)
                {
                    countDown.onCountDownSucceed = new UnityEvent();
                }
                bool shouldNotifyGameEvacuation = countDown.onCountDownSucceed.GetPersistentEventCount() <= 0;
                countDown.onCountDownSucceed.AddListener(() =>
                {
                    if (!modeFActive || modeFState.ActiveExtractionArea != countDown || modeFState.ExtractionResolved)
                    {
                        return;
                    }

                    modeFState.ExtractionResolved = true;
                    OnModeFExtractionSuccess();
                    if (shouldNotifyGameEvacuation)
                    {
                        TryNotifyModeFExtraction(extractionPos);
                    }
                });

                if (countDown.onCountDownStarted == null)
                {
                    countDown.onCountDownStarted = new UnityEvent<CountDownArea>();
                }
                countDown.onCountDownStarted.AddListener((area) =>
                {
                    try { EvacuationCountdownUI.Request(area); } catch { }
                });

                if (countDown.onCountDownStopped == null)
                {
                    countDown.onCountDownStopped = new UnityEvent<CountDownArea>();
                }
                countDown.onCountDownStopped.AddListener((area) =>
                {
                    try { EvacuationCountdownUI.Release(area); } catch { }
                });

                modeFState.ActiveExtractionArea = countDown;
                modeFState.ExtractionPointSpawned = true;

                DevLog("[ModeF] 最终撤离点已生成: " + extractionPos);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] SpawnFinalExtractionPoint 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 撤离成功处理
        /// </summary>
        private void OnModeFExtractionSuccess()
        {
            try
            {
                if (!modeFActive)
                {
                    return;
                }

                int marks = modeFState.PlayerBountyMarks;
                DevLog("[ModeF] 撤离成功！玩家印记: " + marks);

                if (marks > 0)
                {
                    int rewardsGiven = 0;
                    for (int i = 0; i < marks; i++)
                    {
                        try
                        {
                            int rewardTypeId = GetModeFHighQualityRewardTypeID();
                            if (rewardTypeId <= 0) continue;

                            Item reward = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                            if (reward != null)
                            {
                                // 发到寄存点
                                try
                                {
                                    PlayerStorage.Push(reward, true);
                                    rewardsGiven++;
                                }
                                catch
                                {
                                    // 寄存失败则掉在地上
                                    CharacterMainControl player = CharacterMainControl.Main;
                                    if (player != null)
                                    {
                                        reward.Drop(player.transform.position + Vector3.up, true, UnityEngine.Random.insideUnitSphere.normalized, 30f);
                                        rewardsGiven++;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    ShowBigBanner(L10n.T(
                        "<color=green>血猎追击胜利！</color> " + rewardsGiven + " 件高品质奖励已发送到寄存点",
                        "<color=green>Bloodhunt Victory!</color> " + rewardsGiven + " high-quality rewards sent to storage"
                    ));
                    DevLog("[ModeF] 寄存点奖励发放: " + rewardsGiven + " 件");
                }
                else
                {
                    ShowBigBanner(L10n.T(
                        "<color=green>血猎追击胜利！</color> 你成功撤离了",
                        "<color=green>Bloodhunt Victory!</color> You successfully extracted"
                    ));
                }

                ExitModeF();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] OnModeFExtractionSuccess 失败: " + e.Message);
            }
        }

        private void TryNotifyModeFExtraction(Vector3 extractionPos)
        {
            try
            {
                if (LevelManager.Instance == null)
                {
                    return;
                }

                EvacuationInfo info = new EvacuationInfo(
                    Duckov.Scenes.MultiSceneCore.ActiveSubSceneID,
                    extractionPos
                );
                LevelManager.Instance.NotifyEvacuated(info);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] TryNotifyModeFExtraction 失败: " + e.Message);
            }
        }

        #endregion
    }
}
