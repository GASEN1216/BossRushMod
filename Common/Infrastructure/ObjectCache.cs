using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.UI;

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
            _lastSceneName = null;
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
