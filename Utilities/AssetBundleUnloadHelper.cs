// ============================================================================
// AssetBundleUnloadHelper.cs - AssetBundle 卸载通用工具
// ============================================================================
// 模块说明：
//   提供统一的安全卸载入口，避免各模块重复写 try/catch。
//   放在 Utilities/ 下，供各系统复用。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush.Utils
{
    /// <summary>
    /// AssetBundle 卸载通用辅助
    /// </summary>
    public static class AssetBundleUnloadHelper
    {
        /// <summary>
        /// 尝试卸载 AssetBundle，但保留已加载资源。
        /// </summary>
        public static void TryUnload(AssetBundle bundle, string logPrefix)
        {
            if (bundle == null)
            {
                return;
            }

            try
            {
                bundle.Unload(false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " 释放 AssetBundle 失败: " + e.Message);
            }
        }
    }
}
