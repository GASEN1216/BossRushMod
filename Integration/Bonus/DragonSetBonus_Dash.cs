// ============================================================================
// DragonSetBonus_Dash.cs - dragon set dash and afterimage effects
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
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
}
