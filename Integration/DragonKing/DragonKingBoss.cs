// ============================================================================
// DragonKingBoss.cs - 龙王Boss主控制器
// ============================================================================
// 模块说明：
//   管理龙王Boss的生成、属性设置和生命周期
//   作为ModBehaviour的partial class实现
//   基于泰拉瑞亚光之女皇的AI框架设计
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Pathfinding;

namespace BossRush
{
    /// <summary>
    /// 龙王Boss主控制器（partial class）
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 龙王Boss实例引用（多实例支持） ==========
        
        /// <summary>
        /// 所有活跃的龙王Boss实例及其能力控制器（支持多Boss模式）
        /// </summary>
        private Dictionary<CharacterMainControl, DragonKingAbilityController> dragonKingInstances 
            = new Dictionary<CharacterMainControl, DragonKingAbilityController>();
        
        /// <summary>
        /// 龙王掉落事件委托映射（每个实例独立的委托，用于正确取消订阅）
        /// </summary>
        private Dictionary<CharacterMainControl, Action<DamageInfo>> dragonKingLootEventHandlers
            = new Dictionary<CharacterMainControl, Action<DamageInfo>>();
        
        /// <summary>
        /// 龙王是否已注册到预设列表 - 用于防止重复注册
        /// </summary>
        #pragma warning disable CS0414
        private static bool dragonKingRegistered = false;
        #pragma warning restore CS0414
        
        // ========== 龙王套装效果状态（多实例支持） ==========
        
        /// <summary>
        /// 龙王Boss套装效果是否已注册（全局静态事件只需注册一次）
        /// </summary>
        private bool dragonKingSetBonusRegistered = false;
        
        /// <summary>
        /// 所有活跃龙王的Health引用集合（用于OnHurt回调中快速判断是否为龙王）
        /// </summary>
        private HashSet<Health> activeDragonKingHealths = new HashSet<Health>();
        
        // ========== 性能优化：预设缓存 ==========
        
        /// <summary>
        /// 缓存的龙王基础预设（复用???预设）
        /// </summary>
        private static CharacterRandomPreset cachedDragonKingBasePreset = null;
        
        /// <summary>
        /// 是否已搜索过龙王基础预设
        /// </summary>
        private static bool dragonKingBasePresetSearched = false;

        /// <summary>
        /// 清理龙王相关的所有静态缓存（场景切换时调用）
        /// </summary>
        public static void ClearDragonKingStaticCache()
        {
            cachedDragonKingBasePreset = null;
            dragonKingBasePresetSearched = false;

            // 强制清理资源管理器缓存（场景切换时使用）
            DragonKingAssetManager.ForceCleanup();

            // 清理能力控制器缓存
            DragonKingAbilityController.ClearStaticCache();

            // 重置BGM播放状态
            BossRushAudioManager.Instance?.ResetDragonKingBGMState();
        }

        /// <summary>
        /// 释放Boss实例资源（Boss销毁时调用）
        /// </summary>
        public static void ReleaseDragonKingInstance()
        {
            // 使用引用计数清理
            DragonKingAssetManager.ClearCache();

            // 清理能力控制器静态材质缓存
            DragonKingAbilityController.ClearStaticCache();
        }
        
        // ========== 生成方法 ==========
        
        /// <summary>
        /// 生成龙王Boss
        /// </summary>
        public async UniTask<CharacterMainControl> SpawnDragonKing(Vector3 position)
        {
            try
            {
                DevLog($"[DragonKing] 开始生成龙王Boss at {position}");
                
                // 查找基础敌人预设（复用???预设）
                CharacterRandomPreset basePreset = FindDragonKingBasePreset();
                
                if (basePreset == null)
                {
                    DevLog("[DragonKing] [ERROR] 未找到基础敌人预设");
                    NotifyBossSpawnFailed();
                    return null;
                }
                
                DevLog($"[DragonKing] 使用预设: {basePreset.name}");
                
                // 生成角色
                Vector3 dir = Vector3.forward;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                var character = await basePreset.CreateCharacterAsync(position, dir, relatedScene, null, false);
                
                if (character == null)
                {
                    DevLog("[DragonKing] [ERROR] 生成角色失败");
                    NotifyBossSpawnFailed();
                    return null;
                }
                
                dragonKingInstances[character] = null; // 先占位，后面赋值控制器
                character.gameObject.name = "BossRush_DragonKing";
                
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
                    customPreset.name = "DragonKing_Preset";
                    customPreset.showName = true;
                    customPreset.showHealthBar = true;
                    customPreset.nameKey = DragonKingConfig.BossNameKey;
                    character.characterPreset = customPreset;
                    
                    DevLog($"[DragonKing] 已创建独立预设副本, nameKey={customPreset.nameKey}");
                }
                
                // 设置Health组件
                if (character.Health != null)
                {
                    character.Health.showHealthBar = true;
                }
                
                // 设置Boss属性
                SetupDragonKingAttributes(character);
                
                // 应用全局Boss数值倍率
                ApplyBossStatMultiplier(character);
                
                // 装备龙王套装
                await EquipDragonKing(character);
                
                // 禁用原有AI组件，龙王Boss完全由DragonKingAbilityController控制
                DisableDragonKingOriginalAI(character);
                
                // 添加能力控制器
                var abilities = character.gameObject.AddComponent<DragonKingAbilityController>();
                abilities.Initialize(character);
                dragonKingInstances[character] = abilities;
                
                // 激活角色
                character.gameObject.SetActive(true);
                
                // 请求显示血条
                if (character.Health != null)
                {
                    character.Health.RequestHealthBar();
                }
                
                // 设置AI仇恨
                SetupAIAggro(character);
                
                // 订阅死亡事件（使用闭包捕获当前character引用，支持多实例）
                if (character.Health != null)
                {
                    // 使用局部变量捕获，确保每个龙皇的死亡回调指向正确的实例
                    CharacterMainControl capturedChar = character;
                    character.Health.OnDeadEvent.AddListener((dmgInfo) => OnDragonKingDeath(capturedChar, dmgInfo));
                }
                
                // 注册龙王套装效果（火焰伤害免疫并转化为治疗）
                RegisterDragonKingSetBonus(character);
                
                // 记录Boss生成信息
                try
                {
                    bossSpawnTimes[character] = Time.time;
                    bossOriginalLootCounts[character] = 5; // 龙王掉落更多
                    
                    // 使用命名委托替代Lambda，以便后续可以正确取消订阅（避免内存泄漏）
                    // 每个龙皇实例独立的掉落事件处理器
                    CharacterMainControl capturedCharForLoot = character;
                    Action<DamageInfo> lootHandler = (dmgInfo) => {
                        DevLog("[DragonKing] BeforeCharacterSpawnLootOnDead 事件触发");
                        OnBossBeforeSpawnLoot(capturedCharForLoot, dmgInfo);
                    };
                    dragonKingLootEventHandlers[character] = lootHandler;
                    character.BeforeCharacterSpawnLootOnDead += lootHandler;
                    
                    DevLog("[DragonKing] 已订阅掉落事件，bossSpawnTimes.Count=" + bossSpawnTimes.Count);
                }
                catch (Exception recordEx)
                {
                    DevLog($"[DragonKing] [WARNING] 记录Boss生成信息失败: {recordEx.Message}");
                }
                
                DevLog("[DragonKing] 龙王Boss生成完成");
                ShowMessage(L10n.DragonKingAppeared);

                // 播放龙王BGM
                BossRushAudioManager.Instance.PlayDragonKingBGM();
                
                return character;
            }
            catch (Exception e)
            {
                DevLog($"[DragonKing] [ERROR] 生成Boss失败: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// 查找龙王基础预设（复用???预设）
        /// </summary>
        private CharacterRandomPreset FindDragonKingBasePreset()
        {
            // 使用缓存
            if (cachedDragonKingBasePreset != null) return cachedDragonKingBasePreset;
            if (dragonKingBasePresetSearched) return null;
            
            dragonKingBasePresetSearched = true;
            
            // 复用龙裔遗族的预设查找逻辑
            cachedDragonKingBasePreset = FindQuestionMarkPreset();
            if (cachedDragonKingBasePreset == null)
            {
                cachedDragonKingBasePreset = FindFallbackPreset();
            }
            
            return cachedDragonKingBasePreset;
        }
        
        /// <summary>
        /// 查找龙王预设信息
        /// </summary>
        private EnemyPresetInfo FindDragonKingPresetInfo()
        {
            if (enemyPresets == null) return null;
            
            foreach (var p in enemyPresets)
            {
                if (p != null && p.name == DragonKingConfig.BossNameKey)
                {
                    return p;
                }
            }
            return null;
        }
        
        /// <summary>
        /// 设置龙王Boss属性
        /// </summary>
        private void SetupDragonKingAttributes(CharacterMainControl character)
        {
            try
            {
                if (character == null || character.CharacterItem == null) return;
                
                var item = character.CharacterItem;
                
                // 设置血量
                var healthStat = item.GetStat("MaxHealth");
                if (healthStat != null)
                {
                    healthStat.BaseValue = DragonKingConfig.BaseHealth;
                }
                
                // 恢复满血
                if (character.Health != null)
                {
                    character.Health.SetHealth(DragonKingConfig.BaseHealth);
                }
                
                // 设置伤害倍率
                var gunDmgStat = item.GetStat("GunDamageMultiplier");
                if (gunDmgStat != null)
                {
                    gunDmgStat.BaseValue = DragonKingConfig.DamageMultiplier;
                }
                
                var meleeDmgStat = item.GetStat("MeleeDamageMultiplier");
                if (meleeDmgStat != null)
                {
                    meleeDmgStat.BaseValue = DragonKingConfig.DamageMultiplier;
                }
                
                DevLog($"[DragonKing] Boss属性设置完成: HP={DragonKingConfig.BaseHealth}, DmgMult={DragonKingConfig.DamageMultiplier}");
            }
            catch (Exception e)
            {
                DevLog($"[DragonKing] [WARNING] 设置Boss属性失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 装备龙王Boss
        /// </summary>
        private async UniTask EquipDragonKing(CharacterMainControl character)
        {
            try
            {
                if (character == null) return;
                
                DevLog("[DragonKing] 开始为龙王装备套装...");
                
                // 装备龙王之冕
                Item helmItem = FindItemByTypeId(DragonKingConfig.DRAGON_KING_HELM_TYPE_ID);
                if (helmItem == null)
                {
                    helmItem = FindItemByName(DragonKingConfig.DRAGON_KING_HELM_NAME);
                }
                
                if (helmItem != null)
                {
                    EquipArmorItem(character, helmItem, "Helmat".GetHashCode());
                }
                else
                {
                    DevLog("[DragonKing] [WARNING] 未找到龙王之冕装备");
                }
                
                // 装备龙王鳞铠
                Item armorItem = FindItemByTypeId(DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID);
                if (armorItem == null)
                {
                    armorItem = FindItemByName(DragonKingConfig.DRAGON_KING_ARMOR_NAME);
                }
                
                if (armorItem != null)
                {
                    EquipArmorItem(character, armorItem, "Armor".GetHashCode());
                }
                else
                {
                    DevLog("[DragonKing] [WARNING] 未找到龙王鳞铠装备");
                }
                
                // 刷新装备模型显示
                RefreshEquipmentModels(character);
                
                // 加载最高级子弹（如果有武器的话）
                await LoadHighestTierAmmo(character);
                
                DevLog("[DragonKing] 龙王装备完成");
            }
            catch (Exception e)
            {
                DevLog($"[DragonKing] [WARNING] 装备失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 配置龙王AI - 保留原版AI的走路和开枪逻辑
        /// 技能释放时由DragonKingAbilityController控制收枪
        /// </summary>
        private void DisableDragonKingOriginalAI(CharacterMainControl character)
        {
            if (character == null) return;
            
            try
            {
                // 保留原版AI的走路和开枪逻辑
                // 只需要确保AI能正常工作
                
                var aiController = character.GetComponentInChildren<AICharacterController>();
                if (aiController != null)
                {
                    // 保持AI启用
                    aiController.enabled = true;
                    
                    // 设置AI仇恨玩家（Mode E 同阵营时不设置，避免友方龙王攻击玩家）
                    if (!IsModeEActive && CharacterMainControl.Main != null && CharacterMainControl.Main.mainDamageReceiver != null)
                    {
                        aiController.searchedEnemy = CharacterMainControl.Main.mainDamageReceiver;
                    }
                    
                    DevLog("[DragonKing] 保留原版AI，走路和开枪由原版控制");
                }
                
                DevLog("[DragonKing] AI配置完成，技能释放时会收枪");
            }
            catch (Exception e)
            {
                DevLog($"[DragonKing] [WARNING] 配置AI时出错: {e.Message}");
            }
        }
        
        /// <summary>
        /// 龙王死亡回调
        /// </summary>
        /// <summary>
        /// 龙王死亡回调（支持多实例，通过参数识别具体龙皇）
        /// </summary>
        private void OnDragonKingDeath(CharacterMainControl deadKing, DamageInfo damageInfo)
        {
            DevLog("[DragonKing] 龙王被击败");
            ShowMessage(L10n.DragonKingDefeated);

            // 重置BGM播放状态
            BossRushAudioManager.Instance?.ResetDragonKingBGMState();

            // 取消注册该龙皇的套装效果
            UnregisterDragonKingSetBonus(deadKing);

            // 直接触发龙王击杀成就（作为保险措施，防止三阶段联动死亡时成就未触发）
            try
            {
                CheckBossKillAchievements("DragonKing");
                DevLog("[DragonKing] 已触发龙王击杀成就检测");
            }
            catch (System.Exception e)
            {
                DevLog("[DragonKing] 触发成就检测失败: " + e.Message);
            }

            // 清理该实例的能力控制器
            DragonKingAbilityController abilities = null;
            if (dragonKingInstances.TryGetValue(deadKing, out abilities) && abilities != null)
            {
                abilities.OnBossDeath();
            }

            // 从实例字典中移除
            dragonKingInstances.Remove(deadKing);
            
            // 清理掉落事件委托引用
            dragonKingLootEventHandlers.Remove(deadKing);

            // 释放Boss实例资源（使用引用计数）
            ReleaseDragonKingInstance();
        }

        /// <summary>
        /// 通知Boss生成失败（提取重复的Try-Catch块）
        /// </summary>
        private void NotifyBossSpawnFailed()
        {
            try
            {
                EnemyPresetInfo dragonKingPreset = FindDragonKingPresetInfo();
                OnBossSpawnFailed(dragonKingPreset);
            }
            catch (Exception e)
            {
                DevLog($"[DragonKing] [WARNING] NotifyBossSpawnFailed异常: {e.Message}");
            }
        }

        /// <summary>
        /// 检查是否是龙王预设
        /// </summary>
        private bool IsDragonKingPreset(EnemyPresetInfo preset)
        {
            if (preset == null) return false;
            return preset.name == DragonKingConfig.BossNameKey ||
                   preset.displayName == DragonKingConfig.BossNameCN ||
                   preset.displayName == DragonKingConfig.BossNameEN;
        }
        
        /// <summary>
        /// 注册龙王Boss到敌人预设列表
        /// </summary>
        private void RegisterDragonKingPreset()
        {
            if (dragonKingRegistered) return;
            if (enemyPresets == null) return;
            
            // 检查是否已存在
            foreach (var p in enemyPresets)
            {
                if (p != null && p.name == DragonKingConfig.BossNameKey)
                {
                    dragonKingRegistered = true;
                    return;
                }
            }
            
            // 添加龙王预设
            var dragonKingPreset = new EnemyPresetInfo
            {
                name = DragonKingConfig.BossNameKey,
                displayName = L10n.T(DragonKingConfig.BossNameCN, DragonKingConfig.BossNameEN),
                team = (int)Teams.wolf, // 狼群阵营（Mode E 阵营体系统一）
                baseHealth = DragonKingConfig.BaseHealth,
                baseDamage = 50f,
                healthMultiplier = 1f,
                damageMultiplier = DragonKingConfig.DamageMultiplier,
                expReward = 500
            };
            
            enemyPresets.Add(dragonKingPreset);
            dragonKingRegistered = true;
            
            DevLog("[DragonKing] 龙王Boss已注册到敌人预设列表");
        }
        
        // ========== 龙王套装效果（火焰免疫 + 火焰转治疗）==========
        
        /// <summary>
        /// 注册龙王Boss套装效果（火焰伤害免疫并转化为治疗）
        /// 支持多实例：每个龙皇的Health加入集合，全局事件只注册一次
        /// </summary>
        private void RegisterDragonKingSetBonus(CharacterMainControl kingInstance)
        {
            try
            {
                if (kingInstance != null && kingInstance.Health != null)
                {
                    // 将该龙皇的Health加入活跃集合
                    activeDragonKingHealths.Add(kingInstance.Health);
                    
                    // 全局静态事件只注册一次
                    if (!dragonKingSetBonusRegistered)
                    {
                        Health.OnHurt += OnDragonKingBossHurt;
                        dragonKingSetBonusRegistered = true;
                    }
                    
                    DevLog("[DragonKing] 已注册龙王套装效果（火焰免疫），活跃龙皇数: " + activeDragonKingHealths.Count);
                }
                else
                {
                    DevLog("[DragonKing] [WARNING] 注册龙王套装效果失败：Boss实例或Health为空");
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonKing] [ERROR] 注册龙王套装效果失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 取消注册指定龙王的套装效果
        /// 当所有龙皇都死亡后，才取消全局事件订阅
        /// </summary>
        private void UnregisterDragonKingSetBonus(CharacterMainControl kingInstance)
        {
            try
            {
                // 从活跃集合中移除该龙皇
                if (kingInstance != null && kingInstance.Health != null)
                {
                    activeDragonKingHealths.Remove(kingInstance.Health);
                }
                
                // 所有龙皇都死亡后，取消全局事件订阅
                if (activeDragonKingHealths.Count == 0 && dragonKingSetBonusRegistered)
                {
                    Health.OnHurt -= OnDragonKingBossHurt;
                    dragonKingSetBonusRegistered = false;
                    DevLog("[DragonKing] 所有龙皇已死亡，已取消注册龙王套装效果");
                }
                else
                {
                    DevLog("[DragonKing] 取消一个龙皇套装效果，剩余活跃龙皇数: " + activeDragonKingHealths.Count);
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonKing] [ERROR] 取消注册龙王套装效果失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 龙王Boss伤害事件回调 - 火焰伤害免疫并转化为治疗
        /// [性能优化] 使用缓存的Health引用进行快速身份验证
        /// </summary>
        private void OnDragonKingBossHurt(Health health, DamageInfo damageInfo)
        {
            // [性能优化] 快速过滤：使用活跃龙皇Health集合判断
            if (activeDragonKingHealths.Count == 0 || !activeDragonKingHealths.Contains(health)) return;
            
            try
            {
                // 检查是否有火焰伤害（快速路径）
                if (damageInfo.elementFactors == null || damageInfo.elementFactors.Count == 0) return;
                
                // 使用 finalDamage 计算火焰伤害占比
                float totalFinalDamage = damageInfo.finalDamage;
                if (totalFinalDamage <= 0f) return;
                
                // 计算火焰伤害占比
                float fireFactor = 0f;
                float totalFactor = 0f;
                var factors = damageInfo.elementFactors;
                int count = factors.Count;
                for (int i = 0; i < count; i++)
                {
                    var ef = factors[i];
                    if (ef.factor > 0f)
                    {
                        totalFactor += ef.factor;
                        if (ef.elementType == ElementTypes.fire)
                        {
                            fireFactor += ef.factor;
                        }
                    }
                }
                
                // 没有火焰伤害则跳过
                if (fireFactor <= 0f || totalFactor <= 0f) return;
                
                // 计算火焰伤害在最终伤害中的占比，全部转化为治疗
                float fireRatio = fireFactor / totalFactor;
                float actualFireDamage = totalFinalDamage * fireRatio;
                float fireHealAmount = actualFireDamage; // 100% 转化为治疗
                
                // 将火焰伤害因子设为0（免疫火焰伤害）
                for (int i = 0; i < count; i++)
                {
                    var ef = factors[i];
                    if (ef.elementType == ElementTypes.fire && ef.factor > 0f)
                    {
                        factors[i] = new ElementFactor(ElementTypes.fire, 0f);
                    }
                }
                
                DevLog("[DragonKing] Boss火焰伤害吸收: " + actualFireDamage.ToString("F1") + " -> 治疗: " + fireHealAmount.ToString("F1"));
                
                // 延迟治疗（在伤害计算完成后）
                if (fireHealAmount > 0f)
                {
                    StartCoroutine(DelayedDragonKingBossHeal(health, fireHealAmount));
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonKing] [ERROR] OnDragonKingBossHurt 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 延迟治疗龙王Boss（在伤害计算完成后）
        /// </summary>
        private System.Collections.IEnumerator DelayedDragonKingBossHeal(Health health, float amount)
        {
            yield return null; // 等待一帧
            
            if (health != null && !health.IsDead)
            {
                health.AddHealth(amount);
                DevLog("[DragonKing] Boss火焰能量治疗: +" + amount.ToString("F1"));
                
                // 显示治疗数字（使用Health组件的transform获取位置，不依赖单实例引用）
                try
                {
                    if (health != null && health.gameObject != null)
                    {
                        FX.PopText.Pop("+" + amount.ToString("F0"), 
                            health.transform.position + Vector3.up * 2.5f, 
                            new Color(0.2f, 1f, 0.2f), 1.2f, null);
                    }
                }
                catch { }
            }
        }
    }
}
