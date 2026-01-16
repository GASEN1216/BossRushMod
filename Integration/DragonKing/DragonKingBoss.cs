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
        // ========== 龙王Boss实例引用 ==========
        
        /// <summary>
        /// 龙王Boss实例
        /// </summary>
        private CharacterMainControl dragonKingInstance;
        
        /// <summary>
        /// 龙王能力控制器
        /// </summary>
        private DragonKingAbilityController dragonKingAbilities;
        
        /// <summary>
        /// 龙王是否已注册到预设列表 - 用于防止重复注册
        /// </summary>
        #pragma warning disable CS0414
        private static bool dragonKingRegistered = false;
        #pragma warning restore CS0414
        
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
                
                dragonKingInstance = character;
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
                
                // 禁用原有AI组件，龙王Boss完全由DragonKingAbilityController控制
                DisableDragonKingOriginalAI(character);
                
                // 添加能力控制器
                dragonKingAbilities = character.gameObject.AddComponent<DragonKingAbilityController>();
                dragonKingAbilities.Initialize(character);
                
                // 激活角色
                character.gameObject.SetActive(true);
                
                // 请求显示血条
                if (character.Health != null)
                {
                    character.Health.RequestHealthBar();
                }
                
                // 设置AI仇恨
                SetupAIAggro(character);
                
                // 订阅死亡事件
                if (character.Health != null)
                {
                    character.Health.OnDeadEvent.AddListener(OnDragonKingDeath);
                }
                
                // 记录Boss生成信息
                try
                {
                    bossSpawnTimes[character] = Time.time;
                    bossOriginalLootCounts[character] = 5; // 龙王掉落更多
                    character.BeforeCharacterSpawnLootOnDead += (dmgInfo) => OnBossBeforeSpawnLoot(character, dmgInfo);
                }
                catch (Exception recordEx)
                {
                    DevLog($"[DragonKing] [WARNING] 记录Boss生成信息失败: {recordEx.Message}");
                }
                
                DevLog("[DragonKing] 龙王Boss生成完成");
                ShowMessage(L10n.DragonKingAppeared);
                
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
                    
                    // 设置AI仇恨玩家
                    if (CharacterMainControl.Main != null && CharacterMainControl.Main.mainDamageReceiver != null)
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
        private void OnDragonKingDeath(DamageInfo damageInfo)
        {
            DevLog("[DragonKing] 龙王被击败");
            ShowMessage(L10n.DragonKingDefeated);

            // 移除事件监听（防止内存泄漏）
            // 使用缓存的实例引用，因为DamageInfo是结构体且可能没有victim属性
            if (dragonKingInstance != null && dragonKingInstance.Health != null)
            {
                dragonKingInstance.Health.OnDeadEvent.RemoveListener(OnDragonKingDeath);
            }

            // 清理能力控制器
            if (dragonKingAbilities != null)
            {
                dragonKingAbilities.OnBossDeath();
            }

            // 清理实例引用
            dragonKingInstance = null;
            dragonKingAbilities = null;

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
                team = 2, // 敌人阵营
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
    }
}
