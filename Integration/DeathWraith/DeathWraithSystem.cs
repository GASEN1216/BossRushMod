// ============================================================================
// DeathWraithSystem.cs — 死亡亡魂系统
// ============================================================================
// 模块说明：
//   玩家死亡后记录装备与属性，下次进入同一子场景时在死亡位置生成
//   一个复制了玩家外观和装备的Boss级敌怪（亡魂）。
//
//   亡魂命名与属性根据掉落物品价值与玩家总财产比例分为三档：
//   - ≥50%：强壮的XX的亡魂（移速+90%，攻击+50%）
//   - 10%~50%：均衡的XX的亡魂（移速+50%，攻击+25%）
//   - <10%：弱小的XX的亡魂（移速+20%）
//
//   亡魂死后不掉落任何物品。再次死亡覆盖更新亡魂数据。
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
    /// <summary>
    /// 亡魂数据（持久化存储）
    /// </summary>
    [Serializable]
    public class WraithInfo
    {
        public bool valid;
        public uint raidID;
        public string sceneName;
        public string subSceneID;
        public float posX, posY, posZ;
        // 仅保留给旧存档兼容，主角本体并不是通过 CharacterRandomPreset 创建。
        public string playerPresetName;
        public string playerPresetRuntimeName;
        public string playerName;
        public int droppedItemsValue;
        public long playerTotalWealth;
        public float playerMaxHealth;
        public bool hasPlayerFaceData;
        public CustomFaceSettingData playerFaceData;
        public AudioManager.VoiceType playerVoiceType;
        public AudioManager.FootStepMaterialType playerFootStepMaterialType;
        public bool hasBoundMeleeSnapshot;
        public int boundMeleeTypeId;
        public string boundMeleeDisplayName;
        public ItemTreeData boundMeleeItemTreeData;
        public ItemTreeData itemTreeData;
        public bool killed;
    }

    [Serializable]
    public class WraithBoundMeleeSnapshot
    {
        public bool valid;
        public int typeId;
        public string displayName;
        public ItemTreeData itemTreeData;
    }

    internal sealed class DeadBodySpawnContext_DeathWraith
    {
        public uint raidID;
        public string subSceneID;
        public Vector3 worldPosition;
    }

    /// <summary>
    /// 亡魂强度等级
    /// </summary>
    public enum WraithTier
    {
        Weak,       // <10%  弱小的
        Balanced,   // 10%~50% 均衡的
        Strong      // ≥50%  强壮的
    }

    /// <summary>
    /// 死亡亡魂系统（partial class ModBehaviour）
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 亡魂系统 — 常量与字段

        private const string DEATH_WRAITH_LIST_SAVE_KEY = "BossRush_DeathWraith_List";
        private const string DEATH_WRAITH_BOUND_MELEE_SAVE_KEY = "BossRush_DeathWraith_BoundMelee";
        private const string DEATH_WRAITH_NAME_KEY_PREFIX = "BossRush_DeathWraith_Name_";
        private const string DEATH_WRAITH_GUN_AI_PRESET_NAME = "Cname_Boss_Red";
        private const string DEATH_WRAITH_MELEE_AI_PRESET_NAME = "Cname_Wolf";
        private const float DEATH_WRAITH_PENDING_MAX_AGE_SECONDS = 0.5f;
        private const int DEATH_WRAITH_PENDING_MAX_FRAME_DELTA = 1;
        private static readonly FieldInfo LevelManager_CharacterModelField =
            typeof(LevelManager).GetField("characterModel", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CharacterRandomPreset_AiControllerField =
            typeof(CharacterRandomPreset).GetField("aiController", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentHolder_MeleeRefField_DeathWraith =
            typeof(ItemAgentHolder).GetField("_meleeRef", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentHolder_GunRefField_DeathWraith =
            typeof(ItemAgentHolder).GetField("_gunRef", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentHolder_CurrentUsingSocketCacheField_DeathWraith =
            typeof(ItemAgentHolder).GetField("_currentUsingSocketCache", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgent_ItemField_DeathWraith =
            typeof(ItemStatsSystem.ItemAgent).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DuckovItemAgent_SocketsListField_DeathWraith =
            typeof(DuckovItemAgent).GetField("socketsList", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentMeleeWeapon_SoundKeyField_DeathWraith =
            typeof(ItemAgent_MeleeWeapon).GetField("soundKey",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentMeleeWeapon_SlashFxDelayTimeField_DeathWraith =
            typeof(ItemAgent_MeleeWeapon).GetField("slashFxDelayTime",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo ItemAgentMeleeWeapon_OnInitializeMethod_DeathWraith =
            typeof(ItemAgent_MeleeWeapon).GetMethod("OnInitialize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private WraithInfo pendingDeathWraithInfo;
        private int pendingDeathWraithPrimedFrame = -1;
        private float pendingDeathWraithPrimedRealtime = -1f;
        private Dictionary<string, CharacterRandomPreset> deathWraithPresetCacheByNameKey;
        private Dictionary<string, CharacterRandomPreset> deathWraithPresetCacheByRuntimeName;
        private readonly Dictionary<uint, CharacterMainControl> activeWraithsByRaidId =
            new Dictionary<uint, CharacterMainControl>();
        private readonly Dictionary<CharacterMainControl, uint> activeWraithRaidIdByCharacter =
            new Dictionary<CharacterMainControl, uint>();
        private readonly HashSet<uint> spawningWraithRaidIds = new HashSet<uint>();
        private readonly List<DeadBodySpawnContext_DeathWraith> pendingDeadBodySpawnContexts =
            new List<DeadBodySpawnContext_DeathWraith>();

        private void HandleDeathWraithConfigChanged_DeathWraith()
        {
            RefreshDeathWraithEventBindings_DeathWraith();

            if (IsDeathWraithSystemEnabled())
            {
                DevLog("[DeathWraith] 系统配置已开启");
                return;
            }

            DevLog("[DeathWraith] 系统配置已关闭，清理当前与已存亡魂状态");
            ClearDeathWraithState_DeathWraith();
            InvalidateStoredDeathWraithRecords_DeathWraith("配置关闭");
        }

        private void RefreshDeathWraithEventBindings_DeathWraith()
        {
            try
            {
                Health.OnHurt -= PrimeDeathWraithData_DeathWraith;
                Health.OnDead -= RecordDeathWraithData_DeathWraith;
                Health.OnDead -= OnWraithDied_DeathWraith;
                SavesSystem.OnCollectSaveData -= OnCollectSaveData_BoundMeleeSnapshot_DeathWraith;

                if (!IsDeathWraithSystemEnabled())
                {
                    return;
                }

                Health.OnHurt += PrimeDeathWraithData_DeathWraith;
                Health.OnDead += RecordDeathWraithData_DeathWraith;
                Health.OnDead += OnWraithDied_DeathWraith;
                SavesSystem.OnCollectSaveData += OnCollectSaveData_BoundMeleeSnapshot_DeathWraith;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 刷新事件绑定失败: " + e.Message);
            }
        }

        private void OnSetFile_DeathWraith()
        {
            pendingDeadBodySpawnContexts.Clear();
            spawningWraithRaidIds.Clear();
        }

        #endregion

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

        #region 亡魂系统 — 生成

        internal void NotifyOriginalDeadBodySpawnRequested_DeathWraith(DeadBodyManager.DeathInfo info)
        {
            if (!IsDeathWraithSystemEnabled() || info == null || !info.valid)
            {
                return;
            }

            RemovePendingDeadBodySpawnContextByRaidId_DeathWraith(info.raidID);
            pendingDeadBodySpawnContexts.Add(new DeadBodySpawnContext_DeathWraith
            {
                raidID = info.raidID,
                subSceneID = info.subSceneID ?? string.Empty,
                worldPosition = info.worldPosition
            });
        }

        internal void NotifyOriginalDeadBodyLootboxCreated_DeathWraith(
            InteractableLootbox lootbox,
            Item item,
            Vector3 position,
            InteractableLootbox prefab)
        {
            if (!IsDeathWraithSystemEnabled() || lootbox == null)
            {
                return;
            }

            InteractableLootbox tombPrefab = null;
            try
            {
                if (GameplayDataSettings.Prefabs != null)
                {
                    tombPrefab = GameplayDataSettings.Prefabs.LootBoxPrefab_Tomb;
                }
            }
            catch { }

            InteractableLootbox actualPrefab = prefab != null ? prefab : InteractableLootbox.Prefab;
            if (tombPrefab == null || actualPrefab != tombPrefab)
            {
                return;
            }

            DeadBodySpawnContext_DeathWraith context =
                TryConsumePendingDeadBodySpawnContext_DeathWraith(position, GetActiveSubSceneId_DeathWraith());
            if (context == null)
            {
                return;
            }

            TrySpawnStoredDeathWraithForRaid_DeathWraith(context.raidID, lootbox.transform.position);
        }

        internal void NotifyOriginalDeadBodyTouched_DeathWraith(DeadBodyManager.DeathInfo info)
        {
            if (info == null)
            {
                return;
            }

            RemovePendingDeadBodySpawnContextByRaidId_DeathWraith(info.raidID);
            RemoveStoredDeathWraithInfoByRaidId_DeathWraith(info.raidID, "原版遗失物被开启");
        }

        private DeadBodySpawnContext_DeathWraith TryConsumePendingDeadBodySpawnContext_DeathWraith(
            Vector3 worldPosition,
            string subSceneID)
        {
            const float positionTolerance = 0.05f;
            for (int i = 0; i < pendingDeadBodySpawnContexts.Count; i++)
            {
                DeadBodySpawnContext_DeathWraith context = pendingDeadBodySpawnContexts[i];
                if (context == null)
                {
                    continue;
                }

                if (!string.Equals(context.subSceneID ?? string.Empty, subSceneID ?? string.Empty, StringComparison.Ordinal))
                {
                    continue;
                }

                if ((context.worldPosition - worldPosition).sqrMagnitude > positionTolerance * positionTolerance)
                {
                    continue;
                }

                pendingDeadBodySpawnContexts.RemoveAt(i);
                return context;
            }

            return null;
        }

        private void RemovePendingDeadBodySpawnContextByRaidId_DeathWraith(uint raidID)
        {
            for (int i = pendingDeadBodySpawnContexts.Count - 1; i >= 0; i--)
            {
                DeadBodySpawnContext_DeathWraith context = pendingDeadBodySpawnContexts[i];
                if (context != null && context.raidID == raidID)
                {
                    pendingDeadBodySpawnContexts.RemoveAt(i);
                }
            }
        }

        private async void TrySpawnStoredDeathWraithForRaid_DeathWraith(uint raidID, Vector3 spawnPos)
        {
            CharacterMainControl spawnedWraith = null;
            bool registered = false;
            try
            {
                if (!IsDeathWraithSystemEnabled())
                {
                    return;
                }

                if (spawningWraithRaidIds.Contains(raidID) || activeWraithsByRaidId.ContainsKey(raidID))
                {
                    return;
                }

                WraithInfo info = FindStoredDeathWraithInfoByRaidId_DeathWraith(raidID);
                if (info == null || !info.valid)
                {
                    return;
                }

                spawningWraithRaidIds.Add(raidID);
                DevLog("[DeathWraith] 开始生成与原版遗失物绑定的亡魂: raidID=" + raidID);

                CharacterMainControl wraith = null;
                try
                {
                    wraith = await CreateWraithCharacterFromPlayerSnapshot_DeathWraith(info, spawnPos);
                    spawnedWraith = wraith;
                }
                catch (Exception e)
                {
                    DevLog("[DeathWraith] 创建亡魂宿主异常: " + e.Message + "\n" + e.StackTrace);
                    return;
                }

                if (wraith == null)
                {
                    DevLog("[DeathWraith] 角色生成失败");
                    return;
                }

                await UniTask.Yield();

                NormalizeDamageMultiplier(wraith);
                RestoreWraithMaxHealthSnapshot_DeathWraith(wraith, info.playerMaxHealth);
                ApplyBossStatMultiplier(wraith);

                wraith.dropBoxOnDead = false;
                wraith.SetTeam(Teams.scav);
                InitializeWraithAI_DeathWraith(wraith, spawnPos, info);

                WraithTier tier = ClassifyWraithTier_DeathWraith(info.droppedItemsValue, info.playerTotalWealth);
                string displayName = GetWraithDisplayName_DeathWraith(info.playerName, tier);
                string displayNameKey = CreateWraithDisplayNameKey_DeathWraith(displayName);
                ApplyWraithRuntimePreset_DeathWraith(wraith, info, displayNameKey, displayName);
                await PrepareWraithCombatLoadout_DeathWraith(wraith);

                try
                {
                    if (wraith.Health != null)
                    {
                        wraith.Health.showHealthBar = true;
                    }
                }
                catch { }

                ApplyWraithTierStats_DeathWraith(wraith, tier);

                try
                {
                    if (wraith.Health != null)
                    {
                        wraith.Health.SetHealth(wraith.Health.MaxHealth);
                    }
                }
                catch { }

                wraith.gameObject.name = "BossRush_DeathWraith_" + raidID;
                wraith.gameObject.SetActive(true);

                try
                {
                    if (wraith.Health != null)
                    {
                        wraith.Health.RequestHealthBar();
                    }
                }
                catch { }

                RegisterActiveWraith_DeathWraith(raidID, wraith);
                registered = true;
                spawnedWraith = null;

                DevLog("[DeathWraith] 亡魂生成成功: " + displayName + " tier=" + tier + " raidID=" + raidID);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] TrySpawnStoredDeathWraithForRaid 异常: " + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                if (spawnedWraith != null)
                {
                    DestroyWraithInstance_DeathWraith(spawnedWraith, "生成流程异常中断");
                }

                if (!registered)
                {
                    activeWraithsByRaidId.Remove(raidID);
                }
                spawningWraithRaidIds.Remove(raidID);
            }
        }

        /// <summary>
        /// 主角本体由 CharacterCreator + LevelManager.characterModel 创建。
        /// 亡魂应沿用同一条链创建宿主角色，再切换成敌对 AI。
        /// </summary>
        private async UniTask<CharacterMainControl> CreateWraithCharacterFromPlayerSnapshot_DeathWraith(
            WraithInfo info,
            Vector3 spawnPos)
        {
            if (info == null)
            {
                return null;
            }

            LevelManager level = LevelManager.Instance;
            if (level == null || level.CharacterCreator == null)
            {
                DevLog("[DeathWraith] LevelManager.CharacterCreator 不可用，无法创建亡魂");
                return null;
            }

            CharacterModel mainCharacterModelPrefab = GetMainCharacterModelPrefab_DeathWraith(level);
            if (mainCharacterModelPrefab == null)
            {
                DevLog("[DeathWraith] 未找到主角 CharacterModel 预设，无法创建亡魂");
                return null;
            }

            Item characterItem = null;
            try
            {
                if (info.itemTreeData != null)
                {
                    characterItem = await ItemTreeData.InstantiateAsync(info.itemTreeData);
                    if (characterItem != null)
                    {
                        RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                            characterItem,
                            "DeathWraith.CreateCharacter");
                    }
                }

                if (characterItem == null)
                {
                    characterItem = await level.CharacterCreator.LoadOrCreateCharacterItemInstance(
                        GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
                    DevLog("[DeathWraith] 未取到死亡时装备树，使用默认主角物品容器创建亡魂宿主");
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 创建亡魂物品树异常: " + e.Message);
                return null;
            }

            if (characterItem == null)
            {
                DevLog("[DeathWraith] 亡魂宿主物品树为空，放弃生成");
                return null;
            }

            CharacterMainControl wraith = null;
            try
            {
                wraith = await level.CharacterCreator.CreateCharacter(
                    characterItem,
                    mainCharacterModelPrefab,
                    spawnPos,
                    Quaternion.identity);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] CharacterCreator.CreateCharacter 异常: " + e.Message);
                return null;
            }

            if (wraith == null)
            {
                return null;
            }

            ApplyStoredWraithFaceData_DeathWraith(wraith, info);
            await EnsureStoredBoundMeleeEquipped_DeathWraith(wraith, info);
            return wraith;
        }

        private CharacterModel GetMainCharacterModelPrefab_DeathWraith(LevelManager level)
        {
            if (level == null)
            {
                return null;
            }

            try
            {
                if (LevelManager_CharacterModelField == null)
                {
                    DevLog("[DeathWraith] 反射不到 LevelManager.characterModel");
                    return null;
                }

                CharacterModel modelPrefab = LevelManager_CharacterModelField.GetValue(level) as CharacterModel;
                return modelPrefab;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 获取主角 CharacterModel 预设失败: " + e.Message);
                return null;
            }
        }

        private void ApplyStoredWraithFaceData_DeathWraith(CharacterMainControl wraith, WraithInfo info)
        {
            if (wraith == null || info == null || !info.hasPlayerFaceData)
            {
                return;
            }

            try
            {
                if (wraith.characterModel != null)
                {
                    wraith.characterModel.SetFaceFromData(info.playerFaceData);
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 应用亡魂捏脸数据失败: " + e.Message);
            }
        }

        private async UniTask EnsureStoredBoundMeleeEquipped_DeathWraith(
            CharacterMainControl wraith,
            WraithInfo info)
        {
            if (wraith == null || info == null || !info.hasBoundMeleeSnapshot ||
                info.boundMeleeItemTreeData == null)
            {
                return;
            }

            try
            {
                Slot meleeSlot = wraith.MeleeWeaponSlot();
                if (meleeSlot != null && meleeSlot.Content != null &&
                    meleeSlot.Content.TypeID == info.boundMeleeTypeId)
                {
                    return;
                }

                Item meleeItem = await ItemTreeData.InstantiateAsync(info.boundMeleeItemTreeData);
                if (meleeItem == null)
                {
                    DevLog("[DeathWraith] [WARNING] 绑定近战实例化失败");
                    return;
                }

                RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                    meleeItem,
                            "DeathWraith.BoundMelee");

                bool equipped = false;
                Item pluggedOut = null;
                try
                {
                    if (meleeSlot != null)
                    {
                        meleeSlot.Plug(meleeItem, out pluggedOut);
                        equipped = meleeSlot.Content == meleeItem;
                    }
                }
                catch { }

                if (!equipped)
                {
                    try
                    {
                        equipped = wraith.CharacterItem != null &&
                            wraith.CharacterItem.TryPlug(meleeItem, true, null, 0);
                    }
                    catch { }
                }

                if (pluggedOut != null && pluggedOut != meleeItem)
                {
                    Inventory inventory = wraith.CharacterItem != null ? wraith.CharacterItem.Inventory : null;
                    if (!TryAddItemToInventory_DeathWraith(inventory, pluggedOut))
                    {
                        DestroyDetachedItem_DeathWraith(pluggedOut, "绑定近战替换后的旧物品无法回收");
                    }
                }

                if (!equipped)
                {
                    Inventory inventory = wraith.CharacterItem != null ? wraith.CharacterItem.Inventory : null;
                    if (!TryAddItemToInventory_DeathWraith(inventory, meleeItem))
                    {
                        DestroyDetachedItem_DeathWraith(meleeItem, "绑定近战回填失败");
                        return;
                    }
                }

                DevLog("[DeathWraith] 已回填绑定近战武器: "
                    + (string.IsNullOrEmpty(info.boundMeleeDisplayName) ? "<unknown>" : info.boundMeleeDisplayName)
                    + " (TypeID=" + info.boundMeleeTypeId + ")");
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 回填绑定近战武器失败: " + e.Message);
            }
        }

        private void InitializeWraithAI_DeathWraith(
            CharacterMainControl wraith,
            Vector3 spawnPos,
            WraithInfo info)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                wraith.AudioVoiceType = info != null
                    ? info.playerVoiceType
                    : AudioManager.VoiceType.Duck;
                wraith.FootStepMaterialType = info != null
                    ? info.playerFootStepMaterialType
                    : AudioManager.FootStepMaterialType.organic;
            }
            catch { }

            try
            {
                if (wraith.aiCharacterController == null)
                {
                    Item preferredWeapon = SelectPreferredCombatWeaponItem_DeathWraith(wraith);
                    string presetName =
                        ResolveWraithHostPresetNameForWeapon_DeathWraith(preferredWeapon);
                    AICharacterController aiPrefab =
                        GetWraithHostAIPrefab_DeathWraith(presetName);
                    if (aiPrefab != null)
                    {
                        AICharacterController clonedAi = UnityEngine.Object.Instantiate(aiPrefab);
                        wraith.aiCharacterController = clonedAi;
                        DevLog("[DeathWraith] 已为亡魂克隆 AI 控制器: " + aiPrefab.name
                            + " | preset=" + presetName
                            + " | weapon=" + (preferredWeapon != null ? preferredWeapon.DisplayName : "<null>"));
                    }
                    else
                    {
                        DevLog("[DeathWraith] [WARNING] 未找到可用 AI 控制器预设，亡魂可能无法主动战斗");
                    }
                }

                if (wraith.aiCharacterController != null &&
                    wraith.aiCharacterController.CharacterMainControl != wraith)
                {
                    wraith.aiCharacterController.Init(
                        wraith,
                        spawnPos,
                        wraith.AudioVoiceType,
                        wraith.FootStepMaterialType);
                    DevLog("[DeathWraith] 亡魂 AI 初始化完成");
                    try
                    {
                        DevLog("[DeathWraith] AI 参数: reaction=" + wraith.aiCharacterController.baseReactionTime
                            + ", shootDelay=" + wraith.aiCharacterController.shootDelay);
                    }
                    catch { }

                    try
                    {
                        // 与项目中其他“自然感知”AI路径保持一致：不在出生时强制锁定玩家。
                        wraith.aiCharacterController.forceTracePlayerDistance = 0f;
                        wraith.aiCharacterController.searchedEnemy = null;
                        wraith.aiCharacterController.noticed = false;
                        DevLog("[DeathWraith] 亡魂 AI 保持自然感知，不设置初始仇恨目标");
                    }
                    catch (Exception aggroEx)
                    {
                        DevLog("[DeathWraith] 重置初始仇恨状态失败: " + aggroEx.Message);
                    }
                }
                else if (wraith.aiCharacterController != null)
                {
                    DevLog("[DeathWraith] 亡魂 AI 已绑定，无需重复初始化");
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 初始化亡魂 AI 失败: " + e.Message);
            }
        }

        private string ResolveWraithHostPresetNameForWeapon_DeathWraith(Item preferredWeapon)
        {
            if (IsGunItem_DeathWraith(preferredWeapon))
            {
                return DEATH_WRAITH_GUN_AI_PRESET_NAME;
            }

            return DEATH_WRAITH_MELEE_AI_PRESET_NAME;
        }

        private AICharacterController GetWraithHostAIPrefab_DeathWraith(string presetName)
        {
            EnsureWraithPresetCache_DeathWraith();

            try
            {
                CharacterRandomPreset forcedPreset;
                bool matchedForcedPreset =
                    TryGetWraithPresetByNameKey_DeathWraith(
                        presetName,
                        out forcedPreset) ||
                    TryGetWraithPresetByRuntimeName_DeathWraith(
                        presetName,
                        out forcedPreset);

                if (!matchedForcedPreset || forcedPreset == null)
                {
                    DevLog("[DeathWraith] [WARNING] 未找到固定 AI 宿主预设: "
                        + presetName);
                    return null;
                }

                AICharacterController forcedAiPrefab =
                    GetAIPrefabFromPreset_DeathWraith(forcedPreset);
                if (forcedAiPrefab == null)
                {
                    DevLog("[DeathWraith] [WARNING] 固定 AI 宿主预设缺少 AI 控制器: "
                        + forcedPreset.name
                        + " (nameKey=" + forcedPreset.nameKey + ")");
                    return null;
                }

                DevLog("[DeathWraith] 使用固定 AI 宿主预设: "
                    + forcedPreset.name
                    + " (nameKey=" + forcedPreset.nameKey
                    + ", ai=" + forcedAiPrefab.name + ")");
                return forcedAiPrefab;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 解析固定 AI 宿主预设失败: " + e.Message);
            }

            return null;
        }

        private static AICharacterController GetAIPrefabFromPreset_DeathWraith(CharacterRandomPreset preset)
        {
            if (preset == null || CharacterRandomPreset_AiControllerField == null)
            {
                return null;
            }

            try
            {
                return CharacterRandomPreset_AiControllerField.GetValue(preset) as AICharacterController;
            }
            catch
            {
                return null;
            }
        }

        private async UniTask PrepareWraithCombatLoadout_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                ForceRefreshWraithEquipmentAgents_DeathWraith(wraith);

                Item selectedWeapon = SelectPreferredCombatWeaponItem_DeathWraith(wraith);
                if (selectedWeapon == null)
                {
                    DevLog("[DeathWraith] [WARNING] 亡魂没有可用武器");
                    return;
                }

                wraith.ChangeHoldItem(selectedWeapon);
                await UniTask.Yield();

                ItemAgent_Gun gun = wraith.GetGun();
                if (gun != null)
                {
                    SyncWraithCombatMode_DeathWraith(wraith, false);
                    await EnsureWraithGunReady_DeathWraith(wraith, gun);
                    DevLog("[DeathWraith] 已切换为枪械作战: " + gun.Item.DisplayName);
                    return;
                }

                ItemAgent_MeleeWeapon melee = wraith.GetMeleeWeapon();
                if (melee == null && IsLikelyMeleeWeaponItem_DeathWraith(wraith, selectedWeapon))
                {
                    melee = EnsureWraithMeleeAgentReady_DeathWraith(wraith, selectedWeapon);
                }

                if (melee != null)
                {
                    SyncWraithCombatMode_DeathWraith(wraith, true);
                    DevLog("[DeathWraith] 已切换为近战作战: " + melee.Item.DisplayName);
                    return;
                }

                DevLog("[DeathWraith] [WARNING] 切换武器后仍未拿到枪械/近战代理");
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 准备亡魂战斗装备失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private ItemAgent_MeleeWeapon EnsureWraithMeleeAgentReady_DeathWraith(
            CharacterMainControl wraith,
            Item meleeItem)
        {
            if (wraith == null || meleeItem == null)
            {
                return null;
            }

            try
            {
                DuckovItemAgent holdAgent = wraith.CurrentHoldItemAgent;
                if (holdAgent == null)
                {
                    DevLog("[DeathWraith] [WARNING] 近战兜底失败：当前没有手持代理");
                    return null;
                }

                Item holdItem = null;
                try
                {
                    holdItem = holdAgent.Item;
                }
                catch
                {
                }

                if (holdItem != null && holdItem != meleeItem)
                {
                    DevLog("[DeathWraith] [WARNING] 近战兜底中止：当前手持物品与目标近战不一致");
                    return null;
                }

                ItemAgent_MeleeWeapon meleeAgent = holdAgent as ItemAgent_MeleeWeapon;
                if (meleeAgent == null)
                {
                    meleeAgent = holdAgent.GetComponent<ItemAgent_MeleeWeapon>();
                }

                bool addedAtRuntime = false;
                if (meleeAgent == null)
                {
                    meleeAgent = holdAgent.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
                    addedAtRuntime = true;
                }

                CopyMeleeAgentDefaultsFromTemplate_DeathWraith(meleeAgent, meleeItem);

                if (ItemAgent_ItemField_DeathWraith != null)
                {
                    ItemAgent_ItemField_DeathWraith.SetValue(meleeAgent, meleeItem);
                }

                EnsureDuckovItemAgentSocketsInitialized_DeathWraith(holdAgent);
                EnsureDuckovItemAgentSocketsInitialized_DeathWraith(meleeAgent);

                if (ItemAgentMeleeWeapon_OnInitializeMethod_DeathWraith != null)
                {
                    try
                    {
                        ItemAgentMeleeWeapon_OnInitializeMethod_DeathWraith.Invoke(meleeAgent, null);
                    }
                    catch
                    {
                    }
                }

                meleeAgent.SetHolder(wraith);
                holdAgent.handheldSocket = meleeAgent.handheldSocket;
                holdAgent.handAnimationType = meleeAgent.handAnimationType;

                Transform weaponSocket = null;
                if (wraith.characterModel != null)
                {
                    weaponSocket = wraith.characterModel.RightHandSocket;
                    if (weaponSocket == null)
                    {
                        weaponSocket = wraith.characterModel.MeleeWeaponSocket;
                    }
                }

                if (weaponSocket != null)
                {
                    holdAgent.transform.SetParent(weaponSocket, false);
                    holdAgent.transform.localPosition = Vector3.zero;
                    holdAgent.transform.localRotation = Quaternion.identity;
                    if (wraith.agentHolder != null &&
                        ItemAgentHolder_CurrentUsingSocketCacheField_DeathWraith != null)
                    {
                        ItemAgentHolder_CurrentUsingSocketCacheField_DeathWraith.SetValue(
                            wraith.agentHolder,
                            weaponSocket);
                    }
                }

                if (wraith.agentHolder != null)
                {
                    if (ItemAgentHolder_MeleeRefField_DeathWraith != null)
                    {
                        ItemAgentHolder_MeleeRefField_DeathWraith.SetValue(wraith.agentHolder, meleeAgent);
                    }

                    if (ItemAgentHolder_GunRefField_DeathWraith != null)
                    {
                        ItemAgentHolder_GunRefField_DeathWraith.SetValue(wraith.agentHolder, null);
                    }
                }

                ItemAgent_MeleeWeapon resolved = wraith.GetMeleeWeapon();
                if (resolved != null)
                {
                    DevLog("[DeathWraith] 已补齐近战手持代理: "
                        + meleeItem.DisplayName
                        + " | runtimeAdded=" + addedAtRuntime);
                    return resolved;
                }

                DevLog("[DeathWraith] [WARNING] 近战代理兜底执行后仍未拿到 _meleeRef: "
                    + meleeItem.DisplayName);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 近战代理兜底失败: " + e.Message + "\n" + e.StackTrace);
            }

            return null;
        }

        private void CopyMeleeAgentDefaultsFromTemplate_DeathWraith(
            ItemAgent_MeleeWeapon target,
            Item meleeItem)
        {
            if (target == null)
            {
                return;
            }

            ItemAgent_MeleeWeapon template = null;
            try
            {
                if (meleeItem != null)
                {
                    template = meleeItem.GetComponent<ItemAgent_MeleeWeapon>();
                }
            }
            catch
            {
            }

            target.handheldSocket = template != null
                ? template.handheldSocket
                : HandheldSocketTypes.normalHandheld;
            target.handAnimationType = template != null
                ? template.handAnimationType
                : HandheldAnimationType.meleeWeapon;

            if (template != null)
            {
                if (template.hitFx != null)
                {
                    target.hitFx = template.hitFx;
                }

                if (template.slashFx != null)
                {
                    target.slashFx = template.slashFx;
                }

                if (ItemAgentMeleeWeapon_SlashFxDelayTimeField_DeathWraith != null)
                {
                    try
                    {
                        object slashDelay = ItemAgentMeleeWeapon_SlashFxDelayTimeField_DeathWraith.GetValue(template);
                        if (slashDelay != null)
                        {
                            ItemAgentMeleeWeapon_SlashFxDelayTimeField_DeathWraith.SetValue(target, slashDelay);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (ItemAgentMeleeWeapon_SoundKeyField_DeathWraith != null)
            {
                try
                {
                    string soundKey = "Default";
                    if (template != null)
                    {
                        object rawKey = ItemAgentMeleeWeapon_SoundKeyField_DeathWraith.GetValue(template);
                        if (rawKey is string templateKey && !string.IsNullOrWhiteSpace(templateKey))
                        {
                            soundKey = templateKey;
                        }
                    }

                    ItemAgentMeleeWeapon_SoundKeyField_DeathWraith.SetValue(target, soundKey);
                }
                catch
                {
                }
            }
        }

        private void EnsureDuckovItemAgentSocketsInitialized_DeathWraith(DuckovItemAgent agent)
        {
            if (agent == null || DuckovItemAgent_SocketsListField_DeathWraith == null)
            {
                return;
            }

            try
            {
                object socketsList = DuckovItemAgent_SocketsListField_DeathWraith.GetValue(agent);
                if (socketsList == null)
                {
                    DuckovItemAgent_SocketsListField_DeathWraith.SetValue(agent, new List<Transform>());
                }
            }
            catch
            {
            }
        }

        private bool IsLikelyMeleeWeaponItem_DeathWraith(CharacterMainControl wraith, Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                if (item.GetComponent<ItemSetting_MeleeWeapon>() != null)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                Slot meleeSlot = wraith != null ? wraith.MeleeWeaponSlot() : null;
                return meleeSlot != null && meleeSlot.Content == item;
            }
            catch
            {
                return false;
            }
        }

        private void ForceRefreshWraithEquipmentAgents_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null || wraith.CharacterItem == null || wraith.CharacterItem.Slots == null)
            {
                return;
            }

            try
            {
                foreach (Slot slot in wraith.CharacterItem.Slots)
                {
                    if (slot != null && slot.Content != null)
                    {
                        slot.ForceInvokeSlotContentChangedEvent();
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 刷新亡魂装备代理失败: " + e.Message);
            }
        }

        private void SyncWraithCombatMode_DeathWraith(CharacterMainControl wraith, bool meleeMode)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                AICharacterController ai = wraith.aiCharacterController;
                if (ai == null)
                {
                    DevLog("[DeathWraith] [WARNING] 无 AI 控制器，无法同步近战/枪战模式");
                    return;
                }

                ai.melee = meleeMode;
                ai.defaultWeaponOut = true;

                if (meleeMode)
                {
                    DevLog("[DeathWraith] AI 已切换为近战态（保留预设近战参数）");
                }
                else
                {
                    DevLog("[DeathWraith] AI 已切换为枪战态");
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 同步亡魂战斗模式失败: " + e.Message);
            }
        }

        private Item SelectPreferredCombatWeaponItem_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return null;
            }

            Slot primary = wraith.PrimWeaponSlot();
            if (primary != null && IsGunItem_DeathWraith(primary.Content))
            {
                return primary.Content;
            }

            Slot secondary = wraith.SecWeaponSlot();
            if (secondary != null && IsGunItem_DeathWraith(secondary.Content))
            {
                return secondary.Content;
            }

            Slot melee = wraith.MeleeWeaponSlot();
            if (melee != null && melee.Content != null)
            {
                return melee.Content;
            }

            if (primary != null && primary.Content != null)
            {
                return primary.Content;
            }

            if (secondary != null && secondary.Content != null)
            {
                return secondary.Content;
            }

            return null;
        }

        private static bool IsGunItem_DeathWraith(Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                return item.GetComponent<ItemSetting_Gun>() != null;
            }
            catch
            {
                return false;
            }
        }

        private async UniTask EnsureWraithGunReady_DeathWraith(CharacterMainControl wraith, ItemAgent_Gun gun)
        {
            if (wraith == null || gun == null || gun.Item == null)
            {
                return;
            }

            try
            {
                ItemSetting_Gun gunSetting = gun.GunItemSetting;
                Inventory inventory = wraith.CharacterItem != null ? wraith.CharacterItem.Inventory : null;
                if (gunSetting == null || inventory == null)
                {
                    DevLog("[DeathWraith] [WARNING] 枪械缺少 GunSetting 或库存，无法补弹");
                    return;
                }

                Item ammoPrototype = ResolveWraithAmmoPrototype_DeathWraith(gunSetting, inventory);
                if (ammoPrototype == null)
                {
                    DevLog("[DeathWraith] [WARNING] 未能解析亡魂枪械的对应子弹: " + gun.Item.DisplayName);
                    return;
                }

                int targetBulletId = ammoPrototype.TypeID;
                gunSetting.SetTargetBulletType(targetBulletId);

                int existingAmmo = CountAmmoInInventory_DeathWraith(inventory, targetBulletId);
                int desiredAmmo = Math.Max(gunSetting.Capacity * 3, Math.Max(30, ammoPrototype.MaxStackCount));
                if (existingAmmo < desiredAmmo)
                {
                    int added = desiredAmmo - existingAmmo;
                    AddAmmoToInventory_DeathWraith(inventory, ammoPrototype, added);
                    DevLog("[DeathWraith] 已为枪械补充子弹: typeID=" + targetBulletId
                        + ", added=" + added);
                }

                try
                {
                    gun.Item.Variables.SetInt("BulletCount", gunSetting.Capacity, true);
                }
                catch
                {
                    try
                    {
                        gun.Item.Variables.SetInt("BulletCount".GetHashCode(), gunSetting.Capacity);
                    }
                    catch { }
                }

                gunSetting.AutoSetTypeInInventory(inventory);
                gunSetting.LoadBulletsFromInventory(inventory);
                await UniTask.Yield();
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 补充亡魂枪械子弹失败: " + e.Message);
            }
        }

        private Item ResolveWraithAmmoPrototype_DeathWraith(ItemSetting_Gun gunSetting, Inventory inventory)
        {
            if (gunSetting == null)
            {
                return null;
            }

            try
            {
                if (gunSetting.TargetBulletID > 0)
                {
                    Item exactBullet = ItemAssetsCollection.InstantiateSync(gunSetting.TargetBulletID);
                    if (exactBullet != null)
                    {
                        return exactBullet;
                    }
                }
            }
            catch { }

            if (inventory == null)
            {
                return null;
            }

            try
            {
                string weaponCaliber = gunSetting.Item != null
                    ? gunSetting.Item.Constants.GetString("Caliber".GetHashCode(), null)
                    : null;

                foreach (Item item in inventory)
                {
                    if (item == null || !item.GetBool("IsBullet", false))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(weaponCaliber))
                    {
                        string ammoCaliber = item.Constants.GetString("Caliber".GetHashCode(), null);
                        if (!string.Equals(ammoCaliber, weaponCaliber, StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    return item;
                }
            }
            catch { }

            return null;
        }

        private int CountAmmoInInventory_DeathWraith(Inventory inventory, int ammoTypeId)
        {
            if (inventory == null || ammoTypeId <= 0)
            {
                return 0;
            }

            int count = 0;
            try
            {
                foreach (Item item in inventory)
                {
                    if (item != null && item.TypeID == ammoTypeId)
                    {
                        count += Math.Max(1, item.StackCount);
                    }
                }
            }
            catch { }

            return count;
        }

        private void AddAmmoToInventory_DeathWraith(Inventory inventory, Item ammoPrototype, int amountToAdd)
        {
            if (inventory == null || ammoPrototype == null || amountToAdd <= 0)
            {
                return;
            }

            int remaining = amountToAdd;
            int maxStack = Math.Max(1, ammoPrototype.MaxStackCount);
            while (remaining > 0)
            {
                Item ammo = ammoPrototype.CreateInstance();
                if (ammo == null)
                {
                    break;
                }

                int stack = Math.Min(remaining, maxStack);
                ammo.StackCount = Math.Max(1, stack);
                inventory.AddItem(ammo);
                remaining -= ammo.StackCount;
            }
        }

        private void ApplyWraithRuntimePreset_DeathWraith(
            CharacterMainControl wraith,
            WraithInfo info,
            string displayNameKey,
            string displayName)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                CharacterRandomPreset sourcePreset = wraith.characterPreset;
                CharacterRandomPreset runtimePreset = sourcePreset != null
                    ? UnityEngine.Object.Instantiate(sourcePreset)
                    : ScriptableObject.CreateInstance<CharacterRandomPreset>();
                DestroyOwnedWraithPresetClone_DeathWraith(wraith);

                runtimePreset.name = "BossRush_DeathWraithPreset(Clone)";
                runtimePreset.aiCombatFactor = 1f;
                runtimePreset.showName = true;
                runtimePreset.showHealthBar = true;
                runtimePreset.dropBoxOnDead = false;
                runtimePreset.team = Teams.scav;
                runtimePreset.hasSoul = false;
                runtimePreset.voiceType = info != null
                    ? info.playerVoiceType
                    : wraith.AudioVoiceType;
                runtimePreset.footstepMaterialType = info != null
                    ? info.playerFootStepMaterialType
                    : wraith.FootStepMaterialType;
                runtimePreset.nameKey = !string.IsNullOrEmpty(displayNameKey)
                    ? displayNameKey
                    : displayName;

                if (ReflectionCache.CharacterRandomPreset_CharacterIconType != null)
                {
                    ReflectionCache.CharacterRandomPreset_CharacterIconType.SetValue(
                        runtimePreset,
                        CharacterIconTypes.pmc);
                }

                wraith.characterPreset = runtimePreset;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 创建亡魂运行时预设失败: " + e.Message);
            }
        }



        private void RestoreWraithItemRuntimeStateRecursive_DeathWraith(Item item, string reason, int depth = 0)
        {
            if (item == null || depth > 16)
            {
                return;
            }

            try
            {
                CustomItemRuntimeStateHelper.RestoreRuntimeState(item, reason);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 恢复物品运行时状态异常: " + e.Message);
            }

            try
            {
                if (item.Slots != null)
                {
                    foreach (Slot slot in item.Slots)
                    {
                        if (slot == null || slot.Content == null)
                        {
                            continue;
                        }

                        RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                            slot.Content,
                            reason + ":slot:" + slot.Key,
                            depth + 1);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 遍历装备槽恢复异常: " + e.Message);
            }

            try
            {
                if (item.Inventory != null)
                {
                    foreach (Item invItem in item.Inventory)
                    {
                        if (invItem == null)
                        {
                            continue;
                        }

                        RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                            invItem,
                            reason + ":inv",
                            depth + 1);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 遍历背包恢复异常: " + e.Message);
            }
        }

        private void DestroyDetachedItem_DeathWraith(Item item, string reason)
        {
            if (item == null)
            {
                return;
            }

            string itemName = null;
            try
            {
                itemName = item.DisplayName;
            }
            catch { }

            try
            {
                item.Detach();
            }
            catch { }

            try
            {
                if (item.gameObject != null)
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 销毁未转移物品异常: " + e.Message);
            }

            DevLog("[DeathWraith] 已销毁未成功转移的物品: "
                + (string.IsNullOrEmpty(itemName) ? "<unknown>" : itemName)
                + " | reason=" + reason);
        }

        private bool TryAddItemToInventory_DeathWraith(Inventory inventory, Item item)
        {
            if (inventory == null || item == null)
            {
                return false;
            }

            try
            {
                item.Detach();
            }
            catch { }

            try
            {
                if (inventory.AddAndMerge(item, 0))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                return inventory.AddItem(item);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 亡魂系统 — 击杀处理

        /// <summary>
        /// 亡魂被击杀时的处理
        /// </summary>
        private void OnWraithDied_DeathWraith(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                if (deadHealth == null)
                {
                    return;
                }

                var character = deadHealth.TryGetCharacter();
                if (character == null)
                {
                    return;
                }

                uint raidID;
                if (!activeWraithRaidIdByCharacter.TryGetValue(character, out raidID))
                {
                    return;
                }

                ForgetActiveWraith_DeathWraith(character);
                DestroyOwnedWraithPresetClone_DeathWraith(character);
                DevLog("[DeathWraith] 亡魂已被击杀: raidID=" + raidID);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] OnWraithDied 异常: " + e.Message);
            }
        }

        #endregion

        #region 亡魂系统 — 场景清理

        /// <summary>
        /// 场景切换时清理亡魂状态
        /// </summary>
        private void ClearDeathWraithState_DeathWraith()
        {
            ClearPendingDeathWraithInfo_DeathWraith();
            pendingDeadBodySpawnContexts.Clear();
            spawningWraithRaidIds.Clear();

            List<CharacterMainControl> activeWraiths =
                new List<CharacterMainControl>(activeWraithsByRaidId.Values);
            activeWraithsByRaidId.Clear();
            activeWraithRaidIdByCharacter.Clear();

            for (int i = 0; i < activeWraiths.Count; i++)
            {
                CharacterMainControl wraith = activeWraiths[i];
                if (wraith != null)
                {
                    DestroyWraithInstance_DeathWraith(wraith, "场景清理");
                }
            }
        }

        #endregion

        #region 亡魂系统 — 等级分类与属性

        /// <summary>
        /// 根据掉落物价值与玩家总财产比例分类亡魂等级
        /// </summary>
        private static WraithTier ClassifyWraithTier_DeathWraith(int droppedValue, long totalWealth)
        {
            if (totalWealth <= 0) return WraithTier.Weak;
            float ratio = (float)droppedValue / totalWealth;
            if (ratio >= 0.5f) return WraithTier.Strong;
            if (ratio >= 0.1f) return WraithTier.Balanced;
            return WraithTier.Weak;
        }

        /// <summary>
        /// 生成亡魂显示名
        /// </summary>
        private static string GetWraithDisplayName_DeathWraith(string playerName, WraithTier tier)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                playerName = "???";
            }

            string prefix;
            switch (tier)
            {
                case WraithTier.Strong:
                    prefix = L10n.T("强壮的", "Strong ");
                    break;
                case WraithTier.Balanced:
                    prefix = L10n.T("均衡的", "Balanced ");
                    break;
                default:
                    prefix = L10n.T("弱小的", "Weak ");
                    break;
            }

            string suffix = L10n.T("的亡魂", "'s Wraith");
            return prefix + playerName + suffix;
        }

        private string CreateWraithDisplayNameKey_DeathWraith(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return null;
            }

            try
            {
                string key = DEATH_WRAITH_NAME_KEY_PREFIX
                    + Guid.NewGuid().ToString("N").Substring(0, 8);
                if (LocalizationHelper.InjectLocalization(key, displayName))
                {
                    return key;
                }

                DevLog("[DeathWraith] [WARNING] 注入亡魂名字本地化失败: " + displayName);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] CreateWraithDisplayNameKey 异常: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 根据等级应用属性加成（参考 Utilities.cs ApplyBossStatMultiplier 模式）
        /// </summary>
        private static float GetWraithMoveabilityTarget_DeathWraith(WraithTier tier)
        {
            switch (tier)
            {
                case WraithTier.Strong:
                    return 1f;
                case WraithTier.Balanced:
                    return 0.9f;
                default:
                    return 0.8f;
            }
        }

        private void ApplyWraithTierStats_DeathWraith(CharacterMainControl wraith, WraithTier tier)
        {
            if (wraith == null) return;

            try
            {
                var item = wraith.CharacterItem;
                if (item == null) return;

                float speedMult = tier == WraithTier.Strong ? 1.9f :
                    (tier == WraithTier.Balanced ? 1.5f : 1.2f);
                float dmgMult = tier == WraithTier.Strong ? 1.5f :
                    (tier == WraithTier.Balanced ? 1.25f : 1f);
                float hpMult = tier == WraithTier.Strong ? 10f :
                    (tier == WraithTier.Balanced ? 6f : 3f);

                try
                {
                    Stat hpStat = item.GetStat("MaxHealth".GetHashCode());
                    if (hpStat != null)
                    {
                        float old = hpStat.BaseValue;
                        hpStat.BaseValue *= hpMult;
                        DevLog("[DeathWraith] MaxHealth: " + old + " -> " + hpStat.BaseValue);
                    }
                }
                catch { }

                // 移速加成：龙裔二阶段会改 MoveSpeed + Moveability。
                // 亡魂额外同步 WalkSpeed/RunSpeed，确保原版移动控制真实生效。
                try
                {
                    Stat moveStat = item.GetStat("MoveSpeed".GetHashCode());
                    if (moveStat != null)
                    {
                        float old = moveStat.BaseValue;
                        moveStat.BaseValue *= speedMult;
                        DevLog("[DeathWraith] MoveSpeed: " + old + " -> " + moveStat.BaseValue);
                    }
                }
                catch { }

                try
                {
                    Stat walkStat = item.GetStat("WalkSpeed".GetHashCode());
                    if (walkStat != null)
                    {
                        float old = walkStat.BaseValue;
                        walkStat.BaseValue *= speedMult;
                        DevLog("[DeathWraith] WalkSpeed: " + old + " -> " + walkStat.BaseValue);
                    }
                }
                catch { }

                try
                {
                    Stat runStat = item.GetStat("RunSpeed".GetHashCode());
                    if (runStat != null)
                    {
                        float old = runStat.BaseValue;
                        runStat.BaseValue *= speedMult;
                        DevLog("[DeathWraith] RunSpeed: " + old + " -> " + runStat.BaseValue);
                    }
                }
                catch { }

                try
                {
                    Stat moveabilityStat = item.GetStat("Moveability".GetHashCode());
                    if (moveabilityStat != null)
                    {
                        float old = moveabilityStat.BaseValue;
                        moveabilityStat.BaseValue = GetWraithMoveabilityTarget_DeathWraith(tier);
                        DevLog("[DeathWraith] Moveability: " + old + " -> " + moveabilityStat.BaseValue);
                    }
                }
                catch { }

                // 攻击加成
                try
                {
                    Stat gunDmg = item.GetStat("GunDamageMultiplier".GetHashCode());
                    if (gunDmg != null)
                    {
                        float old = gunDmg.BaseValue;
                        gunDmg.BaseValue *= dmgMult;
                        DevLog("[DeathWraith] GunDamageMultiplier: " + old + " -> " + gunDmg.BaseValue);
                    }
                }
                catch { }

                try
                {
                    Stat meleeDmg = item.GetStat("MeleeDamageMultiplier".GetHashCode());
                    if (meleeDmg != null)
                    {
                        float old = meleeDmg.BaseValue;
                        meleeDmg.BaseValue *= dmgMult;
                        DevLog("[DeathWraith] MeleeDamageMultiplier: " + old + " -> " + meleeDmg.BaseValue);
                    }
                }
                catch { }

                try
                {
                    DevLog("[DeathWraith] 当前角色移速快照: walk=" + wraith.CharacterWalkSpeed
                        + ", run=" + wraith.CharacterRunSpeed);
                }
                catch { }

                DevLog("[DeathWraith] 属性加成完成: tier=" + tier
                    + " hpMult=" + hpMult
                    + " speedMult=" + speedMult
                    + " dmgMult=" + dmgMult);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] ApplyWraithTierStats 异常: " + e.Message);
            }
        }

        private void RestoreWraithMaxHealthSnapshot_DeathWraith(CharacterMainControl wraith, float savedMaxHealth)
        {
            if (wraith == null || savedMaxHealth <= 0f)
            {
                return;
            }

            try
            {
                if (wraith.Health == null)
                {
                    return;
                }

                float currentMaxHealth = wraith.Health.MaxHealth;
                if (currentMaxHealth <= 0.01f)
                {
                    return;
                }

                Item item = wraith.CharacterItem;
                if (item == null)
                {
                    return;
                }

                Stat hpStat = item.GetStat("MaxHealth".GetHashCode());
                if (hpStat == null)
                {
                    DevLog("[DeathWraith] [WARNING] 无法回填最大生命：缺少 MaxHealth Stat");
                    return;
                }

                float scale = savedMaxHealth / currentMaxHealth;
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                {
                    DevLog("[DeathWraith] [WARNING] 无法回填最大生命：非法缩放值 " + scale);
                    return;
                }

                float oldBase = hpStat.BaseValue;
                hpStat.BaseValue *= scale;
                DevLog("[DeathWraith] 回填最大生命: current=" + currentMaxHealth
                    + " saved=" + savedMaxHealth
                    + " base=" + oldBase + " -> " + hpStat.BaseValue);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] RestoreWraithMaxHealthSnapshot 异常: " + e.Message);
            }
        }

        #endregion

        #region 亡魂系统 — 玩家名获取

        /// <summary>
        /// 获取玩家显示名（优先 Steam 人格名，回退默认名）
        /// </summary>
        private string GetWraithPlayerName_DeathWraith()
        {
            try
            {
                string steamName = TryGetSteamPersonaName();
                if (!string.IsNullOrEmpty(steamName))
                {
                    return steamName;
                }
            }
            catch { }

            return L10n.T("我", "Me");
        }

        private string GetActiveSceneName_DeathWraith()
        {
            try
            {
                Scene activeScene = SceneManager.GetActiveScene();
                return activeScene.name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetActiveSubSceneId_DeathWraith()
        {
            try
            {
                return MultiSceneCore.ActiveSubSceneID ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private uint GetCurrentRaidId_DeathWraith()
        {
            try
            {
                return RaidUtilities.CurrentRaid.ID;
            }
            catch
            {
                return 0U;
            }
        }

        private List<WraithInfo> LoadStoredDeathWraithInfos_DeathWraith()
        {
            try
            {
                List<WraithInfo> infos =
                    SavesSystem.Load<List<WraithInfo>>(DEATH_WRAITH_LIST_SAVE_KEY);
                return infos ?? new List<WraithInfo>();
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 读取亡魂记录列表失败: " + e.Message);
                return new List<WraithInfo>();
            }
        }

        private void SaveStoredDeathWraithInfos_DeathWraith(List<WraithInfo> infos)
        {
            try
            {
                SavesSystem.Save<List<WraithInfo>>(
                    DEATH_WRAITH_LIST_SAVE_KEY,
                    infos ?? new List<WraithInfo>());
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 保存亡魂记录列表失败: " + e.Message);
            }
        }

        private int GetStoredDeathWraithLimit_DeathWraith()
        {
            try
            {
                return Mathf.Max(1, Duckov.Rules.GameRulesManager.Current.SaveDeadbodyCount);
            }
            catch
            {
                return 4;
            }
        }

        private void AppendStoredDeathWraithInfo_DeathWraith(WraithInfo info)
        {
            if (info == null)
            {
                return;
            }

            List<WraithInfo> infos = LoadStoredDeathWraithInfos_DeathWraith();
            for (int i = infos.Count - 1; i >= 0; i--)
            {
                WraithInfo existing = infos[i];
                if (existing != null && existing.raidID == info.raidID)
                {
                    infos.RemoveAt(i);
                }
            }

            infos.Add(info);

            int limit = GetStoredDeathWraithLimit_DeathWraith();
            while (infos.Count > limit)
            {
                WraithInfo removed = infos[0];
                if (removed != null)
                {
                    DestroyActiveWraithByRaidId_DeathWraith(removed.raidID, "超出原版遗失物记录上限");
                }
                infos.RemoveAt(0);
            }

            SaveStoredDeathWraithInfos_DeathWraith(infos);
        }

        private WraithInfo FindStoredDeathWraithInfoByRaidId_DeathWraith(uint raidID)
        {
            List<WraithInfo> infos = LoadStoredDeathWraithInfos_DeathWraith();
            for (int i = 0; i < infos.Count; i++)
            {
                WraithInfo info = infos[i];
                if (info != null && info.raidID == raidID && info.valid)
                {
                    return info;
                }
            }

            return null;
        }

        private bool RemoveStoredDeathWraithInfoByRaidId_DeathWraith(uint raidID, string reason)
        {
            bool removed = false;
            List<WraithInfo> infos = LoadStoredDeathWraithInfos_DeathWraith();
            for (int i = infos.Count - 1; i >= 0; i--)
            {
                WraithInfo info = infos[i];
                if (info != null && info.raidID == raidID)
                {
                    infos.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
            {
                SaveStoredDeathWraithInfos_DeathWraith(infos);
                DevLog("[DeathWraith] 已移除亡魂记录: raidID=" + raidID
                    + (string.IsNullOrEmpty(reason) ? string.Empty : (", reason=" + reason)));
            }

            return removed;
        }

        private void InvalidateStoredDeathWraithRecords_DeathWraith(string reason)
        {
            SaveStoredDeathWraithInfos_DeathWraith(new List<WraithInfo>());
            DevLog("[DeathWraith] 已清空全部亡魂记录: " + reason);
        }

        private bool IsDeathWraithCharacter_DeathWraith(CharacterMainControl character)
        {
            if (character == null)
            {
                return false;
            }

            try
            {
                if (activeWraithRaidIdByCharacter.ContainsKey(character))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                GameObject go = character.gameObject;
                if (go != null &&
                    !string.IsNullOrEmpty(go.name) &&
                    go.name.StartsWith("BossRush_DeathWraith_", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                CharacterRandomPreset preset = character.characterPreset;
                if (preset != null)
                {
                    string presetName = preset.name;
                    if (!string.IsNullOrEmpty(presetName) &&
                        presetName.StartsWith("BossRush_DeathWraithPreset", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    string nameKey = preset.nameKey;
                    if (!string.IsNullOrEmpty(nameKey) &&
                        nameKey.StartsWith(DEATH_WRAITH_NAME_KEY_PREFIX, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private void RegisterActiveWraith_DeathWraith(uint raidID, CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return;
            }

            activeWraithsByRaidId[raidID] = wraith;
            activeWraithRaidIdByCharacter[wraith] = raidID;
        }

        private void ForgetActiveWraith_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return;
            }

            uint raidID;
            if (activeWraithRaidIdByCharacter.TryGetValue(wraith, out raidID))
            {
                activeWraithRaidIdByCharacter.Remove(wraith);

                CharacterMainControl existing;
                if (activeWraithsByRaidId.TryGetValue(raidID, out existing) && existing == wraith)
                {
                    activeWraithsByRaidId.Remove(raidID);
                }
            }
        }

        private void DestroyActiveWraithByRaidId_DeathWraith(uint raidID, string reason)
        {
            CharacterMainControl wraith;
            if (!activeWraithsByRaidId.TryGetValue(raidID, out wraith) || wraith == null)
            {
                return;
            }

            ForgetActiveWraith_DeathWraith(wraith);
            DestroyWraithInstance_DeathWraith(wraith, reason);
        }

        private void EnsureWraithPresetCache_DeathWraith()
        {
            if (deathWraithPresetCacheByNameKey != null &&
                deathWraithPresetCacheByRuntimeName != null &&
                (deathWraithPresetCacheByNameKey.Count > 0 || deathWraithPresetCacheByRuntimeName.Count > 0))
            {
                return;
            }

            try
            {
                var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                if (allPresets == null || allPresets.Length == 0)
                {
                    DevLog("[DeathWraith] 未找到任何 CharacterRandomPreset");
                    return;
                }

                deathWraithPresetCacheByNameKey =
                    new Dictionary<string, CharacterRandomPreset>(StringComparer.Ordinal);
                deathWraithPresetCacheByRuntimeName =
                    new Dictionary<string, CharacterRandomPreset>(StringComparer.Ordinal);
                foreach (CharacterRandomPreset preset in allPresets)
                {
                    if (preset == null || IsRuntimeCharacterPresetClone(preset))
                    {
                        continue;
                    }

                    string nameKey = preset.nameKey;
                    if (!string.IsNullOrEmpty(nameKey) &&
                        !deathWraithPresetCacheByNameKey.ContainsKey(nameKey))
                    {
                        deathWraithPresetCacheByNameKey[nameKey] = preset;
                    }

                    string runtimeName = preset.name;
                    if (!string.IsNullOrEmpty(runtimeName) &&
                        !deathWraithPresetCacheByRuntimeName.ContainsKey(runtimeName))
                    {
                        deathWraithPresetCacheByRuntimeName[runtimeName] = preset;
                    }
                }

                DevLog("[DeathWraith] 已初始化亡魂预设缓存: nameKey="
                    + deathWraithPresetCacheByNameKey.Count
                    + ", runtimeName=" + deathWraithPresetCacheByRuntimeName.Count);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 初始化角色预设缓存异常: " + e.Message);
            }
        }

        private bool TryGetWraithPresetByNameKey_DeathWraith(
            string nameKey,
            out CharacterRandomPreset preset)
        {
            preset = null;
            return !string.IsNullOrEmpty(nameKey) &&
                deathWraithPresetCacheByNameKey != null &&
                deathWraithPresetCacheByNameKey.TryGetValue(nameKey, out preset) &&
                preset != null;
        }

        private bool TryGetWraithPresetByRuntimeName_DeathWraith(
            string runtimeName,
            out CharacterRandomPreset preset)
        {
            preset = null;
            if (deathWraithPresetCacheByRuntimeName == null)
            {
                return false;
            }

            string normalizedRuntimeName = NormalizeWraithRuntimePresetName_DeathWraith(runtimeName);
            return !string.IsNullOrEmpty(normalizedRuntimeName) &&
                deathWraithPresetCacheByRuntimeName.TryGetValue(normalizedRuntimeName, out preset) &&
                preset != null;
        }


        private string NormalizeWraithRuntimePresetName_DeathWraith(string runtimeName)
        {
            if (string.IsNullOrEmpty(runtimeName))
            {
                return string.Empty;
            }

            string normalized = runtimeName.Trim();
            int cloneIndex = normalized.IndexOf("(Clone)", StringComparison.Ordinal);
            if (cloneIndex >= 0)
            {
                normalized = normalized.Substring(0, cloneIndex).TrimEnd();
            }

            return normalized;
        }


        private void DestroyOwnedWraithPresetClone_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                CharacterRandomPreset preset = wraith.characterPreset;
                if (preset != null && IsRuntimeCharacterPresetClone(preset))
                {
                    UnityEngine.Object.Destroy(preset);
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 销毁亡魂预设副本异常: " + e.Message);
            }
        }

        private void DestroyWraithInstance_DeathWraith(CharacterMainControl wraith, string reason)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                wraith.dropBoxOnDead = false;
            }
            catch { }

            // 销毁克隆的 characterPreset，防止 ScriptableObject 泄漏
            DestroyOwnedWraithPresetClone_DeathWraith(wraith);

            try
            {
                if (wraith.gameObject != null)
                {
                    UnityEngine.Object.Destroy(wraith.gameObject);
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(reason))
            {
                DevLog("[DeathWraith] 销毁亡魂实例: " + reason);
            }
        }

        #endregion
    }
}
