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
//   - <10%：弱小的XX的亡魂（无额外加成）
//
//   亡魂死后不掉落任何物品。再次死亡覆盖更新亡魂数据。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Economy;
using Duckov.Scenes;
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
        public string sceneName;
        public string subSceneID;
        public float posX, posY, posZ;
        public string playerPresetName;
        public string playerPresetRuntimeName;
        public string playerName;
        public int droppedItemsValue;
        public long playerTotalWealth;
        public float playerMaxHealth;
        public ItemTreeData itemTreeData;
        public bool killed;
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

        private const string DEATH_WRAITH_SAVE_KEY = "BossRush_DeathWraith";
        private const string DEATH_WRAITH_NAME_KEY_PREFIX = "BossRush_DeathWraith_Name_";
        private const float DEATH_WRAITH_PENDING_MAX_AGE_SECONDS = 0.5f;
        private const int DEATH_WRAITH_PENDING_MAX_FRAME_DELTA = 1;
        private CharacterMainControl currentWraith;
        private bool deathWraithSpawnInProgress;
        private int deathWraithSceneToken;
        private WraithInfo pendingDeathWraithInfo;
        private int pendingDeathWraithPrimedFrame = -1;
        private float pendingDeathWraithPrimedRealtime = -1f;
        private Dictionary<string, CharacterRandomPreset> deathWraithPresetCacheByNameKey;
        private Dictionary<string, CharacterRandomPreset> deathWraithPresetCacheByRuntimeName;

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

            if (!IsDeathWraithSupportedContext_DeathWraith())
            {
                ClearPendingDeathWraithInfo_DeathWraith();
                return;
            }

            // 若当前场景有存活亡魂 → 销毁
            if (currentWraith != null)
            {
                DestroyWraithInstance_DeathWraith(currentWraith, "玩家再次死亡");
                currentWraith = null;
                Health.OnDead -= OnWraithDied_DeathWraith;
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

            SavesSystem.Save<WraithInfo>(DEATH_WRAITH_SAVE_KEY, info);
            DevLog("[DeathWraith] 已记录亡魂数据"
                + (string.IsNullOrEmpty(source) ? "" : ("[" + source + "]"))
                + ": scene=" + info.sceneName
                + ", subScene=" + info.subSceneID
                + ", preset=" + info.playerPresetName
                + " runtimePreset=" + info.playerPresetRuntimeName
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
                }
                catch { }

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

                return new WraithInfo
                {
                    valid = true,
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

        /// <summary>
        /// 延迟等待关卡初始化完成后生成亡魂
        /// </summary>
        internal IEnumerator DelayedSpawnDeathWraith_DeathWraith(Scene expectedScene)
        {
            int sceneToken = deathWraithSceneToken;
            if (deathWraithSpawnInProgress)
            {
                DevLog("[DeathWraith] 跳过重复的生成请求");
                yield break;
            }

            deathWraithSpawnInProgress = true;
            bool handedOffToAsync = false;
            float elapsed = 0f;
            try
            {
                while (!IsDeathWraithSceneReady_DeathWraith(expectedScene) && elapsed < 30f)
                {
                    if (sceneToken != deathWraithSceneToken)
                    {
                        yield break;
                    }

                    yield return new WaitForSeconds(0.5f);
                    elapsed += 0.5f;
                }

                if (sceneToken != deathWraithSceneToken)
                {
                    yield break;
                }

                if (IsDeathWraithSceneReady_DeathWraith(expectedScene))
                {
                    handedOffToAsync = true;
                    TrySpawnDeathWraith_DeathWraith(sceneToken);
                }
            }
            finally
            {
                if (!handedOffToAsync && sceneToken == deathWraithSceneToken)
                {
                    deathWraithSpawnInProgress = false;
                }
            }
        }

        /// <summary>
        /// 尝试在死亡位置生成亡魂（async void）
        /// </summary>
        private async void TrySpawnDeathWraith_DeathWraith(int sceneToken)
        {
            CharacterMainControl spawnedWraith = null;
            try
            {
                if (sceneToken != deathWraithSceneToken) return;

                // 加载亡魂数据
                WraithInfo info = null;
                try
                {
                    info = SavesSystem.Load<WraithInfo>(DEATH_WRAITH_SAVE_KEY);
                }
                catch { }

                if (info == null || !info.valid || info.killed) return;

                // 仅在对应场景/子场景生成
                if (!IsDeathWraithSupportedContext_DeathWraith()) return;
                if (!IsDeathWraithSceneMatch_DeathWraith(info)) return;

                EnsureWraithPresetCache_DeathWraith();

                // 防重复生成
                if (currentWraith != null) return;

                DevLog("[DeathWraith] 开始生成亡魂...");

                // 查找角色预设
                CharacterRandomPreset targetPreset = FindWraithPreset_DeathWraith(info);
                if (targetPreset == null)
                {
                    DevLog("[DeathWraith] 未找到可用预设，放弃生成");
                    return;
                }

                // 生成角色
                Vector3 spawnPos = new Vector3(info.posX, info.posY, info.posZ);
                int relatedScene = SceneManager.GetActiveScene().buildIndex;
                CharacterMainControl wraith = null;

                try
                {
                    wraith = await targetPreset.CreateCharacterAsync(
                        spawnPos, Vector3.forward, relatedScene, null, false);
                    spawnedWraith = wraith;
                }
                catch (Exception e)
                {
                    DevLog("[DeathWraith] CreateCharacterAsync 异常: " + e.Message);
                    return;
                }

                if (sceneToken != deathWraithSceneToken)
                {
                    DestroyWraithInstance_DeathWraith(spawnedWraith, "场景已切换（创建后）");
                    spawnedWraith = null;
                    return;
                }

                if (wraith == null)
                {
                    DevLog("[DeathWraith] 角色生成失败");
                    return;
                }

                // 让出一帧（参考 EnemySpawnCore.cs:238）
                await UniTask.Yield();

                if (sceneToken != deathWraithSceneToken)
                {
                    DestroyWraithInstance_DeathWraith(spawnedWraith, "场景已切换（等待初始化后）");
                    spawnedWraith = null;
                    return;
                }

                // 装备复制
                if (info.itemTreeData != null)
                {
                    await RestoreWraithEquipment_DeathWraith(wraith, info.itemTreeData);
                }

                if (sceneToken != deathWraithSceneToken)
                {
                    DestroyWraithInstance_DeathWraith(spawnedWraith, "场景已切换（装备恢复后）");
                    spawnedWraith = null;
                    return;
                }

                NormalizeDamageMultiplier(wraith);
                RestoreWraithMaxHealthSnapshot_DeathWraith(wraith, info.playerMaxHealth);
                ApplyBossStatMultiplier(wraith);

                // 禁止掉落
                wraith.dropBoxOnDead = false;

                // 设置敌对阵营
                wraith.SetTeam(Teams.scav);

                // 设置显示名（克隆 preset 模式，参考 ModeFRespawn.cs:637-641）
                WraithTier tier = ClassifyWraithTier_DeathWraith(info.droppedItemsValue, info.playerTotalWealth);
                string displayName = GetWraithDisplayName_DeathWraith(info.playerName, tier);
                string displayNameKey = CreateWraithDisplayNameKey_DeathWraith(displayName);

                if (wraith.characterPreset != null)
                {
                    try
                    {
                        CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(wraith.characterPreset);
                        wraith.characterPreset = customPreset; // 立即替换，确保后续清理销毁的是克隆体
                        customPreset.aiCombatFactor = 1f;
                        customPreset.showName = true;
                        customPreset.showHealthBar = true;
                        customPreset.dropBoxOnDead = false;
                        customPreset.nameKey = !string.IsNullOrEmpty(displayNameKey)
                            ? displayNameKey
                            : displayName;
                    }
                    catch (Exception e)
                    {
                        DevLog("[DeathWraith] 克隆预设异常: " + e.Message);
                    }
                }

                // 同步 Health 组件的血条显示（参考 DragonKingBoss.cs:163-167）
                try
                {
                    if (wraith.Health != null)
                    {
                        wraith.Health.showHealthBar = true;
                    }
                }
                catch { }

                // 应用等级属性加成
                ApplyWraithTierStats_DeathWraith(wraith, tier);

                // 同步血量
                try
                {
                    if (wraith.Health != null)
                    {
                        wraith.Health.SetHealth(wraith.Health.MaxHealth);
                    }
                }
                catch { }

                // 注册死亡回调
                currentWraith = wraith;
                Health.OnDead -= OnWraithDied_DeathWraith;
                Health.OnDead += OnWraithDied_DeathWraith;

                // 激活
                wraith.gameObject.name = "BossRush_DeathWraith";
                wraith.gameObject.SetActive(true);
                spawnedWraith = null;

                DevLog("[DeathWraith] 亡魂生成成功: " + displayName + " tier=" + tier);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] TrySpawnDeathWraith 异常: " + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                if (spawnedWraith != null)
                {
                    DestroyWraithInstance_DeathWraith(spawnedWraith, "生成流程异常中断");
                    if (currentWraith == spawnedWraith)
                    {
                        currentWraith = null;
                        Health.OnDead -= OnWraithDied_DeathWraith;
                    }
                }

                if (sceneToken == deathWraithSceneToken)
                {
                    deathWraithSpawnInProgress = false;
                }
            }
        }

        /// <summary>
        /// 查找亡魂使用的角色预设
        /// </summary>
        private CharacterRandomPreset FindWraithPreset_DeathWraith(WraithInfo info)
        {
            if (info == null)
            {
                return null;
            }

            if ((deathWraithPresetCacheByNameKey == null || deathWraithPresetCacheByNameKey.Count == 0) &&
                (deathWraithPresetCacheByRuntimeName == null || deathWraithPresetCacheByRuntimeName.Count == 0))
            {
                DevLog("[DeathWraith] 亡魂预设缓存为空");
                return null;
            }

            CharacterRandomPreset preset = null;

            if (TryGetWraithPresetByNameKey_DeathWraith(info.playerPresetName, out preset))
            {
                DevLog("[DeathWraith] 使用玩家预设 nameKey: " + info.playerPresetName);
                return preset;
            }

            if (TryGetWraithPresetByRuntimeName_DeathWraith(info.playerPresetRuntimeName, out preset))
            {
                DevLog("[DeathWraith] 使用玩家预设 runtimeName: " + info.playerPresetRuntimeName);
                return preset;
            }

            // 兼容旧存档：旧字段可能存的是 runtime name
            if (TryGetWraithPresetByRuntimeName_DeathWraith(info.playerPresetName, out preset))
            {
                DevLog("[DeathWraith] 使用旧存档兼容匹配 runtimeName: " + info.playerPresetName);
                return preset;
            }

            if (string.IsNullOrEmpty(info.playerPresetName) &&
                string.IsNullOrEmpty(info.playerPresetRuntimeName))
            {
                CharacterRandomPreset currentPlayerPreset = GetCurrentPlayerPresetForWraithFallback_DeathWraith();
                if (currentPlayerPreset != null)
                {
                    DevLog("[DeathWraith] 存档缺少预设标识，使用当前玩家预设作为安全回退");
                    return currentPlayerPreset;
                }
            }

            DevLog("[DeathWraith] 未匹配到玩家预设: nameKey=" + info.playerPresetName
                + ", runtimeName=" + info.playerPresetRuntimeName);
            return null;
        }

        /// <summary>
        /// 将保存的玩家装备恢复到亡魂身上
        /// </summary>
        private async UniTask RestoreWraithEquipment_DeathWraith(CharacterMainControl wraith, ItemTreeData savedItemTree)
        {
            Item restoredItem = null;
            try
            {
                // 从 ItemTreeData 恢复物品树
                restoredItem = await ItemTreeData.InstantiateAsync(savedItemTree);
                if (restoredItem == null)
                {
                    DevLog("[DeathWraith] ItemTreeData.InstantiateAsync 返回 null");
                    return;
                }

                RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                    restoredItem,
                    "DeathWraith.RestoredTree");

                Item wraithItem = wraith.CharacterItem;
                if (wraithItem == null)
                {
                    DevLog("[DeathWraith] wraith.CharacterItem 为 null");
                    return;
                }

                // 清空亡魂默认背包（参考 ModeDEquipment ClearEnemyInventory）
                try
                {
                    Inventory wraithInv = wraithItem.Inventory;
                    if (wraithInv != null && wraithInv.Content != null)
                    {
                        var content = wraithInv.Content;
                        for (int i = content.Count - 1; i >= 0; --i)
                        {
                            var existing = content[i];
                            if (existing != null)
                            {
                                existing.Detach();
                                UnityEngine.Object.Destroy(existing.gameObject);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[DeathWraith] 清空默认背包异常: " + e.Message);
                }

                // 清空亡魂默认装备槽
                try
                {
                    if (wraithItem.Slots != null)
                    {
                        foreach (Slot slot in wraithItem.Slots)
                        {
                            if (slot != null && slot.Content != null)
                            {
                                try
                                {
                                    Item unplugged = slot.Unplug();
                                    if (unplugged != null)
                                    {
                                        UnityEngine.Object.Destroy(unplugged.gameObject);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[DeathWraith] 清空默认装备异常: " + e.Message);
                }

                // 将恢复的物品逐槽位装备到亡魂
                try
                {
                    if (restoredItem.Slots != null)
                    {
                        foreach (Slot restoredSlot in restoredItem.Slots)
                        {
                            if (restoredSlot == null || restoredSlot.Content == null) continue;
                            try
                            {
                                Item content = restoredSlot.Unplug();
                                if (content != null)
                                {
                                    bool plugged = wraithItem.TryPlug(content, true, null, 0);
                                    if (plugged)
                                    {
                                        RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                                            content,
                                            "DeathWraith.EquippedSlot");
                                    }
                                    else
                                    {
                                        // 装备失败则放入背包
                                        if (TryAddItemToInventory_DeathWraith(wraithItem.Inventory, content))
                                        {
                                            RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                                                content,
                                                "DeathWraith.EquippedFallbackInventory");
                                        }
                                        else
                                        {
                                            DevLog("[DeathWraith] 装备放入背包失败: " + content.DisplayName);
                                            DestroyDetachedItem_DeathWraith(content, "装备回退到背包失败");
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                DevLog("[DeathWraith] 单个装备复制异常: " + e.Message);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[DeathWraith] 装备复制异常: " + e.Message);
                }

                // 转移背包物品
                try
                {
                    if (restoredItem.Inventory != null && wraithItem.Inventory != null)
                    {
                        // 收集再转移，避免迭代中修改集合
                        var itemsToMove = new List<Item>();
                        foreach (Item invItem in restoredItem.Inventory)
                        {
                            if (invItem != null) itemsToMove.Add(invItem);
                        }
                        foreach (Item invItem in itemsToMove)
                        {
                            try
                            {
                                if (TryAddItemToInventory_DeathWraith(wraithItem.Inventory, invItem))
                                {
                                    RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                                        invItem,
                                        "DeathWraith.InventoryTransfer");
                                }
                                else
                                {
                                    DevLog("[DeathWraith] 背包物品转移失败: " + invItem.DisplayName);
                                    DestroyDetachedItem_DeathWraith(invItem, "背包物品转移失败");
                                }
                            }
                            catch (Exception e)
                            {
                                DevLog("[DeathWraith] 单个背包物品转移异常: " + e.Message);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[DeathWraith] 背包转移异常: " + e.Message);
                }

                DevLog("[DeathWraith] 装备复制完成");
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] RestoreWraithEquipment 异常: " + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                try
                {
                    if (restoredItem != null && restoredItem.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(restoredItem.gameObject);
                    }
                }
                catch { }
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
                if (currentWraith == null) return;

                // 校验是否为亡魂
                var character = deadHealth.TryGetCharacter();
                if (character == null || character != currentWraith) return;

                // 标记已击杀
                try
                {
                    WraithInfo info = SavesSystem.Load<WraithInfo>(DEATH_WRAITH_SAVE_KEY);
                    if (info != null)
                    {
                        info.killed = true;
                        SavesSystem.Save<WraithInfo>(DEATH_WRAITH_SAVE_KEY, info);
                    }
                }
                catch { }

                // 销毁克隆的 characterPreset，防止 ScriptableObject 泄漏
                DestroyOwnedWraithPresetClone_DeathWraith(currentWraith);

                currentWraith = null;
                Health.OnDead -= OnWraithDied_DeathWraith;
                DevLog("[DeathWraith] 亡魂已被击杀");
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
            deathWraithSceneToken++;
            deathWraithSpawnInProgress = false;
            ClearPendingDeathWraithInfo_DeathWraith();

            if (currentWraith != null)
            {
                DestroyWraithInstance_DeathWraith(currentWraith, "场景清理");
                currentWraith = null;
            }

            Health.OnDead -= OnWraithDied_DeathWraith;
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
        private void ApplyWraithTierStats_DeathWraith(CharacterMainControl wraith, WraithTier tier)
        {
            if (tier == WraithTier.Weak) return;
            if (wraith == null) return;

            try
            {
                var item = wraith.CharacterItem;
                if (item == null) return;

                float speedMult = tier == WraithTier.Strong ? 1.9f : 1.5f;
                float dmgMult = tier == WraithTier.Strong ? 1.5f : 1.25f;

                // 移速加成（使用 MoveSpeed，与仓库其他系统一致）
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

                DevLog("[DeathWraith] 属性加成完成: tier=" + tier + " speedMult=" + speedMult + " dmgMult=" + dmgMult);
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

        private bool IsDeathWraithSceneMatch_DeathWraith(WraithInfo info)
        {
            if (info == null)
            {
                return false;
            }

            string currentSceneName = GetActiveSceneName_DeathWraith();
            if (!string.IsNullOrEmpty(info.sceneName) &&
                !string.Equals(currentSceneName, info.sceneName, StringComparison.Ordinal))
            {
                return false;
            }

            string currentSubSceneId = GetActiveSubSceneId_DeathWraith();
            if (!string.IsNullOrEmpty(info.subSceneID) &&
                !string.Equals(currentSubSceneId, info.subSceneID, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
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

        private CharacterRandomPreset GetCurrentPlayerPresetForWraithFallback_DeathWraith()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || main.characterPreset == null)
                {
                    return null;
                }

                CharacterRandomPreset preset;
                if (TryGetWraithPresetByNameKey_DeathWraith(main.characterPreset.nameKey, out preset))
                {
                    return preset;
                }

                if (TryGetWraithPresetByRuntimeName_DeathWraith(main.characterPreset.name, out preset))
                {
                    return preset;
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 获取当前玩家预设回退失败: " + e.Message);
            }

            return null;
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

        private bool IsDeathWraithSceneReady_DeathWraith(Scene expectedScene)
        {
            try
            {
                if (!expectedScene.IsValid() || !expectedScene.isLoaded)
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
                if (SceneLoader.IsSceneLoading)
                {
                    return false;
                }
            }
            catch { }

            try
            {
                if (CharacterMainControl.Main == null)
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
                if (GameCamera.Instance == null)
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
                return LevelManager.LevelInited;
            }
            catch
            {
                return false;
            }
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
