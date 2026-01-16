// ============================================================================
// ReflectionCache.cs - 反射缓存工具类
// ============================================================================
// 模块说明：
//   缓存反射操作的结果，避免重复反射带来的性能开销
//   支持字段、属性、方法的缓存
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BossRush.Common.Utils
{
    /// <summary>
    /// 反射缓存工具类 - 提高性能的反射操作缓存
    /// </summary>
    public static class ReflectionCache
    {
        // ========== 缓存存储 ==========

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        private static readonly Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, PropertyInfo> PropertyCache = new Dictionary<string, PropertyInfo>();
        private static readonly Dictionary<string, MethodInfo> MethodCache = new Dictionary<string, MethodInfo>();

        // ========== 类型查找 ==========

        /// <summary>
        /// 获取类型（带缓存）
        /// </summary>
        /// <param name="typeName">类型名称（包含命名空间）</param>
        /// <param name="assemblyName">程序集名称（可选）</param>
        /// <returns>找到的类型，未找到返回 null</returns>
        public static Type GetType(string typeName, string assemblyName = null)
        {
            string key = assemblyName != null ? $"{assemblyName}:{typeName}" : typeName;

            if (TypeCache.TryGetValue(key, out var cachedType))
            {
                return cachedType;
            }

            Type type = null;

            // 尝试通过 Type.GetType
            if (assemblyName != null)
            {
                type = Type.GetType($"{typeName}, {assemblyName}");
            }
            if (type == null)
            {
                type = Type.GetType(typeName);
            }

            // 如果找不到，遍历已加载的程序集
            if (type == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assemblyName == null || assembly.FullName.StartsWith(assemblyName))
                    {
                        type = assembly.GetType(typeName);
                        if (type != null) break;
                    }
                }
            }

            if (type != null)
            {
                TypeCache[key] = type;
            }

            return type;
        }

        // ========== 字段操作 ==========

        /// <summary>
        /// 获取字段信息（带缓存）
        /// </summary>
        /// <param name="type">类型</param>
        /// <param name="fieldName">字段名</param>
        /// <param name="bindingFlags">绑定标志</param>
        /// <returns>字段信息，未找到返回 null</returns>
        public static FieldInfo GetField(Type type, string fieldName, BindingFlags bindingFlags)
        {
            string key = $"{type.FullName}:{fieldName}:{bindingFlags}";

            if (FieldCache.TryGetValue(key, out var cachedField))
            {
                return cachedField;
            }

            var field = type.GetField(fieldName, bindingFlags);

            if (field != null)
            {
                FieldCache[key] = field;
            }

            return field;
        }

        /// <summary>
        /// 获取字段值
        /// </summary>
        /// <param name="obj">对象</param>
        /// <param name="fieldName">字段名</param>
        /// <param name="bindingFlags">绑定标志</param>
        /// <returns>字段值，获取失败返回 null</returns>
        public static object GetFieldValue(object obj, string fieldName, BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance)
        {
            if (obj == null) return null;

            var field = GetField(obj.GetType(), fieldName, bindingFlags);
            return field?.GetValue(obj);
        }

        /// <summary>
        /// 设置字段值
        /// </summary>
        /// <param name="obj">对象</param>
        /// <param name="fieldName">字段名</param>
        /// <param name="value">值</param>
        /// <param name="bindingFlags">绑定标志</param>
        /// <returns>是否成功</returns>
        public static bool SetFieldValue(object obj, string fieldName, object value, BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance)
        {
            if (obj == null) return false;

            var field = GetField(obj.GetType(), fieldName, bindingFlags);
            if (field == null) return false;

            try
            {
                field.SetValue(obj, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ========== 属性操作 ==========

        /// <summary>
        /// 获取属性信息（带缓存）
        /// </summary>
        /// <param name="type">类型</param>
        /// <param name="propertyName">属性名</param>
        /// <param name="bindingFlags">绑定标志</param>
        /// <returns>属性信息，未找到返回 null</returns>
        public static PropertyInfo GetProperty(Type type, string propertyName, BindingFlags bindingFlags)
        {
            string key = $"{type.FullName}:{propertyName}:{bindingFlags}";

            if (PropertyCache.TryGetValue(key, out var cachedProperty))
            {
                return cachedProperty;
            }

            var property = type.GetProperty(propertyName, bindingFlags);

            if (property != null)
            {
                PropertyCache[key] = property;
            }

            return property;
        }

        /// <summary>
        /// 获取属性值
        /// </summary>
        /// <param name="obj">对象</param>
        /// <param name="propertyName">属性名</param>
        /// <param name="bindingFlags">绑定标志</param>
        /// <returns>属性值，获取失败返回 null</returns>
        public static object GetPropertyValue(object obj, string propertyName, BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance)
        {
            if (obj == null) return null;

            var property = GetProperty(obj.GetType(), propertyName, bindingFlags);
            return property?.GetValue(obj);
        }

        /// <summary>
        /// 设置属性值
        /// </summary>
        /// <param name="obj">对象</param>
        /// <param name="propertyName">属性名</param>
        /// <param name="value">值</param>
        /// <param name="bindingFlags">绑定标志</param>
        /// <returns>是否成功</returns>
        public static bool SetPropertyValue(object obj, string propertyName, object value, BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance)
        {
            if (obj == null) return false;

            var property = GetProperty(obj.GetType(), propertyName, bindingFlags);
            if (property == null) return false;

            try
            {
                property.SetValue(obj, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ========== 方法操作 ==========

        /// <summary>
        /// 获取方法信息（带缓存）
        /// </summary>
        /// <param name="type">类型</param>
        /// <param name="methodName">方法名</param>
        /// <param name="bindingFlags">绑定标志</param>
        /// <returns>方法信息，未找到返回 null</returns>
        public static MethodInfo GetMethod(Type type, string methodName, BindingFlags bindingFlags)
        {
            string key = $"{type.FullName}:{methodName}:{bindingFlags}";

            if (MethodCache.TryGetValue(key, out var cachedMethod))
            {
                return cachedMethod;
            }

            var method = type.GetMethod(methodName, bindingFlags);

            if (method != null)
            {
                MethodCache[key] = method;
            }

            return method;
        }

        /// <summary>
        /// 获取方法信息（带参数类型）
        /// </summary>
        /// <param name="type">类型</param>
        /// <param name="methodName">方法名</param>
        /// <param name="parameterTypes">参数类型数组</param>
        /// <returns>方法信息，未找到返回 null</returns>
        public static MethodInfo GetMethod(Type type, string methodName, Type[] parameterTypes)
        {
            string key = $"{type.FullName}:{methodName}:{string.Join(",", System.Array.ConvertAll(parameterTypes, t => t.Name))}";

            if (MethodCache.TryGetValue(key, out var cachedMethod))
            {
                return cachedMethod;
            }

            var method = type.GetMethod(methodName, parameterTypes);

            if (method != null)
            {
                MethodCache[key] = method;
            }

            return method;
        }

        /// <summary>
        /// 调用方法
        /// </summary>
        /// <param name="obj">对象</param>
        /// <param name="methodName">方法名</param>
        /// <param name="parameters">参数数组</param>
        /// <returns>返回值，调用失败返回 null</returns>
        public static object InvokeMethod(object obj, string methodName, object[] parameters = null)
        {
            if (obj == null) return null;

            Type[] paramTypes = null;
            if (parameters != null && parameters.Length > 0)
            {
                paramTypes = new Type[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    paramTypes[i] = parameters[i]?.GetType() ?? typeof(object);
                }
            }

            MethodInfo method = paramTypes != null
                ? GetMethod(obj.GetType(), methodName, paramTypes)
                : GetMethod(obj.GetType(), methodName, BindingFlags.Public | BindingFlags.Instance);

            return method?.Invoke(obj, parameters);
        }

        // ========== 缓存管理 ==========

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public static void ClearCache()
        {
            TypeCache.Clear();
            FieldCache.Clear();
            PropertyCache.Clear();
            MethodCache.Clear();
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public static string GetCacheStats()
        {
            return $"ReflectionCache Stats - Types: {TypeCache.Count}, Fields: {FieldCache.Count}, Properties: {PropertyCache.Count}, Methods: {MethodCache.Count}";
        }
    }
}
