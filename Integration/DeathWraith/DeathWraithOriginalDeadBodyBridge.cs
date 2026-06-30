using System;
using UnityEngine;
using Duckov;
using ItemStatsSystem.Data;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        internal void NotifyOriginalMainCharacterDeathInfoCaptured_DeathWraith(
            DeadBodyManager.DeathInfo info)
        {
            if (!IsDeathWraithSystemEnabled() || info == null || !info.valid)
            {
                return;
            }

            try
            {
                StorePendingOriginalDeadBodyInfo_DeathWraith(CreateOriginalDeadBodyInfo_DeathWraith(info));

                if (GetPendingDeathWraithContext_DeathWraith() == null)
                {
                    CharacterMainControl main = null;
                    try
                    {
                        main = CharacterMainControl.Main;
                    }
                    catch { }

                    PendingDeathWraithContext_DeathWraith context =
                        BuildPendingDeathWraithContext_DeathWraith(main);
                    if (context != null)
                    {
                        StorePendingDeathWraithInfo_DeathWraith(context);
                    }
                }

                TryFinalizePendingDeathWraithRecord_DeathWraith("OriginalDeadBody");
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] capture original dead-body info exception: " + e.Message);
            }
        }

        private OriginalDeadBodyInfo_DeathWraith CreateOriginalDeadBodyInfo_DeathWraith(
            DeadBodyManager.DeathInfo info)
        {
            if (info == null || !info.valid || info.itemTreeData == null)
            {
                return null;
            }

            return new OriginalDeadBodyInfo_DeathWraith
            {
                valid = true,
                raidID = info.raidID,
                subSceneID = info.subSceneID ?? string.Empty,
                worldPosition = info.worldPosition,
                itemTreeData = info.itemTreeData
            };
        }

        private OriginalDeadBodyInfo_DeathWraith GetPendingOriginalDeadBodyInfo_DeathWraith()
        {
            OriginalDeadBodyInfo_DeathWraith info = pendingOriginalDeadBodyInfo_DeathWraith;
            int primedFrame = pendingOriginalDeadBodyInfoFrame_DeathWraith;
            float primedRealtime = pendingOriginalDeadBodyInfoRealtime_DeathWraith;

            if (info == null || !info.valid || info.itemTreeData == null)
            {
                return null;
            }

            if (!IsPendingDeathWraithInfoFresh_DeathWraith(primedFrame, primedRealtime))
            {
                ClearPendingOriginalDeadBodyInfo_DeathWraith();
                return null;
            }

            return info;
        }

        private void StorePendingOriginalDeadBodyInfo_DeathWraith(OriginalDeadBodyInfo_DeathWraith info)
        {
            pendingOriginalDeadBodyInfo_DeathWraith = info;
            if (info != null)
            {
                pendingOriginalDeadBodyInfoFrame_DeathWraith = Time.frameCount;
                pendingOriginalDeadBodyInfoRealtime_DeathWraith = Time.realtimeSinceStartup;
                return;
            }

            pendingOriginalDeadBodyInfoFrame_DeathWraith = -1;
            pendingOriginalDeadBodyInfoRealtime_DeathWraith = -1f;
        }

        private bool TryFinalizePendingDeathWraithRecord_DeathWraith(string source)
        {
            PendingDeathWraithContext_DeathWraith pendingContext =
                GetPendingDeathWraithContext_DeathWraith();
            OriginalDeadBodyInfo_DeathWraith originalInfo =
                GetPendingOriginalDeadBodyInfo_DeathWraith();
            if (pendingContext == null || originalInfo == null)
            {
                // 兜底仅对“死亡触发”入口生效（OnDead / 模式自定义致死路径如 ModeFBleedFallback）。
                // "OriginalDeadBody" 是原版尸体快照到达的合并入口，该路径下 originalInfo 必然已就位，
                // 不会落到这里；因此只要不是合并入口，就按旧行为用当前玩家数据补记，避免丢失死亡记录。
                bool isDeathTriggerSource =
                    !string.IsNullOrEmpty(source) && source != "OriginalDeadBody";
                if (pendingContext != null && isDeathTriggerSource)
                {
                    // 兜底：如果原版尸体快照在这一拍还没到（或某些自定义致死路径根本不触发它），
                    // 回退到旧路径，用当前玩家数据补记，避免丢亡魂记录。
                    CharacterMainControl main = null;
                    try
                    {
                        main = CharacterMainControl.Main;
                    }
                    catch { }

                    WraithInfo fallbackInfo = BuildCurrentPlayerWraithInfo_DeathWraith(main);
                    if (fallbackInfo != null)
                    {
                        AppendStoredDeathWraithInfo_DeathWraith(fallbackInfo);
                        ClearPendingDeathWraithInfo_DeathWraith();
                        DevLog("[DeathWraith] fallback death-wraith snapshot recorded");
                        return true;
                    }
                }

                return false;
            }

            if (pendingContext.raidID != 0U &&
                originalInfo.raidID != 0U &&
                pendingContext.raidID != originalInfo.raidID)
            {
                return false;
            }

            WraithInfo finalizedInfo =
                BuildWraithInfoFromPendingContext_DeathWraith(pendingContext, originalInfo);
            if (finalizedInfo == null)
            {
                return false;
            }

            AppendStoredDeathWraithInfo_DeathWraith(finalizedInfo);
            ClearPendingDeathWraithInfo_DeathWraith();

            DevLog("[DeathWraith] recorded merged death-wraith snapshot"
                + (string.IsNullOrEmpty(source) ? string.Empty : ("[" + source + "]"))
                + ": raidID=" + finalizedInfo.raidID
                + ", scene=" + finalizedInfo.sceneName
                + ", subScene=" + finalizedInfo.subSceneID
                + ", faceSaved=" + finalizedInfo.hasPlayerFaceData
                + ", meleeSaved=" + finalizedInfo.hasBoundMeleeSnapshot
                + ", value=" + finalizedInfo.droppedItemsValue
                + ", wealth=" + finalizedInfo.playerTotalWealth
                + ", maxHp=" + finalizedInfo.playerMaxHealth
                + ", pos=(" + finalizedInfo.posX + "," + finalizedInfo.posY + "," + finalizedInfo.posZ + ")");
            return true;
        }

        private WraithInfo BuildWraithInfoFromPendingContext_DeathWraith(
            PendingDeathWraithContext_DeathWraith pendingContext,
            OriginalDeadBodyInfo_DeathWraith originalInfo)
        {
            if (pendingContext == null ||
                !pendingContext.valid ||
                originalInfo == null ||
                !originalInfo.valid ||
                originalInfo.itemTreeData == null)
            {
                return null;
            }

            string subSceneID = !string.IsNullOrEmpty(originalInfo.subSceneID)
                ? originalInfo.subSceneID
                : (pendingContext.subSceneID ?? string.Empty);
            uint raidID = originalInfo.raidID != 0U
                ? originalInfo.raidID
                : pendingContext.raidID;
            Vector3 worldPosition = originalInfo.worldPosition;

            return new WraithInfo
            {
                valid = true,
                raidID = raidID,
                sceneName = pendingContext.sceneName ?? string.Empty,
                subSceneID = subSceneID,
                posX = worldPosition.x,
                posY = worldPosition.y,
                posZ = worldPosition.z,
                playerPresetName = pendingContext.playerPresetName,
                playerPresetRuntimeName = pendingContext.playerPresetRuntimeName,
                playerName = pendingContext.playerName,
                droppedItemsValue = pendingContext.droppedItemsValue,
                playerTotalWealth = pendingContext.playerTotalWealth,
                playerMaxHealth = pendingContext.playerMaxHealth,
                hasPlayerFaceData = pendingContext.hasPlayerFaceData,
                playerFaceData = pendingContext.playerFaceData,
                playerVoiceType = pendingContext.playerVoiceType,
                playerFootStepMaterialType = pendingContext.playerFootStepMaterialType,
                hasBoundMeleeSnapshot = pendingContext.hasBoundMeleeSnapshot,
                boundMeleeTypeId = pendingContext.boundMeleeTypeId,
                boundMeleeDisplayName = pendingContext.boundMeleeDisplayName,
                boundMeleeItemTreeData = pendingContext.boundMeleeItemTreeData,
                itemTreeData = originalInfo.itemTreeData,
                killed = false
            };
        }
    }
}
