// ============================================================================
// DragonSetBonus.cs - 龙套装效果系统
// ============================================================================
// 模块说明：
//   实现龙头 + 龙甲的套装效果：
//   - 同时穿戴时免疫火焰伤害
//   - 火焰伤害转化为治疗（吸收火焰能量）
//   - 眼睛发出红色光芒特效
//   - 物理伤害减少 20%（通过 Modifier 系统）
//
// 套装组成：
//   - 龙头（Dragon Helm）- 7级头盔，HeadArmor = 7
//   - 龙甲（Dragon Armor）- 7级护甲，BodyArmor = 7
//
// 实现方式：
//   监听 CharacterMainControl.OnMainCharacterSlotContentChangedEvent 事件
//   仅在装备槽变化时检测套装状态，避免轮询浪费资源
//   通过 Health.OnHurt 事件拦截火焰伤害并转化为治疗
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using ItemStatsSystem.Items;
using FX;
using Duckov.Buffs;

namespace BossRush
{
    /// <summary>
    /// 龙套装效果管理器
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 龙套装配置
        
        // 龙套装物品名称（用于匹配）
        private const string DRAGON_HELM_NAME = "龙头";
        private const string DRAGON_ARMOR_NAME = "龙甲";
        
        // 龙王套装物品名称（用于匹配）
        private const string DRAGON_KING_HELM_NAME = "龙王之冕";
        private const string DRAGON_KING_ARMOR_NAME = "龙王鳞铠";
        
        // 装备槽名称
        private const string ARMOR_SLOT_NAME = "Armor";
        private const string HELMET_SLOT_NAME = "Helmat"; // 游戏原版拼写
        
        // 套装状态
        private bool dragonSetActive = false;
        private bool dragonKingSetActive = false; // 标记是否为龙王套装
        private bool dragonSetEventRegistered = false;
        private bool dragonHurtEventRegistered = false;
        
        // 眼睛特效
        private GameObject dragonEyeEffect = null;
        private Light dragonEyeLight1 = null;
        private Light dragonEyeLight2 = null;
        
        // [性能优化] 缓存反射 FieldInfo，避免重复反射调用
        private static FieldInfo cachedSlotChangedEventField = null;
        private static bool slotChangedEventFieldCached = false;
        
        // [性能优化] 缓存头部骨骼名称数组
        private static readonly string[] HEAD_BONE_NAMES = new string[] 
        { 
            "Head", "head", "Bip001 Head", "mixamorig:Head", 
            "Bone_Head", "head_bone", "HeadBone"
        };
        
        // [性能优化] 缓存找到的头部 Transform
        private Transform cachedHeadTransform = null;
        
        #endregion
        
        #region 事件注册
        
        /// <summary>
        /// 注册龙套装事件监听（在 OnEnable 中调用）
        /// </summary>
        private void RegisterDragonSetEvents()
        {
            if (dragonSetEventRegistered) return;
            
            try
            {
                // [性能优化] 使用缓存的 FieldInfo，避免重复反射
                FieldInfo eventField = GetCachedSlotChangedEventField();
                
                if (eventField != null)
                {
                    var currentDelegate = eventField.GetValue(null) as Delegate;
                    var newDelegate = Delegate.Combine(currentDelegate, 
                        new Action<CharacterMainControl, Slot>(OnMainCharacterSlotContentChanged));
                    eventField.SetValue(null, newDelegate);
                    
                    dragonSetEventRegistered = true;
                    DevLog("[DragonSet] 已注册装备槽变化事件");
                }
                else
                {
                    DevLog("[DragonSet] 未找到 OnMainCharacterSlotContentChangedEvent 字段");
                }
                
                // 订阅场景加载事件，在玩家进入存档时检测套装
                LevelManager.OnAfterLevelInitialized += OnLevelInitializedCheckDragonSet;
                DevLog("[DragonSet] 已注册场景加载事件");
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] 注册事件失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// [性能优化] 获取缓存的事件字段 FieldInfo
        /// </summary>
        private static FieldInfo GetCachedSlotChangedEventField()
        {
            if (!slotChangedEventFieldCached)
            {
                cachedSlotChangedEventField = typeof(CharacterMainControl).GetField(
                    "OnMainCharacterSlotContentChangedEvent",
                    BindingFlags.Public | BindingFlags.Static);
                slotChangedEventFieldCached = true;
            }
            return cachedSlotChangedEventField;
        }
        
        /// <summary>
        /// 场景加载完成后检测套装状态（处理已穿戴装备进入游戏的情况）
        /// </summary>
        private void OnLevelInitializedCheckDragonSet()
        {
            try
            {
                // [性能优化] 场景切换时清理缓存的头部 Transform，因为角色可能重建
                cachedHeadTransform = null;
                
                DevLog("[DragonSet] 场景加载完成，检测套装状态...");
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    CheckDragonSetStatus(main);
                }
                else
                {
                    DevLog("[DragonSet] 主角尚未初始化，跳过检测");
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] OnLevelInitializedCheckDragonSet 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 取消注册龙套装事件监听（在 OnDisable 中调用）
        /// </summary>
        private void UnregisterDragonSetEvents()
        {
            if (!dragonSetEventRegistered) return;
            
            try
            {
                // [性能优化] 使用缓存的 FieldInfo
                FieldInfo eventField = GetCachedSlotChangedEventField();
                
                if (eventField != null)
                {
                    var currentDelegate = eventField.GetValue(null) as Delegate;
                    var newDelegate = Delegate.Remove(currentDelegate, 
                        new Action<CharacterMainControl, Slot>(OnMainCharacterSlotContentChanged));
                    eventField.SetValue(null, newDelegate);
                }
                
                // 取消订阅场景加载事件
                LevelManager.OnAfterLevelInitialized -= OnLevelInitializedCheckDragonSet;
                    
                dragonSetEventRegistered = false;
                DevLog("[DragonSet] 已取消注册装备槽变化事件");
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] 取消注册事件失败: " + e.Message);
            }
            
            // 取消注册伤害事件
            UnregisterDragonHurtEvent();
            
            // 清理特效
            DeactivateDragonSetBonus();
            
            // [性能优化] 清理缓存的头部 Transform
            cachedHeadTransform = null;
        }
        
        /// <summary>
        /// 注册伤害事件（用于火焰伤害转治疗）
        /// </summary>
        private void RegisterDragonHurtEvent()
        {
            if (dragonHurtEventRegistered) return;
            
            try
            {
                Health.OnHurt += OnDragonSetHurt;
                dragonHurtEventRegistered = true;
                DevLog("[DragonSet] 已注册伤害事件");
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] 注册伤害事件失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 取消注册伤害事件
        /// </summary>
        private void UnregisterDragonHurtEvent()
        {
            if (!dragonHurtEventRegistered) return;
            
            try
            {
                Health.OnHurt -= OnDragonSetHurt;
                dragonHurtEventRegistered = false;
                DevLog("[DragonSet] 已取消注册伤害事件");
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] 取消注册伤害事件失败: " + e.Message);
            }
        }
        
        #endregion
        
        #region 套装检测
        
        /// <summary>
        /// 装备槽内容变化回调
        /// </summary>
        private void OnMainCharacterSlotContentChanged(CharacterMainControl character, Slot slot)
        {
            if (character == null || slot == null) return;
            
            // 只关心护甲槽和头盔槽的变化
            string slotKey = slot.Key;
            if (slotKey != ARMOR_SLOT_NAME && slotKey != HELMET_SLOT_NAME) return;
            
            // 详细日志：显示槽位内容
            string contentInfo = slot.Content != null ? 
                (slot.Content.DisplayName + " / " + slot.Content.DisplayNameRaw) : "空";
            DevLog("[DragonSet] 装备槽变化: " + slotKey + " -> " + contentInfo);
            
            // 检测套装状态
            CheckDragonSetStatus(character);
        }
        
        /// <summary>
        /// 检测龙套装状态
        /// </summary>
        private void CheckDragonSetStatus(CharacterMainControl character)
        {
            try
            {
                if (character == null || character.CharacterItem == null)
                {
                    DevLog("[DragonSet] 角色或角色物品为空，跳过检测");
                    if (dragonSetActive)
                    {
                        DeactivateDragonSetBonus();
                    }
                    return;
                }
                
                // 检测装备状态
                bool hasDragonHelm = false;
                bool hasDragonArmor = false;
                bool hasDragonKingHelm = false;
                bool hasDragonKingArmor = false;
                
                // 获取头盔
                Item helmetItem = character.GetHelmatItem();
                if (helmetItem != null)
                {
                    string helmNameRaw = helmetItem.DisplayNameRaw;
                    string helmName = helmetItem.DisplayName;
                    DevLog("[DragonSet] 当前头盔: " + (helmName ?? "null"));
                    
                    // 检测标准龙头
                    if ((!string.IsNullOrEmpty(helmNameRaw) && helmNameRaw.Contains(DRAGON_HELM_NAME)) ||
                        (!string.IsNullOrEmpty(helmName) && helmName.Contains(DRAGON_HELM_NAME)))
                    {
                        hasDragonHelm = true;
                        DevLog("[DragonSet] ✓ 匹配龙头!");
                    }
                    // 检测龙王之冕
                    else if ((!string.IsNullOrEmpty(helmNameRaw) && helmNameRaw.Contains(DRAGON_KING_HELM_NAME)) ||
                             (!string.IsNullOrEmpty(helmName) && helmName.Contains(DRAGON_KING_HELM_NAME)))
                    {
                        hasDragonKingHelm = true;
                        DevLog("[DragonSet] ✓ 匹配龙王之冕!");
                    }
                }
                
                // 获取护甲
                Item armorItem = character.GetArmorItem();
                if (armorItem != null)
                {
                    string armorNameRaw = armorItem.DisplayNameRaw;
                    string armorName = armorItem.DisplayName;
                    DevLog("[DragonSet] 当前护甲: " + (armorName ?? "null"));
                    
                    // 检测标准龙甲
                    if ((!string.IsNullOrEmpty(armorNameRaw) && armorNameRaw.Contains(DRAGON_ARMOR_NAME)) ||
                        (!string.IsNullOrEmpty(armorName) && armorName.Contains(DRAGON_ARMOR_NAME)))
                    {
                        hasDragonArmor = true;
                        DevLog("[DragonSet] ✓ 匹配龙甲!");
                    }
                    // 检测龙王鳞铠
                    else if ((!string.IsNullOrEmpty(armorNameRaw) && armorNameRaw.Contains(DRAGON_KING_ARMOR_NAME)) ||
                             (!string.IsNullOrEmpty(armorName) && armorName.Contains(DRAGON_KING_ARMOR_NAME)))
                    {
                        hasDragonKingArmor = true;
                        DevLog("[DragonSet] ✓ 匹配龙王鳞铠!");
                    }
                }
                
                // 判断套装是否激活
                bool hasFullDragonSet = hasDragonHelm && hasDragonArmor;
                bool hasFullDragonKingSet = hasDragonKingHelm && hasDragonKingArmor;
                bool shouldBeActive = hasFullDragonSet || hasFullDragonKingSet;
                
                if (shouldBeActive && !dragonSetActive)
                {
                    ActivateDragonSetBonus(character, hasFullDragonKingSet);
                }
                else if (!shouldBeActive && dragonSetActive)
                {
                    DeactivateDragonSetBonus();
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] CheckDragonSetStatus 出错: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 激活龙套装效果
        /// </summary>
        private void ActivateDragonSetBonus(CharacterMainControl character, bool isDragonKing = false)
        {
            try
            {
                dragonSetActive = true;
                dragonKingSetActive = isDragonKing;
                DevLog(isDragonKing ? "[DragonSet] 龙王套装效果激活！" : "[DragonSet] 龙套装效果激活！");
                
                // 龙王套装作为龙裔套装的上位，同样触发火焰转治疗
                // 注册伤害事件（用于火焰伤害转治疗）
                RegisterDragonHurtEvent();
                
                // 创建眼睛红光特效
                CreateDragonEyeEffect(character);
                
                // 启动呼吸灯协程
                StartDragonEyeEffectCoroutine();
                
                // 显示提示（物理减伤已通过装备属性实现，会显示在装备详情中）
                string titleCN = isDragonKing ? "<color=#FFD700>【龙王之庇护】</color>" : "<color=#FF4500>【龙之庇护】</color>";
                string titleEN = isDragonKing ? "<color=#FFD700>[Dragon King's Protection]</color>" : "<color=#FF4500>[Dragon's Protection]</color>";
                
                ShowMessage(L10n.T(
                    titleCN + " 套装效果激活！\n火焰伤害转化为治疗",
                    titleEN + " Set bonus activated!\nFire damage heals you"
                ));
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] ActivateDragonSetBonus 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 停用龙套装效果
        /// </summary>
        private void DeactivateDragonSetBonus()
        {
            if (!dragonSetActive) return;
            
            try
            {
                dragonSetActive = false;
                dragonKingSetActive = false;
                DevLog("[DragonSet] 龙套装效果停用");
                
                // 停止呼吸灯协程
                StopDragonEyeEffectCoroutine();
                
                // 取消注册伤害事件
                UnregisterDragonHurtEvent();
                
                // 销毁眼睛特效
                DestroyDragonEyeEffect();
                
                // 清理冲刺残影
                ClearAfterimages();
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] DeactivateDragonSetBonus 出错: " + e.Message);
            }
        }
        
        #endregion
        
        #region 火焰伤害转治疗
        
        /// <summary>
        /// 伤害事件回调 - 处理火焰伤害转治疗
        /// </summary>
        private void OnDragonSetHurt(Health health, DamageInfo damageInfo)
        {
            try
            {
                // 只处理主角的伤害（龙王套装和龙裔套装都触发火焰转治疗）
                if (!dragonSetActive || health == null || !health.IsMainCharacterHealth) return;
                
                // 检查是否有火焰伤害
                if (damageInfo.elementFactors == null || damageInfo.elementFactors.Count == 0) return;
                
                // 使用 finalDamage（减免后的实际伤害）来计算治疗量
                float totalFinalDamage = damageInfo.finalDamage;
                if (totalFinalDamage <= 0f) return;
                
                // 计算火焰伤害占比
                float fireFactor = 0f;
                float totalFactor = 0f;
                for (int i = 0; i < damageInfo.elementFactors.Count; i++)
                {
                    var ef = damageInfo.elementFactors[i];
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
                
                // 计算火焰伤害在最终伤害中的占比
                float fireRatio = fireFactor / totalFactor;
                float actualFireDamage = totalFinalDamage * fireRatio;
                float fireHealAmount = actualFireDamage * 0.8f; // 80% 转化为治疗
                
                // 将火焰伤害因子设为 0（免疫火焰伤害）
                for (int i = 0; i < damageInfo.elementFactors.Count; i++)
                {
                    var ef = damageInfo.elementFactors[i];
                    if (ef.elementType == ElementTypes.fire && ef.factor > 0f)
                    {
                        damageInfo.elementFactors[i] = new ElementFactor(ElementTypes.fire, 0f);
                    }
                }
                
                DevLog("[DragonSet] 玩家火焰伤害吸收: " + actualFireDamage.ToString("F1") + " -> 治疗: " + fireHealAmount.ToString("F1"));
                
                // 如果有火焰治疗，延迟添加生命值
                if (fireHealAmount > 0f)
                {
                    StartCoroutine(DelayedHeal(health, fireHealAmount));
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] OnDragonSetHurt 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 延迟治疗（在伤害计算完成后）
        /// </summary>
        private System.Collections.IEnumerator DelayedHeal(Health health, float amount)
        {
            yield return null; // 等待一帧
            
            if (health != null && !health.IsDead)
            {
                health.AddHealth(amount);
                DevLog("[DragonSet] 火焰能量治疗: +" + amount);
                
                // 显示治疗数字（使用 FX.PopText）
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        FX.PopText.Pop("+" + amount.ToString("F0"), main.transform.position + Vector3.up * 2f, 
                            new Color(0.2f, 1f, 0.2f), 1.2f, null);
                    }
                }
                catch { }
            }
        }
        
        #endregion
        
        #region 眼睛特效
        
        // 呼吸灯协程引用
        private Coroutine dragonEyeEffectCoroutine = null;
        
        /// <summary>
        /// 启动龙眼呼吸灯协程
        /// </summary>
        private void StartDragonEyeEffectCoroutine()
        {
            StopDragonEyeEffectCoroutine();
            dragonEyeEffectCoroutine = StartCoroutine(DragonEyeEffectLoop());
        }
        
        /// <summary>
        /// 停止龙眼呼吸灯协程
        /// </summary>
        private void StopDragonEyeEffectCoroutine()
        {
            if (dragonEyeEffectCoroutine != null)
            {
                StopCoroutine(dragonEyeEffectCoroutine);
                dragonEyeEffectCoroutine = null;
            }
        }
        
        /// <summary>
        /// 龙眼呼吸灯循环协程
        /// </summary>
        private System.Collections.IEnumerator DragonEyeEffectLoop()
        {
            while (dragonSetActive && dragonEyeLight1 != null && dragonEyeLight2 != null)
            {
                // 呼吸灯效果：光强在 5 ~ 15 之间波动
                float pulse = 10f + Mathf.Sin(Time.time * 2f) * 5f;
                dragonEyeLight1.intensity = pulse;
                dragonEyeLight2.intensity = pulse;
                yield return null;
            }
            dragonEyeEffectCoroutine = null;
        }
        
        /// <summary>
        /// 创建龙眼红光特效
        /// </summary>
        private void CreateDragonEyeEffect(CharacterMainControl character)
        {
            try
            {
                // 销毁旧特效
                DestroyDragonEyeEffect();
                
                // [性能优化] 优先使用缓存的头部 Transform
                Transform headTransform = cachedHeadTransform;
                if (headTransform == null)
                {
                    headTransform = FindHeadTransform(character);
                    cachedHeadTransform = headTransform; // 缓存找到的结果
                }
                
                if (headTransform == null)
                {
                    DevLog("[DragonSet] 未找到头部 Transform，使用角色位置");
                    headTransform = character.transform;
                }
                
                // 创建特效容器 - 眼睛位置
                dragonEyeEffect = new GameObject("DragonEyeEffect");
                dragonEyeEffect.transform.SetParent(headTransform, false);
                dragonEyeEffect.transform.localPosition = new Vector3(0f, 0.15f, 0.2f);
                
                // 创建左眼光源 - Point Light
                GameObject leftEye = new GameObject("LeftEyeLight");
                leftEye.transform.SetParent(dragonEyeEffect.transform, false);
                leftEye.transform.localPosition = new Vector3(-0.08f, 0f, 0f);
                dragonEyeLight1 = leftEye.AddComponent<Light>();
                ConfigureDragonEyeLight(dragonEyeLight1);
                
                // 创建右眼光源 - Point Light
                GameObject rightEye = new GameObject("RightEyeLight");
                rightEye.transform.SetParent(dragonEyeEffect.transform, false);
                rightEye.transform.localPosition = new Vector3(0.08f, 0f, 0f);
                dragonEyeLight2 = rightEye.AddComponent<Light>();
                ConfigureDragonEyeLight(dragonEyeLight2);
                
                DevLog("[DragonSet] 龙眼特效已创建，挂载到: " + headTransform.name);
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] CreateDragonEyeEffect 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 配置龙眼光源（Point Light）
        /// </summary>
        private void ConfigureDragonEyeLight(Light light)
        {
            light.type = LightType.Point;          // 点光源
            light.color = new Color(1f, 0.1f, 0.05f); // 深红色
            light.intensity = 0.5f;                // 微弱亮度
            light.range = 0.25f;                   // 小范围
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }
        
        /// <summary>
        /// 查找角色头部 Transform
        /// </summary>
        private Transform FindHeadTransform(CharacterMainControl character)
        {
            try
            {
                // [性能优化] 使用静态缓存的骨骼名称数组
                foreach (string boneName in HEAD_BONE_NAMES)
                {
                    Transform head = character.transform.Find(boneName);
                    if (head != null) return head;
                    
                    // 递归查找
                    head = FindChildRecursive(character.transform, boneName);
                    if (head != null) return head;
                }
                
                // 尝试通过 CharacterModel 获取
                var modelField = typeof(CharacterMainControl).GetField("characterModel", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (modelField != null)
                {
                    var model = modelField.GetValue(character);
                    if (model != null)
                    {
                        var headSocketField = model.GetType().GetField("headSocket", 
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        
                        if (headSocketField != null)
                        {
                            return headSocketField.GetValue(model) as Transform;
                        }
                    }
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 递归查找子物体
        /// </summary>
        private Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Contains(name)) return child;
                
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
        
        /// <summary>
        /// 销毁龙眼特效
        /// </summary>
        private void DestroyDragonEyeEffect()
        {
            if (dragonEyeEffect != null)
            {
                UnityEngine.Object.Destroy(dragonEyeEffect);
                dragonEyeEffect = null;
                dragonEyeLight1 = null;
                dragonEyeLight2 = null;
                DevLog("[DragonSet] 龙眼特效已销毁");
            }
        }
        
        #endregion
        
        #region 龙影冲刺
        
        // 冲刺配置 - 龙套装
        private const float DASH_DISTANCE = 3f;           // 龙套装冲刺距离
        private const float DASH_DURATION = 0.1f;         // 冲刺持续时间
        private const float DASH_COOLDOWN = 1.5f;         // 龙套装冲刺冷却时间
        private const float DOUBLE_TAP_THRESHOLD = 0.15f;  // 双击判定时间阈值（0.15秒）
        private const int AFTERIMAGE_COUNT = 3;           // 残影数量
        private const float INPUT_THRESHOLD = 0.5f;       // 输入阈值，判定方向键是否按下
        
        // 冲刺配置 - 龙王套装
        private const float DRAGON_KING_DASH_DISTANCE_FIRST = 6f;     // 龙王套装第一次冲刺距离（6米）
        private const float DRAGON_KING_DASH_DISTANCE_SECOND = 3f;    // 龙王套装第二次冲刺距离（龙套装1倍）
        private const float DRAGON_KING_DASH_DURATION = 0.15f;        // 龙王套装冲刺持续时间（稍长，配合更远距离）
        private const float DRAGON_KING_CHAIN_WINDOW = 0.15f;         // 龙王套装连续冲刺窗口期（与双击间隔一致）
        private const float DRAGON_KING_DASH_COOLDOWN = 0.5f;          // 龙王套装冲刺冷却时间（0.5秒）
        
        // [性能优化] 缓存 LayerMask，避免每次冲刺都调用字符串查找
        private static readonly int DASH_OBSTACLE_LAYER_MASK = LayerMask.GetMask("Default", "Wall", "Obstacle");
        
        // 冲刺状态
        private bool isDragonDashing = false;
        private float lastDashTime = -999f;
        
        // 龙王套装连续冲刺状态
        private bool isInChainDashWindow = false;         // 是否处于连续冲刺窗口期
        private float chainDashWindowEndTime = 0f;        // 连续冲刺窗口结束时间
        private bool hasUsedChainDash = false;            // 是否已使用连续冲刺
        
        // 双击检测 - 基于移动输入轴（兼容自定义按键）
        // 四个方向：前(+Y)、后(-Y)、左(-X)、右(+X)
        private float lastForwardPressTime = -999f;   // 前
        private float lastBackPressTime = -999f;      // 后
        private float lastLeftPressTime = -999f;      // 左
        private float lastRightPressTime = -999f;     // 右
        
        // 上一帧的输入状态（用于检测按下瞬间）
        private bool wasForwardPressed = false;
        private bool wasBackPressed = false;
        private bool wasLeftPressed = false;
        private bool wasRightPressed = false;
        
        // 记录触发冲刺的方向键
        private Vector3 lastDoubleTapDirection = Vector3.zero;
        
        // 残影列表
        private System.Collections.Generic.List<GameObject> afterimages = new System.Collections.Generic.List<GameObject>();
        
        /// <summary>
        /// 龙影冲刺 Update 检测（在主 Update 中调用）
        /// </summary>
        private void UpdateDragonDash()
        {
            // 龙套装或龙王套装激活时都检测冲刺
            if (!dragonSetActive) return;
            
            // 检查配置是否启用冲刺（龙套装需要配置，龙王套装始终启用）
            if (!dragonKingSetActive && (config == null || !config.enableDragonDash)) return;
            
            // 冷却中不检测（但龙王套装连续冲刺窗口期内可以触发）
            // 龙王套装使用独立的冷却时间
            float currentCooldown = dragonKingSetActive ? DRAGON_KING_DASH_COOLDOWN : DASH_COOLDOWN;
            if (!isInChainDashWindow && Time.time - lastDashTime < currentCooldown) return;
            
            // 冲刺中不检测新输入
            if (isDragonDashing) return;
            
            // 龙王套装：检测连续冲刺窗口期
            if (dragonKingSetActive && isInChainDashWindow)
            {
                // 窗口期已过
                if (Time.time > chainDashWindowEndTime)
                {
                    isInChainDashWindow = false;
                    hasUsedChainDash = false;
                }
                else
                {
                    // 窗口期内检测单次方向键按下
                    CheckChainDashInput();
                    return;
                }
            }
            
            // 检测双击
            CheckDoubleTapDash();
        }
        
        /// <summary>
        /// 检测龙王套装连续冲刺输入（窗口期内单次按下方向键）
        /// </summary>
        private void CheckChainDashInput()
        {
            if (hasUsedChainDash) return;
            
            // 获取当前移动输入
            InputManager inputManager = LevelManager.Instance?.InputManager;
            if (inputManager == null) return;
            
            Vector2 moveInput = inputManager.MoveAxisInput;
            
            // 检测当前帧各方向是否按下（超过阈值）
            bool isForwardPressed = moveInput.y > INPUT_THRESHOLD;
            bool isBackPressed = moveInput.y < -INPUT_THRESHOLD;
            bool isRightPressed = moveInput.x > INPUT_THRESHOLD;
            bool isLeftPressed = moveInput.x < -INPUT_THRESHOLD;
            
            // 检测按下瞬间（从未按下变为按下）
            Vector3 dashDir = Vector3.zero;
            
            if (isForwardPressed && !wasForwardPressed)
            {
                dashDir = Vector3.forward;
            }
            else if (isBackPressed && !wasBackPressed)
            {
                dashDir = Vector3.back;
            }
            else if (isLeftPressed && !wasLeftPressed)
            {
                dashDir = Vector3.left;
            }
            else if (isRightPressed && !wasRightPressed)
            {
                dashDir = Vector3.right;
            }
            
            // 更新上一帧状态
            wasForwardPressed = isForwardPressed;
            wasBackPressed = isBackPressed;
            wasLeftPressed = isLeftPressed;
            wasRightPressed = isRightPressed;
            
            // 触发连续冲刺
            if (dashDir != Vector3.zero)
            {
                hasUsedChainDash = true;
                isInChainDashWindow = false;
                lastDoubleTapDirection = dashDir;
                TriggerDragonKingChainDash();
            }
        }
        
        /// <summary>
        /// 检测双击方向键触发冲刺（兼容自定义按键）
        /// 通过监听 InputManager.MoveAxisInput 来检测移动输入，而非硬编码 WASD
        /// </summary>
        private void CheckDoubleTapDash()
        {
            // 获取当前移动输入
            InputManager inputManager = LevelManager.Instance?.InputManager;
            if (inputManager == null) return;
            
            Vector2 moveInput = inputManager.MoveAxisInput;
            float currentTime = Time.time;
            
            // 检测当前帧各方向是否按下（超过阈值）
            bool isForwardPressed = moveInput.y > INPUT_THRESHOLD;
            bool isBackPressed = moveInput.y < -INPUT_THRESHOLD;
            bool isRightPressed = moveInput.x > INPUT_THRESHOLD;
            bool isLeftPressed = moveInput.x < -INPUT_THRESHOLD;
            
            // 前 - 检测按下瞬间（从未按下变为按下）
            if (isForwardPressed && !wasForwardPressed)
            {
                if (currentTime - lastForwardPressTime < DOUBLE_TAP_THRESHOLD)
                {
                    lastDoubleTapDirection = Vector3.forward;
                    TriggerDragonDash();
                    lastForwardPressTime = -999f;
                }
                else
                {
                    lastForwardPressTime = currentTime;
                }
            }
            
            // 后 - 检测按下瞬间
            if (isBackPressed && !wasBackPressed)
            {
                if (currentTime - lastBackPressTime < DOUBLE_TAP_THRESHOLD)
                {
                    lastDoubleTapDirection = Vector3.back;
                    TriggerDragonDash();
                    lastBackPressTime = -999f;
                }
                else
                {
                    lastBackPressTime = currentTime;
                }
            }
            
            // 左 - 检测按下瞬间
            if (isLeftPressed && !wasLeftPressed)
            {
                if (currentTime - lastLeftPressTime < DOUBLE_TAP_THRESHOLD)
                {
                    lastDoubleTapDirection = Vector3.left;
                    TriggerDragonDash();
                    lastLeftPressTime = -999f;
                }
                else
                {
                    lastLeftPressTime = currentTime;
                }
            }
            
            // 右 - 检测按下瞬间
            if (isRightPressed && !wasRightPressed)
            {
                if (currentTime - lastRightPressTime < DOUBLE_TAP_THRESHOLD)
                {
                    lastDoubleTapDirection = Vector3.right;
                    TriggerDragonDash();
                    lastRightPressTime = -999f;
                }
                else
                {
                    lastRightPressTime = currentTime;
                }
            }
            
            // 更新上一帧状态
            wasForwardPressed = isForwardPressed;
            wasBackPressed = isBackPressed;
            wasLeftPressed = isLeftPressed;
            wasRightPressed = isRightPressed;
        }
        
        /// <summary>
        /// 触发龙影冲刺
        /// </summary>
        private void TriggerDragonDash()
        {
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null) return;
            
            // 获取相机朝向，将方向键方向转换为世界坐标方向
            Vector3 dashDirection = GetCameraRelativeDirection(lastDoubleTapDirection);
            if (dashDirection == Vector3.zero) return;
            
            // 龙王套装使用不同的冲刺逻辑
            if (dragonKingSetActive)
            {
                DevLog("[DragonKingSet] 龙王冲刺触发！方向: " + dashDirection);
                lastDashTime = Time.time;
                StartCoroutine(DragonKingDashCoroutine(main, dashDirection, DRAGON_KING_DASH_DISTANCE_FIRST, true));
            }
            else
            {
                DevLog("[DragonSet] 龙影冲刺触发！方向: " + dashDirection);
                lastDashTime = Time.time;
                StartCoroutine(DragonDashCoroutine(main, dashDirection));
            }
        }
        
        /// <summary>
        /// 触发龙王套装连续冲刺（第二次冲刺）
        /// </summary>
        private void TriggerDragonKingChainDash()
        {
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null) return;
            
            // 获取相机朝向，将方向键方向转换为世界坐标方向
            Vector3 dashDirection = GetCameraRelativeDirection(lastDoubleTapDirection);
            if (dashDirection == Vector3.zero) return;
            
            DevLog("[DragonKingSet] 龙王连续冲刺触发！方向: " + dashDirection);
            lastDashTime = Time.time;
            StartCoroutine(DragonKingDashCoroutine(main, dashDirection, DRAGON_KING_DASH_DISTANCE_SECOND, false));
        }
        
        /// <summary>
        /// 将方向键方向转换为相机相对的世界方向
        /// </summary>
        private Vector3 GetCameraRelativeDirection(Vector3 inputDirection)
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null) return inputDirection;
                
                // 获取相机的前方和右方（忽略Y轴）
                Vector3 camForward = cam.transform.forward;
                camForward.y = 0;
                camForward.Normalize();
                
                Vector3 camRight = cam.transform.right;
                camRight.y = 0;
                camRight.Normalize();
                
                // 将输入方向转换为世界方向
                Vector3 worldDirection = camForward * inputDirection.z + camRight * inputDirection.x;
                return worldDirection.normalized;
            }
            catch
            {
                return inputDirection;
            }
        }
        
        /// <summary>
        /// 龙影冲刺协程 - 使用原版 SetForceMoveVelocity 逻辑
        /// </summary>
        private System.Collections.IEnumerator DragonDashCoroutine(CharacterMainControl main, Vector3 direction)
        {
            isDragonDashing = true;
            
            Vector3 startPos = main.transform.position;
            
            // 清理旧残影
            ClearAfterimages();
            
            // 计算冲刺速度（距离/时间）
            float dashSpeed = DASH_DISTANCE / DASH_DURATION;
            
            float elapsed = 0f;
            int afterimageIndex = 0;
            float afterimageInterval = DASH_DURATION / AFTERIMAGE_COUNT;
            float nextAfterimageTime = 0f;
            
            // 使用原版的 SetForceMoveVelocity 方式移动，让物理系统处理碰撞
            while (elapsed < DASH_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / DASH_DURATION);
                
                // 使用缓动曲线计算当前速度倍率（ease-out）
                float speedMultiplier = 1f - Mathf.Pow(t, 2f); // 开始快，结束慢
                speedMultiplier = Mathf.Max(0.3f, speedMultiplier); // 最低保持 30% 速度
                
                // 设置强制移动速度，让物理系统处理碰撞
                main.SetForceMoveVelocity(direction * dashSpeed * speedMultiplier);
                
                // 生成残影（在当前位置）
                if (elapsed >= nextAfterimageTime && afterimageIndex < AFTERIMAGE_COUNT)
                {
                    CreateAfterimageAtPosition(main, main.transform.position);
                    afterimageIndex++;
                    nextAfterimageTime += afterimageInterval;
                }
                
                yield return null;
            }
            
            // 冲刺结束，恢复正常移动
            main.SetForceMoveVelocity(Vector3.zero);
            
            isDragonDashing = false;
            
            // 延迟清理残影
            StartCoroutine(ClearAfterimagesDelayed(0.5f));
        }
        
        /// <summary>
        /// 龙王套装冲刺协程 - 带熔浆效果
        /// </summary>
        /// <param name="main">玩家角色</param>
        /// <param name="direction">冲刺方向</param>
        /// <param name="distance">冲刺距离</param>
        /// <param name="enableChainWindow">是否开启连续冲刺窗口</param>
        private System.Collections.IEnumerator DragonKingDashCoroutine(CharacterMainControl main, Vector3 direction, float distance, bool enableChainWindow)
        {
            isDragonDashing = true;
            
            Vector3 startPos = main.transform.position;
            
            // 清理旧残影
            ClearAfterimages();
            
            // 计算冲刺速度（距离/时间）
            float dashSpeed = distance / DRAGON_KING_DASH_DURATION;
            
            float elapsed = 0f;
            int afterimageIndex = 0;
            float afterimageInterval = DRAGON_KING_DASH_DURATION / AFTERIMAGE_COUNT;
            float nextAfterimageTime = 0f;
            
            // 熔浆生成间隔
            float lavaInterval = 0.05f;
            float nextLavaTime = 0f;
            
            // 播放冲刺音效
            PlaySoundEffect(DragonKingConfig.Sound_DashBurst);
            
            // 使用原版的 SetForceMoveVelocity 方式移动，让物理系统处理碰撞
            while (elapsed < DRAGON_KING_DASH_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / DRAGON_KING_DASH_DURATION);
                
                // 使用缓动曲线计算当前速度倍率（ease-out）
                float speedMultiplier = 1f - Mathf.Pow(t, 2f); // 开始快，结束慢
                speedMultiplier = Mathf.Max(0.3f, speedMultiplier); // 最低保持 30% 速度
                
                // 设置强制移动速度，让物理系统处理碰撞
                main.SetForceMoveVelocity(direction * dashSpeed * speedMultiplier);
                
                // 生成残影（在当前位置）- 使用龙王特效颜色
                if (elapsed >= nextAfterimageTime && afterimageIndex < AFTERIMAGE_COUNT)
                {
                    CreateDragonKingAfterimageAtPosition(main, main.transform.position);
                    afterimageIndex++;
                    nextAfterimageTime += afterimageInterval;
                }
                
                // 生成熔浆区域
                if (elapsed >= nextLavaTime)
                {
                    CreateLavaZone(main.transform.position);
                    nextLavaTime += lavaInterval;
                }
                
                yield return null;
            }
            
            // 冲刺结束，恢复正常移动
            main.SetForceMoveVelocity(Vector3.zero);
            
            isDragonDashing = false;
            
            // 如果是第一次冲刺，开启连续冲刺窗口
            if (enableChainWindow)
            {
                isInChainDashWindow = true;
                chainDashWindowEndTime = Time.time + DRAGON_KING_CHAIN_WINDOW;
                hasUsedChainDash = false;
                DevLog("[DragonKingSet] 连续冲刺窗口开启，持续 " + DRAGON_KING_CHAIN_WINDOW + " 秒");
            }
            
            // 延迟清理残影
            StartCoroutine(ClearAfterimagesDelayed(0.5f));
        }
        
        /// <summary>
        /// 创建熔浆区域（玩家版本 - 不伤害玩家和友方单位，只伤害敌人）
        /// 使用龙王Boss的DashTrailPrefab预制体作为视觉特效
        /// </summary>
        private void CreateLavaZone(Vector3 position)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                
                // 使用龙王Boss的冲刺轨迹预制体作为视觉特效
                var effect = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.DashTrailPrefab,
                    position,
                    main != null ? main.transform.rotation : Quaternion.identity
                );
                
                if (effect != null)
                {
                    // 添加玩家版熔浆区域组件（不伤害玩家和友方单位）
                    PlayerLavaZone lavaComponent = effect.AddComponent<PlayerLavaZone>();
                    lavaComponent.Initialize(
                        DragonKingConfig.LavaDamage,
                        DragonKingConfig.LavaDamageInterval,
                        DragonKingConfig.LavaDuration,
                        DragonKingConfig.LavaRadius
                    );
                    
                    // 特效持续时间与熔浆区域一致
                    UnityEngine.Object.Destroy(effect, DragonKingConfig.LavaDuration);
                }
                else
                {
                    // 如果预制体加载失败，使用简单的备用方案
                    GameObject lavaZone = new GameObject("PlayerLavaZone");
                    lavaZone.transform.position = position;
                    
                    PlayerLavaZone lavaComponent = lavaZone.AddComponent<PlayerLavaZone>();
                    lavaComponent.Initialize(
                        DragonKingConfig.LavaDamage,
                        DragonKingConfig.LavaDamageInterval,
                        DragonKingConfig.LavaDuration,
                        DragonKingConfig.LavaRadius
                    );
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonKingSet] CreateLavaZone 异常: " + e.Message);
            }
        }
        
        /// <summary>
        /// 创建龙王套装残影（金色/橙色）
        /// </summary>
        private void CreateDragonKingAfterimageAtPosition(CharacterMainControl main, Vector3 position)
        {
            try
            {
                // 创建简单的残影精灵
                GameObject afterimage = new GameObject("DragonKingAfterimage");
                afterimage.transform.position = position + Vector3.up * 1f; // 角色中心高度
                afterimage.transform.rotation = main.transform.rotation;
                
                // 添加精灵渲染器
                SpriteRenderer sr = afterimage.AddComponent<SpriteRenderer>();
                
                // 使用内置的白色圆形精灵
                sr.sprite = CreateSimpleCircleSprite();
                sr.color = new Color(1f, 0.6f, 0.1f, 0.7f); // 金橙色半透明（龙王特色）
                sr.sortingOrder = 100;
                
                // 设置适中的尺寸
                afterimage.transform.localScale = new Vector3(2f, 3f, 1f);
                
                // 添加光源增强视觉效果
                Light light = afterimage.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.5f, 0.1f); // 金橙色
                light.intensity = 3f;
                light.range = 2f;
                light.shadows = LightShadows.None;
                
                afterimages.Add(afterimage);
                
                // 启动淡出协程
                StartCoroutine(FadeOutAfterimageLight(afterimage, sr, light, 0.5f));
            }
            catch (Exception e)
            {
                DevLog("[DragonKingSet] CreateDragonKingAfterimageAtPosition 异常: " + e.Message);
            }
        }
        
        // [性能优化] 缓存残影材质，避免重复创建
        private static Material cachedAfterimageSpriteMaterial = null;
        
        /// <summary>
        /// 在指定位置创建残影 - 轻量级粒子效果版本
        /// [性能优化] 不再复制完整角色模型，改用简单的精灵残影
        /// </summary>
        private void CreateAfterimageAtPosition(CharacterMainControl main, Vector3 position)
        {
            try
            {
                // 创建简单的残影精灵
                GameObject afterimage = new GameObject("DragonAfterimage_Light");
                afterimage.transform.position = position + Vector3.up * 1f; // 角色中心高度
                afterimage.transform.rotation = main.transform.rotation;
                
                // 添加精灵渲染器
                SpriteRenderer sr = afterimage.AddComponent<SpriteRenderer>();
                
                // 使用内置的白色圆形精灵（Unity默认）
                sr.sprite = CreateSimpleCircleSprite();
                sr.color = new Color(1f, 0.3f, 0.1f, 0.6f); // 橙红色半透明
                sr.sortingOrder = 100;
                
                // 设置适中的尺寸
                afterimage.transform.localScale = new Vector3(1.8f, 2.8f, 1f);
                
                // 添加光源增强视觉效果
                Light light = afterimage.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.3f, 0.1f);
                light.intensity = 2f;
                light.range = 1.5f;
                light.shadows = LightShadows.None;
                
                afterimages.Add(afterimage);
                
                // 启动淡出协程（延长持续时间）
                StartCoroutine(FadeOutAfterimageLight(afterimage, sr, light, 0.5f));
            }
            catch (Exception e)
            {
                DevLog("[DragonSet] CreateAfterimageAtPosition 异常: " + e.Message);
            }
        }
        
        // [性能优化] 缓存生成的精灵
        private static Sprite cachedCircleSprite = null;
        
        /// <summary>
        /// 创建简单的圆形精灵（用于残影效果）
        /// [性能优化] 使用缓存避免重复创建
        /// </summary>
        private Sprite CreateSimpleCircleSprite()
        {
            if (cachedCircleSprite != null) return cachedCircleSprite;
            
            // 创建一个更大更清晰的渐变圆形纹理
            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            
            float center = size / 2f;
            float maxDist = center;
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(1f - (dist / maxDist));
                    // 更锐利的边缘，中心更亮
                    alpha = Mathf.Pow(alpha, 0.7f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            
            tex.SetPixels(pixels);
            tex.Apply();
            
            cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return cachedCircleSprite;
        }
        
        /// <summary>
        /// 轻量级残影淡出效果
        /// [性能优化] 只修改单个 SpriteRenderer 的颜色，无材质实例化
        /// </summary>
        private System.Collections.IEnumerator FadeOutAfterimageLight(GameObject afterimage, SpriteRenderer sr, Light light, float duration)
        {
            if (afterimage == null || sr == null) yield break;
            
            Color startColor = sr.color;
            float startIntensity = light != null ? light.intensity : 0f;
            Vector3 startScale = afterimage.transform.localScale;
            float elapsed = 0f;
            
            while (elapsed < duration && afterimage != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                // 使用缓动函数让淡出更自然
                float easedT = t * t; // ease-in
                
                // 透明度衰减
                float alpha = Mathf.Lerp(startColor.a, 0f, easedT);
                sr.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
                
                // 光源强度衰减
                if (light != null)
                {
                    light.intensity = Mathf.Lerp(startIntensity, 0f, easedT);
                }
                
                // 轻微放大效果（扩散感）
                float scale = Mathf.Lerp(1f, 1.3f, easedT);
                afterimage.transform.localScale = new Vector3(startScale.x * scale, startScale.y * scale, 1f);
                
                yield return null;
            }
            
            // 淡出完成后销毁
            if (afterimage != null)
            {
                afterimages.Remove(afterimage);
                UnityEngine.Object.Destroy(afterimage);
            }
        }
        
        /// <summary>
        /// 延迟清理残影
        /// </summary>
        private System.Collections.IEnumerator ClearAfterimagesDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            ClearAfterimages();
        }
        
        /// <summary>
        /// 清理所有残影
        /// </summary>
        private void ClearAfterimages()
        {
            foreach (var ai in afterimages)
            {
                if (ai != null)
                {
                    UnityEngine.Object.Destroy(ai);
                }
            }
            afterimages.Clear();
        }
        
        #endregion
    }
    
    /// <summary>
    /// 玩家版熔浆区域组件 - 只伤害敌人，不伤害玩家和友方单位
    /// 使用 Team.IsEnemy 判断敌友关系
    /// </summary>
    public class PlayerLavaZone : MonoBehaviour
    {
        // ========== 配置参数 ==========
        private float damage = 5f;
        private float damageInterval = 0.5f;
        private float duration = 3f;
        private float radius = 1f;
        
        // ========== 状态 ==========
        private float createTime = 0f;
        private float lastDamageTime = 0f;
        
        // ========== 缓存 ==========
        private static Duckov.Buffs.Buff cachedBurnBuff = null;
        private static int characterLayerMask = -1;
        private Collider[] hitBuffer = new Collider[16];
        
        /// <summary>
        /// 初始化熔浆区域
        /// </summary>
        public void Initialize(float dmg, float interval, float dur, float rad)
        {
            damage = dmg;
            damageInterval = interval;
            duration = dur;
            radius = rad;
            createTime = Time.time;
            
            // 缓存点燃Buff
            if (cachedBurnBuff == null)
            {
                cachedBurnBuff = Duckov.Utilities.GameplayDataSettings.Buffs.Burn;
            }
            
            // 缓存LayerMask
            if (characterLayerMask == -1)
            {
                characterLayerMask = LayerMask.GetMask("Character");
            }
        }
        
        void Update()
        {
            // 检查是否超时
            float elapsed = Time.time - createTime;
            if (elapsed > duration)
            {
                Destroy(gameObject);
                return;
            }
            
            // 检测并伤害敌人
            if (Time.time - lastDamageTime >= damageInterval)
            {
                DamageEnemiesInRange();
                lastDamageTime = Time.time;
            }
        }
        
        /// <summary>
        /// 伤害范围内的敌人（不伤害玩家和友方单位）
        /// 使用 Team.IsEnemy 判断敌友关系
        /// </summary>
        private void DamageEnemiesInRange()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, radius, hitBuffer, characterLayerMask);
            
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = hitBuffer[i];
                if (col == null) continue;
                
                // 获取角色组件
                CharacterMainControl character = col.GetComponentInParent<CharacterMainControl>();
                if (character == null) continue;
                
                // 获取Health组件
                Health health = character.Health;
                if (health == null) continue;
                
                // 跳过已死亡的
                if (health.IsDead) continue;
                
                // 使用 Team.IsEnemy 判断敌友关系
                // 玩家阵营是 Teams.player，宠物和雇佣兵也是 Teams.player
                // 只对敌人造成伤害
                Teams targetTeam = character.Team;
                if (!Team.IsEnemy(Teams.player, targetTeam))
                {
                    // 不是敌人，跳过（包括玩家、宠物、雇佣兵等友方单位）
                    continue;
                }
                
                // 造成伤害
                ApplyLavaDamageToEnemy(health);
            }
        }
        
        /// <summary>
        /// 对敌人造成熔浆伤害
        /// </summary>
        private void ApplyLavaDamageToEnemy(Health enemyHealth)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                
                // 创建伤害信息（来源为玩家）
                DamageInfo damageInfo = new DamageInfo(player);
                damageInfo.damageValue = damage;
                damageInfo.damageType = DamageTypes.normal;
                damageInfo.damagePoint = enemyHealth.transform.position;
                damageInfo.AddElementFactor(ElementTypes.fire, 1f);
                
                // 造成伤害
                enemyHealth.Hurt(damageInfo);
                
                // 施加点燃Buff
                if (cachedBurnBuff != null)
                {
                    CharacterMainControl enemy = enemyHealth.TryGetCharacter();
                    if (enemy != null)
                    {
                        enemy.AddBuff(cachedBurnBuff, player, 0);
                    }
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[PlayerLavaZone] 伤害敌人失败: " + e.Message);
            }
        }
    }
}
