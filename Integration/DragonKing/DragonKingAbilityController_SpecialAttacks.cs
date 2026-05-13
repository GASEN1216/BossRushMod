using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.Utilities;
using Duckov.UI.DialogueBubbles;
using BossRush.Common.Effects;

namespace BossRush
{
    public partial class DragonKingAbilityController
    {
        /// <summary>
        /// 太阳舞旋转弹幕发射循环
        /// 同时向24个方向（每15°一个）发射子弹，每0.2s整体旋转5°
        /// </summary>
        private IEnumerator SunDanceBarrageLoop()
        {
            // 初始方向：指向玩家
            Vector3 initialDir = Vector3.forward;
            Vector3 playerPos;
            if (bossCharacter != null && TryGetPlayerAimPosition(out playerPos, true))
            {
                initialDir = (playerPos - bossCharacter.transform.position).normalized;
                initialDir.y = 0f;
                if (initialDir.sqrMagnitude < 0.01f) initialDir = Vector3.forward;
                initialDir = initialDir.normalized;
            }

            float currentRotation = 0f;           // 当前整体旋转角度
            // 使用配置文件中的弹幕参数
            float rotationPerTick = DragonKingConfig.SunDanceBarrageRotationPerTick;
            float angleStep = DragonKingConfig.SunDanceBarrageAngleStep;
            int directionCount = DragonKingConfig.SunDanceBarrageDirectionCount;
            int tickCount = 0;

            while (isSunDanceActive && bossCharacter != null)
            {
                // 同时向指定数量方向发射子弹
                for (int i = 0; i < directionCount; i++)
                {
                    // 计算当前方向（基于初始方向 + 整体旋转 + 方向索引偏移）
                    float angle = currentRotation + (i * angleStep);
                    Vector3 fireDir = Quaternion.Euler(0f, angle, 0f) * initialDir;

                    // 发射子弹
                    SpawnSunDanceTrackingBullet(fireDir);
                }

                tickCount++;

                // 整体旋转
                currentRotation += rotationPerTick;
                if (currentRotation >= 360f) currentRotation -= 360f;

                // 等待下一次发射（使用配置的tick间隔）
                yield return wait02s;
            }
        }

        /// <summary>
        /// 发射太阳舞子弹
        /// 使用龙王武器的子弹预制体和原武器子弹速度
        /// 直线飞行，不追踪
        /// </summary>
        private void SpawnSunDanceTrackingBullet(Vector3 direction)
        {
            // 快速路径检查
            if (bossCharacter == null || cachedWeaponBullet == null)
                return;
            if (LevelManager.Instance == null || LevelManager.Instance.BulletPool == null)
                return;

            Projectile bullet = null;

            try
            {
                // 使用原武器的子弹速度乘以配置的倍率（太阳舞弹幕速度降低）
                float bulletSpeed = cachedWeaponBulletSpeed * DragonKingConfig.SunDanceBulletSpeedMultiplier;

                // 计算发射位置（Boss胸口位置，使用锁定位置而非当前位置）
                Vector3 muzzlePos = sunDanceLockPosition + Vector3.up * DragonKingConfig.BossChestHeightOffset;

                // 播放射击音效（内部已做节流）
                PlayWeaponShootSound();

                // 从BulletPool获取子弹
                bullet = LevelManager.Instance.BulletPool.GetABullet(cachedWeaponBullet);
                if (bullet == null)
                    return;

                // 设置子弹位置和方向（不改变大小，保持原预制体大小）
                bullet.transform.position = muzzlePos;
                bullet.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

                // 创建ProjectileContext
                ProjectileContext ctx = default(ProjectileContext);
                ctx.direction = direction.normalized;
                ctx.speed = bulletSpeed;
                ctx.distance = 50f;
                ctx.halfDamageDistance = 25f;
                ctx.damage = DragonKingConfig.SunDanceDamagePerTick;
                ctx.penetrate = 0;
                ctx.critRate = 0f;
                ctx.critDamageFactor = 1.5f;
                ctx.armorPiercing = 0f;
                ctx.armorBreak = 0f;
                ctx.fromCharacter = bossCharacter;
                ctx.team = bossCharacter.Team;
                ctx.element_Fire = 1f; // 火属性子弹
                ctx.firstFrameCheck = false;

                // 不追踪，直线飞行
                ctx.traceTarget = null;
                ctx.traceAbility = 0f;

                // 使用Init方法初始化子弹
                bullet.Init(ctx);

            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 发射太阳舞子弹失败: {e.Message}");
            }

            // Init后会重置hitLayers，必须重新设置穿墙属性
            // 将反射调用移到try-catch外部，减少异常处理开销
            // 使用缓存的反射字段，性能优化（只查找一次字段）
            if (bullet != null && cachedHitLayersField != null)
            {
                try
                {
                    cachedHitLayersField.SetValue(bullet, piercingLayerMask);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog($"[DragonKing] [WARNING] 设置穿墙LayerMask异常: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 播放武器射击音效
        /// </summary>
        private void PlayWeaponShootSound()
        {
            try
            {
                if (bossCharacter == null || string.IsNullOrEmpty(cachedWeaponShootEventName))
                {
                    return;
                }

                if (Time.time - lastWeaponShootSoundTime < 0.05f)
                {
                    return;
                }

                if (!TryPassSharedSoundThrottle(cachedWeaponShootEventName, GLOBAL_WEAPON_SOUND_INTERVAL))
                {
                    return;
                }

                lastWeaponShootSoundTime = Time.time;
                if (CacheAudioPostMethod() && cachedAudioPostDelegate != null)
                {
                    cachedAudioPostDelegate(cachedWeaponShootEventName, bossCharacter.gameObject);
                }
            }
            catch
            {
                // 音效播放失败不影响游戏逻辑
            }
        }

        /// <summary>
        /// 缓存AudioManager.Post方法（只反射一次）
        /// </summary>
        private static bool CacheAudioPostMethod()
        {
            if (cachedAudioPostResolved)
            {
                return cachedAudioPostDelegate != null;
            }

            cachedAudioPostResolved = true;

            try
            {
                var audioManagerType = typeof(LevelManager).Assembly.GetType("Duckov.AudioManager");
                if (audioManagerType != null)
                {
                    var method = audioManagerType.GetMethod("Post",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null,
                        new System.Type[] { typeof(string), typeof(GameObject) },
                        null);
                    if (method != null)
                    {
                        cachedAudioPostDelegate = Delegate.CreateDelegate(
                            typeof(AudioPostDelegate),
                            method,
                            false) as AudioPostDelegate;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 缓存AudioManager.Post异常: {e.Message}");
            }

            return cachedAudioPostDelegate != null;
        }

        /// <summary>
        /// 创建预警圆圈
        /// </summary>
        private GameObject CreateWarningCircle(Vector3 position, float chargeTime)
        {
            GameObject circleObj = RentWarningCircle();
            circleObj.transform.position = position + Vector3.up * 0.05f;
            circleObj.transform.rotation = Quaternion.identity;
            circleObj.transform.localScale = Vector3.one;

            WarningCircleAnimation animation = circleObj.GetComponent<WarningCircleAnimation>();
            if (animation != null)
            {
                animation.ResetAnimation(chargeTime, WARNING_CIRCLE_START_RADIUS, WARNING_CIRCLE_END_RADIUS);
            }

            return circleObj;
        }

        /// <summary>
        /// 给太阳舞光束的 Edge 添加伤害触发器
        /// </summary>
        private void SetupSunBeamDamageTriggers(GameObject beamGroup)
        {
            if (beamGroup == null) return;

            SunBeamTriggerCache cache = beamGroup.GetComponent<SunBeamTriggerCache>();
            if (cache == null)
            {
                cache = beamGroup.AddComponent<SunBeamTriggerCache>();
            }

            int count = cache.Initialize(this);
            ModBehaviour.DevLog($"[DragonKing] 太阳舞伤害触发器已设置，数量: {count}");
        }

        /// <summary>
        /// 太阳舞光束造成伤害（由触发器调用）
        /// </summary>
        public void ApplySunBeamDamage()
        {
            ApplyDamageToPlayer(DragonKingConfig.SunDanceDamagePerTick);
        }


        // ========== 永恒彩虹攻击 ==========

        /// <summary>
        /// 执行永恒彩虹攻击
        /// 生成13颗星环，螺旋扩散后收缩
        /// </summary>
        private IEnumerator ExecuteEverlastingRainbow()
        {
            ModBehaviour.DevLog("[DragonKing] 执行永恒彩虹攻击");

            if (bossCharacter == null) yield break;

            Vector3 centerPos = bossCharacter.transform.position;
            int starCount = DragonKingConfig.RainbowStarCount;
            float maxRadius = DragonKingConfig.RainbowMaxRadius;
            float rotationSpeed = DragonKingConfig.RainbowRotationSpeed;
            float duration = DragonKingConfig.RainbowDuration;

            // 播放永恒彩虹生成音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_RainbowSpawn);

            // 生成星星 - 预分配List容量
            List<GameObject> stars = new List<GameObject>(starCount);
            List<float> starAngles = new List<float>(starCount);

            float angleStep = 360f / starCount;
            for (int i = 0; i < starCount; i++)
            {
                float angle = i * angleStep;
                Vector3 spawnPos = centerPos + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;

                GameObject star = DragonKingAssetManager.AcquireSharedEffect(
                    DragonKingConfig.RainbowStarPrefab,
                    spawnPos,
                    Quaternion.identity
                );

                if (star != null)
                {
                    stars.Add(star);
                    starAngles.Add(angle);
                    TrackManagedProjectile(star);
                }
            }

            // 螺旋运动
            float startTime = Time.time;
            float halfDuration = duration * 0.5f;

            while (Time.time - startTime < duration && bossCharacter != null)
            {
                float elapsed = Time.time - startTime;

                // 实时获取龙王位置作为中心点（跟随龙王移动）
                Vector3 currentCenter = bossCharacter.transform.position;
                Vector3 targetPos;
                bool hasTargetSnapshot = TryGetPlayerSnapshot(out _, out targetPos);

                // 计算当前半径（先扩散后收缩）
                float currentRadius;
                if (elapsed < halfDuration)
                {
                    // 扩散阶段
                    currentRadius = maxRadius * (elapsed / halfDuration);
                }
                else
                {
                    // 收缩阶段
                    currentRadius = maxRadius * (1f - (elapsed - halfDuration) / halfDuration);
                }

                // 更新每颗星星的位置
                for (int i = 0; i < stars.Count; i++)
                {
                    if (stars[i] == null) continue;

                    // 顺时针旋转
                    starAngles[i] += rotationSpeed * Time.deltaTime;

                    float angle = starAngles[i] * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(
                        Mathf.Cos(angle) * currentRadius,
                        1f,
                        Mathf.Sin(angle) * currentRadius
                    );

                    // 使用实时中心点
                    stars[i].transform.position = currentCenter + offset;

                    // 检测伤害
                    if (CheckProjectileHit(stars[i].transform.position, DragonKingConfig.RainbowTrailDamage, hasTargetSnapshot, targetPos))
                    {
                        // 不销毁星星，只造成伤害
                    }
                }

                yield return null;
            }

            // 清理星星
            foreach (var star in stars)
            {
                if (star != null)
                {
                    activeProjectiles.Remove(star);
                    DragonKingAssetManager.ReleaseEffect(star);
                }
            }
        }

        // ========== 以太长矛攻击 ==========

        /// <summary>
        /// 执行以太长矛攻击 - 横向贯穿版本
        /// 从屏幕两边每0.1s画一条横穿玩家脚下的50米长线
        /// 警告1秒后射出长矛，每10条暂停0.5秒，共3波
        /// </summary>
        private IEnumerator ExecuteEtherealLance()
        {
            ModBehaviour.DevLog("[DragonKing] 执行以太长矛攻击（横向贯穿）");

            if (bossCharacter == null || playerCharacter == null) yield break;

            int waves = 3;              // 3波
            int linesPerWave = 10;      // 每波10条线
            float lineLength = 50f;     // 线长50米
            // 注：lineInterval=0.1f对应wait01s, warningTime=1f对应wait1s, wavePause=0.5f对应wait05s

            // 使用缓存的彩虹渐变（避免每次攻击重复创建）
            Gradient rainbowGradient = GetSharedRainbowGradient();

            // 执行3波攻击
            for (int wave = 0; wave < waves; wave++)
            {
                // 预分配List容量
                List<GameObject> warningLines = new List<GameObject>(linesPerWave);
                ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_LanceWarning);

                // 每波创建10条警告线（每0.1秒一条）
                for (int i = 0; i < linesPerWave; i++)
                {
                    // 动态获取玩家当前位置（高度设为身体中间）
                    Vector3 currentPos = playerCharacter.transform.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;

                    // 每条线都在玩家脚下，旋转5°
                    float rotation = i * 5f;

                    // 创建警告线（中心在玩家脚下，带旋转）
                    GameObject line = CreateHorizontalWarningLine(currentPos, lineLength, rainbowGradient, rotation);
                    if (line != null)
                    {
                        warningLines.Add(line);
                        activeWarningLines.Add(line); // 添加到全局清理列表

                        // 播放长矛警告音效（每条线都播放）
                    }

                    yield return wait01s; // lineInterval = 0.1f
                }

                // 等待警告显示（已经过了1秒，等待剩余时间）
                // 注：linesPerWave * lineInterval = 10 * 0.1 = 1.0s，与warningTime相等，无需额外等待

                // 射出所有长矛
                if (warningLines.Count > 0)
                {
                    ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_LanceFire);
                }

                foreach (var line in warningLines)
                {
                    if (line != null)
                    {
                        // 播放长矛发射音效（每条长矛都播放）

                        FireLanceFromWarningLine(line);
                        activeWarningLines.Remove(line); // 从全局清理列表移除
                        ReturnWarningLine(line);
                    }
                }
                warningLines.Clear();

                // 波次间暂停（最后一波不暂停）
                if (wave < waves - 1)
                {
                    yield return wait05s; // wavePause = 0.5f
                }
            }

            ModBehaviour.DevLog("[DragonKing] 以太长矛攻击完成");
        }

        /// <summary>
        ///创建警告线（50米长，可旋转，支持初始透明度）
        /// </summary>
        private GameObject CreateHorizontalWarningLine(Vector3 center, float length, Gradient gradient, float rotationY, float initialAlpha = 1f)
        {
            try
            {
                GameObject lineObj = RentWarningLine();
                lineObj.transform.position = center;

                LineRenderer lr = lineObj.GetComponent<LineRenderer>();

                // 使用共享材质
                Material mat = GetSharedInternalColoredMaterial();
                if (mat != null)
                {
                    lr.sharedMaterial = mat;
                }

                // 设置线条属性
                lr.startWidth = 0.05f;
                lr.endWidth = 0.05f;
                lr.numCornerVertices = 0;
                lr.numCapVertices = 0;
                lr.sortingOrder = 100;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                // 设置彩虹渐变（应用初始透明度）
                if (initialAlpha <= 0f)
                {
                    lr.colorGradient = GetTransparentRainbowGradient();
                }
                else if (initialAlpha < 1f)
                {
                    // 创建带初始透明度的渐变副本
                    Gradient fadedGradient = new Gradient();
                    fadedGradient.SetKeys(
                        gradient.colorKeys,
                        new GradientAlphaKey[] {
                            new GradientAlphaKey(initialAlpha, 0f),
                            new GradientAlphaKey(initialAlpha * 0.95f, 0.15f),
                            new GradientAlphaKey(initialAlpha * 0.8f, 0.35f),
                            new GradientAlphaKey(initialAlpha * 0.6f, 0.55f),
                            new GradientAlphaKey(initialAlpha * 0.4f, 0.75f),
                            new GradientAlphaKey(initialAlpha * 0.3f, 1f)
                        }
                    );
                    lr.colorGradient = fadedGradient;
                }
                else
                {
                    lr.colorGradient = gradient;
                }

                // 计算旋转后的方向
                Vector3 forwardDir = Quaternion.Euler(0, rotationY, 0) * Vector3.forward;
                Vector3 backDir = Quaternion.Euler(0, rotationY, 0) * Vector3.back;

                // 设置线条顶点（50米长，沿旋转后的方向）
                lr.positionCount = 2;
                lr.SetPosition(0, center + backDir * (length / 2f));
                lr.SetPosition(1, center + forwardDir * (length / 2f));

                // 存储旋转角度到lineObj的transform中，供发射长矛时使用
                lineObj.transform.rotation = Quaternion.Euler(0, rotationY, 0);

                return lineObj;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] 创建警告线失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 警告线淡入效果协程
        /// 优化：复用Gradient和GradientAlphaKey数组，减少GC压力
        /// </summary>
        private IEnumerator FadeInWarningLines(List<GameObject> lines, float duration)
        {
            float elapsed = 0f;

            // 预先缓存所有LineRenderer（避免每帧GetComponent）
            var renderers = new List<LineRenderer>(lines.Count);
            foreach (var line in lines)
            {
                if (line != null)
                {
                    var lr = line.GetComponent<LineRenderer>();
                    if (lr != null) renderers.Add(lr);
                }
            }

            // 预分配Gradient和GradientAlphaKey数组（避免每帧创建）
            Gradient fadeGradient = new Gradient();
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[6];
            alphaKeys[0] = new GradientAlphaKey(0f, 0f);
            alphaKeys[1] = new GradientAlphaKey(0f, 0.15f);
            alphaKeys[2] = new GradientAlphaKey(0f, 0.35f);
            alphaKeys[3] = new GradientAlphaKey(0f, 0.55f);
            alphaKeys[4] = new GradientAlphaKey(0f, 0.75f);
            alphaKeys[5] = new GradientAlphaKey(0f, 1f);

            // 缓存颜色键（只需获取一次）
            GradientColorKey[] colorKeys = null;
            if (renderers.Count > 0 && renderers[0] != null)
            {
                colorKeys = renderers[0].colorGradient.colorKeys;
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Clamp01(elapsed / duration);

                // 更新预分配的alpha值（避免每帧创建新数组）
                alphaKeys[0].alpha = alpha;
                alphaKeys[1].alpha = alpha * 0.95f;
                alphaKeys[2].alpha = alpha * 0.8f;
                alphaKeys[3].alpha = alpha * 0.6f;
                alphaKeys[4].alpha = alpha * 0.4f;
                alphaKeys[5].alpha = alpha * 0.3f;

                // 更新所有线条的透明度
                foreach (var lr in renderers)
                {
                    if (lr == null) continue;

                    // 复用Gradient对象，只更新alpha键
                    if (colorKeys == null) colorKeys = lr.colorGradient.colorKeys;
                    fadeGradient.SetKeys(colorKeys, alphaKeys);
                    lr.colorGradient = fadeGradient;
                }

                yield return null;
            }
        }

        /// <summary>
        /// 从警告线位置射出实际长矛（双向发射）
        /// </summary>
        private void FireLanceFromWarningLine(GameObject warningLine)
        {
            FireLanceFromWarningLine(warningLine, true, true);
        }

        /// <summary>
        /// 从警告线位置射出实际长矛（可指定方向）
        /// </summary>
        /// <param name="warningLine">警告线对象</param>
        /// <param name="fireFromFront">是否从前端发射（向后射）</param>
        /// <param name="fireFromBack">是否从后端发射（向前射）</param>
        private void FireLanceFromWarningLine(GameObject warningLine, bool fireFromFront, bool fireFromBack)
        {
            if (warningLine == null) return;

            Vector3 linePos = warningLine.transform.position;
            Quaternion lineRotation = warningLine.transform.rotation;

            // 获取旋转后的前后方向
            Vector3 forwardDir = lineRotation * Vector3.forward;
            Vector3 backDir = lineRotation * Vector3.back;

            // 从指定侧生成长矛射向中间
            float spawnDistance = 30f;  // 从30米外射入
            float lanceSpeed = 40f;     // 长矛速度

            // 后方长矛（向前射）
            if (fireFromBack)
            {
                GameObject backLance = DragonKingAssetManager.AcquireSharedEffect(
                    DragonKingConfig.EtherealLancePrefab,
                    linePos + backDir * spawnDistance,
                    Quaternion.LookRotation(forwardDir)
                );

                if (backLance != null)
                {
                    // 激活 Blade 子物体（使长矛可见）
                    ActivateLanceBlade(backLance);
                    RegisterLanceProjectile(backLance, forwardDir, lanceSpeed, spawnDistance * 2f);
                }
            }

            // 前方长矛（向后射）
            if (fireFromFront)
            {
                GameObject forwardLance = DragonKingAssetManager.AcquireSharedEffect(
                    DragonKingConfig.EtherealLancePrefab,
                    linePos + forwardDir * spawnDistance,
                    Quaternion.LookRotation(backDir)
                );

                if (forwardLance != null)
                {
                    // 激活 Blade 子物体（使长矛可见）
                    ActivateLanceBlade(forwardLance);
                    RegisterLanceProjectile(forwardLance, backDir, lanceSpeed, spawnDistance * 2f);
                }
            }
        }

        /// <summary>
        /// 激活长矛的 Blade 子物体
        /// </summary>
        private void ActivateLanceBlade(GameObject lance)
        {
            if (lance == null) return;

            // 查找 Blade 子物体并激活
            Transform blade = lance.transform.Find("Blade");
            if (blade != null)
            {
                blade.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 检测长矛是否命中玩家（使用射线检测）
        /// </summary>
        private bool CheckLanceHit(GameObject lance)
        {
            CharacterMainControl targetCharacter;
            Vector3 playerPos;
            if (!TryGetPlayerSnapshot(out targetCharacter, out playerPos)) return false;
            return CheckLanceHit(lance, true, targetCharacter, playerPos);
        }

        private bool CheckLanceHit(GameObject lance, bool hasTargetSnapshot, CharacterMainControl targetCharacter, Vector3 playerPos)
        {
            if (!hasTargetSnapshot || lance == null || targetCharacter == null || targetCharacter.Health == null || targetCharacter.Health.IsDead)
            {
                return false;
            }

            const float lanceLength = 2f;
            const float broadPhaseRadius = lanceLength + 1f;
            const float lanceLengthSqr = lanceLength * lanceLength;

            Vector3 lancePos = lance.transform.position;

            // 使用射线检测长矛到玩家的路径
            Vector3 toPlayer = playerPos - lancePos;
            float toPlayerSqr = toPlayer.sqrMagnitude;
            if (toPlayerSqr > broadPhaseRadius * broadPhaseRadius)
            {
                return false;
            }

            // 近距离命中仍保留旧逻辑，避免改变玩家体感
            if (toPlayerSqr < lanceLengthSqr)
            {
                ApplyDamageToPlayer(DragonKingConfig.EtherealLanceDamage);
                return true;
            }

            Bounds targetHitboxBounds;
            if (TryGetSharedTargetHitboxBounds(targetCharacter, out targetHitboxBounds) &&
                targetHitboxBounds.SqrDistance(lancePos) > lanceLengthSqr)
            {
                return false;
            }

            float toPlayerDistance = Mathf.Sqrt(toPlayerSqr);
            if (toPlayerDistance <= 0.001f)
            {
                ApplyDamageToPlayer(DragonKingConfig.EtherealLanceDamage);
                return true;
            }

            Vector3 direction = toPlayer / toPlayerDistance;

            // 长矛长度约2米，检测前方是否有玩家
            RaycastHit hit;

            if (Physics.Raycast(lancePos, direction, out hit, lanceLength))
            {
                // 检测是否击中玩家
                if (hit.collider.gameObject == targetCharacter.gameObject ||
                    hit.collider.transform.IsChildOf(targetCharacter.transform))
                {
                    ApplyDamageToPlayer(DragonKingConfig.EtherealLanceDamage);
                    return true;
                }
            }

            return false;
        }


        // ========== 以太长矛2攻击（切屏） ==========

        /// <summary>
        /// 执行以太长矛2攻击（切屏剑阵）
        /// 4波长矛从不同方向切过屏幕
        /// </summary>
        private IEnumerator ExecuteEtherealLance2()
        {
            ModBehaviour.DevLog("[DragonKing] 执行以太长矛2攻击（同时画线）");

            if (bossCharacter == null || playerCharacter == null) yield break;

            int waveCount = DragonKingConfig.ScreenLanceWaveCount;
            int linesPerWave = 16;        // 每波16条线（同时画出）
            float lineLength = 50f;       // 线长50米
            // 注：warningTime=0.5f对应wait05s, wavePause=0.5f对应wait05s

            // 使用缓存的彩虹渐变（避免每次攻击重复创建）
            Gradient rainbowGradient = GetSharedRainbowGradient();

            // 执行4波攻击
            for (int wave = 0; wave < waveCount; wave++)
            {
                // 预分配List容量
                List<GameObject> warningLines = new List<GameObject>(linesPerWave);

                // 模拟玩家移动轨迹来生成线位置
                Vector3 basePos = playerCharacter.transform.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;
                Vector3 playerForward = playerCharacter.transform.forward;
                playerForward.y = 0;
                playerForward = playerForward.normalized;

                // 同时创建所有警告线（每条旋转5度），初始透明
                for (int i = 0; i < linesPerWave; i++)
                {
                    // 计算每条线的位置（内联计算，无需额外数组）
                    Vector3 linePos;
                    if (i < 8)
                    {
                        // 前8条：从0到2m，均匀分布
                        float t = (i + 1) / 8f;
                        linePos = basePos + playerForward * (t * 2f);
                    }
                    else
                    {
                        // 后8条：从2m缩减到1m
                        float t = (i - 7) / 8f;
                        linePos = basePos + playerForward * (2f - t * 1f);
                    }

                    float rotation = i * 5f;
                    GameObject line = CreateHorizontalWarningLine(linePos, lineLength, rainbowGradient, rotation, 0f);
                    if (line != null)
                    {
                        warningLines.Add(line);
                        activeWarningLines.Add(line); // 添加到全局清理列表
                    }
                }

                // 启动淡入效果（0.3秒淡入）
                float fadeInDuration = 0.3f;
                StartCoroutine(FadeInWarningLines(warningLines, fadeInDuration));

                // 等待警告显示（warningTime = 0.5f）
                yield return wait05s;

                // 射出长矛（前两波从前端射，后两波从后端射）
                bool fireFromFront = (wave < 2);  // 波次0,1从前端射
                bool fireFromBack = (wave >= 2);  // 波次2,3从后端射

                foreach (var line in warningLines)
                {
                    if (line != null)
                    {
                        FireLanceFromWarningLine(line, fireFromFront, fireFromBack);
                        activeWarningLines.Remove(line); // 从全局清理列表移除
                        ReturnWarningLine(line);
                    }
                }
                warningLines.Clear();

                // 波次间暂停（最后一波不暂停，wavePause = 0.5f）
                if (wave < waveCount - 1)
                {
                    yield return wait05s;
                }
            }

            ModBehaviour.DevLog("[DragonKing] 以太长矛2攻击完成");
        }


        // ========== 碰撞伤害处理 ==========

        /// <summary>
        /// 碰撞检测器回调 - 当玩家进入碰撞范围时调用
        /// </summary>
        public void OnCollisionWithPlayer(CharacterMainControl player)
        {
            if (player == null || bossCharacter == null) return;
            if (CurrentPhase == DragonKingPhase.Dead) return;

            // 阶段转换时不触发碰撞伤害
            if (CurrentPhase == DragonKingPhase.Transitioning) return;

            // 碰撞检测器被禁用时不触发
            if (collisionDetector != null && !collisionDetector.enabled) return;

            // 检查冷却时间
            if (Time.time - lastCollisionDamageTime < DragonKingConfig.CollisionCooldown) return;

            lastCollisionDamageTime = Time.time;

            // 播放碰撞音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_Hit);

            // 应用碰撞伤害
            ApplyCollisionDamage(player);

            // 应用击退
            ApplyKnockback(player);

        }

        /// <summary>
        /// 应用碰撞伤害
        /// </summary>
        private void ApplyCollisionDamage(CharacterMainControl player)
        {
            try
            {
                if (player == null) return;

                // 创建伤害信息
                DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                dmgInfo.damageValue = DragonKingConfig.CollisionDamage;
                dmgInfo.damageType = DamageTypes.normal;

                // 计算伤害方向（从Boss指向玩家）
                if (bossCharacter != null)
                {
                    dmgInfo.damageNormal = (player.transform.position - bossCharacter.transform.position).normalized;
                }

                // 使用原版伤害系统
                bool damageApplied = false;

                // 优先使用mainDamageReceiver
                if (player.mainDamageReceiver != null)
                {
                    player.mainDamageReceiver.Hurt(dmgInfo);
                    damageApplied = true;
                }
                // 后备：直接使用Health组件
                else if (player.Health != null)
                {
                    player.Health.Hurt(dmgInfo);
                    damageApplied = true;
                }

                if (damageApplied)
                {
                    // 播放受伤音效
                    PlayHurtSound();
                }
                else
                {
                    ModBehaviour.DevLog("[DragonKing] [WARNING] 无法应用碰撞伤害 - 玩家没有有效的伤害接收器");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 应用碰撞伤害失败: {e.Message}");
            }
        }

        /// <summary>
        /// 应用击退效果
        /// </summary>
        private void ApplyKnockback(CharacterMainControl player)
        {
            try
            {
                if (player == null || bossCharacter == null) return;

                // 计算击退方向（从Boss指向玩家）
                Vector3 knockbackDir = (player.transform.position - bossCharacter.transform.position).normalized;
                knockbackDir.y = 0.3f; // 稍微向上
                knockbackDir = knockbackDir.normalized;

                // 应用击退力
                float force = 8f;
                var rb = player.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.AddForce(knockbackDir * force, ForceMode.Impulse);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 应用击退失败: {e.Message}");
            }
        }

    }
}
