using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.UI;
using Duckov.Economy;
using TMPro;

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
            _lastSceneName = null;
        }

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
    }
}
