// ============================================================================
// NPCBubbleAnimator.cs - NPC头顶气泡动画组件
// ============================================================================
// 模块说明：
//   在NPC头顶显示序列帧动画（如心裂开效果）
//   使用 SpriteRenderer 实现，自动面向相机
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
        private Transform targetTransform;  // 跟随的目标
        private float displayTimer = 0f;
        private bool animationCompleted = false;
        
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
                
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
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
        
        private void Initialize()
        {
            // 创建SpriteRenderer
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sortingOrder = 100;  // 确保在前面显示
            
            // 设置初始帧
            if (frames != null && frames.Length > 0)
            {
                spriteRenderer.sprite = frames[0];
            }
            
            // 设置缩放
            transform.localScale = Vector3.one * spriteScale;
            
            // 更新位置
            UpdatePosition();
        }
        
        void Update()
        {
            // 更新位置（跟随目标）
            UpdatePosition();
            
            // 面向相机（Billboard效果）
            FaceCamera();
            
            // 更新动画帧
            if (isPlaying)
            {
                UpdateAnimation();
            }
            
            // 更新显示计时器
            if (animationCompleted || loop)
            {
                displayTimer += Time.deltaTime;
                if (displayDuration > 0 && displayTimer >= displayDuration)
                {
                    if (destroyOnComplete)
                    {
                        Destroy(gameObject);
                    }
                    else
                    {
                        Stop();
                    }
                }
            }
        }
        
        private void UpdatePosition()
        {
            if (targetTransform != null)
            {
                transform.position = targetTransform.position + Vector3.up * heightOffset;
            }
        }
        
        private void FaceCamera()
        {
            if (Camera.main != null)
            {
                // Billboard效果：始终面向相机
                transform.rotation = Camera.main.transform.rotation;
            }
        }
        
        private void UpdateAnimation()
        {
            if (frames == null || frames.Length == 0) return;
            
            frameTimer += Time.deltaTime;
            float frameInterval = 1f / frameRate;
            
            if (frameTimer >= frameInterval)
            {
                frameTimer -= frameInterval;
                currentFrame++;
                
                if (currentFrame >= frames.Length)
                {
                    if (loop)
                    {
                        currentFrame = 0;
                    }
                    else
                    {
                        currentFrame = frames.Length - 1;
                        animationCompleted = true;
                        isPlaying = false;
                        
                        // 如果没有设置显示持续时间，动画完成后立即销毁
                        if (displayDuration <= 0 && destroyOnComplete)
                        {
                            Destroy(gameObject);
                            return;
                        }
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
        
        /// <summary>设置透明度</summary>
        public void SetAlpha(float alpha)
        {
            if (spriteRenderer != null)
            {
                Color c = spriteRenderer.color;
                c.a = alpha;
                spriteRenderer.color = c;
            }
        }
        
        /// <summary>设置颜色</summary>
        public void SetColor(Color color)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = color;
            }
        }
    }
}
