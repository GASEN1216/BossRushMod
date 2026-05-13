// ============================================================================
// DeathWraithSystem partial - extracted from DeathWraithSystem.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 亡魂系统 — 死亡记录与预缓存

        /// <summary>
        /// 判断 Health 组件是否属于主角，并输出主角引用。
        /// 优先使用 TryGetCharacter()，失败时回退到 GetComponent + IsMainCharacter。
        /// </summary>
        private static bool IsMainPlayerHealth_DeathWraith(Health health, out CharacterMainControl main)
        {
            main = null;
            if (health == null) return false;

            try
            {
                main = CharacterMainControl.Main;
            }
            catch { }
            if (main == null) return false;

            try
            {
                CharacterMainControl character = health.TryGetCharacter();
                if (character != null && character == main) return true;
            }
            catch
            {
                try
                {
                    if (CharacterMainControlExtensions.IsMainCharacter(
                            health.GetComponent<CharacterMainControl>()))
                        return true;
                }
                catch { }
            }

            main = null;
            return false;
        }

        /// <summary>
        /// 玩家受到致死伤害前缓存亡魂数据，避免主角死亡流程先清空背包。
        /// </summary>
        private void PrimeDeathWraithData_DeathWraith(Health hurtHealth, DamageInfo damageInfo)
        {
            try
            {
                if (!IsDeathWraithSystemEnabled())
                {
                    ClearPendingDeathWraithInfo_DeathWraith();
                    return;
                }

                if (!IsMainPlayerHealth_DeathWraith(hurtHealth, out CharacterMainControl main))
                {
                    if (hurtHealth != null)
                    {
                        // Health 非空但不是主角 → 无需处理
                        return;
                    }
                    // Health 为空或主角不存在 → 清理待决数据
                    ClearPendingDeathWraithInfo_DeathWraith();
                    return;
                }

                if (hurtHealth.CurrentHealth > 0f)
                {
                    ClearPendingDeathWraithInfo_DeathWraith();
                    return;
                }

                if (!IsDeathWraithSupportedContext_DeathWraith())
                {
                    ClearPendingDeathWraithInfo_DeathWraith();
                    return;
                }

                WraithInfo primedInfo = BuildCurrentPlayerWraithInfo_DeathWraith(main);
                StorePendingDeathWraithInfo_DeathWraith(primedInfo);
                if (primedInfo != null)
                {
                    DevLog("[DeathWraith] 已预缓存致死前亡魂数据: frame=" + pendingDeathWraithPrimedFrame);
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] PrimeDeathWraithData 异常: " + e.Message);
            }
        }

        /// <summary>
        /// 玩家死亡时记录亡魂数据（所有模式通用）
        /// </summary>
        private void RecordDeathWraithData_DeathWraith(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                if (!IsMainPlayerHealth_DeathWraith(deadHealth, out CharacterMainControl main))
                    return;

                RecordDeathWraithDataForMainCharacter_DeathWraith(
                    main,
                    damageInfo,
                    "OnDead");
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] RecordDeathWraithData 异常: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private void RecordManualDeathWraithData_DeathWraith(
            CharacterMainControl main,
            DamageInfo damageInfo,
            string source)
        {
            try
            {
                if (main == null)
                {
                    return;
                }

                RecordDeathWraithDataForMainCharacter_DeathWraith(main, damageInfo, source);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] RecordManualDeathWraithData 异常: "
                    + e.Message + "\n" + e.StackTrace);
            }
        }

        private void RecordDeathWraithDataForMainCharacter_DeathWraith(
            CharacterMainControl main,
            DamageInfo damageInfo,
            string source)
        {
            if (main == null)
            {
                return;
            }

            if (!IsDeathWraithSystemEnabled())
            {
                ClearPendingDeathWraithInfo_DeathWraith();
                InvalidateStoredDeathWraithRecords_DeathWraith("配置关闭，跳过死亡记录");
                return;
            }

            if (!IsDeathWraithSupportedContext_DeathWraith())
            {
                ClearPendingDeathWraithInfo_DeathWraith();
                return;
            }

            WraithInfo info = ConsumePendingDeathWraithInfo_DeathWraith(main);
            if (info == null)
            {
                info = BuildCurrentPlayerWraithInfo_DeathWraith(main);
            }

            if (info == null)
            {
                DevLog("[DeathWraith] 未能构建亡魂数据，跳过记录"
                    + (string.IsNullOrEmpty(source) ? "" : (": source=" + source)));
                return;
            }

            AppendStoredDeathWraithInfo_DeathWraith(info);
            DevLog("[DeathWraith] 已记录亡魂数据"
                + (string.IsNullOrEmpty(source) ? "" : ("[" + source + "]"))
                + ": raidID=" + info.raidID
                + ": scene=" + info.sceneName
                + ", subScene=" + info.subSceneID
                + ", faceSaved=" + info.hasPlayerFaceData
                + ", meleeSaved=" + info.hasBoundMeleeSnapshot
                + " value=" + info.droppedItemsValue
                + " wealth=" + info.playerTotalWealth
                + " maxHp=" + info.playerMaxHealth
                + " pos=(" + info.posX + "," + info.posY + "," + info.posZ + ")");
        }

        private WraithInfo BuildCurrentPlayerWraithInfo_DeathWraith(CharacterMainControl main)
        {
            if (main == null)
            {
                return null;
            }

            try
            {
                string presetNameKey = "";
                string presetRuntimeName = "";
                try
                {
                    if (main.characterPreset != null)
                    {
                        presetNameKey = main.characterPreset.nameKey ?? "";
                        presetRuntimeName = NormalizeWraithRuntimePresetName_DeathWraith(
                            main.characterPreset.name);
                    }
                    else
                    {
                        // characterPreset 为 null 时，尝试用 GameObject name 作为 runtimeName
                        presetRuntimeName = NormalizeWraithRuntimePresetName_DeathWraith(main.name);
                        DevLog("[DeathWraith] characterPreset 为 null，使用 main.name 作为 runtimeName: " + presetRuntimeName);
                    }
                }
                catch { }

                CustomFaceSettingData playerFaceData = default(CustomFaceSettingData);
                bool hasPlayerFaceData = TryCapturePlayerFaceData_DeathWraith(main, out playerFaceData);
                ItemTreeData boundMeleeItemTree = null;
                int boundMeleeTypeId = 0;
                string boundMeleeDisplayName = null;
                bool hasBoundMeleeSnapshot = TryCaptureBoundMeleeSnapshot_DeathWraith(
                    main,
                    out boundMeleeItemTree,
                    out boundMeleeTypeId,
                    out boundMeleeDisplayName);
                if (!hasBoundMeleeSnapshot)
                {
                    hasBoundMeleeSnapshot = TryLoadPersistedBoundMeleeSnapshot_DeathWraith(
                        out boundMeleeItemTree,
                        out boundMeleeTypeId,
                        out boundMeleeDisplayName);
                }

                int droppedValue = 0;
                try
                {
                    if (main.CharacterItem != null)
                    {
                        droppedValue = main.CharacterItem.GetTotalRawValue();
                    }
                }
                catch { }

                long totalMoney = 0;
                try
                {
                    totalMoney = (long)EconomyManager.Money;
                }
                catch { }

                long totalWealth = Math.Max(0L, totalMoney) + Math.Max(0, droppedValue);
                float playerMaxHealth = 0f;
                try
                {
                    if (main.Health != null)
                    {
                        playerMaxHealth = main.Health.MaxHealth;
                    }
                }
                catch { }

                ItemTreeData itemTree = null;
                try
                {
                    if (main.CharacterItem != null)
                    {
                        itemTree = ItemTreeData.FromItem(main.CharacterItem);
                    }
                }
                catch (Exception e)
                {
                    DevLog("[DeathWraith] ItemTreeData.FromItem 异常: " + e.Message);
                }

                string playerName = GetWraithPlayerName_DeathWraith();
                AudioManager.VoiceType voiceType = AudioManager.VoiceType.Duck;
                AudioManager.FootStepMaterialType footStepMaterialType = AudioManager.FootStepMaterialType.organic;
                try
                {
                    voiceType = main.AudioVoiceType;
                    footStepMaterialType = main.FootStepMaterialType;
                }
                catch { }

                return new WraithInfo
                {
                    valid = true,
                    raidID = GetCurrentRaidId_DeathWraith(),
                    sceneName = GetActiveSceneName_DeathWraith(),
                    subSceneID = GetActiveSubSceneId_DeathWraith(),
                    posX = main.transform.position.x,
                    posY = main.transform.position.y,
                    posZ = main.transform.position.z,
                    playerPresetName = !string.IsNullOrEmpty(presetNameKey)
                        ? presetNameKey
                        : presetRuntimeName,
                    playerPresetRuntimeName = presetRuntimeName,
                    playerName = playerName,
                    droppedItemsValue = droppedValue,
                    playerTotalWealth = totalWealth,
                    playerMaxHealth = playerMaxHealth,
                    hasPlayerFaceData = hasPlayerFaceData,
                    playerFaceData = playerFaceData,
                    playerVoiceType = voiceType,
                    playerFootStepMaterialType = footStepMaterialType,
                    hasBoundMeleeSnapshot = hasBoundMeleeSnapshot,
                    boundMeleeTypeId = boundMeleeTypeId,
                    boundMeleeDisplayName = boundMeleeDisplayName,
                    boundMeleeItemTreeData = boundMeleeItemTree,
                    itemTreeData = itemTree,
                    killed = false
                };
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] BuildCurrentPlayerWraithInfo 异常: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 主角外观由 CharacterModel + CustomFaceSettingData 驱动，而不是 characterPreset。
        /// 优先抓取当前可见模型上的捏脸数据，失败时回退到主角专用存档。
        /// </summary>
        private bool TryCapturePlayerFaceData_DeathWraith(
            CharacterMainControl main,
            out CustomFaceSettingData faceData)
        {
            faceData = default(CustomFaceSettingData);

            try
            {
                if (main != null &&
                    main.characterModel != null &&
                    main.characterModel.CustomFace != null)
                {
                    faceData = main.characterModel.CustomFace.ConvertToSaveData();
                    faceData.savedSetting = true;
                    return true;
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 从当前主角模型抓取捏脸数据失败: " + e.Message);
            }

            try
            {
                LevelManager level = LevelManager.Instance;
                if (level != null && level.CustomFaceManager != null)
                {
                    faceData = level.CustomFaceManager.LoadMainCharacterSetting();
                    faceData.savedSetting = true;
                    DevLog("[DeathWraith] 当前模型未提供捏脸实例，改用主角捏脸存档");
                    return true;
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 读取主角捏脸存档失败: " + e.Message);
            }

            return false;
        }

        private bool TryCaptureBoundMeleeSnapshot_DeathWraith(
            CharacterMainControl main,
            out ItemTreeData meleeItemTreeData,
            out int meleeTypeId,
            out string meleeDisplayName)
        {
            meleeItemTreeData = null;
            meleeTypeId = 0;
            meleeDisplayName = null;

            Item meleeItem = GetCurrentPlayerBoundMeleeItem_DeathWraith(main);
            if (meleeItem == null)
            {
                return false;
            }

            try
            {
                meleeItemTreeData = ItemTreeData.FromItem(meleeItem);
                meleeTypeId = meleeItem.TypeID;
                meleeDisplayName = meleeItem.DisplayName;
                return meleeItemTreeData != null;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 记录绑定近战快照失败: " + e.Message);
                meleeItemTreeData = null;
                meleeTypeId = 0;
                meleeDisplayName = null;
                return false;
            }
        }

        private Item GetCurrentPlayerBoundMeleeItem_DeathWraith(CharacterMainControl main)
        {
            if (main == null)
            {
                return null;
            }

            try
            {
                Slot meleeSlot = main.MeleeWeaponSlot();
                if (meleeSlot != null && meleeSlot.Content != null)
                {
                    return meleeSlot.Content;
                }
            }
            catch { }

            try
            {
                ItemAgent_MeleeWeapon meleeAgent = main.GetMeleeWeapon();
                if (meleeAgent != null)
                {
                    return meleeAgent.Item;
                }
            }
            catch { }

            return null;
        }

        private bool TryLoadPersistedBoundMeleeSnapshot_DeathWraith(
            out ItemTreeData meleeItemTreeData,
            out int meleeTypeId,
            out string meleeDisplayName)
        {
            meleeItemTreeData = null;
            meleeTypeId = 0;
            meleeDisplayName = null;

            try
            {
                WraithBoundMeleeSnapshot snapshot =
                    SavesSystem.Load<WraithBoundMeleeSnapshot>(DEATH_WRAITH_BOUND_MELEE_SAVE_KEY);
                if (snapshot == null || !snapshot.valid || snapshot.itemTreeData == null)
                {
                    return false;
                }

                meleeItemTreeData = snapshot.itemTreeData;
                meleeTypeId = snapshot.typeId;
                meleeDisplayName = snapshot.displayName;
                return true;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 读取绑定近战快照失败: " + e.Message);
                return false;
            }
        }

        internal void SaveCurrentPlayerBoundMeleeSnapshot_DeathWraith(string source)
        {
            try
            {
                if (!IsDeathWraithSystemEnabled())
                {
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                ItemTreeData meleeItemTreeData;
                int meleeTypeId;
                string meleeDisplayName;
                if (!TryCaptureBoundMeleeSnapshot_DeathWraith(
                    main,
                    out meleeItemTreeData,
                    out meleeTypeId,
                    out meleeDisplayName))
                {
                    return;
                }

                SavesSystem.Save<WraithBoundMeleeSnapshot>(
                    DEATH_WRAITH_BOUND_MELEE_SAVE_KEY,
                    new WraithBoundMeleeSnapshot
                    {
                        valid = true,
                        typeId = meleeTypeId,
                        displayName = meleeDisplayName,
                        itemTreeData = meleeItemTreeData
                    });

                if (!string.IsNullOrEmpty(source))
                {
                    DevLog("[DeathWraith] 已保存绑定近战快照[" + source + "]: "
                        + meleeDisplayName + " (TypeID=" + meleeTypeId + ")");
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] SaveCurrentPlayerBoundMeleeSnapshot 异常: " + e.Message);
            }
        }

        internal void OnCollectSaveData_BoundMeleeSnapshot_DeathWraith()
        {
            SaveCurrentPlayerBoundMeleeSnapshot_DeathWraith("CollectSaveData");
        }

        private WraithInfo ConsumePendingDeathWraithInfo_DeathWraith(CharacterMainControl main)
        {
            WraithInfo info = pendingDeathWraithInfo;
            int primedFrame = pendingDeathWraithPrimedFrame;
            float primedRealtime = pendingDeathWraithPrimedRealtime;
            ClearPendingDeathWraithInfo_DeathWraith();

            if (info == null || !info.valid)
            {
                return null;
            }

            if (!IsPendingDeathWraithInfoFresh_DeathWraith(primedFrame, primedRealtime))
            {
                DevLog("[DeathWraith] 放弃使用过期的预缓存亡魂数据: primedFrame="
                    + primedFrame + ", nowFrame=" + Time.frameCount);
                return null;
            }

            if (main == null)
            {
                return info;
            }

            try
            {
                info.sceneName = GetActiveSceneName_DeathWraith();
                info.subSceneID = GetActiveSubSceneId_DeathWraith();
            }
            catch { }

            try
            {
                info.posX = main.transform.position.x;
                info.posY = main.transform.position.y;
                info.posZ = main.transform.position.z;
            }
            catch { }

            return info;
        }

        private void StorePendingDeathWraithInfo_DeathWraith(WraithInfo info)
        {
            pendingDeathWraithInfo = info;
            if (info != null)
            {
                pendingDeathWraithPrimedFrame = Time.frameCount;
                pendingDeathWraithPrimedRealtime = Time.realtimeSinceStartup;
                return;
            }

            pendingDeathWraithPrimedFrame = -1;
            pendingDeathWraithPrimedRealtime = -1f;
        }

        private void ClearPendingDeathWraithInfo_DeathWraith()
        {
            pendingDeathWraithInfo = null;
            pendingDeathWraithPrimedFrame = -1;
            pendingDeathWraithPrimedRealtime = -1f;
        }

        private bool IsPendingDeathWraithInfoFresh_DeathWraith(int primedFrame, float primedRealtime)
        {
            if (primedFrame < 0 || primedRealtime < 0f)
            {
                return false;
            }

            int frameDelta = Time.frameCount - primedFrame;
            if (frameDelta <= DEATH_WRAITH_PENDING_MAX_FRAME_DELTA)
            {
                return true;
            }

            float ageSeconds = Time.realtimeSinceStartup - primedRealtime;
            return ageSeconds <= DEATH_WRAITH_PENDING_MAX_AGE_SECONDS;
        }

        private bool IsDeathWraithSupportedContext_DeathWraith()
        {
            try
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return false;
                }

                string sceneName = activeScene.name;
                if (string.IsNullOrEmpty(sceneName))
                {
                    return false;
                }

                if (sceneName.IndexOf("Loading", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sceneName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                return CharacterMainControl.Main != null;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
