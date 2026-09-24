using System;
using System.Collections;
using UnityEngine;
using Duckov.Scenes;

namespace BossRush
{
    internal sealed partial class IntegrationRuntimeModule
    {
        internal IEnumerator WaitForCustomTeleportSceneReady()
        {
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                bool mainExists = ReadMainExistsWithWarning("TeleportPlayerToCustomPosition");
                bool levelInited = ReadLevelInitedWithWarning("TeleportPlayerToCustomPosition");

                if (mainExists && levelInited)
                {
                    break;
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            yield return _owner.IntegrationSharedWait05s;
        }

        internal Vector3 ApplyCustomTeleportPosition(Vector3 targetPosition, CharacterMainControl main, bool isModeEEntry)
        {
            Vector3 finalPosition = targetPosition;
            if (isModeEEntry)
            {
                return finalPosition;
            }

            // Prefer the ground point nearest the configured Y value to avoid landing on an indoor roof.
            Vector3 rayStart = targetPosition + Vector3.up * 1f;
            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 5f);

            if (hits != null && hits.Length > 0)
            {
                float configY = targetPosition.y;
                float bestY = targetPosition.y;
                float lowestY = float.MaxValue;

                foreach (var h in hits)
                {
                    if (Mathf.Abs(h.point.y - configY) < 1f)
                    {
                        bestY = h.point.y + 0.1f;
                        break;
                    }

                    if (h.point.y < lowestY)
                    {
                        lowestY = h.point.y;
                        bestY = h.point.y + 0.1f;
                    }
                }

                finalPosition = new Vector3(targetPosition.x, bestY, targetPosition.z);
                ModBehaviour.DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用 RaycastAll 修正落点: " + finalPosition + " (配置Y=" + configY + ")");
            }
            else
            {
                RaycastHit hit;
                if (Physics.Raycast(rayStart, Vector3.down, out hit, 5f))
                {
                    finalPosition = hit.point + new Vector3(0f, 0.1f, 0f);
                    ModBehaviour.DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用单次 Raycast 修正落点: " + finalPosition);
                }
            }

            GameCamera camera = GameCamera.Instance;
            Vector3 cameraOffset = Vector3.zero;
            if (camera != null)
            {
                cameraOffset = camera.transform.position - main.transform.position;
            }

            try
            {
                main.SetPosition(finalPosition);
                ModBehaviour.DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用 SetPosition 传送玩家到 " + finalPosition);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] SetPosition 失败: " + e.Message + "，改用 transform.position");
                main.transform.position = finalPosition;
                if (camera != null)
                {
                    camera.transform.position = main.transform.position + cameraOffset;
                }
            }

            if (camera != null)
            {
                camera.transform.position = main.transform.position + cameraOffset;
            }

            ModBehaviour.DevLog("[BossRush] TeleportPlayerToCustomPosition: 传送完成");
            return finalPosition;
        }

        internal IEnumerator ForceTeleportToSubScene(string targetSubSceneID, Vector3 targetPosition)
        {
            ModBehaviour.DevLog("[BossRush] ForceTeleportToSubScene: 开始强制传送到子场景 " + targetSubSceneID);

            const float maxWait = 10f;
            const float interval = 0.1f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                bool mainExists = ReadMainExistsWithWarning("ForceTeleportToSubScene");
                bool levelInited = ReadLevelInitedWithWarning("ForceTeleportToSubScene");

                if (mainExists && levelInited) break;

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            yield return _owner.IntegrationSharedWait1s;

            try
            {
                MultiSceneTeleporter[] teleporters = UnityEngine.Object.FindObjectsOfType<MultiSceneTeleporter>(true);
                MultiSceneTeleporter targetTeleporter = null;

                foreach (MultiSceneTeleporter t in teleporters)
                {
                    if (t == null) continue;

                    try
                    {
                        MultiSceneLocation target = t.Target;
                        string targetSceneID = target.SceneID;

                        if (targetSceneID == targetSubSceneID)
                        {
                            targetTeleporter = t;
                            break;
                        }

                        if (targetSubSceneID == "Level_StormZone_B0")
                        {
                            string interactName = t.InteractName ?? "";
                            if (interactName.Contains("下去") || interactName.Contains("地下") ||
                                t.name.Contains("Down") || t.name.Contains("B0") ||
                                (targetSceneID != null && targetSceneID.Contains("B0")))
                            {
                                targetTeleporter = t;
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        string teleporterName = string.Empty;
                        try
                        {
                            teleporterName = t.name;
                        }
                        catch
                        {
                            teleporterName = "<unknown>";
                        }

                        LogIntegrationWarningLimited(
                            "ForceTeleportToSubScene_teleporter_scan",
                            "ForceTeleportToSubScene 读取传送器信息失败: " + teleporterName,
                            e);
                    }
                }

                if (targetTeleporter != null)
                {
                    ModBehaviour.DevLog("[BossRush] ForceTeleportToSubScene: 触发传送器 " + targetTeleporter.name);
                    targetTeleporter.DoTeleport();
                    yield break;
                }
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(
                    "ForceTeleportToSubScene_search",
                    "ForceTeleportToSubScene 查找目标传送器失败，准备回退到备用方案",
                    e);
            }

            try
            {
                Duckov.Scenes.MultiSceneCore multiSceneCore = Duckov.Scenes.MultiSceneCore.Instance;
                if (multiSceneCore != null)
                {
                    ModBehaviour.DevLog("[BossRush] ForceTeleportToSubScene: 使用 LoadAndTeleport 备用方案");
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(multiSceneCore.LoadAndTeleport(targetSubSceneID, targetPosition, true));
                }
                else
                {
                    _owner.SetBossRushArenaPlannedForIntegration(false);
                    _owner.StartCoroutine(_owner.TeleportPlayerToCustomPositionForIntegration(targetPosition));
                    _owner.ClearBossRushPendingMapEntryForIntegration();
                    _owner.ClearBossRushPendingEntryFlowStateForIntegration();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ForceTeleportToSubScene 失败: " + e.Message);
                _owner.SetBossRushArenaPlannedForIntegration(false);
                _owner.ClearBossRushPendingMapEntryForIntegration();
                _owner.ClearBossRushPendingEntryFlowStateForIntegration();
            }
        }

        internal Vector3[] ResolveMapSpawnPointsForScene(string sceneName)
        {
            BossRushMapConfig mapConfig = ModBehaviour.GetMapConfigBySceneName(sceneName);
            if (mapConfig != null && mapConfig.spawnPoints != null)
            {
                ModBehaviour.DevLog("[BossRush] SetCurrentMapSpawnPoints: 使用 " + mapConfig.displayName + " 刷新点，共 " + mapConfig.spawnPoints.Length + " 个");
                return mapConfig.spawnPoints;
            }

            ModBehaviour.DevLog("[BossRush] [WARNING] SetCurrentMapSpawnPoints: 未找到场景 JSON 配置 " + sceneName);
            return null;
        }
    }
}
