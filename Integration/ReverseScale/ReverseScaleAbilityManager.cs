// ============================================================================
// ReverseScaleAbilityManager.cs - 逆鳞图腾能力管理器
// ============================================================================
// 模块说明：
//   处理逆鳞的核心逻辑：
//   - 监听玩家受伤事件
//   - 当血量降至1滴血时触发效果
//   - 恢复50%血量、发射棱彩弹、显示气泡、销毁装备
//   
// 重要：逆鳞图腾的 Health.OnHurt 事件订阅必须在整个游戏过程中保持，
//       不能因为场景切换而取消。只有在图腾被销毁（触发效果后）时才取消订阅。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// 逆鳞图腾能力管理器 - 处理逆鳞触发逻辑
    /// 注意：此管理器独立管理 Health.OnHurt 事件订阅，不依赖 EquipmentEffectManager 的停用逻辑
    /// </summary>
    public class ReverseScaleAbilityManager : MonoBehaviour
    {
        // ========== 单例 ==========

        private static ReverseScaleAbilityManager _instance;

        public static ReverseScaleAbilityManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<ReverseScaleAbilityManager>();
                }
                return _instance;
            }
        }

        // ========== 配置 ==========

        private ReverseScaleConfig config => ReverseScaleConfig.Instance;

        // ========== 状态 ==========

        private bool isRegistered = false;
        private bool hurtEventRegistered = false;
        private CharacterMainControl registeredCharacter = null;
        private Item equippedItem = null;
        private Slot equippedSlot = null;
        
        // 标记是否因为触发效果而销毁（区分场景切换和真正的卸下）
        private bool effectTriggered = false;

        // 缓存的 LayerMask（避免每帧字符串查找）
        private static int characterLayerMask = -1;
        
        // 性能优化：预分配敌人搜索缓冲区（避免每次搜索产生 GC）
        private static Collider[] enemySearchBuffer = new Collider[32];

        // ========== 生命周期 ==========

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // 订阅场景加载事件，用于在场景切换后重新绑定角色
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            // 只有在真正销毁时才取消事件订阅
            ForceUnregisterHurtEvent();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        /// <summary>
        /// 场景加载完成时调用 - 重新绑定角色引用
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!isRegistered || !hurtEventRegistered)
            {
                return;
            }
            
            // 场景加载后，延迟重新绑定角色
            StartCoroutine(DelayedRebindCharacter());
        }
        
        /// <summary>
        /// 延迟重新绑定角色（等待角色完全加载）
        /// </summary>
        private IEnumerator DelayedRebindCharacter()
        {
            yield return new WaitForSeconds(0.5f);
            
            CharacterMainControl main = CharacterMainControl.Main;
            if (main != null && main != registeredCharacter)
            {
                registeredCharacter = main;
                FindEquippedReverseScale(main);
                ModBehaviour.DevLog($"{config.LogPrefix} 场景切换后重新绑定角色: {main.name}");
            }
        }

        // ========== 公开方法 ==========

        /// <summary>
        /// 确保管理器实例存在
        /// </summary>
        public static void EnsureInstance()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("ReverseScaleAbilityManager");
                _instance = go.AddComponent<ReverseScaleAbilityManager>();
                DontDestroyOnLoad(go);
            }
        }

        /// <summary>
        /// 注册能力（装备逆鳞时调用）
        /// </summary>
        public void RegisterAbility(CharacterMainControl character)
        {
            if (isRegistered)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 能力已注册，跳过");
                return;
            }

            if (character == null)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 角色为空，无法注册");
                return;
            }

            registeredCharacter = character;

            // 查找装备的逆鳞物品
            FindEquippedReverseScale(character);

            // 注册伤害事件
            RegisterHurtEvent();

            isRegistered = true;
            ModBehaviour.DevLog($"{config.LogPrefix} 能力已激活");
        }

        /// <summary>
        /// 取消注册能力（由 EquipmentEffectManager 调用，但不会取消 Health.OnHurt 事件订阅）
        /// 重要：逆鳞图腾的事件订阅必须保持，只有在触发效果后才真正取消
        /// </summary>
        public void UnregisterAbility()
        {
            if (!isRegistered)
            {
                return;
            }

            // 重要：不要在这里取消 Health.OnHurt 事件订阅！
            // 场景切换时 EquipmentEffectManager 会调用此方法，但我们需要保持事件订阅
            // 只有在 effectTriggered = true 时才真正取消订阅
            
            if (effectTriggered)
            {
                // 效果已触发，真正取消订阅
                UnregisterHurtEvent();
                registeredCharacter = null;
                equippedItem = null;
                equippedSlot = null;
                isRegistered = false;
                effectTriggered = false;
                
                // 重要：同时重置 EffectManager 的状态，以便下次装备新逆鳞时能正确激活
                if (ReverseScaleEffectManager.Instance != null)
                {
                    ReverseScaleEffectManager.Instance.ResetActivationState();
                }
                
                ModBehaviour.DevLog($"{config.LogPrefix} 能力已停用（效果已触发）");
            }
            else
            {
                // 可能是场景切换导致的调用，保持事件订阅
                ModBehaviour.DevLog($"{config.LogPrefix} UnregisterAbility 被调用，但保持 Health.OnHurt 事件订阅（可能是场景切换）");
            }
        }
        
        /// <summary>
        /// 强制取消注册能力（真正的清理，用于 Mod 卸载等场景）
        /// </summary>
        public void ForceUnregisterAbility()
        {
            ForceUnregisterHurtEvent();
            registeredCharacter = null;
            equippedItem = null;
            equippedSlot = null;
            isRegistered = false;
            effectTriggered = false;
            ModBehaviour.DevLog($"{config.LogPrefix} 能力已强制停用");
        }

        /// <summary>
        /// 重新绑定到新角色
        /// </summary>
        public void RebindToCharacter(CharacterMainControl character)
        {
            if (character == null) return;

            registeredCharacter = character;
            FindEquippedReverseScale(character);

            ModBehaviour.DevLog($"{config.LogPrefix} 已重新绑定到角色: {character.name}");
        }

        /// <summary>
        /// 场景切换时调用
        /// </summary>
        public void OnSceneChanged()
        {
            // 逆鳞不需要特殊的场景切换处理
        }

        /// <summary>
        /// 清理管理器（Mod 卸载时调用）
        /// </summary>
        public static void Cleanup()
        {
            if (_instance != null)
            {
                _instance.ForceUnregisterAbility();
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        // ========== 内部方法 ==========

        /// <summary>
        /// 查找装备的逆鳞物品
        /// </summary>
        private void FindEquippedReverseScale(CharacterMainControl character)
        {
            if (character == null || character.CharacterItem == null) return;

            foreach (Slot slot in character.CharacterItem.Slots)
            {
                if (slot == null || slot.Content == null) continue;
                if (!slot.Key.StartsWith(ReverseScaleConfig.TotemSlotPrefix)) continue;

                if (slot.Content.TypeID == ReverseScaleConfig.TotemTypeId)
                {
                    equippedItem = slot.Content;
                    equippedSlot = slot;
                    ModBehaviour.DevLog($"{config.LogPrefix} 找到装备的逆鳞: 槽位={slot.Key}");
                    return;
                }
            }
        }

        /// <summary>
        /// 注册伤害事件
        /// </summary>
        private void RegisterHurtEvent()
        {
            if (hurtEventRegistered) return;

            try
            {
                Health.OnHurt += OnPlayerHurt;
                hurtEventRegistered = true;
                ModBehaviour.DevLog($"{config.LogPrefix} 已注册伤害事件");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 注册伤害事件失败: {e.Message}");
            }
        }

        /// <summary>
        /// 取消注册伤害事件（仅在效果触发后调用）
        /// </summary>
        private void UnregisterHurtEvent()
        {
            if (!hurtEventRegistered) return;

            try
            {
                Health.OnHurt -= OnPlayerHurt;
                hurtEventRegistered = false;
                ModBehaviour.DevLog($"{config.LogPrefix} 已取消注册伤害事件（效果已触发）");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 取消注册伤害事件失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 强制取消注册伤害事件（用于 Mod 卸载等场景）
        /// </summary>
        private void ForceUnregisterHurtEvent()
        {
            if (!hurtEventRegistered) return;

            try
            {
                Health.OnHurt -= OnPlayerHurt;
                hurtEventRegistered = false;
                ModBehaviour.DevLog($"{config.LogPrefix} 已强制取消注册伤害事件");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 强制取消注册伤害事件失败: {e.Message}");
            }
        }

        /// <summary>
        /// 玩家受伤事件回调 - 检测是否触发逆鳞效果
        /// </summary>
        /// <remarks>
        /// 重要：OnHurt 事件在 Health.Hurt() 中于 CurrentHealth -= finalDamage 之后、
        /// 死亡判定之前触发。因此 health.CurrentHealth 已经是扣血后的值。
        /// 
        /// 注意：此方法会动态检查玩家是否仍然装备着逆鳞图腾，
        /// 而不是依赖 isRegistered 标记，以处理场景切换时的状态不一致问题。
        /// </remarks>
        private void OnPlayerHurt(Health health, DamageInfo damageInfo)
        {
            try
            {
                // 只处理主角
                if (health == null || !health.IsMainCharacterHealth)
                {
                    return;
                }
                
                // 动态检查玩家是否仍然装备着逆鳞图腾
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || main.CharacterItem == null)
                {
                    return;
                }
                
                // 查找装备的逆鳞
                bool hasReverseScale = false;
                Slot foundSlot = null;
                Item foundItem = null;
                
                foreach (Slot slot in main.CharacterItem.Slots)
                {
                    if (slot == null || slot.Content == null) continue;
                    if (!slot.Key.StartsWith(ReverseScaleConfig.TotemSlotPrefix)) continue;

                    if (slot.Content.TypeID == ReverseScaleConfig.TotemTypeId)
                    {
                        hasReverseScale = true;
                        foundSlot = slot;
                        foundItem = slot.Content;
                        break;
                    }
                }
                
                if (!hasReverseScale)
                {
                    // 玩家没有装备逆鳞，跳过
                    return;
                }
                
                // 更新缓存的引用
                equippedSlot = foundSlot;
                equippedItem = foundItem;
                registeredCharacter = main;

                // 调试：记录血量和阈值
                ModBehaviour.DevLog($"{config.LogPrefix} [DEBUG] CurrentHealth={health.CurrentHealth}, Threshold={config.TriggerHealthThreshold}, finalDamage={damageInfo.finalDamage}");

                // 此时 CurrentHealth 已经是扣血后的值
                // 当血量降至阈值或以下（包括0或负数），触发效果
                if (health.CurrentHealth <= config.TriggerHealthThreshold)
                {
                    ModBehaviour.DevLog($"{config.LogPrefix} 触发条件满足! 扣血后血量={health.CurrentHealth}, 本次伤害={damageInfo.finalDamage}");

                    TriggerReverseScaleEffect(health);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} OnPlayerHurt 出错: {e.Message}\\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// 触发逆鳞效果：恢复血量、发射棱彩弹、显示气泡、销毁装备
        /// </summary>
        private void TriggerReverseScaleEffect(Health health)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                // 1. 计算恢复量并恢复血量
                // 修复：当血量为负数或0时，先设为1血再恢复，避免恢复后仍然死亡
                float currentHealth = health.CurrentHealth;
                if (currentHealth <= 0f)
                {
                    health.SetHealth(1f);
                    ModBehaviour.DevLog($"{config.LogPrefix} 血量为{currentHealth}，先设为1血");
                }
                float healAmount = health.MaxHealth * config.HealPercent;
                health.AddHealth(healAmount);
                ModBehaviour.DevLog($"{config.LogPrefix} 恢复血量: +{healAmount}, 当前血量: {health.CurrentHealth}");

                // 2. 触发棱彩弹攻击（触之必怒！）
                FirePrismaticBolts(main);

                // 3. 显示气泡提示
                ShowBubble();

                // 4. 销毁装备
                DestroyTotem();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} TriggerReverseScaleEffect 出错: {e.Message}");
            }
        }

        /// <summary>
        /// 发射逆鳞棱彩弹（以玩家为中心向四周发射）
        /// </summary>
        private void FirePrismaticBolts(CharacterMainControl player)
        {
            try
            {
                Vector3 playerPos = player.transform.position;
                int boltCount = config.PrismaticBoltCount;
                float angleStep = 360f / boltCount;
                float scale = config.PrismaticBoltScale;

                ModBehaviour.DevLog($"{config.LogPrefix} 触之必怒！发射 {boltCount} 颗棱彩弹");

                for (int i = 0; i < boltCount; i++)
                {
                    float angle = i * angleStep;
                    Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector3 spawnPos = playerPos + direction * 0.5f + Vector3.up * 1f;

                    // 使用龙王资源管理器创建棱彩弹
                    GameObject bolt = DragonKingAssetManager.InstantiateEffect(
                        DragonKingConfig.PrismaticBoltPrefab,
                        spawnPos,
                        Quaternion.LookRotation(direction)
                    );

                    if (bolt != null)
                    {
                        bolt.transform.localScale = Vector3.one * scale;

                        // 启动追踪协程
                        StartCoroutine(TrackingBolt(bolt, direction));

                        // 播放音效
                        ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_BoltSpawn);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} FirePrismaticBolts 出错: {e.Message}");
            }
        }

        /// <summary>
        /// 棱彩弹追踪协程 - 向外飞行并追踪敌人（与龙王Boss一致）
        /// 性能优化：降低检测频率，缩小搜索范围，缓存组件引用
        /// </summary>
        private IEnumerator TrackingBolt(GameObject bolt, Vector3 initialDirection)
        {
            if (bolt == null) yield break;

            // 缓存配置参数（避免每帧访问属性）
            float lifetime = config.PrismaticBoltLifetime;
            float speed = config.PrismaticBoltSpeed;
            float damage = config.PrismaticBoltDamage;
            float hitRadius = config.PrismaticBoltHitRadius;
            float trackingStrength = config.PrismaticBoltTrackingStrength;
            float trackingDuration = config.PrismaticBoltTrackingDuration;

            // 缓存 LayerMask（只在首次使用时查找）
            if (characterLayerMask == -1)
            {
                characterLayerMask = LayerMask.GetMask("Character");
            }

            float startTime = Time.time;
            Vector3 currentDirection = initialDirection.normalized;
            
            // 性能优化：缓存追踪目标，减少每帧搜索
            Transform cachedTarget = null;
            float lastTargetSearchTime = 0f;
            const float TARGET_SEARCH_INTERVAL = 0.15f; // 每0.15秒搜索一次目标（约6-7帧）
            
            // 性能优化：碰撞检测间隔
            int frameCounter = 0;
            const int COLLISION_CHECK_INTERVAL = 3; // 每3帧检测一次碰撞
            
            // 预分配碰撞数组（避免每帧GC）
            Collider[] hitBuffer = new Collider[8];

            while (Time.time - startTime < lifetime && bolt != null)
            {
                float elapsedTime = Time.time - startTime;
                Vector3 currentPos = bolt.transform.position;
                frameCounter++;
                
                // 在追踪持续时间内进行追踪
                if (elapsedTime < trackingDuration)
                {
                    // 性能优化：降低目标搜索频率
                    if (Time.time - lastTargetSearchTime > TARGET_SEARCH_INTERVAL || cachedTarget == null)
                    {
                        cachedTarget = FindNearestEnemyOptimized(currentPos);
                        lastTargetSearchTime = Time.time;
                    }
                    
                    if (cachedTarget != null)
                    {
                        // 计算朝向敌人的方向（目标点为敌人胸口位置）
                        Vector3 targetPos = cachedTarget.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;
                        Vector3 toTarget = (targetPos - currentPos).normalized;
                        
                        // 使用追踪强度进行方向插值
                        currentDirection = Vector3.Lerp(currentDirection, toTarget, trackingStrength * Time.deltaTime * 5f).normalized;
                        
                        // 更新弹幕朝向
                        if (currentDirection != Vector3.zero)
                        {
                            bolt.transform.rotation = Quaternion.LookRotation(currentDirection);
                        }
                    }
                }
                // 超过追踪持续时间后，保持当前方向直线飞行

                // 移动弹幕
                bolt.transform.position += currentDirection * speed * Time.deltaTime;

                // 性能优化：降低碰撞检测频率（每3帧检测一次）
                if (frameCounter % COLLISION_CHECK_INTERVAL == 0)
                {
                    currentPos = bolt.transform.position;
                    int hitCount = Physics.OverlapSphereNonAlloc(currentPos, hitRadius, hitBuffer, characterLayerMask);

                    for (int i = 0; i < hitCount; i++)
                    {
                        var hit = hitBuffer[i];
                        if (hit == null) continue;
                        
                        // 性能优化：先检查 Health 组件（更常见），再检查是否是玩家
                        Health enemyHealth = hit.GetComponentInParent<Health>();
                        if (enemyHealth == null || enemyHealth.IsMainCharacterHealth) continue;

                        // 对敌人造成伤害
                        DamageInfo dmgInfo = new DamageInfo(CharacterMainControl.Main);
                        dmgInfo.damageValue = damage;
                        dmgInfo.damagePoint = currentPos;
                        dmgInfo.damageNormal = currentDirection;
                        enemyHealth.Hurt(dmgInfo);

                        ModBehaviour.DevLog($"{config.LogPrefix} 棱彩弹命中敌人，伤害: {damage}");

                        // 播放命中音效
                        ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_BoltHit);

                        // 命中后销毁
                        UnityEngine.Object.Destroy(bolt);
                        yield break;
                    }
                }

                yield return null;
            }

            // 生命周期结束，销毁弹幕
            if (bolt != null)
            {
                UnityEngine.Object.Destroy(bolt);
            }
        }
        
        /// <summary>
        /// 查找最近的敌人（用于棱彩弹追踪）- 优化版本
        /// 性能优化：缩小搜索半径，使用 NonAlloc，提前退出
        /// </summary>
        private Transform FindNearestEnemyOptimized(Vector3 position)
        {
            Transform nearest = null;
            float nearestDistanceSqr = float.MaxValue; // 使用平方距离避免开方运算
            
            // 性能优化：缩小搜索半径（30m → 15m），使用 NonAlloc 避免 GC
            const float SEARCH_RADIUS = 15f;
            int hitCount = Physics.OverlapSphereNonAlloc(position, SEARCH_RADIUS, enemySearchBuffer, characterLayerMask);
            
            for (int i = 0; i < hitCount; i++)
            {
                var collider = enemySearchBuffer[i];
                if (collider == null) continue;
                
                // 性能优化：先检查 Health 组件（更快的排除条件）
                Health health = collider.GetComponentInParent<Health>();
                if (health == null || health.IsMainCharacterHealth || health.CurrentHealth <= 0) continue;
                
                // 使用平方距离比较（避免 sqrt 运算）
                float distanceSqr = (collider.transform.position - position).sqrMagnitude;
                if (distanceSqr < nearestDistanceSqr)
                {
                    nearestDistanceSqr = distanceSqr;
                    nearest = collider.transform;
                }
            }
            
            return nearest;
        }
        
        /// <summary>
        /// 查找最近的敌人（保留原方法以兼容其他调用）
        /// </summary>
        private Transform FindNearestEnemy(Vector3 position)
        {
            return FindNearestEnemyOptimized(position);
        }

        /// <summary>
        /// 显示逆鳞气泡提示
        /// 注意：使用 async UniTaskVoid 替代 async void，避免异常丢失问题
        /// </summary>
        private async Cysharp.Threading.Tasks.UniTaskVoid ShowBubbleAsync()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    string bubbleText = L10n.T(
                        ReverseScaleConfig.BUBBLE_TEXT_CN,
                        ReverseScaleConfig.BUBBLE_TEXT_EN
                    );
                    await DialogueBubblesManager.Show(bubbleText, main.transform, 2f, false, false, -1f, 3f);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} ShowBubble 出错: {e.Message}");
            }
        }
        
        /// <summary>
        /// 显示逆鳞气泡提示（同步入口，内部调用异步方法）
        /// </summary>
        private void ShowBubble()
        {
            ShowBubbleAsync().Forget();
        }

        /// <summary>
        /// 销毁逆鳞装备
        /// </summary>
        private void DestroyTotem()
        {
            try
            {
                // 标记效果已触发，允许真正取消事件订阅
                effectTriggered = true;
                
                if (equippedSlot != null && equippedItem != null)
                {
                    // 从槽位卸下物品（使用 Unplug 正确移除）
                    Item unpluggedItem = equippedSlot.Unplug();

                    // 销毁物品 GameObject
                    if (unpluggedItem != null && unpluggedItem.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(unpluggedItem.gameObject);
                    }

                    ModBehaviour.DevLog($"{config.LogPrefix} 图腾已销毁");
                }

                // 取消注册能力（此时 effectTriggered = true，会真正取消事件订阅）
                UnregisterAbility();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} DestroyTotem 出错: {e.Message}");
            }
        }
    }
}
