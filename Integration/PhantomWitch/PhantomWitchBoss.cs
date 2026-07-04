// ============================================================================
// PhantomWitchBoss.cs - 幽灵女巫Boss主控制器
// ============================================================================
// 模块说明：
//   管理幽灵女巫Boss的生成、属性设置和生命周期
//   作为ModBehaviour的partial class实现
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>
    /// 幽灵女巫Boss主控制器（partial class）
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 幽灵女巫Boss实例引用 ==========

        /// <summary>
        /// 所有活跃的幽灵女巫Boss实例及其能力控制器
        /// </summary>
        private Dictionary<CharacterMainControl, PhantomWitchAbilityController> phantomWitchInstances
            = new Dictionary<CharacterMainControl, PhantomWitchAbilityController>();

        /// <summary>
        /// 幽灵女巫是否已注册到预设列表
        /// </summary>
        #pragma warning disable CS0414
        private static bool phantomWitchRegistered = false;
        #pragma warning restore CS0414

        // ========== 性能优化：预设缓存 ==========

        /// <summary>
        /// 缓存的幽灵女巫基础预设
        /// </summary>
        private static CharacterRandomPreset cachedPhantomWitchBasePreset = null;

        /// <summary>
        /// 是否已搜索过幽灵女巫基础预设
        /// </summary>
        private static bool phantomWitchBasePresetSearched = false;

        /// <summary>
        /// 清理幽灵女巫相关的所有静态缓存（场景切换时调用）
        /// </summary>
        public static void ClearPhantomWitchStaticCache()
        {
            cachedPhantomWitchBasePreset = null;
            phantomWitchBasePresetSearched = false;
            phantomWitchRegistered = false;
            PhantomWitchAssetManager.ForceCleanup();
            PhantomWitchCurseRealmVisual.ClearCache();
            PhantomWitchAbilityController.ClearStaticCache();
        }

        /// <summary>
        /// 释放Boss实例资源（Boss销毁时调用）
        /// </summary>
        public static void ReleasePhantomWitchInstance()
        {
            PhantomWitchAssetManager.ClearCache();

            if (!PhantomWitchAssetManager.HasActiveReferences)
            {
                PhantomWitchAbilityController.ClearStaticCache();
            }
        }

        private void CleanupPhantomWitchTrackedStateOnArenaExit()
        {
            HashSet<CharacterMainControl> trackedCharacters = new HashSet<CharacterMainControl>();
            foreach (var kv in phantomWitchInstances)
            {
                if (kv.Key != null) trackedCharacters.Add(kv.Key);
            }
            foreach (var kv in bossSpawnTimes)
            {
                if (kv.Key != null) trackedCharacters.Add(kv.Key);
            }

            HashSet<CharacterMainControl> destroyed = new HashSet<CharacterMainControl>();
            foreach (CharacterMainControl character in trackedCharacters)
            {
                bool releasedByController = false;

                PhantomWitchAbilityController abilities;
                if (phantomWitchInstances.TryGetValue(character, out abilities) && abilities != null)
                {
                    try
                    {
                        abilities.OnBossDeath();
                        releasedByController = true;
                    }
                    catch (Exception e)
                    {
                        DevLog("[PhantomWitch] [WARNING] 离开竞技场时清理能力控制器失败: " + e.Message);
                    }
                }

                if (currentBoss == character)
                {
                    currentBoss = null;
                }

                CleanupTrackedPhantomWitchCharacter(character, destroyed, releasedByController);
            }

            phantomWitchInstances.Clear();
        }

        private void CleanupTrackedPhantomWitchCharacter(
            CharacterMainControl character,
            HashSet<CharacterMainControl> cleanedCharacters,
            bool assetReferenceAlreadyReleased)
        {
            BossCleanupHelpers.CleanupTrackedBossCharacter(
                character,
                cleanedCharacters,
                PhantomWitchConfig.BossNameKey,
                "PhantomWitch_Preset",
                "[PhantomWitch]",
                ClearBossRandomLootTracking,
                FinalizeBossRushLootboxPathTracking,
                assetReferenceAlreadyReleased ? null : (Action)ReleasePhantomWitchInstance);
        }

        // ========== 生成方法 ==========

        /// <summary>
        /// 生成幽灵女巫Boss
        /// </summary>
        public async UniTask<CharacterMainControl> SpawnPhantomWitch(
            Vector3 position,
            bool notifyBossRushOnFailure = true,
            bool deferActivationUntilNextFrame = false)
        {
            CharacterMainControl character = null;
            PhantomWitchAbilityController abilities = null;
            bool assetReferenceAdded = false;

            try
            {
                DevLog($"[PhantomWitch] 开始生成幽灵女巫Boss at {position}");

                // 查找基础敌人预设
                CharacterRandomPreset basePreset = FindPhantomWitchBasePreset();

                if (basePreset == null)
                {
                    DevLog("[PhantomWitch] [ERROR] 未找到基础敌人预设");
                    if (notifyBossRushOnFailure)
                    {
                        NotifyPhantomWitchSpawnFailed();
                    }
                    return null;
                }

                DevLog($"[PhantomWitch] 使用预设: {basePreset.name}");

                // 生成角色
                Vector3 dir = Vector3.forward;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                character = await basePreset.CreateCharacterAsync(position, dir, relatedScene, null, false);

                if (character == null)
                {
                    DevLog("[PhantomWitch] [ERROR] 生成角色失败");
                    if (notifyBossRushOnFailure)
                    {
                        NotifyPhantomWitchSpawnFailed();
                    }
                    return null;
                }

                // 自定义 Boss 的后续配装/能力初始化都在这里完成，先让出一帧把生成尖峰摊平。
                await UniTask.Yield();

                character.gameObject.name = "BossRush_PhantomWitch";

                // 设置为当前Boss
                currentBoss = character;

                // 多Boss模式支持
                if (bossesPerWave > 1 && currentWaveBosses != null && !currentWaveBosses.Contains(character))
                {
                    currentWaveBosses.Add(character);
                }

                // 创建独立预设副本
                if (character.characterPreset != null)
                {
                    CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(character.characterPreset);
                    customPreset.name = PhantomWitchConfig.BossNameKey;
                    customPreset.showName = true;
                    customPreset.showHealthBar = true;
                    customPreset.nameKey = PhantomWitchConfig.BossNameKey;

                    // 对齐龙裔/龙皇：把 characterIconType 改成 boss，
                    // 否则名字左侧没有 Boss 标识，看起来和普通幽灵一样。
                    if (ReflectionCache.CharacterRandomPreset_CharacterIconType != null)
                    {
                        try
                        {
                            ReflectionCache.CharacterRandomPreset_CharacterIconType.SetValue(
                                customPreset,
                                CharacterIconTypes.boss);
                        }
                        catch (Exception iconEx)
                        {
                            DevLog("[PhantomWitch] [WARNING] 设置 characterIconType=boss 失败: " + iconEx.Message);
                        }
                    }

                    character.characterPreset = customPreset;

                    DevLog($"[PhantomWitch] 已创建独立预设副本, nameKey={customPreset.nameKey}");
                }

                // 设置Health组件
                if (character.Health != null)
                {
                    character.Health.showHealthBar = true;
                }

                // BossRush 里把幽灵女巫整体放大 1 倍（= 2 倍视觉尺寸），
                // 凸显 Boss 体型。放到属性/AI 初始化之前，避免碰撞器半径被缓存。
                try
                {
                    character.transform.localScale = Vector3.one * PhantomWitchConfig.BossModelScale;
                }
                catch (Exception scaleEx)
                {
                    DevLog("[PhantomWitch] [WARNING] 放大 Boss 模型失败: " + scaleEx.Message);
                }

                // 设置Boss属性
                SetupPhantomWitchAttributes(character);

                // 应用全局Boss数值倍率
                ApplyBossStatMultiplier(character);

                // 在第一次可能分配共享Buff/FX前登记引用，异常时由catch统一释放。
                PhantomWitchAssetManager.AddReference();
                assetReferenceAdded = true;

                // 装备幽灵女巫近战武器（优先正式镰刀，占位回退断界戟）
                EquipPhantomWitchWeapon(character);

                // 武器实例化和配置包含多次资源访问，低端机上分帧能明显减少出场顿挫。
                await UniTask.Yield();

                // 添加能力控制器（放在独立 GO 上，避免 character.Hide/Show
                // 内部 SetActive(false) 静默杀死所有协程导致技能卡死）
                GameObject controllerGO = new GameObject("PhantomWitch_AbilityController");
                abilities = controllerGO.AddComponent<PhantomWitchAbilityController>();
                abilities.Initialize(character, position);
                phantomWitchInstances[character] = abilities;

                if (deferActivationUntilNextFrame)
                {
                    await UniTask.Yield();
                }

                // 激活角色
                character.gameObject.SetActive(true);

                // 请求显示血条
                if (character.Health != null)
                {
                    character.Health.RequestHealthBar();
                }

                // 设置AI仇恨
                SetupAIAggro(character);

                // 位置兜底
                StartCoroutine(DelayedBossPositionValidation(character, 0.5f));
                RegisterEnemyRecoveryAnchor(character, position);

                // 订阅死亡事件
                if (character.Health != null)
                {
                    CharacterMainControl capturedChar = character;
                    character.Health.OnDeadEvent.AddListener((dmgInfo) => OnPhantomWitchDeath(capturedChar, dmgInfo));
                }

                // 出场特效（纯代码生成，CreateEffect 内部已自动销毁）
                PhantomWitchAssetManager.CreateEffect(position, PhantomWitchConfig.EffectDefaultDuration);

                // 记录Boss生成信息
                try
                {
                    int originalLootCount = 3;
                    if (character.CharacterItem != null && character.CharacterItem.Inventory != null)
                    {
                        originalLootCount = 3;
                    }

                    RegisterBossRandomLootTracking(character, originalLootCount, 0f);
                }
                catch (Exception recordEx)
                {
                    DevLog($"[PhantomWitch] [WARNING] 记录Boss生成信息失败: {recordEx.Message}");
                }

                DevLog("[PhantomWitch] 幽灵女巫Boss生成完成");
                ShowMessage(L10n.T(PhantomWitchConfig.SpawnMessageCN, PhantomWitchConfig.SpawnMessageEN));

                return character;
            }
            catch (Exception e)
            {
                DevLog($"[PhantomWitch] [ERROR] 生成Boss失败: {e.Message}\n{e.StackTrace}");
                if (assetReferenceAdded)
                {
                    if (abilities != null)
                    {
                        abilities.ReleaseAssetReferenceIfNeeded();
                    }
                    else
                    {
                        ReleasePhantomWitchInstance();
                    }
                }
                CleanupFailedPhantomWitchSpawn(character);
                if (notifyBossRushOnFailure)
                {
                    NotifyPhantomWitchSpawnFailed();
                }
                return null;
            }
        }

        private void CleanupFailedPhantomWitchSpawn(CharacterMainControl character)
        {
            if (character == null)
            {
                return;
            }

            phantomWitchInstances.Remove(character);
            ClearBossRandomLootTracking(character);
            FinalizeBossRushLootboxPathTracking(character);
            BossCleanupHelpers.DestroyRuntimePreset(
                character,
                PhantomWitchConfig.BossNameKey,
                "PhantomWitch_Preset",
                "[PhantomWitch]");

            if (currentWaveBosses != null)
            {
                currentWaveBosses.Remove(character);
            }

            if (currentBoss == character)
            {
                currentBoss = null;
            }

            try
            {
                if (character.gameObject != null)
                {
                    UnityEngine.Object.Destroy(character.gameObject);
                }
            }
            catch (Exception cleanupEx)
            {
                DevLog("[PhantomWitch] [WARNING] 回滚失败的Boss实例时异常: " + cleanupEx.Message);
            }
        }

        /// <summary>
        /// 设置幽灵女巫Boss属性
        /// </summary>
        private void SetupPhantomWitchAttributes(CharacterMainControl character)
        {
            if (character == null || character.CharacterItem == null)
            {
                return;
            }

            try
            {
                // 设置血量
                Stat maxHealthStat = character.CharacterItem.GetStat("MaxHealth");
                if (maxHealthStat != null)
                {
                    maxHealthStat.BaseValue = PhantomWitchConfig.BaseHealth;
                }

                if (character.Health != null)
                {
                    character.Health.SetHealth(PhantomWitchConfig.BaseHealth);
                    character.Health.showHealthBar = true;
                }

                DevLog("[PhantomWitch] Boss属性设置完成");
            }
            catch (Exception e)
            {
                DevLog($"[PhantomWitch] [WARNING] 设置Boss属性失败: {e.Message}");
            }
        }

        /// <summary>
        /// 为幽灵女巫装备近战武器
        /// </summary>
        private void EquipPhantomWitchWeapon(CharacterMainControl character)
        {
            if (character == null || character.CharacterItem == null)
            {
                return;
            }

            try
            {
                Item weaponItem = CreatePhantomWitchWeaponInstance();
                if (weaponItem == null)
                {
                    DevLog("[PhantomWitch] [WARNING] 未能创建镰刀武器实例，保留基础预设当前武器");
                    return;
                }

                PreparePhantomWitchWeaponItem(weaponItem);

                RemoveWeaponFromSlot(character.PrimWeaponSlot(), "[PhantomWitch] 主武器");
                RemoveWeaponFromSlot(character.MeleeWeaponSlot(), "[PhantomWitch] 近战武器");

                Inventory inventory = character.CharacterItem.Inventory;
                if (inventory != null)
                {
                    inventory.AddItem(weaponItem);
                }

                bool equipped = false;
                Item pluggedOut = null;
                Slot primarySlot = character.PrimWeaponSlot();
                Slot meleeSlot = character.MeleeWeaponSlot();
                if (meleeSlot != null)
                {
                    meleeSlot.Plug(weaponItem, out pluggedOut);
                    equipped = meleeSlot.Content == weaponItem;
                }

                if (!equipped)
                {
                    try
                    {
                        equipped = character.CharacterItem.TryPlug(weaponItem, true, null, 0);
                    }
                    catch
                    {
                    }
                }

                if (pluggedOut != null && pluggedOut != weaponItem)
                {
                    UnityEngine.Object.Destroy(pluggedOut.gameObject);
                }

                if (equipped)
                {
                    if (primarySlot != null && primarySlot.Content != null && primarySlot.Content != weaponItem)
                    {
                        RemoveWeaponFromSlot(primarySlot, "[PhantomWitch] 主武器");
                    }

                    character.ChangeHoldItem(weaponItem);
                    RefreshEquipmentModels(character);
                    DevLog("[PhantomWitch] 已装备近战武器: " + weaponItem.name + " (TypeID=" + weaponItem.TypeID + ")");
                }
                else
                {
                    DevLog("[PhantomWitch] [WARNING] 镰刀武器实例创建成功但未能装备到角色槽位");
                }
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitch] [WARNING] 装备近战武器失败: " + e.Message);
            }
        }

        private void PreparePhantomWitchWeaponItem(Item weaponItem)
        {
            if (weaponItem == null)
            {
                return;
            }

            try
            {
                if (weaponItem.TypeID == FenHuangHalberdIds.WeaponTypeId)
                {
                    FenHuangHalberdWeaponConfig.TryConfigure(weaponItem, "FenHuangHalberd");
                }
                else if (weaponItem.TypeID == PhantomWitchConfig.ReservedScytheTypeId)
                {
                    PhantomWitchScytheWeaponConfig.TryConfigure(weaponItem);
                }
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitch] [WARNING] 准备镰刀物品失败: " + e.Message);
            }
        }

        private Item CreatePhantomWitchWeaponInstance()
        {
            Item reservedWeapon = TryInstantiateWeapon(PhantomWitchConfig.ReservedScytheTypeId);
            if (reservedWeapon != null)
            {
                DevLog("[PhantomWitch] 使用正式镰刀资源 TypeID=" + PhantomWitchConfig.ReservedScytheTypeId);
                return reservedWeapon;
            }

            Item placeholderWeapon = TryInstantiateWeapon(PhantomWitchConfig.PlaceholderScytheTypeId);
            if (placeholderWeapon != null)
            {
                DevLog("[PhantomWitch] 正式镰刀资源缺失，回退断界戟占位 TypeID=" + PhantomWitchConfig.PlaceholderScytheTypeId);
                return placeholderWeapon;
            }

            return null;
        }

        private Item TryInstantiateWeapon(int typeId)
        {
            try
            {
                if (ItemAssetsCollection.GetPrefab(typeId) == null)
                {
                    return null;
                }

                return ItemAssetsCollection.InstantiateSync(typeId);
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitch] [WARNING] 实例化武器失败, TypeID=" + typeId + ", error=" + e.Message);
                return null;
            }
        }

        private void RemoveWeaponFromSlot(Slot slot, string slotLabel)
        {
            if (slot == null || slot.Content == null)
            {
                return;
            }

            try
            {
                Item unpluggedItem = slot.Unplug();
                if (unpluggedItem != null)
                {
                    DevLog(slotLabel + " 已移除: " + unpluggedItem.name + " (TypeID=" + unpluggedItem.TypeID + ")");
                    UnityEngine.Object.Destroy(unpluggedItem.gameObject);
                }
            }
            catch (Exception e)
            {
                DevLog(slotLabel + " 移除失败: " + e.Message);
            }
        }

        /// <summary>
        /// 查找幽灵女巫基础预设（精确匹配 Cname_Ghost）
        /// </summary>
        private CharacterRandomPreset FindPhantomWitchBasePreset()
        {
            if (phantomWitchBasePresetSearched)
            {
                return cachedPhantomWitchBasePreset;
            }

            phantomWitchBasePresetSearched = true;

            try
            {
                var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();

                // 第一优先：精确匹配 Cname_Ghost（原版幽灵敌人）
                foreach (var preset in allPresets)
                {
                    if (preset == null) continue;
                    if (preset.nameKey == PhantomWitchConfig.BasePresetNameKey)
                    {
                        cachedPhantomWitchBasePreset = preset;
                        DevLog($"[PhantomWitch] 找到 Cname_Ghost 预设: {preset.name}, health={preset.health}");
                        return cachedPhantomWitchBasePreset;
                    }
                }

                // 回退：尝试 Cname_Boss_Red
                foreach (var preset in allPresets)
                {
                    if (preset == null) continue;
                    if (preset.nameKey == PhantomWitchConfig.FallbackPresetNameKey)
                    {
                        cachedPhantomWitchBasePreset = preset;
                        DevLog($"[PhantomWitch] [WARNING] Cname_Ghost 不可用，回退到: {preset.name}");
                        return cachedPhantomWitchBasePreset;
                    }
                }

                DevLog("[PhantomWitch] [WARNING] 未找到任何可用预设");
                return null;
            }
            catch (Exception e)
            {
                DevLog($"[PhantomWitch] [ERROR] 查找基础预设失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 幽灵女巫死亡回调
        /// </summary>
        private void OnPhantomWitchDeath(CharacterMainControl deadWitch, DamageInfo damageInfo)
        {
            DevLog("[PhantomWitch] 幽灵女巫被击败");
            ShowMessage(L10n.T(PhantomWitchConfig.DefeatedMessageCN, PhantomWitchConfig.DefeatedMessageEN));

            if (deadWitch != null)
            {
                try
                {
                    PhantomWitchAssetManager.CreateDeathEffect(deadWitch.transform.position);
                }
                catch (Exception e)
                {
                    DevLog("[PhantomWitch] [WARNING] 死亡特效创建失败: " + e.Message);
                }
            }

            // 清理能力控制器
            PhantomWitchAbilityController abilities = null;
            if (phantomWitchInstances.TryGetValue(deadWitch, out abilities) && abilities != null)
            {
                abilities.OnBossDeath();
            }

            // 从实例字典中移除
            phantomWitchInstances.Remove(deadWitch);
            ClearBossRandomLootTracking(deadWitch);
            FinalizeBossRushLootboxPathTracking(deadWitch);

            BossCleanupHelpers.DestroyRuntimePreset(
                deadWitch,
                PhantomWitchConfig.BossNameKey,
                "PhantomWitch_Preset",
                "[PhantomWitch]");
        }

        /// <summary>
        /// 通知Boss生成失败
        /// </summary>
        private void NotifyPhantomWitchSpawnFailed()
        {
            try
            {
                EnemyPresetInfo witchPreset = FindPhantomWitchPresetInfo();
                OnBossSpawnFailed(witchPreset);
            }
            catch (Exception e)
            {
                DevLog($"[PhantomWitch] [WARNING] NotifyPhantomWitchSpawnFailed异常: {e.Message}");
            }
        }

        /// <summary>
        /// 查找幽灵女巫预设信息
        /// </summary>
        private EnemyPresetInfo FindPhantomWitchPresetInfo()
        {
            if (enemyPresets == null) return null;

            foreach (var p in enemyPresets)
            {
                if (p != null && p.name == PhantomWitchConfig.BossNameKey)
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>
        /// 检查是否是幽灵女巫预设
        /// </summary>
        private bool IsPhantomWitchPreset(EnemyPresetInfo preset)
        {
            if (preset == null) return false;
            return preset.name == PhantomWitchConfig.BossNameKey ||
                   preset.displayName == PhantomWitchConfig.BossNameCN ||
                   preset.displayName == PhantomWitchConfig.BossNameEN;
        }

        private bool IsManagedBossPreset(EnemyPresetInfo preset)
        {
            return IsDragonDescendantPreset(preset)
                || IsDragonKingPreset(preset)
                || IsPhantomWitchPreset(preset);
        }

        /// <summary>
        /// 注册幽灵女巫Boss到敌人预设列表
        /// </summary>
        private void RegisterPhantomWitchPreset()
        {
            if (phantomWitchRegistered) return;
            if (enemyPresets == null) return;

            // 检查是否已存在
            foreach (var p in enemyPresets)
            {
                if (p != null && p.name == PhantomWitchConfig.BossNameKey)
                {
                    phantomWitchRegistered = true;
                    return;
                }
            }

            // 添加幽灵女巫预设
            var witchPreset = new EnemyPresetInfo
            {
                name = PhantomWitchConfig.BossNameKey,
                displayName = L10n.T(PhantomWitchConfig.BossNameCN, PhantomWitchConfig.BossNameEN),
                team = (int)Teams.scav,
                baseHealth = PhantomWitchConfig.BaseHealth,
                baseDamage = 30f,
                healthMultiplier = 1f,
                damageMultiplier = PhantomWitchConfig.DamageMultiplier,
                expReward = 400
            };

            enemyPresets.Add(witchPreset);
            phantomWitchRegistered = true;

            DevLog("[PhantomWitch] 幽灵女巫Boss已注册到敌人预设列表");
        }
    }
}
