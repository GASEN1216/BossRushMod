using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.UI;
using Duckov.Economy;
using TMPro;
using System;
using System.Collections.Generic;

namespace BossRush
{
    // ============================================================================
    // ObjectCache - 场景对象缓存（性能优化）
    // ============================================================================
    /// <summary>
    /// 场景对象缓存 - 存储 FindObjectsOfType 结果，按场景自动失效
    /// </summary>
    internal static class ObjectCache
    {
        private static BoxCollider[] _cachedBoxColliders;
        private static NotificationText[] _cachedNotificationTexts;
        private static CharacterSpawnerRoot[] _cachedCharacterSpawnerRoots;
        private static StockShop[] _cachedStockShops;
        private static TMP_FontAsset[] _cachedTmpFonts;
        private static readonly Dictionary<Type, UnityEngine.Object[]> _cachedObjectsByType =
            new Dictionary<Type, UnityEngine.Object[]>();
        private static string _lastSceneName;

        /// <summary>
        /// 检查并刷新缓存（场景变化时自动失效）
        /// </summary>
        public static void RefreshIfNeeded()
        {
            try
            {
                string currentScene = SceneManager.GetActiveScene().name;
                if (_lastSceneName != currentScene)
                {
                    _cachedBoxColliders = null;
                    _cachedNotificationTexts = null;
                    _cachedCharacterSpawnerRoots = null;
                    _cachedStockShops = null;
                    _cachedTmpFonts = null;
                    _cachedObjectsByType.Clear();
                    _lastSceneName = currentScene;
                }
            }
            catch { }
        }

        /// <summary>
        /// 强制刷新所有缓存
        /// </summary>
        public static void ForceRefresh()
        {
            _cachedBoxColliders = null;
            _cachedNotificationTexts = null;
            _cachedCharacterSpawnerRoots = null;
            _cachedStockShops = null;
            _cachedTmpFonts = null;
            _cachedCharacterPresets = null;
            _cachedObjectsByType.Clear();
            _lastSceneName = null;
        }

        /// <summary>
        /// Mod 卸载时清空所有静态缓存（与项目其它静态缓存生命周期约定一致），
        /// 避免持有已销毁场景对象的引用跨 Mod 生命周期泄漏。
        /// </summary>
        public static void ResetStaticCaches()
        {
            ForceRefresh();
        }

        // --------------------------------------------------------------------
        // 关于下面各 getter 的失效判断为什么不一致（不是笔误，别顺手"统一"掉）
        // --------------------------------------------------------------------
        // 分两类：
        //
        // 1) 场景级：用 FindObjectsOfType 拿的是当前场景里的活对象，过图或对象被销毁后
        //    数组里会出现 Unity 的"假 null"，因此用 IsUnityObjectArrayAlive 逐个体检，
        //    发现死对象就整体重扫。GetCharacterSpawnerRoots / GetStockShops /
        //    GetSceneObjectsByType 属于这一类。
        //
        // 2) 资产级：用 Resources.FindObjectsOfTypeAll 拿的是已加载资产（含未激活的），
        //    不随场景卸载而销毁，所以只判 null 就够，不需要每次遍历整个数组。
        //    GetTmpFonts 虽然也是资产级，但历史上按场景级写法处理，属于偏保守、
        //    不影响正确性的多余检查，这里保持原样不动。
        //
        // GetBoxColliders 目前**没有任何调用点**（全仓仅剩定义），它只判 null 的写法
        // 因此不构成实际问题；将来若要启用，需要先按上面的分类决定用哪种失效判断。
        private static bool IsUnityObjectArrayAlive<T>(T[] objects) where T : UnityEngine.Object
        {
            if (objects == null || objects.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 获取缓存的 BoxCollider 数组
        /// </summary>
        public static BoxCollider[] GetBoxColliders()
        {
            RefreshIfNeeded();
            if (_cachedBoxColliders == null)
            {
                _cachedBoxColliders = UnityEngine.Object.FindObjectsOfType<BoxCollider>();
            }
            return _cachedBoxColliders;
        }

        /// <summary>
        /// 获取缓存的 CharacterSpawnerRoot 数组
        /// </summary>
        public static CharacterSpawnerRoot[] GetCharacterSpawnerRoots()
        {
            RefreshIfNeeded();
            if (!IsUnityObjectArrayAlive(_cachedCharacterSpawnerRoots))
            {
                _cachedCharacterSpawnerRoots = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>();
            }
            return _cachedCharacterSpawnerRoots;
        }

        /// <summary>
        /// 获取缓存的基地商店数组
        /// </summary>
        public static StockShop[] GetStockShops()
        {
            RefreshIfNeeded();
            if (!IsUnityObjectArrayAlive(_cachedStockShops))
            {
                _cachedStockShops = UnityEngine.Object.FindObjectsOfType<StockShop>();
            }
            return _cachedStockShops;
        }

        /// <summary>
        /// 获取缓存的 TMP 字体资源数组
        /// </summary>
        public static TMP_FontAsset[] GetTmpFonts()
        {
            RefreshIfNeeded();
            if (!IsUnityObjectArrayAlive(_cachedTmpFonts))
            {
                _cachedTmpFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            }
            return _cachedTmpFonts;
        }

        public static TMP_FontAsset GetFirstTmpFont()
        {
            TMP_FontAsset[] fonts = GetTmpFonts();
            return fonts != null && fonts.Length > 0 ? fonts[0] : null;
        }

        private static CharacterRandomPreset[] _cachedCharacterPresets;

        /// <summary>
        /// 角色随机 preset 缓存。
        /// </summary>
        /// <remarks>
        /// 有意与其它 getter 不对称，别按其它 getter 的样子"补齐"：
        /// - 不调 RefreshIfNeeded()，RefreshIfNeeded() 里也不清它——
        ///   preset 走 Resources.FindObjectsOfTypeAll 拿的是资产而不是场景实例，
        ///   过图不会失效，每次过图重扫纯属浪费（刷怪路径会频繁取它）。
        /// - 但 ForceRefresh()/ResetStaticCaches() 里**要**清它，
        ///   因为 Mod 卸载或强制刷新时不能把旧程序集的资产引用留下来。
        /// </remarks>
        public static CharacterRandomPreset[] GetCharacterPresets()
        {
            if (_cachedCharacterPresets == null)
            {
                _cachedCharacterPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
            }
            return _cachedCharacterPresets;
        }

        /// <summary>
        /// 获取缓存的 NotificationText 数组
        /// </summary>
        public static NotificationText[] GetNotificationTexts()
        {
            RefreshIfNeeded();
            if (_cachedNotificationTexts == null)
            {
                _cachedNotificationTexts = Resources.FindObjectsOfTypeAll<NotificationText>();
            }
            return _cachedNotificationTexts;
        }

        public static UnityEngine.Object[] GetSceneObjectsByType(Type type)
        {
            if (type == null)
            {
                return null;
            }

            RefreshIfNeeded();
            UnityEngine.Object[] cached;
            if (_cachedObjectsByType.TryGetValue(type, out cached) && IsUnityObjectArrayAlive(cached))
            {
                return cached;
            }

            cached = UnityEngine.Object.FindObjectsOfType(type);
            _cachedObjectsByType[type] = cached;
            return cached;
        }

        public static void InvalidateSceneObjectsByType(Type type)
        {
            if (type == null)
            {
                return;
            }

            _cachedObjectsByType.Remove(type);
        }
    }
}
