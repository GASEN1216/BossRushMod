// ============================================================================
// DialogueActorFactory.cs - 对话角色工厂
// ============================================================================
// 模块说明：
//   统一创建和配置 DuckovDialogueActor 组件，提供：
//   - 简化的创建接口
//   - 自动设置私有字段（通过反射）
//   - 支持头像、名称、偏移量等配置
//   - 缓存已创建的 Actor 避免重复创建
//   
// 使用方式：
//   var actor = DialogueActorFactory.Create(gameObject, "npc_id", "名字本地化键");
//   var actor = DialogueActorFactory.CreateBilingual(gameObject, "npc_id", "中文名", "English Name");
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using NodeCanvas.DialogueTrees;

namespace BossRush
{
    /// <summary>
    /// 对话角色工厂 - 统一创建 DuckovDialogueActor
    /// </summary>
    public static class DialogueActorFactory
    {
        // 日志标签
        private const string LOG_TAG = "[DialogueActorFactory]";
        
        // 默认偏移量（对话指示器在角色头顶的位置）
        private static readonly Vector3 DEFAULT_OFFSET = new Vector3(0f, 2f, 0f);
        
        // 缓存的反射字段信息
        private static FieldInfo fieldId = null;
        private static FieldInfo fieldNameKey = null;
        private static FieldInfo fieldOffset = null;
        private static FieldInfo fieldPortraitSprite = null;
        private static bool reflectionInitialized = false;
        
        // 已创建的 Actor 缓存（避免重复创建）
        private static Dictionary<GameObject, DuckovDialogueActor> actorCache = new Dictionary<GameObject, DuckovDialogueActor>();
        
        /// <summary>
        /// 初始化反射字段信息
        /// </summary>
        private static void InitializeReflection()
        {
            if (reflectionInitialized) return;
            
            try
            {
                Type actorType = typeof(DuckovDialogueActor);
                BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                
                fieldId = actorType.GetField("id", flags);
                fieldNameKey = actorType.GetField("nameKey", flags);
                fieldOffset = actorType.GetField("offset", flags);
                fieldPortraitSprite = actorType.GetField("_portraitSprite", flags);
                
                reflectionInitialized = true;
                ModBehaviour.DevLog(LOG_TAG + " 反射字段初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] 反射初始化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 创建对话角色（使用本地化键作为名称）
        /// </summary>
        /// <param name="gameObject">要附加组件的 GameObject</param>
        /// <param name="actorId">角色唯一ID（用于 DialogueTree 引用）</param>
        /// <param name="nameKey">名称本地化键</param>
        /// <param name="offset">对话指示器偏移量（可选）</param>
        /// <param name="portrait">头像精灵（可选）</param>
        /// <returns>创建的 DuckovDialogueActor 组件</returns>
        public static DuckovDialogueActor Create(
            GameObject gameObject, 
            string actorId, 
            string nameKey, 
            Vector3? offset = null, 
            Sprite portrait = null)
        {
            if (gameObject == null)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] gameObject 为空");
                return null;
            }
            
            // 检查缓存
            DuckovDialogueActor existingActor;
            if (actorCache.TryGetValue(gameObject, out existingActor) && existingActor != null)
            {
                ModBehaviour.DevLog(LOG_TAG + " 使用缓存的 Actor: " + actorId);
                return existingActor;
            }
            
            // 检查是否已有组件
            existingActor = gameObject.GetComponent<DuckovDialogueActor>();
            if (existingActor != null)
            {
                actorCache[gameObject] = existingActor;
                ModBehaviour.DevLog(LOG_TAG + " 使用已存在的 Actor 组件: " + actorId);
                return existingActor;
            }
            
            try
            {
                // 初始化反射
                InitializeReflection();
                
                // 添加组件
                DuckovDialogueActor actor = gameObject.AddComponent<DuckovDialogueActor>();
                
                // 设置私有字段
                SetField(actor, fieldId, actorId);
                SetField(actor, fieldNameKey, nameKey);
                SetField(actor, fieldOffset, offset ?? DEFAULT_OFFSET);
                
                if (portrait != null)
                {
                    SetField(actor, fieldPortraitSprite, portrait);
                }
                
                // 缓存
                actorCache[gameObject] = actor;
                
                ModBehaviour.DevLog(LOG_TAG + " 创建 Actor 成功: id=" + actorId + ", nameKey=" + nameKey);
                return actor;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] 创建 Actor 失败: " + e.Message);
                return null;
            }
        }
        
        /// <summary>
        /// 创建对话角色（使用双语名称，自动注入本地化）
        /// </summary>
        /// <param name="gameObject">要附加组件的 GameObject</param>
        /// <param name="actorId">角色唯一ID</param>
        /// <param name="nameCN">中文名称</param>
        /// <param name="nameEN">英文名称</param>
        /// <param name="offset">对话指示器偏移量（可选）</param>
        /// <param name="portrait">头像精灵（可选）</param>
        /// <returns>创建的 DuckovDialogueActor 组件</returns>
        public static DuckovDialogueActor CreateBilingual(
            GameObject gameObject, 
            string actorId, 
            string nameCN, 
            string nameEN, 
            Vector3? offset = null, 
            Sprite portrait = null)
        {
            // 生成本地化键
            string nameKey = "BossRush_Actor_" + actorId + "_Name";
            
            // 注入本地化
            string localizedName = L10n.T(nameCN, nameEN);
            LocalizationHelper.InjectLocalization(nameKey, localizedName);
            
            // 创建 Actor
            return Create(gameObject, actorId, nameKey, offset, portrait);
        }
        
        /// <summary>
        /// 获取已创建的 Actor（如果存在）
        /// </summary>
        /// <param name="gameObject">目标 GameObject</param>
        /// <returns>已创建的 Actor，不存在则返回 null</returns>
        public static DuckovDialogueActor Get(GameObject gameObject)
        {
            if (gameObject == null) return null;
            
            DuckovDialogueActor actor;
            if (actorCache.TryGetValue(gameObject, out actor) && actor != null)
            {
                return actor;
            }
            
            // 尝试从组件获取
            actor = gameObject.GetComponent<DuckovDialogueActor>();
            if (actor != null)
            {
                actorCache[gameObject] = actor;
            }
            
            return actor;
        }
        
        /// <summary>
        /// 通过 ID 获取已注册的 Actor
        /// </summary>
        /// <param name="actorId">角色ID</param>
        /// <returns>对应的 Actor，不存在则返回 null</returns>
        public static DuckovDialogueActor GetById(string actorId)
        {
            try
            {
                // 使用游戏原生的静态方法获取
                return DuckovDialogueActor.Get(actorId);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 更新 Actor 的名称（支持动态切换）
        /// </summary>
        /// <param name="actor">目标 Actor</param>
        /// <param name="nameKey">新的名称本地化键</param>
        public static void UpdateName(DuckovDialogueActor actor, string nameKey)
        {
            if (actor == null) return;
            
            try
            {
                InitializeReflection();
                SetField(actor, fieldNameKey, nameKey);
                ModBehaviour.DevLog(LOG_TAG + " 更新 Actor 名称: " + nameKey);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 更新名称失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 更新 Actor 的名称（双语版本）
        /// </summary>
        /// <param name="actor">目标 Actor</param>
        /// <param name="nameCN">中文名称</param>
        /// <param name="nameEN">英文名称</param>
        public static void UpdateNameBilingual(DuckovDialogueActor actor, string nameCN, string nameEN)
        {
            if (actor == null) return;
            
            try
            {
                // 生成临时本地化键
                string nameKey = "BossRush_Actor_TempName_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                
                // 注入本地化
                string localizedName = L10n.T(nameCN, nameEN);
                LocalizationHelper.InjectLocalization(nameKey, localizedName);
                
                // 更新名称
                UpdateName(actor, nameKey);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 更新双语名称失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 移除 Actor 缓存
        /// </summary>
        /// <param name="gameObject">目标 GameObject</param>
        public static void Remove(GameObject gameObject)
        {
            if (gameObject != null && actorCache.ContainsKey(gameObject))
            {
                actorCache.Remove(gameObject);
            }
        }
        
        /// <summary>
        /// 清理所有缓存
        /// </summary>
        public static void ClearCache()
        {
            actorCache.Clear();
            ModBehaviour.DevLog(LOG_TAG + " 缓存已清理");
        }
        
        /// <summary>
        /// 设置字段值的辅助方法
        /// </summary>
        private static void SetField(object obj, FieldInfo field, object value)
        {
            if (field != null && obj != null)
            {
                field.SetValue(obj, value);
            }
        }
    }
}
