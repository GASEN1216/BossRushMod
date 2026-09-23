// ============================================================================
// NPCBubbleAnimator.cs - NPC头顶气泡动画组件
// ============================================================================
// 模块说明：
//   在NPC头顶显示序列帧动画（如心裂开效果）
//   使用 SpriteRenderer 实现，自动面向相机
//
//   2026-09-23 审美审查 UD-46：旧版一出现就是满尺寸、不透明，到时间 Destroy 一帧消失，悬在头顶一动不动，
//   看起来像贴图 bug。现在：
//   - 入场 0.18 秒：缩放 0.6→1（EaseOut，弹出来）、alpha 0→1（SmoothStep）；
//   - 存在期间 1.2 秒内 EaseOut 上浮 0.25 米；
//   - 离场：最后 0.3 秒 SmoothStep 淡到 0 再销毁（displayDuration≤0 的「播完即毁」也补 0.3 秒淡出）；
//   - 计时改 unscaled：官方对话期间 timeScale 可能是 0，旧版的心会冻在头顶。暂停菜单开着时停推进。
//   - 同一目标、同一套序列帧 0.3 秒内重复创建时复用已有的那个（送礼的正向反应和升级庆祝会同帧各冒一颗心）。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// NPC头顶气泡动画组件
    /// 支持序列帧动画播放
    /// </summary>
    public class NPCBubbleAnimator : MonoBehaviour
    {
        // ============================================================================
        // 配置参数
        // ============================================================================
        
        /// <summary>头顶偏移高度</summary>
        public float heightOffset = 2.5f;
        
        /// <summary>动画帧率（每秒帧数）</summary>
        public float frameRate = 8f;
        
        /// <summary>Sprite缩放</summary>
        public float spriteScale = 0.1f;
        
        /// <summary>是否循环播放</summary>
        public bool loop = false;
        
        /// <summary>播放完成后自动销毁</summary>
        public bool destroyOnComplete = true;
        
        /// <summary>显示持续时间（秒），0表示播放完动画后立即结束</summary>
        public float displayDuration = 2.5f;
        
        // ============================================================================
        // 内部状态
        // ============================================================================
        
        private SpriteRenderer spriteRenderer;
        private Sprite[] frames;
        private int currentFrame = 0;
        private float frameTimer = 0f;
        private bool isPlaying = false;
        private Transform targetTransform;
        private float displayTimer = 0f;
        private bool animationCompleted = false;
        private float cachedFrameInterval;
        private Transform cachedTransform;
        private static Camera cachedBillboardCamera;
        private static Transform cachedBillboardCameraTransform;
        private static int cachedBillboardCameraFrame = -1;

        // 出场 / 离场表现（UD-46）
        private const float EnterSeconds = 0.18f;
        private const float EnterScale = 0.6f;
        private const float ExitSeconds = 0.3f;
        private const float RiseMeters = 0.25f;
        private const float RiseSeconds = 1.2f;
        /// <summary>同一目标、同一套帧在这么短的间隔内再次创建时复用已有气泡。</summary>
        private const float DuplicateWindowSeconds = 0.3f;

        /// <summary>存活中的气泡（只用于去重；OnDestroy 时移除，不持有已销毁对象）。</summary>
        private static readonly List<NPCBubbleAnimator> activeBubbles = new List<NPCBubbleAnimator>();

        private float age;
        private float currentRise;
        private float lastAppliedAlpha = -1f;
        private float lastAppliedScale = -1f;
        private Color baseColor = Color.white;
        
        // ============================================================================
        // 静态工厂方法
        // ============================================================================
        
        /// <summary>
        /// 在指定Transform头顶创建气泡动画
        /// </summary>
        /// <param name="target">跟随的目标Transform</param>
        /// <param name="frames">序列帧Sprite数组</param>
        /// <param name="heightOffset">头顶偏移高度</param>
        /// <param name="duration">显示持续时间</param>
        /// <param name="loop">是否循环</param>
        /// <returns>创建的动画组件</returns>
        public static NPCBubbleAnimator Create(
            Transform target, 
            Sprite[] frames, 
            float heightOffset = 2.5f,
            float duration = 2.5f,
            bool loop = false)
        {
            if (target == null || frames == null || frames.Length == 0)
            {
                ModBehaviour.DevLog("[NPCBubbleAnimator] 创建失败：参数无效");
                return null;
            }

            NPCBubbleAnimator recent = FindRecentDuplicate(target, frames);
            if (recent != null)
            {
                return recent;
            }

            // 创建GameObject
            GameObject bubbleObj = new GameObject("NPCBubble_Animation");
            
            // 添加组件
            NPCBubbleAnimator animator = bubbleObj.AddComponent<NPCBubbleAnimator>();
            animator.targetTransform = target;
            animator.frames = frames;
            animator.heightOffset = heightOffset;
            animator.displayDuration = duration;
            animator.loop = loop;
            
            // 初始化并开始播放
            animator.Initialize();
            animator.Play();
            
            ModBehaviour.DevLog("[NPCBubbleAnimator] 创建成功，帧数: " + frames.Length);
            return animator;
        }
        
        /// <summary>
        /// 从AssetBundle加载序列帧并创建动画
        /// </summary>
        public static NPCBubbleAnimator CreateFromBundle(
            Transform target,
            string bundleName,
            string[] spriteNames,
            float heightOffset = 2.5f,
            float duration = 2.5f,
            bool loop = false)
        {
            try
            {
                // 加载AssetBundle
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                string bundlePath = Path.Combine(modDir, "Assets", "ui", bundleName);
                
                if (!File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[NPCBubbleAnimator] AssetBundle不存在: " + bundlePath);
                    return null;
                }
                
                AssetBundle bundle = ResourceBundleLoader.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[NPCBubbleAnimator] 加载AssetBundle失败");
                    return null;
                }
                
                // 加载所有Sprite
                List<Sprite> spriteList = new List<Sprite>();
                foreach (string spriteName in spriteNames)
                {
                    Sprite sprite = bundle.LoadAsset<Sprite>(spriteName);
                    if (sprite != null)
                    {
                        spriteList.Add(sprite);
                    }
                }
                
                bundle.Unload(false);
                
                if (spriteList.Count == 0)
                {
                    ModBehaviour.DevLog("[NPCBubbleAnimator] 未加载到任何Sprite");
                    return null;
                }
                
                return Create(target, spriteList.ToArray(), heightOffset, duration, loop);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCBubbleAnimator] 从Bundle创建失败: " + e.Message);
                return null;
            }
        }
        
        // ============================================================================
        // 生命周期
        // ============================================================================

        private void Awake()
        {
            cachedTransform = transform;
            activeBubbles.Add(this);
        }

        private void OnDestroy()
        {
            activeBubbles.Remove(this);
        }

        private void Initialize()
        {
            // 创建SpriteRenderer
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sortingOrder = 100;  // 确保在前面显示
            baseColor = spriteRenderer.color;

            // 设置初始帧
            if (frames != null && frames.Length > 0)
            {
                spriteRenderer.sprite = frames[0];
            }

            // 设置缩放与透明度：从入场首帧开始（小一圈、全透明），不在第一帧就满尺寸弹出来
            UpdatePresentation();

            // 更新位置
            UpdatePosition();
        }

        void Update()
        {
            // 更新位置（跟随目标）
            UpdatePosition();

            // 面向相机（Billboard效果）
            FaceCamera();

            // 暂停菜单开着时停推进（下面全走 unscaled 时间）
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            age += deltaTime;

            // 更新动画帧
            if (isPlaying)
            {
                UpdateAnimation(deltaTime);
            }

            // 更新显示计时器
            if (animationCompleted || loop)
            {
                displayTimer += deltaTime;
                if (HasTimedEnd() && displayTimer >= GetEndSeconds())
                {
                    if (destroyOnComplete)
                    {
                        Destroy(gameObject);
                        return;
                    }
                    Stop();
                }
            }

            UpdatePresentation();
        }

        /// <summary>
        /// 会不会按时结束：设了显示时长，或者「播完即毁」（displayDuration≤0 且不循环，补一段淡出再毁）。
        /// </summary>
        private bool HasTimedEnd()
        {
            return displayDuration > 0f || (!loop && destroyOnComplete);
        }

        private float GetEndSeconds()
        {
            return displayDuration > 0f ? displayDuration : ExitSeconds;
        }

        /// <summary>
        /// 入场缩放 / 淡入、上浮与离场淡出。只有值变化时才写 SpriteRenderer 与缩放。
        /// </summary>
        private void UpdatePresentation()
        {
            float enter = Mathf.Clamp01(age / EnterSeconds);
            float alpha = BossRushUI.SmoothStep(enter);
            float scale = spriteScale * Mathf.Lerp(EnterScale, 1f, BossRushUI.EaseOut(enter));

            if (destroyOnComplete && HasTimedEnd() && (animationCompleted || loop))
            {
                float exitStart = GetEndSeconds() - ExitSeconds;
                float exit = Mathf.Clamp01((displayTimer - exitStart) / ExitSeconds);
                alpha *= 1f - BossRushUI.SmoothStep(exit);
            }

            currentRise = RiseMeters * BossRushUI.EaseOut(age / RiseSeconds);

            if (scale != lastAppliedScale)
            {
                lastAppliedScale = scale;
                GetCachedTransform().localScale = Vector3.one * scale;
            }
            if (alpha != lastAppliedAlpha)
            {
                lastAppliedAlpha = alpha;
                ApplyColor(alpha);
            }
        }

        private void ApplyColor(float presentationAlpha)
        {
            if (spriteRenderer == null)
            {
                return;
            }
            Color c = baseColor;
            c.a = baseColor.a * presentationAlpha;
            spriteRenderer.color = c;
        }

        /// <summary>同一目标、同一套帧、刚创建不久的气泡（送礼反应与升级庆祝同帧各要一颗心时只留一颗）。</summary>
        private static NPCBubbleAnimator FindRecentDuplicate(Transform target, Sprite[] frames)
        {
            for (int i = activeBubbles.Count - 1; i >= 0; i--)
            {
                NPCBubbleAnimator bubble = activeBubbles[i];
                if (bubble == null)
                {
                    activeBubbles.RemoveAt(i);
                    continue;
                }
                if (bubble.targetTransform == target && bubble.frames == frames && bubble.age < DuplicateWindowSeconds)
                {
                    return bubble;
                }
            }
            return null;
        }

        private void UpdatePosition()
        {
            if (targetTransform != null)
            {
                GetCachedTransform().position = targetTransform.position + Vector3.up * (heightOffset + currentRise);
            }
        }
        
        private void FaceCamera()
        {
            Camera mainCamera = GetBillboardCamera();
            Transform cameraTransform = cachedBillboardCameraTransform;
            if (mainCamera == null)
            {
                return;
            }

            if (cameraTransform != null)
            {
                // Billboard效果：始终面向相机
                GetCachedTransform().rotation = cameraTransform.rotation;
            }
        }

        private static Camera GetBillboardCamera()
        {
            if (cachedBillboardCameraFrame != Time.frameCount)
            {
                cachedBillboardCamera = Camera.main;
                cachedBillboardCameraTransform = cachedBillboardCamera != null ? cachedBillboardCamera.transform : null;
                cachedBillboardCameraFrame = Time.frameCount;
            }

            return cachedBillboardCamera;
        }

        private Transform GetCachedTransform()
        {
            if (cachedTransform == null)
            {
                cachedTransform = transform;
            }

            return cachedTransform;
        }
        
        private void UpdateAnimation(float deltaTime)
        {
            if (frames == null || frames.Length == 0) return;

            frameTimer += deltaTime;

            if (frameTimer >= cachedFrameInterval)
            {
                frameTimer -= cachedFrameInterval;
                currentFrame++;

                if (currentFrame >= frames.Length)
                {
                    if (loop)
                    {
                        currentFrame = 0;
                    }
                    else
                    {
                        // 没有设置显示持续时间时，不再当帧销毁：由 Update 的计时器补 0.3 秒淡出后再毁
                        currentFrame = frames.Length - 1;
                        animationCompleted = true;
                        isPlaying = false;
                    }
                }

                spriteRenderer.sprite = frames[currentFrame];
            }
        }
        
        // ============================================================================
        // 公共接口
        // ============================================================================
        
        /// <summary>开始播放动画</summary>
        public void Play()
        {
            isPlaying = true;
            currentFrame = 0;
            frameTimer = 0f;
            displayTimer = 0f;
            animationCompleted = false;
            cachedFrameInterval = frameRate > 0f ? 1f / frameRate : 0.125f;
            
            if (frames != null && frames.Length > 0)
            {
                spriteRenderer.sprite = frames[0];
            }
        }
        
        /// <summary>停止播放</summary>
        public void Stop()
        {
            isPlaying = false;
        }
        
        /// <summary>暂停播放</summary>
        public void Pause()
        {
            isPlaying = false;
        }
        
        /// <summary>继续播放</summary>
        public void Resume()
        {
            if (!animationCompleted)
            {
                isPlaying = true;
            }
        }
        
        /// <summary>设置透明度（作为基准透明度，入场 / 离场的淡入淡出在它之上相乘）</summary>
        public void SetAlpha(float alpha)
        {
            baseColor.a = alpha;
            lastAppliedAlpha = -1f;
            UpdatePresentation();
        }

        /// <summary>设置颜色（作为基准色，入场 / 离场的淡入淡出在它之上相乘）</summary>
        public void SetColor(Color color)
        {
            baseColor = color;
            lastAppliedAlpha = -1f;
            UpdatePresentation();
        }
    }
}
