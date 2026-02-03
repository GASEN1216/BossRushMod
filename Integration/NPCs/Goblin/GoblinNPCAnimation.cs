// ============================================================================
// GoblinNPCAnimation.cs - 哥布林NPC控制器（动画和气泡）
// ============================================================================
// 模块说明：
//   哥布林NPC控制器的动画部分，使用 partial class 机制
//   包含名字标签、气泡显示、动画参数设置方法
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using Duckov.UI.DialogueBubbles;
using BossRush.Constants;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 哥布林NPC控制器 - 动画和气泡显示
    /// </summary>
    public partial class GoblinNPCController
    {
        // ============================================================================
        // 名字标签
        // ============================================================================
        
        private GameObject nameTagObject;
        private TMPro.TextMeshPro nameTagText;
        
        // ============================================================================
        // 名字标签方法
        // ============================================================================
        
        /// <summary>
        /// 创建头顶名字标签（与快递员阿稳完全一致的设置）
        /// </summary>
        private void CreateNameTag()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                // 创建名字标签对象
                nameTagObject = new GameObject("GoblinNameTag");
                nameTagObject.transform.SetParent(transform);
                nameTagObject.transform.localPosition = new Vector3(0f, GoblinNPCConstants.NAME_TAG_HEIGHT, 0f);
                
                // 添加 TextMeshPro 组件
                nameTagText = nameTagObject.AddComponent<TMPro.TextMeshPro>();
                
                // 使用本地化名称
                string goblinName = "叮当";
                try
                {
                    goblinName = BossRush.L10n.T("叮当", "Dingdang");
                }
                catch
                {
                    // 如果本地化失败，使用默认名称
                }
                
                nameTagText.text = goblinName;
                nameTagText.fontSize = 4f;  // 与快递员阿稳一致
                nameTagText.alignment = TMPro.TextAlignmentOptions.Center;
                
                // 强制设置颜色为白色（与快递员阿稳一致）
                nameTagText.color = new Color(1f, 1f, 1f, 1f);
                nameTagText.faceColor = new Color32(255, 255, 255, 255);
                
                // 设置文字始终面向相机
                nameTagText.enableAutoSizing = false;
                
                // 设置排序层级确保可见
                nameTagText.sortingOrder = 100;
                
                // 禁用富文本以防止颜色标签影响
                nameTagText.richText = false;
                
                ModBehaviour.DevLog("[GoblinNPC] 名字标签创建成功: " + goblinName);
            }, "GoblinNPCAnimation.CreateNameTag");
        }
        
        // ============================================================================
        // 动画控制方法
        // ============================================================================
        
        /// <summary>
        /// 开始播放急停动画（距离4米时触发）
        /// 设置 IsRunning=false，触发 DoStop，然后设置 IsIdle=true
        /// </summary>
        private void StartBrakeAnimation()
        {
            isBraking = true;
            
            // 1. 先停止跑步动画
            SafeSetBool(hash_IsRunning, false);
            
            // 2. 触发急停动画（DoStop trigger）
            SafeSetTrigger(hash_DoStop);
            
            // 3. 设置待机状态
            SafeSetBool(hash_IsIdle, true);
            
            ModBehaviour.DevLog("[GoblinNPC] 距离4米，播放急停动画（IsRunning=false, DoStop触发, IsIdle=true）");
        }
        
        /// <summary>
        /// 开始待机动画
        /// </summary>
        private void StartIdleAnimation()
        {
            isIdling = true;
            SafeSetBool(hash_IsIdle, true);
            SafeSetBool(hash_IsRunning, false);
        }
        
        /// <summary>
        /// 停止待机动画
        /// </summary>
        private void StopIdleAnimation()
        {
            isIdling = false;
            SafeSetBool(hash_IsIdle, false);
        }
        
        // ============================================================================
        // 安全设置动画参数的辅助方法
        // ============================================================================
        
        private void SafeSetBool(int hash, bool value)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (animator != null) animator.SetBool(hash, value);
            }, "GoblinNPCAnimation.SafeSetBool");
        }
        
        private void SafeSetTrigger(int hash)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (animator != null) animator.SetTrigger(hash);
            }, "GoblinNPCAnimation.SafeSetTrigger");
        }
        
        private void SafeSetFloat(int hash, float value)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (animator != null) animator.SetFloat(hash, value);
            }, "GoblinNPCAnimation.SafeSetFloat");
        }
        
        // ============================================================================
        // 气泡显示方法
        // ============================================================================
        
        /// <summary>
        /// 显示裂开的心气泡
        /// 优先使用序列帧动画，如果没有则使用文字气泡
        /// </summary>
        public void ShowBrokenHeartBubble()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                // 尝试使用序列帧动画
                if (TryShowBrokenHeartAnimation())
                {
                    ModBehaviour.DevLog("[GoblinNPC] 显示心裂开动画");
                    return;
                }
                
                // 回退：使用文字气泡
                string brokenHeart = "心碎";
                DialogueBubblesManager.Show(
                    brokenHeart, 
                    transform, 
                    GoblinNPCConstants.NAME_TAG_HEIGHT + GoblinNPCConstants.BUBBLE_OFFSET_Y,
                    false,
                    false,
                    -1f,
                    GoblinNPCConstants.BUBBLE_DURATION
                );
                ModBehaviour.DevLog("[GoblinNPC] 显示文字气泡（回退方案）");
            }, "GoblinNPCAnimation.ShowBrokenHeartBubble");
        }
        
        /// <summary>
        /// 显示冒爱心气泡
        /// 优先使用序列帧动画，如果没有则使用文字气泡
        /// </summary>
        public void ShowLoveHeartBubble()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                // 尝试使用序列帧动画
                if (TryShowLoveHeartAnimation())
                {
                    ModBehaviour.DevLog("[GoblinNPC] 显示冒爱心动画");
                    return;
                }
                
                // 回退：使用文字气泡
                string loveHeart = "♥";
                DialogueBubblesManager.Show(
                    loveHeart, 
                    transform, 
                    GoblinNPCConstants.NAME_TAG_HEIGHT + GoblinNPCConstants.BUBBLE_OFFSET_Y,
                    false,
                    false,
                    -1f,
                    GoblinNPCConstants.BUBBLE_DURATION
                );
                ModBehaviour.DevLog("[GoblinNPC] 显示爱心文字气泡（回退方案）");
            }, "GoblinNPCAnimation.ShowLoveHeartBubble");
        }
        
        /// <summary>
        /// 尝试显示冒爱心的序列帧动画
        /// </summary>
        /// <returns>是否成功显示</returns>
        private bool TryShowLoveHeartAnimation()
        {
            return NPCExceptionHandler.TryExecute(() =>
            {
                // 尝试从AssetBundle加载序列帧
                string modDir = System.IO.Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                string bundlePath = System.IO.Path.Combine(modDir, "Assets", "ui", "love_heart");
                
                if (!System.IO.File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[GoblinNPC] 冒爱心动画资源不存在: " + bundlePath);
                    return false;
                }
                
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 加载冒爱心动画Bundle失败");
                    return false;
                }
                
                // 加载所有Sprite
                Sprite[] frames = bundle.LoadAllAssets<Sprite>();
                bundle.Unload(false);
                
                if (frames == null || frames.Length == 0)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 冒爱心动画没有Sprite");
                    return false;
                }
                
                // 按名称排序（确保帧顺序正确）
                System.Array.Sort(frames, (a, b) => string.Compare(a.name, b.name));
                
                // 创建动画
                NPCBubbleAnimator.Create(
                    transform,
                    frames,
                    GoblinNPCConstants.NAME_TAG_HEIGHT + GoblinNPCConstants.BUBBLE_ANIMATION_OFFSET_Y,
                    GoblinNPCConstants.BUBBLE_DURATION,
                    false
                );
                
                return true;
            }, "GoblinNPCAnimation.TryShowLoveHeartAnimation", false);
        }
        
        /// <summary>
        /// 尝试显示心裂开的序列帧动画
        /// </summary>
        /// <returns>是否成功显示</returns>
        private bool TryShowBrokenHeartAnimation()
        {
            return NPCExceptionHandler.TryExecute(() =>
            {
                // 尝试从AssetBundle加载序列帧
                string modDir = System.IO.Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                string bundlePath = System.IO.Path.Combine(modDir, "Assets", "ui", "broken_heart");
                
                if (!System.IO.File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[GoblinNPC] 心裂开动画资源不存在: " + bundlePath);
                    return false;
                }
                
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 加载心裂开动画Bundle失败");
                    return false;
                }
                
                // 加载所有Sprite
                Sprite[] frames = bundle.LoadAllAssets<Sprite>();
                bundle.Unload(false);
                
                if (frames == null || frames.Length == 0)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 心裂开动画没有Sprite");
                    return false;
                }
                
                // 按名称排序（确保帧顺序正确）
                System.Array.Sort(frames, (a, b) => string.Compare(a.name, b.name));
                
                // 创建动画
                NPCBubbleAnimator.Create(
                    transform,
                    frames,
                    GoblinNPCConstants.NAME_TAG_HEIGHT + GoblinNPCConstants.BUBBLE_ANIMATION_OFFSET_Y,
                    GoblinNPCConstants.BUBBLE_DURATION,
                    false
                );
                
                return true;
            }, "GoblinNPCAnimation.TryShowBrokenHeartAnimation", false);
        }
    }
}
