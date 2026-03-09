// ============================================================================
// FenHuangHalberdAction.cs - 焚皇断界戟右键技能「龙皇裂地」
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityAction，实现右键按下后的龙皇裂地技能
//   - 前摇 0.3s
//   - 沿前方直线逐步生成火柱（复用 DashTrailPrefab + PlayerLavaZone）
//   - 检查龙焰印记触发爆燃
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 焚皇断界戟右键技能 —— 龙皇裂地
    /// </summary>
    public class FenHuangHalberdAction : EquipmentAbilityAction
    {
        // ========== 配置引用 ==========
        private static FenHuangHalberdConfig configInstance;

        /// <summary>
        /// 设置配置实例（由 Manager 在初始化时调用）
        /// </summary>
        public static void SetConfig(FenHuangHalberdConfig cfg)
        {
            configInstance = cfg;
        }

        protected override EquipmentAbilityConfig GetConfig()
        {
            if (configInstance == null)
            {
                configInstance = new FenHuangHalberdConfig();
            }
            return configInstance;
        }

        // ========== 状态变量 ==========

        /// <summary>
        /// 释放方向（在前摇开始时锁定）
        /// </summary>
        private Vector3 fissureDirection;

        /// <summary>
        /// 释放起点
        /// </summary>
        private Vector3 fissureOrigin;

        /// <summary>
        /// 已生成的火柱数量
        /// </summary>
        private int spawnedPillarCount;

        /// <summary>
        /// 下一个火柱的生成时间
        /// </summary>
        private float nextPillarTime;

        /// <summary>
        /// 是否已经过了前摇
        /// </summary>
        private bool castPhaseComplete;

        /// <summary>
        /// 已被爆燃处理的目标（防重复）
        /// </summary>
        private HashSet<int> detonatedTargets = new HashSet<int>();

        /// <summary>
        /// 碰撞检测缓冲区
        /// </summary>
        private static readonly Collider[] hitBuffer = new Collider[16];

        // ========== 能力生命周期 ==========

        protected override bool IsReadyInternal()
        {
            // 检查当前手持物是否为焚皇断界戟
            if (characterController == null) return false;

            ItemAgent_MeleeWeapon melee = characterController.GetMeleeWeapon();
            if (melee == null) return false;
            if (melee.Item == null) return false;

            return melee.Item.TypeID == FenHuangHalberdIds.WeaponTypeId;
        }

        protected override bool OnAbilityStart()
        {
            // 锁定方向和起点
            fissureDirection = characterController.CurrentAimDirection;
            fissureDirection.y = 0f;
            if (fissureDirection.sqrMagnitude < 0.01f)
            {
                fissureDirection = characterController.modelRoot.forward;
            }
            fissureDirection.Normalize();

            fissureOrigin = characterController.transform.position;

            // 重置状态
            spawnedPillarCount = 0;
            nextPillarTime = FenHuangHalberdConfig.FissureCastTime;
            castPhaseComplete = false;
            detonatedTargets.Clear();

            LogIfVerbose("龙皇裂地 - 开始释放！");
            return true;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {
            // 前摇阶段：等待
            if (!castPhaseComplete)
            {
                if (actionElapsedTime >= FenHuangHalberdConfig.FissureCastTime)
                {
                    castPhaseComplete = true;
                    LogIfVerbose("龙皇裂地 - 前摇结束，开始生成火柱");
                }
                return;
            }

            // 火柱生成阶段
            if (spawnedPillarCount < FenHuangHalberdConfig.FirePillarCount
                && actionElapsedTime >= nextPillarTime)
            {
                SpawnFirePillar(spawnedPillarCount);
                spawnedPillarCount++;
                nextPillarTime += FenHuangHalberdConfig.FirePillarInterval;
            }

            // 技能结束检查
            if (actionElapsedTime >= FenHuangHalberdConfig.TotalActionDuration)
            {
                StopAction();
            }
        }

        protected override void OnAbilityStop()
        {
            detonatedTargets.Clear();
            LogIfVerbose("龙皇裂地 - 技能结束");
        }

        /// <summary>
        /// 不自动消耗持续体力
        /// </summary>
        protected override bool ShouldAutoConsumeStamina()
        {
            return false;
        }

        /// <summary>
        /// 技能期间不能使用武器
        /// </summary>
        protected override bool CanUseHandWhileActive()
        {
            return false;
        }

        // ========== 火柱生成 ==========

        /// <summary>
        /// 在裂隙路径上生成第 index 个火柱
        /// </summary>
        private void SpawnFirePillar(int index)
        {
            try
            {
                // 计算火柱位置：从角色前方开始，沿方向等距分布
                float spacing = FenHuangHalberdConfig.FissureLength / FenHuangHalberdConfig.FirePillarCount;
                float distance = spacing * (index + 1);
                Vector3 pillarPos = fissureOrigin + fissureDirection * distance;

                // 复用龙皇套装的 DashTrailPrefab 作为视觉特效
                GameObject pillarObj = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.DashTrailPrefab,
                    pillarPos,
                    Quaternion.identity
                );

                // 如果预制体不可用，创建简易空对象作为载体
                if (pillarObj == null)
                {
                    pillarObj = new GameObject("FenHuang_FirePillar");
                    pillarObj.transform.position = pillarPos;
                }

                // 统一添加 PlayerLavaZone（不伤害玩家和友方）
                PlayerLavaZone lavaComponent = pillarObj.AddComponent<PlayerLavaZone>();
                lavaComponent.Initialize(
                    FenHuangHalberdConfig.FirePillarDamage,
                    FenHuangHalberdConfig.FirePillarDamageInterval,
                    FenHuangHalberdConfig.FirePillarDuration,
                    FenHuangHalberdConfig.FirePillarRadius
                );

                UnityEngine.Object.Destroy(pillarObj, FenHuangHalberdConfig.FirePillarDuration);

                // 检查火柱位置附近是否有带龙焰印记的敌人 → 触发爆燃
                CheckDetonation(pillarPos);
            }
            catch (Exception e)
            {
                LogIfVerbose("火柱生成异常: " + e.Message);
            }
        }

        // ========== 爆燃检测 ==========

        /// <summary>
        /// 检查指定位置附近是否有带龙焰印记的敌人，触发爆燃
        /// </summary>
        private void CheckDetonation(Vector3 position)
        {
            try
            {
                int hitCount = Physics.OverlapSphereNonAlloc(
                    position,
                    FenHuangHalberdConfig.FirePillarRadius + 0.5f,
                    hitBuffer,
                    Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask
                );

                for (int i = 0; i < hitCount; i++)
                {
                    Collider col = hitBuffer[i];
                    if (col == null) continue;

                    DamageReceiver receiver = col.GetComponent<DamageReceiver>();
                    if (receiver == null) continue;

                    // 跳过友方
                    if (!Team.IsEnemy(Teams.player, receiver.Team)) continue;

                    // 跳过已爆燃的目标
                    int targetId = receiver.GetInstanceID();
                    if (detonatedTargets.Contains(targetId)) continue;

                    // 检查是否有龙焰印记
                    int markCount = DragonFlameMarkTracker.GetMarkCount(receiver);
                    if (markCount <= 0) continue;

                    // 触发爆燃！
                    TriggerDetonation(receiver, markCount);
                    detonatedTargets.Add(targetId);
                }
            }
            catch (Exception e)
            {
                LogIfVerbose("爆燃检测异常: " + e.Message);
            }
        }

        /// <summary>
        /// 触发龙焰印记爆燃
        /// </summary>
        private void TriggerDetonation(DamageReceiver target, int markCount)
        {
            try
            {
                // 消耗所有印记
                int consumed = DragonFlameMarkTracker.ConsumeMark(target);
                if (consumed <= 0) return;

                // 计算爆燃伤害
                float detonationDamage = consumed * FenHuangHalberdConfig.DetonationDamagePerMark;

                // 构造伤害
                CharacterMainControl player = CharacterMainControl.Main;
                DamageInfo damageInfo = new DamageInfo(player);
                damageInfo.damageValue = detonationDamage;
                damageInfo.damageType = DamageTypes.normal;
                damageInfo.damagePoint = target.transform.position;
                damageInfo.damageNormal = Vector3.up;
                damageInfo.AddElementFactor(ElementTypes.fire, 1f);
                damageInfo.fromWeaponItemID = FenHuangHalberdIds.WeaponTypeId;

                target.Hurt(damageInfo);

                // 创建爆燃视觉效果（纯代码生成的橙红光球爆炸）
                CreateDetonationEffect(target.transform.position);

                LogIfVerbose($"爆燃！消耗 {consumed} 层印记，造成 {detonationDamage} 伤害");
            }
            catch (Exception e)
            {
                LogIfVerbose("爆燃执行异常: " + e.Message);
            }
        }

        // ========== 爆燃特效（代码生成） ==========

        /// <summary>
        /// 创建简单的爆燃视觉效果：橙红色光球爆炸 + 快速淡出
        /// </summary>
        private static void CreateDetonationEffect(Vector3 position)
        {
            try
            {
                // 创建根物体
                GameObject fx = new GameObject("FenHuang_Detonation");
                fx.transform.position = position + Vector3.up * 0.8f;

                // 添加点光源（模拟爆炸闪光）
                Light light = fx.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.4f, 0.05f); // 橙红色
                light.intensity = 8f;
                light.range = 4f;
                light.shadows = LightShadows.None;

                // 添加简单的橙红色精灵
                SpriteRenderer sr = fx.AddComponent<SpriteRenderer>();
                sr.sprite = CreateExplosionSprite();
                sr.color = new Color(1f, 0.35f, 0.05f, 0.9f);
                sr.sortingOrder = 200;
                fx.transform.localScale = new Vector3(2.5f, 2.5f, 1f);

                // 启动淡出
                var fader = fx.AddComponent<DetonationFader>();
                fader.Initialize(FenHuangHalberdConfig.DetonationEffectDuration, light, sr);
            }
            catch
            {
                // 静默失败
            }
        }

        // 缓存爆炸精灵
        private static Sprite cachedExplosionSprite;

        /// <summary>
        /// 创建发光圆形精灵（缓存）
        /// </summary>
        private static Sprite CreateExplosionSprite()
        {
            if (cachedExplosionSprite != null) return cachedExplosionSprite;

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
                    alpha = Mathf.Pow(alpha, 0.5f); // 柔和边缘
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            cachedExplosionSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return cachedExplosionSprite;
        }
    }

    /// <summary>
    /// 爆燃淡出组件 - 挂在爆燃特效 GameObject 上，控制淡出和销毁
    /// </summary>
    public class DetonationFader : MonoBehaviour
    {
        private float duration;
        private Light pointLight;
        private SpriteRenderer spriteRenderer;
        private float elapsed;
        private float startIntensity;
        private Color startColor;
        private Vector3 startScale;

        public void Initialize(float dur, Light light, SpriteRenderer sr)
        {
            duration = dur;
            pointLight = light;
            spriteRenderer = sr;
            elapsed = 0f;
            startIntensity = light != null ? light.intensity : 0f;
            startColor = sr != null ? sr.color : Color.white;
            startScale = transform.localScale;
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 快速扩大 + 淡出
            float scale = Mathf.Lerp(1f, 2f, t);
            transform.localScale = new Vector3(startScale.x * scale, startScale.y * scale, 1f);

            float alpha = Mathf.Lerp(startColor.a, 0f, t * t);
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            }

            if (pointLight != null)
            {
                pointLight.intensity = Mathf.Lerp(startIntensity, 0f, t);
            }

            if (elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }
    }
}
