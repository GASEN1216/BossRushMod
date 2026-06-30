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
                string titleCN = isDragonKing ? "<color=#FFD700>【龙王之庇护】</color>" : "<color=#FFD700>【龙之庇护】</color>";
                string titleEN = isDragonKing ? "<color=#FFD700>[Dragon King's Protection]</color>" : "<color=#FFD700>[Dragon's Protection]</color>";

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
        private Transform cachedTransform = null;

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
            cachedTransform = transform;

            // 缓存点燃Buff
            if (cachedBurnBuff == null)
            {
                try
                {
                    cachedBurnBuff = Duckov.Utilities.GameplayDataSettings.Buffs.Burn;
                }
                catch { }
            }

            // 缓存LayerMask
            if (characterLayerMask == -1)
            {
                characterLayerMask = LayerMask.GetMask("Character");
                if (characterLayerMask == 0)
                {
                    characterLayerMask = ~0;
                }
            }
        }

        void Update()
        {
            float currentTime = Time.time;

            // 检查是否超时
            float elapsed = currentTime - createTime;
            if (elapsed > duration)
            {
                Destroy(gameObject);
                return;
            }

            // 检测并伤害敌人
            if (currentTime - lastDamageTime >= damageInterval)
            {
                DamageEnemiesInRange();
                lastDamageTime = currentTime;
            }
        }

        /// <summary>
        /// 伤害范围内的敌人（不伤害玩家和友方单位）
        /// 使用 Team.IsEnemy 判断敌友关系
        /// </summary>
        private void DamageEnemiesInRange()
        {
            Transform zoneTransform = cachedTransform;
            if (zoneTransform == null)
            {
                zoneTransform = transform;
                cachedTransform = zoneTransform;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(zoneTransform.position, radius, hitBuffer, characterLayerMask);

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
